using System.Net;
using System.Text.Json;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 6-5, Notfallkarte für den Offline-Speicher (FR-65, AD-8, AD-18, AD-30) gegen echtes PostgreSQL:
/// <c>GET /api/freizeiten/{freizeitId}/personen/notfallkarte</c> für Leitung und Team. Personen bestimmt der Server selbst: alle mit
/// jüngster, bestätigter (Stufe Team) und gültiger Freigabe dieser Freizeit, sortiert nach Nachname und Vorname; je Person nur die Fragen
/// <c>allergien</c>, <c>medikamente</c>, <c>krankheiten</c>, <c>notfallkontakte</c>, soweit freigegeben. Je ausgelieferter Person ein Eintrag im
/// Einsicht-Protokoll mit eigenem Zweck (höchstens einmal am Tag, nicht für sich selbst); Antworten mit <c>Cache-Control: no-store</c>.
/// </summary>
public sealed class NotfallkarteTests(PostgresContainer postgres) : IAsyncLifetime
{
    /// <summary>Zweck „Notfallkarte aufs Gerät geladen“ im Einsicht-Protokoll.</summary>
    private const int Zweck = 5;

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/personen/notfallkarte";

    private async Task<List<JsonElement>> KarteAsync(HttpClient client)
    {
        using var antwort = await client.GetAsync(Pfad(_s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    private Task<long> ProtokollAsync(Guid lesend) =>
        WertAsync<long>(
            _s.Verbindung, $"SELECT count(*) FROM personen.einsicht_protokoll WHERE \"LesendePersonId\" = @l AND \"Zweck\" = {Zweck}", ("l", lesend));

    private Task<long> ProtokollAsync(Guid lesend, Guid betroffen) =>
        WertAsync<long>(
            _s.Verbindung,
            $"SELECT count(*) FROM personen.einsicht_protokoll WHERE \"LesendePersonId\" = @l AND \"BetroffenePersonId\" = @b AND \"Zweck\" = {Zweck}",
            ("l", lesend), ("b", betroffen));

    private Task<long> ProtokollGesamtAsync() =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM personen.einsicht_protokoll WHERE \"Zweck\" = {Zweck}");

    private async Task<Guid> BestaetigtAsync(Guid kind)
    {
        var anmeldung = await _s.KindAnmeldenAsync(kind);
        await _s.BestaetigenAsync(anmeldung);
        await _s.ZustellenAsync();
        return anmeldung;
    }

    private static string Name(JsonElement p) => $"{p.GetProperty("vorname").GetString()} {p.GetProperty("nachname").GetString()}";

    [Fact]
    public async Task Leitung_bekommt_bestaetigte_Personen_mit_nur_den_vier_Notfallfragen_soweit_freigegeben()
    {
        // X fragt jetzt auch Medikamente und Krankheiten ab, dazu Schwimmen und Hinweise (nicht notfallrelevant).
        await _s.FragenAsync(_s.X, "allergien,medikamente,notfallkontakte,krankheiten,schwimmen,hinweise");
        await _s.AntwortAsync(_s.Emma, "medikamente", "Salbutamol-Spray bei Bedarf");
        await _s.AntwortAsync(_s.Emma, "hinweise", "Schläft gern lang");
        await BestaetigtAsync(_s.Emma);

        var karte = await KarteAsync(_s.Miriam);

        var emma = Assert.Single(karte, p => p.GetProperty("personId").GetGuid() == _s.Emma);
        Assert.Equal(["fragen", "nachname", "personId", "vorname"], Namen(emma));
        Assert.Equal("Emma Muster", Name(emma));
        Assert.Equal(["allergien", "medikamente", "notfallkontakte", "krankheiten"], EinsichtJson.Kennungen(emma));
        Assert.Equal(
            ["milch", "Salbutamol-Spray bei Bedarf", NotfallkontaktEmma, "Asthma"],
            emma.GetProperty("fragen").EnumerateArray().Select(f => f.GetProperty("wert").GetString()));
        Assert.DoesNotContain("Schläft", emma.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(1L, await ProtokollAsync(_s.MiriamId, _s.Emma));
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung,
            $"SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @e AND \"FreizeitId\" = @f AND \"OrganisationId\" = @o AND \"Zweck\" = {Zweck}",
            ("e", _s.Emma), ("f", _s.X), ("o", _s.A)));
    }

    [Fact]
    public async Task Nicht_freigegebene_Notfallfragen_fehlen_die_Freizeit_fragt_Krankheiten_nicht_ab()
    {
        // Szenario: X fragt Allergien, Notfallkontakte und Schwimmen ab; Krankheiten hat Katrin beantwortet, sie sind aber nicht freigegeben.
        await BestaetigtAsync(_s.Emma);

        var emma = Assert.Single(await KarteAsync(_s.Tobias), p => p.GetProperty("personId").GetGuid() == _s.Emma);

        Assert.Equal(["allergien", "notfallkontakte"], EinsichtJson.Kennungen(emma));
        Assert.DoesNotContain("Asthma", emma.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Team_laedt_die_Karte_die_Eltern_sehen_den_eigenen_Zweck_im_Zugriffsprotokoll()
    {
        await BestaetigtAsync(_s.Emma);

        await KarteAsync(_s.Tobias);

        Assert.Equal(1L, await ProtokollAsync(_s.TobiasId, _s.Emma));
        var protokoll = (await JsonAsync(await _s.Katrin.GetAsync("/api/ich/zugriffsprotokoll", Abbruch))).EnumerateArray().ToList();
        var eintrag = Assert.Single(protokoll, e => e.GetProperty("zweck").GetString() == "notfallkarteGeladen");
        Assert.Equal(("Emma Muster", "Tobias Wanderer"), (eintrag.GetProperty("betroffenerName").GetString(), eintrag.GetProperty("lesenderName").GetString()));
    }

    [Fact]
    public async Task Datenauskunft_nennt_das_Laden_der_Notfallkarte_mit_der_lesenden_Person()
    {
        // Tobias (volljährig, eigenes Konto, als Mitarbeitender bestätigt) steht auf der Karte, die Miriam lädt.
        await KarteAsync(_s.Miriam);
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);

        var auskunft = (await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<Zeltlotse.Api.Contracts.Betrieb.DatenauskunftAntwort>(
            _s.Tobias, "/api/ich/daten", Abbruch))!;

        var zeile = Assert.Single(
            Assert.Single(auskunft.Abschnitte, a => a.Titel == "Zugriffsprotokoll").Zeilen,
            z => z.Wert.EndsWith(": Notfallkarte aufs Gerät geladen", StringComparison.Ordinal));
        Assert.StartsWith("Miriam ", zeile.Wert, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nur_eingegangene_Personen_fehlen_ohne_Protokoll()
    {
        await _s.KindAnmeldenAsync(_s.Emma);

        Assert.DoesNotContain(await KarteAsync(_s.Miriam), p => p.GetProperty("personId").GetGuid() == _s.Emma);
        Assert.DoesNotContain(await KarteAsync(_s.Tobias), p => p.GetProperty("personId").GetGuid() == _s.Emma);
        Assert.Equal(0L, await WertAsync<long>(
            _s.Verbindung, $"SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @e AND \"Zweck\" = {Zweck}", ("e", _s.Emma)));
    }

    [Fact]
    public async Task Sortiert_nach_Nachname_dann_Vorname_mit_dem_bestaetigten_Team()
    {
        await BestaetigtAsync(_s.Mia);
        await BestaetigtAsync(_s.Emma);

        var karte = await KarteAsync(_s.Miriam);

        // Tobias ist als Mitarbeitender bestätigt und steht deshalb mit auf der Karte.
        Assert.Equal(["Emma Muster", "Mia Muster", "Tobias Wanderer"], karte.Select(Name));
    }

    [Fact]
    public async Task Nach_dem_Ende_des_Freigabefensters_ist_die_Karte_leer()
    {
        await BestaetigtAsync(_s.Emma);
        _s.Factory.Uhr.SetUtcNow(FreigabeEnde.AddMinutes(1));
        await AnmeldenAsync(_s.Factory, _s.Miriam, FreizeitHelfer.Miriam);

        Assert.Empty(await KarteAsync(_s.Miriam));
        Assert.Equal(0L, await ProtokollGesamtAsync());
    }

    [Fact]
    public async Task Nach_einer_Absage_fehlt_die_Person()
    {
        var emma = await BestaetigtAsync(_s.Emma);
        await _s.AbsagenAsync(emma);
        await _s.ZustellenAsync();
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));

        Assert.DoesNotContain(await KarteAsync(_s.Miriam), p => p.GetProperty("personId").GetGuid() == _s.Emma);
    }

    [Fact]
    public async Task Zwei_Abrufe_am_selben_Tag_ein_Eintrag_je_Person_die_eigene_Person_ohne_Eintrag()
    {
        await BestaetigtAsync(_s.Emma);

        await KarteAsync(_s.Tobias);
        var karte = await KarteAsync(_s.Tobias);

        Assert.Contains(karte, p => p.GetProperty("personId").GetGuid() == _s.TobiasId);
        Assert.Equal(1L, await ProtokollAsync(_s.TobiasId, _s.Emma));
        Assert.Equal(1L, await ProtokollAsync(_s.TobiasId));
        Assert.Equal(0L, await WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @t", ("t", _s.TobiasId)));
    }

    [Fact]
    public async Task Die_Antwort_traegt_Cache_Control_no_store()
    {
        using var antwort = await _s.Miriam.GetAsync(Pfad(_s.X), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.True(antwort.Headers.CacheControl?.NoStore, "Cache-Control: no-store fehlt");
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
            using var antwort = await client.GetAsync(Pfad(freizeit), Abbruch);
            status.Add(antwort.StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized], status);
        Assert.Equal(0L, await ProtokollGesamtAsync());
    }

    [Fact]
    public async Task Teilnehmer_Zugang_QR_Gast_hat_keinen_Zugriff()
    {
        await BestaetigtAsync(_s.Emma);
        var modul = await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/module", Abbruch));
        using (var schalten = await SendenAsync(
                   _s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{_s.X}/module",
                   $$"""{"version":{{modul.GetProperty("version").GetUInt32()}},"alterWert":"false","teilnehmerZugang":"true"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, schalten.StatusCode);
        }

        using var erneuern = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/qr-zugang/erneuern");
        var adresse = (await JsonAsync(erneuern)).GetProperty("adresse").GetString()!;
        using var gast = _s.Factory.CreateClient();
        using (var einloesen = await PostAsync(gast, "/api/teilnehmer/einloesen", new { token = adresse[(adresse.LastIndexOf('/') + 1)..] }))
        {
            Assert.Equal(HttpStatusCode.NoContent, einloesen.StatusCode);
        }

        using var antwort = await gast.GetAsync(Pfad(_s.X), Abbruch);

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
        Assert.Equal(0L, await ProtokollGesamtAsync());
    }

    [Fact]
    public async Task Die_Rolle_kommt_aus_der_Rollenliste_von_Personen_ohne_Zeile_dort_404()
    {
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM personen.freizeit_rolle WHERE \"PersonId\" = @m", ("m", _s.MiriamId));

        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(Pfad(_s.X), Abbruch)).StatusCode);
    }
}
