using System.Net;
using System.Text.Json;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-6, Freizeitpass-Stand (FR-25, FR-27, AD-8, AD-30) gegen echtes PostgreSQL: <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass-stand</c>
/// zählt je Person der Freizeit die gefragten und die offenen Angaben ihrer jüngsten aktiven Freigabe (unbeantwortet oder älter als 12 Monate),
/// ohne Antworten zu lesen und ohne Einsicht-Protokoll; nur Personen mit offenen Angaben. Dazu die Pflichtfälle der Freizeit-Route.
/// </summary>
public sealed class FreizeitpassStandTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private async Task<List<JsonElement>> StandAsync(HttpClient? client = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(StandPfad(_s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["personen"], Namen(json));
        return [.. json.GetProperty("personen").EnumerateArray()];
    }

    private static string Zeile(JsonElement person) =>
        $"{person.GetProperty("vorname").GetString()} {person.GetProperty("nachname").GetString()} {person.GetProperty("gefragt").GetInt32()}/{person.GetProperty("offen").GetInt32()}";

    [Fact]
    public async Task Matrix_Stand_Emma_offen_1_von_3_Mia_3_von_3_Tobias_ohne_Fragen_fehlt_ohne_Protokoll_und_ohne_Antworttexte()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        await _s.KindAnmeldenAsync(_s.Mia);
        var protokollVorher = await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll");

        using var antwort = await _s.Miriam.GetAsync(StandPfad(_s.X), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var text = await antwort.Content.ReadAsStringAsync(Abbruch);
        var personen = JsonDocument.Parse(text).RootElement.GetProperty("personen").EnumerateArray().ToList();
        Assert.Equal(["Emma Muster 3/1", "Mia Muster 3/3"], personen.Select(Zeile));
        Assert.Equal(["erinnertAm", "gefragt", "nachname", "offen", "personId", "vorname"], Namen(personen[0]));
        Assert.Equal((_s.Emma, JsonValueKind.Null), (personen[0].GetProperty("personId").GetGuid(), personen[0].GetProperty("erinnertAm").ValueKind));
        Assert.DoesNotContain("milch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("allergien", text, StringComparison.Ordinal);
        Assert.Equal(protokollVorher, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll"));
    }

    [Fact]
    public async Task Vollstaendig_beantwortet_fehlt_die_Person_eine_nicht_gefragte_Antwort_zaehlt_nicht()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        // Krankheiten ist beantwortet, aber nicht gefragt: Sie zählt weder bei „gefragt“ noch bei „offen“.
        Assert.Equal(["Emma Muster 3/1"], (await StandAsync()).Select(Zeile));

        await _s.AntwortAsync(_s.Emma, "schwimmen", "ja");

        Assert.Empty(await StandAsync());
    }

    [Fact]
    public async Task Eine_geleerte_Antwort_bleibt_offen_eine_Antwort_nur_mit_Sonstiges_gilt_als_beantwortet()
    {
        await _s.KindAnmeldenAsync(_s.Mia);
        await _s.AntwortAsync(_s.Mia, "schwimmen", "ja");
        Assert.Equal(["Mia Muster 3/2"], (await StandAsync()).Select(Zeile));

        var schwimmen = (await JsonAsync(await _s.Katrin.GetAsync($"/api/ich/freizeitpass/{_s.Mia}", Abbruch))).GetProperty("fragen").EnumerateArray()
            .Single(f => f.GetProperty("kennung").GetString() == "schwimmen");
        using (var leeren = await SendenAsync(
                   _s.Katrin, HttpMethod.Patch, $"/api/ich/freizeitpass/{_s.Mia}/antworten/schwimmen",
                   new Dictionary<string, object?> { ["version"] = schwimmen.GetProperty("version").GetUInt32(), ["alterWert"] = "ja", ["wert"] = "" }))
        {
            Assert.Equal(HttpStatusCode.OK, leeren.StatusCode);
        }

        // Die Zeile bleibt, aber ohne Wert und ohne „Sonstiges“: offen.
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung,
            "SELECT count(*) FROM personen.freizeitpass_antwort WHERE \"PersonId\" = @p AND \"Kennung\" = 'schwimmen' AND \"Wert\" IS NULL AND \"Sonstiges\" IS NULL",
            ("p", _s.Mia)));
        Assert.Equal(["Mia Muster 3/3"], (await StandAsync()).Select(Zeile));

        using (var sonstiges = await SendenAsync(
                   _s.Katrin, HttpMethod.Patch, $"/api/ich/freizeitpass/{_s.Mia}/antworten/allergien",
                   new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["sonstiges"] = "Kiwi" }))
        {
            Assert.Equal(HttpStatusCode.OK, sonstiges.StatusCode);
        }

        Assert.Equal(["Mia Muster 3/2"], (await StandAsync()).Select(Zeile));
    }

    [Fact]
    public async Task Erinnert_am_gilt_nur_fuer_diese_Freizeit()
    {
        await _s.KindAnmeldenAsync(_s.Mia);
        await _s.KindAnmeldenAsync(_s.Mia, _s.TeilnehmendeZ);
        using (var erinnern = await PostAsync(_s.Miriam, ErinnernPfad(_s.Z), new Zeltlotse.Api.Contracts.Personen.FreizeitpassErinnerungSenden(_s.Mia)))
        {
            Assert.Equal(HttpStatusCode.OK, erinnern.StatusCode);
        }

        var mia = Assert.Single(await StandAsync());
        Assert.Equal(("Mia Muster 3/3", JsonValueKind.Null), (Zeile(mia), mia.GetProperty("erinnertAm").ValueKind));
        using var z = await _s.Miriam.GetAsync(StandPfad(_s.Z), Abbruch);
        Assert.Equal(JsonValueKind.String, (await JsonAsync(z)).GetProperty("personen")[0].GetProperty("erinnertAm").ValueKind);
    }

    [Fact]
    public async Task Veraltet_Antworten_aelter_als_12_Monate_zaehlen_als_offen_solange_die_Freigabe_aktiv_ist()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        await _s.AntwortAsync(_s.Emma, "schwimmen", "ja");
        Assert.Empty(await StandAsync());
        var jetzt = _s.Factory.Uhr.GetUtcNow();

        // Genau 12 Monate alt gilt noch; einen Tag älter ist „Stimmt das noch?“ und zählt als offen (wie bei den Eltern).
        await AusfuehrenAsync(
            _s.Verbindung, "UPDATE personen.freizeitpass_antwort SET \"BestaetigtAm\" = @t WHERE \"PersonId\" = @p", ("t", jetzt.AddMonths(-12)), ("p", _s.Emma));
        Assert.Empty(await StandAsync());
        await AusfuehrenAsync(
            _s.Verbindung, "UPDATE personen.freizeitpass_antwort SET \"BestaetigtAm\" = @t WHERE \"PersonId\" = @p AND \"Kennung\" <> 'schwimmen'",
            ("t", jetzt.AddMonths(-12).AddDays(-1)), ("p", _s.Emma));

        Assert.Equal(["Emma Muster 3/2"], (await StandAsync()).Select(Zeile));
    }

    [Fact]
    public async Task Fenster_beendet_nach_einer_Absage_oder_nach_dem_Ende_des_Freigabefensters_fehlt_die_Person()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var mia = await _s.KindAnmeldenAsync(_s.Mia);
        await _s.AbsagenAsync(mia);
        await _s.ZustellenAsync();

        Assert.Equal(["Emma Muster 3/1"], (await StandAsync()).Select(Zeile));

        _s.Factory.Uhr.SetUtcNow(FreigabeEnde);
        // Die Sitzung ist nach über 30 Tagen abgelaufen.
        await AnmeldenAsync(_s.Factory, _s.Miriam, FreizeitHelfer.Miriam);

        Assert.Empty(await StandAsync());
    }

    [Fact]
    public async Task Eine_bestaetigte_Anmeldung_zaehlt_wie_eine_eingegangene_und_massgeblich_ist_die_juengste_Freigabe()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        await _s.BestaetigenAsync(emma);
        await _s.ZustellenAsync();
        Assert.Equal(["Emma Muster 3/1"], (await StandAsync()).Select(Zeile));

        // Eine ältere, beendete Freigabe derselben Freizeit mit anderen Fragen zählt nicht.
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO personen.freigabe (\"Id\", \"OrganisationId\", \"PersonId\", \"FreizeitId\", \"AnmeldungId\", \"Stufe\", \"GueltigBis\", \"AngelegtAm\", \"Fragen\") " +
            "VALUES (@id, @o, @p, @f, @a, 1, @bis, @am, ARRAY['medikamente'])",
            ("id", Guid.CreateVersion7()), ("o", _s.A), ("p", _s.Emma), ("f", _s.X), ("a", Guid.CreateVersion7()), ("bis", _s.Factory.Uhr.GetUtcNow().AddDays(-1)), ("am", _s.Factory.Uhr.GetUtcNow().AddYears(-1)));

        Assert.Equal(["Emma Muster 3/1"], (await StandAsync()).Select(Zeile));
    }

    [Fact]
    public async Task Sortiert_nach_Nachname_dann_Vorname_ohne_die_anfragende_Person_und_ohne_Personen_ohne_Profil()
    {
        var anna = await FamilienHelfer.KindAsync(_s.Katrin, "Anna", nachname: "Zimmer");
        await _s.KindAnmeldenAsync(anna);
        await _s.KindAnmeldenAsync(_s.Mia);
        await _s.KindAnmeldenAsync(_s.Emma);
        // Eine aktive Freigabe der anfragenden Leitung selbst und eine ohne Profil zählen nicht.
        foreach (var person in new[] { _s.MiriamId, Guid.CreateVersion7() })
        {
            await AusfuehrenAsync(
                _s.Verbindung,
                "INSERT INTO personen.freigabe (\"Id\", \"OrganisationId\", \"PersonId\", \"FreizeitId\", \"AnmeldungId\", \"Stufe\", \"GueltigBis\", \"AngelegtAm\", \"Fragen\") " +
                "VALUES (@id, @o, @p, @f, @a, 1, @bis, @am, ARRAY['allergien'])",
                ("id", Guid.CreateVersion7()), ("o", _s.A), ("p", person), ("f", _s.X), ("a", Guid.CreateVersion7()), ("bis", FreigabeEnde), ("am", _s.Factory.Uhr.GetUtcNow()));
        }

        Assert.Equal(["Emma Muster 3/1", "Mia Muster 3/3", "Anna Zimmer 3/3"], (await StandAsync()).Select(Zeile));
    }

    [Fact]
    public async Task Rechte_Leitung_200_Team_403_Org_Admin_ohne_Rolle_404_andere_Freizeit_404_fremde_Organisation_404()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var andere = await FreizeitHelfer.AnlegenAsync(_s.Andrea, _s.A, "Herbstfreizeit");
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var koerper = new Zeltlotse.Api.Contracts.Personen.FreizeitpassErinnerungSenden(_s.Emma);

        var status = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, andere), (_s.Miriam, fremde) })
        {
            using var stand = await client.GetAsync(StandPfad(freizeit), Abbruch);
            status.Add(stand.StatusCode);
        }

        foreach (var (client, freizeit) in new[] { (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, andere), (_s.Miriam, fremde) })
        {
            using var erinnern = await PostAsync(client, ErinnernPfad(freizeit), koerper);
            status.Add(erinnern.StatusCode);
        }

        Assert.Equal(
            [
                HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
                HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
            ],
            status);
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.freizeitpass_erinnerung"));
    }

    [Fact]
    public async Task Leitung_darf_Anmeldungen_bearbeiten_und_die_Rollenlisten_von_Erinnern_und_Anmeldungen_sind_gleich()
    {
        Assert.Equal(
            FreizeitpassEinsichtTests.Liste("Zeltlotse.Freizeiten", "Zeltlotse.Freizeiten.Rechte.FreizeitRechte", "Anmeldungen"),
            FreizeitpassEinsichtTests.Liste("Zeltlotse.Personen", "Zeltlotse.Personen.Freizeitpass.FreizeitpassRechte", "Erinnern"));
        var erlaubt = (await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/kontext", Abbruch))).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString());
        Assert.Contains(Zeltlotse.Api.Contracts.Gemeinsam.Aktionsschluessel.AnmeldungenBearbeiten, erlaubt);
    }
}
