using System.Net;
using System.Reflection;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-5, Freizeitpass im Freizeit-Kontext (FR-24, AD-8, AD-12, AD-30) gegen echtes PostgreSQL:
/// <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass/{personId}</c> für Leitung und Team mit den drei Zuständen, nur den Fragen der
/// Freigabe, dem Einsicht-Protokoll (höchstens einmal am Tag, nicht beim eigenen Pass) und den Fällen <c>404</c>.
/// </summary>
public sealed class FreizeitpassEinsichtTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    [Fact]
    public async Task Leitung_sieht_vor_der_Bestaetigung_genau_die_Fragen_der_Freigabe_und_Katrin_sieht_den_Eintrag_im_Zugriffsprotokoll()
    {
        await _s.KindAnmeldenAsync(_s.Emma);

        using var antwort = await _s.Miriam.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var pass = await JsonAsync(antwort);
        Assert.Equal(["beendetAm", "einsichtBis", "fragen", "nachname", "personId", "vorname", "zustand"], Namen(pass));
        Assert.Equal(("sichtbar", "Emma", "Muster"), (pass.GetProperty("zustand").GetString(), pass.GetProperty("vorname").GetString(), pass.GetProperty("nachname").GetString()));
        Assert.Equal(FreigabeEnde, pass.GetProperty("einsichtBis").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, pass.GetProperty("beendetAm").ValueKind);
        // Krankheiten hat Katrin beantwortet, die Freizeit fragt sie aber nicht ab; Schwimmen ist abgefragt, aber unbeantwortet.
        Assert.Equal(["allergien", "notfallkontakte", "schwimmen"], EinsichtJson.Kennungen(pass));
        var fragen = pass.GetProperty("fragen").EnumerateArray().ToList();
        Assert.Equal(["milch", NotfallkontaktEmma, null], fragen.Select(f => f.GetProperty("wert").GetString()));
        Assert.DoesNotContain("Asthma", pass.GetRawText(), StringComparison.Ordinal);

        // Ein zweiter Abruf am selben Tag ergibt keinen zweiten Eintrag.
        (await _s.Miriam.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch)).Dispose();
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung,
            "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @e AND \"LesendePersonId\" = @m AND \"Zweck\" = 2 AND \"FreizeitId\" = @f",
            ("e", _s.Emma), ("m", _s.MiriamId), ("f", _s.X)));
        var protokoll = (await JsonAsync(await _s.Katrin.GetAsync("/api/ich/zugriffsprotokoll", Abbruch))).EnumerateArray().ToList();
        var eintrag = Assert.Single(protokoll, e => e.GetProperty("zweck").GetString() == "freizeitpassEingesehen");
        Assert.Equal(("Emma Muster", "Miriam Zeltner", _s.X), (
            eintrag.GetProperty("betroffenerName").GetString(), eintrag.GetProperty("lesenderName").GetString(), eintrag.GetProperty("freizeitId").GetGuid()));
    }

    [Fact]
    public async Task Team_sieht_vor_der_Bestaetigung_ab_Bestaetigung_ohne_Protokoll_und_danach_die_Angaben()
    {
        var mia = await _s.KindAnmeldenAsync(_s.Mia);
        await _s.AntwortAsync(_s.Mia, "schwimmen", "nein");

        using (var vorher = await _s.Tobias.GetAsync(PassPfad(_s.X, _s.Mia), Abbruch))
        {
            Assert.Equal(HttpStatusCode.OK, vorher.StatusCode);
            var pass = await JsonAsync(vorher);
            Assert.Equal("abBestaetigung", pass.GetProperty("zustand").GetString());
            Assert.Empty(pass.GetProperty("fragen").EnumerateArray());
            Assert.Equal(FreigabeEnde, pass.GetProperty("einsichtBis").GetDateTimeOffset());
        }

        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"LesendePersonId\" = @t", ("t", _s.TobiasId)));

        await _s.BestaetigenAsync(mia);
        await _s.ZustellenAsync();
        (await _s.Tobias.GetAsync(PassPfad(_s.X, _s.Mia), Abbruch)).Dispose();
        using var nachher = await _s.Tobias.GetAsync(PassPfad(_s.X, _s.Mia), Abbruch);

        var sichtbar = await JsonAsync(nachher);
        Assert.Equal("sichtbar", sichtbar.GetProperty("zustand").GetString());
        Assert.Equal(["allergien", "notfallkontakte", "schwimmen"], EinsichtJson.Kennungen(sichtbar));
        Assert.Equal("nein", sichtbar.GetProperty("fragen")[2].GetProperty("wert").GetString());
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"LesendePersonId\" = @t AND \"BetroffenePersonId\" = @m AND \"Zweck\" = 2",
            ("t", _s.TobiasId), ("m", _s.Mia)));
    }

    [Fact]
    public async Task Nach_dem_Ende_des_Freigabefensters_ist_die_Einsicht_beendet_ohne_Fragen_und_ohne_Protokoll()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        _s.Factory.Uhr.SetUtcNow(FreigabeEnde.AddMinutes(1));
        // Die Sitzung ist nach fast einem Jahr abgelaufen; Miriam meldet sich neu an.
        await AnmeldenAsync(_s.Factory, _s.Miriam, FreizeitHelfer.Miriam);

        using var antwort = await _s.Miriam.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var pass = await JsonAsync(antwort);
        Assert.Equal("beendet", pass.GetProperty("zustand").GetString());
        Assert.Equal(FreigabeEnde, pass.GetProperty("beendetAm").GetDateTimeOffset());
        Assert.Empty(pass.GetProperty("fragen").EnumerateArray());
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"Zweck\" = 2"));
    }

    [Fact]
    public async Task Nach_einer_Absage_ist_die_Einsicht_zum_Zeitpunkt_der_Absage_beendet()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var absage = _s.VolleSekunde();
        await _s.AbsagenAsync(emma);
        await _s.ZustellenAsync();
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));

        var pass = await JsonAsync(await _s.Miriam.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch));

        Assert.Equal(("beendet", absage, absage), (
            pass.GetProperty("zustand").GetString(), pass.GetProperty("beendetAm").GetDateTimeOffset(), pass.GetProperty("einsichtBis").GetDateTimeOffset()));
        Assert.Empty(pass.GetProperty("fragen").EnumerateArray());
    }

    [Fact]
    public async Task Eine_Person_ohne_Freigabe_fuer_diese_Freizeit_und_eine_unbekannte_Person_ergeben_404()
    {
        // Emma ist nur in Z angemeldet; Miriam ist Leitung beider Freizeiten.
        await _s.KindAnmeldenAsync(_s.Emma, _s.TeilnehmendeZ);
        Assert.Equal(HttpStatusCode.OK, (await _s.Miriam.GetAsync(PassPfad(_s.Z, _s.Emma), Abbruch)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(PassPfad(_s.X, Guid.CreateVersion7()), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Tobias.GetAsync(PassPfad(_s.Z, _s.Emma), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Org_Admin_ohne_Rolle_fremde_Organisation_Global_Admin_und_Eltern_bekommen_404_ohne_Sitzung_401()
    {
        await _s.KindAnmeldenAsync(_s.Emma);

        foreach (var client in new[] { _s.Andrea, _s.Lea, _s.Gerd, _s.Katrin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Anonym.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch)).StatusCode);
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"Zweck\" = 2"));
    }

    [Fact]
    public async Task Die_Rolle_kommt_aus_der_Rollenliste_von_Personen_ohne_Zeile_dort_404()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM personen.freizeit_rolle WHERE \"PersonId\" = @m", ("m", _s.MiriamId));

        // Der Host lässt Miriam als Leitung (Freizeiten) durch; Personen kennt die Rolle nicht und gibt nichts heraus.
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Der_eigene_Pass_kommt_ohne_Eintrag_ins_Zugriffsprotokoll()
    {
        using var antwort = await _s.Tobias.GetAsync(PassPfad(_s.X, _s.TobiasId), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("sichtbar", (await JsonAsync(antwort)).GetProperty("zustand").GetString());
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"Zweck\" = 2"));
    }

    [Fact]
    public async Task Massgeblich_ist_die_Freigabe_mit_dem_juengsten_Anlegen()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO personen.freigabe (\"Id\", \"OrganisationId\", \"PersonId\", \"FreizeitId\", \"AnmeldungId\", \"Stufe\", \"GueltigBis\", \"AngelegtAm\", \"Fragen\") " +
            "VALUES (@id, @o, @p, @f, @a, 2, @bis, @am, ARRAY['krankheiten'])",
            ("id", Guid.CreateVersion7()), ("o", _s.A), ("p", _s.Emma), ("f", _s.X), ("a", Guid.CreateVersion7()), ("bis", FreigabeEnde), ("am", DateTimeOffset.UtcNow.AddYears(-1)));

        var pass = await JsonAsync(await _s.Miriam.GetAsync(PassPfad(_s.X, _s.Emma), Abbruch));

        Assert.Equal(["allergien", "notfallkontakte", "schwimmen"], EinsichtJson.Kennungen(pass));
    }

    [Fact]
    public async Task Leitung_und_Team_duerfen_freizeitpaesse_sehen_die_Rollenlisten_von_Freizeiten_und_Personen_sind_gleich()
    {
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            var erlaubt = (await JsonAsync(await client.GetAsync($"/api/freizeiten/{_s.X}/kontext", Abbruch))).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString());
            Assert.Contains(Aktionsschluessel.FreizeitpaesseSehen, erlaubt);
        }

        Assert.Equal("freizeitpaesse-sehen", Aktionsschluessel.FreizeitpaesseSehen);
        Assert.Equal(Liste("Zeltlotse.Freizeiten", "Zeltlotse.Freizeiten.Rechte.FreizeitRechte", "Freizeitpaesse"), Liste("Zeltlotse.Personen", "Zeltlotse.Personen.Freizeitpass.FreizeitpassRechte", "Einsicht"));
    }

    internal static IEnumerable<object> Liste(string assembly, string typ, string feld)
    {
        var werte = (Array)Assembly.Load(assembly).GetType(typ, throwOnError: true)!.GetField(feld, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return werte.Cast<object>().Order();
    }
}
