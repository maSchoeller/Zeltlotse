using System.Net;
using System.Text.Json;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-11, Notfallordner (FR-33, AD-8, AD-30) gegen echtes PostgreSQL:
/// <c>GET /api/freizeiten/{freizeitId}/personen/notfallordner?person=…</c> für Leitung und Team. Nur Personen, deren jüngste Freigabe
/// bestätigt (Stufe Team) und noch gültig ist, mit genau den Fragen der Freigabe; je aufgenommener Person ein Eintrag im Einsicht-Protokoll
/// (höchstens einmal am Tag, nicht für die lesende Person selbst); Eingabefehler <c>400</c>, Rechte <c>404</c>/<c>401</c>.
/// </summary>
public sealed class NotfallordnerTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit, params Guid[] personen) =>
        $"/api/freizeiten/{freizeit}/personen/notfallordner{(personen.Length == 0 ? "" : "?" + string.Join("&", personen.Select(p => $"person={p}")))}";

    private async Task<List<JsonElement>> OrdnerAsync(HttpClient client, params Guid[] personen)
    {
        using var antwort = await client.GetAsync(Pfad(_s.X, personen), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    private Task<long> ProtokollAsync(Guid lesend) =>
        WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"LesendePersonId\" = @l AND \"Zweck\" = 2", ("l", lesend));

    private Task<long> ProtokollAsync(Guid lesend, Guid betroffen) =>
        WertAsync<long>(
            _s.Verbindung,
            "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"LesendePersonId\" = @l AND \"BetroffenePersonId\" = @b AND \"Zweck\" = 2",
            ("l", lesend), ("b", betroffen));

    private async Task<Guid> BestaetigtAsync(Guid kind)
    {
        var anmeldung = await _s.KindAnmeldenAsync(kind);
        await _s.BestaetigenAsync(anmeldung);
        await _s.ZustellenAsync();
        return anmeldung;
    }

    [Fact]
    public async Task Leitung_druckt_eine_bestaetigte_Person_mit_genau_den_Fragen_der_Freigabe_und_einem_Protokolleintrag()
    {
        await BestaetigtAsync(_s.Emma);

        var ordner = await OrdnerAsync(_s.Miriam, _s.Emma);

        var emma = Assert.Single(ordner);
        Assert.Equal(["fragen", "nachname", "personId", "vorname"], Namen(emma));
        Assert.Equal((_s.Emma, "Emma", "Muster"), (emma.GetProperty("personId").GetGuid(), emma.GetProperty("vorname").GetString(), emma.GetProperty("nachname").GetString()));
        // Krankheiten hat Katrin beantwortet, die Freizeit fragt sie aber nicht ab; Schwimmen ist abgefragt, aber unbeantwortet.
        Assert.Equal(["allergien", "notfallkontakte", "schwimmen"], EinsichtJson.Kennungen(emma));
        Assert.Equal(["milch", NotfallkontaktEmma, null], emma.GetProperty("fragen").EnumerateArray().Select(f => f.GetProperty("wert").GetString()));
        Assert.DoesNotContain("Asthma", emma.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(1L, await ProtokollAsync(_s.MiriamId, _s.Emma));
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung,
            "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @e AND \"FreizeitId\" = @f AND \"OrganisationId\" = @o",
            ("e", _s.Emma), ("f", _s.X), ("o", _s.A)));
    }

    [Fact]
    public async Task Team_druckt_die_bestaetigte_Person_und_die_Eltern_sehen_die_Einsicht_im_Zugriffsprotokoll()
    {
        await BestaetigtAsync(_s.Emma);

        var ordner = await OrdnerAsync(_s.Tobias, _s.Emma);

        Assert.Equal(_s.Emma, Assert.Single(ordner).GetProperty("personId").GetGuid());
        Assert.Equal(1L, await ProtokollAsync(_s.TobiasId, _s.Emma));
        var protokoll = (await JsonAsync(await _s.Katrin.GetAsync("/api/ich/zugriffsprotokoll", Abbruch))).EnumerateArray().ToList();
        var eintrag = Assert.Single(protokoll, e => e.GetProperty("zweck").GetString() == "freizeitpassEingesehen");
        Assert.Equal(("Emma Muster", "Tobias Wanderer"), (eintrag.GetProperty("betroffenerName").GetString(), eintrag.GetProperty("lesenderName").GetString()));
    }

    [Fact]
    public async Task Nicht_bestaetigte_Personen_fehlen_fuer_Leitung_und_Team_ohne_Protokoll()
    {
        await _s.KindAnmeldenAsync(_s.Emma);

        Assert.Empty(await OrdnerAsync(_s.Miriam, _s.Emma));
        Assert.Empty(await OrdnerAsync(_s.Tobias, _s.Emma));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"Zweck\" = 2"));
    }

    [Fact]
    public async Task Gemischt_nur_bestaetigte_einmal_doppelte_und_fremde_Kennungen_fallen_still_weg()
    {
        await BestaetigtAsync(_s.Emma);
        await _s.KindAnmeldenAsync(_s.Mia);

        var ordner = await OrdnerAsync(_s.Miriam, _s.Mia, _s.Emma, Guid.CreateVersion7(), _s.Emma);

        Assert.Equal([_s.Emma], ordner.Select(p => p.GetProperty("personId").GetGuid()));
        Assert.Equal(1L, await ProtokollAsync(_s.MiriamId));
    }

    [Fact]
    public async Task Sortiert_nach_Nachname_dann_Vorname()
    {
        await BestaetigtAsync(_s.Mia);
        await BestaetigtAsync(_s.Emma);

        // Tobias ist als Mitarbeitender bestätigt; der Client schickt ihn hier nur zur Prüfung der Sortierung mit.
        var ordner = await OrdnerAsync(_s.Miriam, _s.TobiasId, _s.Mia, _s.Emma);

        Assert.Equal(["Emma Muster", "Mia Muster", "Tobias Wanderer"], ordner.Select(p => $"{p.GetProperty("vorname").GetString()} {p.GetProperty("nachname").GetString()}"));
    }

    [Fact]
    public async Task Nach_dem_Ende_des_Freigabefensters_ist_der_Ordner_leer()
    {
        await BestaetigtAsync(_s.Emma);
        _s.Factory.Uhr.SetUtcNow(FreigabeEnde.AddMinutes(1));
        // Die Sitzung ist nach fast einem Jahr abgelaufen; Miriam meldet sich neu an.
        await AnmeldenAsync(_s.Factory, _s.Miriam, FreizeitHelfer.Miriam);

        Assert.Empty(await OrdnerAsync(_s.Miriam, _s.Emma));
        Assert.Equal(0L, await ProtokollAsync(_s.MiriamId));
    }

    [Fact]
    public async Task Nach_einer_Absage_ist_der_Ordner_leer()
    {
        var emma = await BestaetigtAsync(_s.Emma);
        await _s.AbsagenAsync(emma);
        await _s.ZustellenAsync();
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(await OrdnerAsync(_s.Miriam, _s.Emma));
        Assert.Equal(0L, await ProtokollAsync(_s.MiriamId));
    }

    [Fact]
    public async Task Zwei_Abrufe_am_selben_Tag_ergeben_einen_Eintrag_je_Person()
    {
        await BestaetigtAsync(_s.Emma);
        await BestaetigtAsync(_s.Mia);

        await OrdnerAsync(_s.Miriam, _s.Emma, _s.Mia);
        await OrdnerAsync(_s.Miriam, _s.Emma, _s.Mia);

        Assert.Equal(1L, await ProtokollAsync(_s.MiriamId, _s.Emma));
        Assert.Equal(1L, await ProtokollAsync(_s.MiriamId, _s.Mia));
    }

    [Fact]
    public async Task Die_eigene_Person_steht_im_Ordner_ohne_Eintrag_ins_Zugriffsprotokoll()
    {
        await BestaetigtAsync(_s.Emma);

        var ordner = await OrdnerAsync(_s.Tobias, _s.TobiasId, _s.Emma);

        Assert.Equal(2, ordner.Count);
        Assert.Equal(0L, await WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @t", ("t", _s.TobiasId)));
        Assert.Equal(1L, await ProtokollAsync(_s.TobiasId, _s.Emma));
    }

    [Fact]
    public async Task Ohne_Kennung_mit_151_Kennungen_oder_ungueltiger_Kennung_400_mit_150_Kennungen_200()
    {
        var grenze = Enumerable.Range(0, 150).Select(_ => Guid.CreateVersion7()).ToArray();
        var zuViele = Enumerable.Range(0, 151).Select(_ => Guid.CreateVersion7()).ToArray();

        using var ohne = await _s.Miriam.GetAsync(Pfad(_s.X), Abbruch);
        using var ueber = await _s.Miriam.GetAsync(Pfad(_s.X, zuViele), Abbruch);
        using var ungueltig = await _s.Miriam.GetAsync($"{Pfad(_s.X, _s.Emma)}&person=keine-kennung", Abbruch);
        using var genau = await _s.Miriam.GetAsync(Pfad(_s.X, grenze), Abbruch);

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.OK],
            new[] { ohne.StatusCode, ueber.StatusCode, ungueltig.StatusCode, genau.StatusCode });
        Assert.True((await ProblemAsync(ohne)).GetProperty("errors").TryGetProperty("person", out _));
        Assert.True((await ProblemAsync(ueber)).GetProperty("errors").TryGetProperty("person", out _));
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_Leitung_nur_von_Z_und_fremde_Organisation_404_ohne_Sitzung_401()
    {
        await BestaetigtAsync(_s.Emma);
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);

        var status = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in new[] { (_s.Andrea, _s.X), (leitungZ, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) })
        {
            using var antwort = await client.GetAsync(Pfad(freizeit, _s.Emma), Abbruch);
            status.Add(antwort.StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized], status);
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"Zweck\" = 2"));
    }

    [Fact]
    public async Task Massgeblich_ist_die_juengste_Freigabe_eine_aeltere_bestaetigte_zaehlt_nicht()
    {
        // Emma ist nur eingegangen (jüngste Freigabe Stufe Leitung); daneben eine ältere, noch gültige Freigabe der Stufe Team.
        await _s.KindAnmeldenAsync(_s.Emma);
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO personen.freigabe (\"Id\", \"OrganisationId\", \"PersonId\", \"FreizeitId\", \"AnmeldungId\", \"Stufe\", \"GueltigBis\", \"AngelegtAm\", \"Fragen\") " +
            "VALUES (@id, @o, @p, @f, @a, 2, @bis, @am, ARRAY['krankheiten'])",
            ("id", Guid.CreateVersion7()), ("o", _s.A), ("p", _s.Emma), ("f", _s.X), ("a", Guid.CreateVersion7()), ("bis", FreigabeEnde), ("am", DateTimeOffset.UtcNow.AddYears(-1)));

        Assert.Empty(await OrdnerAsync(_s.Miriam, _s.Emma));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"Zweck\" = 2"));
    }

    [Fact]
    public async Task Die_Rolle_kommt_aus_der_Rollenliste_von_Personen_ohne_Zeile_dort_404()
    {
        await BestaetigtAsync(_s.Emma);
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM personen.freizeit_rolle WHERE \"PersonId\" = @m", ("m", _s.MiriamId));

        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(Pfad(_s.X, _s.Emma), Abbruch)).StatusCode);
    }
}
