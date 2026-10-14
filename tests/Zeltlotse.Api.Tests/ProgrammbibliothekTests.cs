using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using OrgRolle = Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-6, Programmbibliothek (CAP-24, FR-41, AD-6, AD-16, AD-30) gegen echtes PostgreSQL: Ablegen als Momentaufnahme ohne Status und Personen
/// (auch wiederholt und gleichzeitig genau ein Eintrag), keine Mahlzeiten, Lesen nur in der eigenen Organisation (auch nach dem Abschluss),
/// Übernehmen als neuer Punkt mit neuem Material „gebraucht“ und freien Aufgaben (mit Feldfehlern), die abgeschlossene Freizeit, die Org-Ansicht mit
/// Entfernen, gelöschter Punkt und Lösch-Lauf (Eintrag bleibt) sowie die Pflichtfälle beider Routenarten. Freizeit X (Organisation A, Org-Admin
/// Andrea, Leitung Miriam, Team Tobias) geht vom 02. bis 13.08.2027, Freizeit Z gehört Organisation B (Org-Admin Lea, Leitung Miriam).
/// </summary>
public sealed class ProgrammbibliothekTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string ProgrammPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/programm";

    private static string AblegenPfad(Guid freizeit, Guid punkt) => $"{ProgrammPfad(freizeit)}/punkte/{punkt}/bibliothek";

    private static string BibliothekPfad(Guid freizeit) => $"{ProgrammPfad(freizeit)}/bibliothek";

    private static string UebernehmenPfad(Guid freizeit, Guid eintrag) => $"{BibliothekPfad(freizeit)}/{eintrag}/uebernehmen";

    private static string OrgPfad(Guid org) => $"/api/organisationen/{org}/programmbibliothek";

    /// <summary>Legt einen Programmpunkt an und liefert seine Kennung.</summary>
    private async Task<Guid> PunktAsync(string titel, Guid? freizeit = null, string tag = "2027-08-04")
    {
        using var antwort = await PostAsync(_s.Miriam, $"{ProgrammPfad(freizeit ?? _s.X)}/punkte", new { titel, tag, beginn = "14:00" });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return Id(await JsonAsync(antwort));
    }

    private async Task<Guid> MaterialAsync(Guid punkt, string bezeichnung, string? menge = null)
    {
        using var antwort = await PostAsync(_s.Miriam, $"{ProgrammPfad(_s.X)}/punkte/{punkt}/material", new { bezeichnung, menge });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return Id(await JsonAsync(antwort));
    }

    private async Task<Guid> AufgabeAsync(Guid punkt, string titel, string phase)
    {
        using var antwort = await PostAsync(_s.Miriam, $"{ProgrammPfad(_s.X)}/aufgaben", new { titel, phase, programmpunktId = punkt });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return Id(await JsonAsync(antwort));
    }

    /// <summary>
    /// „Geländespiel“ in X: Seile „4 × 10 m“ eingepackt, Fahnen (Tobias bringt sie mit), Aufgabe „Fahnen verstecken“ (Während, von Tobias übernommen
    /// und erledigt).
    /// </summary>
    private async Task<Guid> GelaendespielAsync()
    {
        var punkt = await PunktAsync("Geländespiel");
        var seile = await MaterialAsync(punkt, "Seile", "4 × 10 m");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_s.Miriam, $"{ProgrammPfad(_s.X)}/material/{seile}/status", new { status = "Eingepackt" })).StatusCode);
        var fahnen = await MaterialAsync(punkt, "Fahnen");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_s.Tobias, $"{ProgrammPfad(_s.X)}/material/{fahnen}/mitbringen")).StatusCode);
        var aufgabe = await AufgabeAsync(punkt, "Fahnen verstecken", "Waehrend");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_s.Tobias, $"{ProgrammPfad(_s.X)}/aufgaben/{aufgabe}/uebernehmen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_s.Tobias, $"{ProgrammPfad(_s.X)}/aufgaben/{aufgabe}/erledigt")).StatusCode);
        return punkt;
    }

    private Task<HttpResponseMessage> AblegenAsync(Guid punkt, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, AblegenPfad(freizeit ?? _s.X, punkt));

    private async Task<List<JsonElement>> BibliothekAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(BibliothekPfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    private async Task<List<JsonElement>> OrgBibliothekAsync(HttpClient? client = null, Guid? org = null)
    {
        using var antwort = await (client ?? _s.Andrea).GetAsync(OrgPfad(org ?? _s.A), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    /// <summary>Legt einen Punkt ab und liefert die Kennung des (einzigen) Eintrags mit diesem Titel.</summary>
    private async Task<Guid> EintragAsync(Guid punkt, string titel, Guid? freizeit = null)
    {
        using var antwort = await AblegenAsync(punkt, freizeit: freizeit);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return Id(Assert.Single(await BibliothekAsync(freizeit: freizeit), e => Text(e, "titel") == titel));
    }

    private Task<HttpResponseMessage> UebernehmenAsync(Guid eintrag, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, UebernehmenPfad(freizeit ?? _s.X, eintrag), koerper);

    private async Task<List<JsonElement>> PunkteAsync(Guid? freizeit = null)
    {
        using var antwort = await _s.Miriam.GetAsync(ProgrammPfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).GetProperty("punkte").EnumerateArray()];
    }

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM programm.{tabelle} WHERE {bedingung}", parameter);

    private Task GelaufenAsync(Guid freizeit) =>
        AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", freizeit));

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static string? Text(JsonElement element, string feld) => element.GetProperty(feld).GetString();

    private static bool Wahr(JsonElement element, string feld) => element.GetProperty(feld).GetBoolean();

    private static async Task<IEnumerable<string>> FehlerFelderAsync(HttpResponseMessage antwort) =>
        [.. (await ProblemAsync(antwort)).GetProperty("errors").EnumerateObject().Select(f => f.Name)];

    // ---------- Ablegen ----------

    [Fact]
    public async Task Ablegen_Tobias_legt_Titel_Material_und_Aufgaben_ohne_Status_und_Personen_ab_ein_zweites_Mal_nichts_Neues()
    {
        var punkt = await GelaendespielAsync();

        using var erste = await AblegenAsync(punkt, _s.Tobias);
        using var zweite = await AblegenAsync(punkt, _s.Tobias);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (erste.StatusCode, zweite.StatusCode));
        var json = await JsonAsync(erste);
        Assert.Equal((punkt, "Geländespiel", true), (Id(json), Text(json, "titel"), Wahr(json, "inBibliothek")));
        Assert.True(Wahr(await JsonAsync(zweite), "inBibliothek"));
        Assert.Equal(1L, await AnzahlAsync("bibliotheks_eintrag"));
        var eintrag = Assert.Single(await BibliothekAsync());
        Assert.Equal(["abgelegtAm", "aufgaben", "id", "material", "titel"], Namen(eintrag));
        Assert.Equal("Geländespiel", Text(eintrag, "titel"));
        Assert.Equal(_s.Factory.Uhr.GetUtcNow(), eintrag.GetProperty("abgelegtAm").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));
        var material = eintrag.GetProperty("material").EnumerateArray().ToList();
        Assert.All(material, m => Assert.Equal(["bezeichnung", "menge"], Namen(m)));
        Assert.Equal([("Fahnen", (string?)null), ("Seile", "4 × 10 m")], material.Select(m => (Text(m, "bezeichnung"), Text(m, "menge"))));
        var aufgabe = Assert.Single(eintrag.GetProperty("aufgaben").EnumerateArray());
        Assert.Equal(["phase", "titel"], Namen(aufgabe));
        Assert.Equal(("Fahnen verstecken", "Waehrend"), (Text(aufgabe, "titel"), Text(aufgabe, "phase")));
        Assert.DoesNotContain(_s.TobiasId.ToString(), eintrag.GetRawText(), StringComparison.Ordinal);
        Assert.True(Wahr(Assert.Single(await PunkteAsync(), p => Id(p) == punkt), "inBibliothek"));
        Assert.All((await PunkteAsync()).Where(p => Id(p) != punkt), p => Assert.False(Wahr(p, "inBibliothek")));
    }

    [Fact]
    public async Task Gleichzeitiges_Ablegen_legt_genau_einen_Eintrag_an()
    {
        var punkt = await GelaendespielAsync();

        var antworten = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => AblegenAsync(punkt, i % 2 == 0 ? _s.Miriam : _s.Tobias)));

        foreach (var antwort in antworten)
        {
            using (antwort)
            {
                Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            }
        }

        Assert.Equal(1L, await AnzahlAsync("bibliotheks_eintrag"));
    }

    [Fact]
    public async Task Mahlzeit_ablegen_409_konflikt_kein_Eintrag()
    {
        var fruehstueck = Id((await PunkteAsync()).First(p => Text(p, "mahlzeit") == "Fruehstueck"));

        using var antwort = await AblegenAsync(fruehstueck);

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        Assert.Equal(0L, await AnzahlAsync("bibliotheks_eintrag"));
    }

    // ---------- Lesen ----------

    [Fact]
    public async Task Bibliothek_ist_leer_obwohl_die_Freizeit_Programmpunkte_hat_und_Z_in_Organisation_B_sieht_die_Eintraege_von_A_nicht()
    {
        var punkt = await GelaendespielAsync();
        await PunktAsync("Lagerfeuer");
        Assert.Empty(await BibliothekAsync());

        await EintragAsync(punkt, "Geländespiel");

        Assert.Single(await BibliothekAsync(_s.Tobias));
        Assert.Empty(await BibliothekAsync(freizeit: _s.Z));
        Assert.Empty(await OrgBibliothekAsync(_s.Lea, _s.B));
    }

    [Fact]
    public async Task Sortierung_nach_Titel_dann_die_neueste_Fassung_zuerst()
    {
        var erstes = await PunktAsync("Lagerfeuer");
        await AblegenAsync(erstes);
        _s.Factory.Uhr.Advance(TimeSpan.FromDays(1));
        var zweites = await PunktAsync("lagerfeuer", tag: "2027-08-05");
        await AblegenAsync(zweites);
        var drittes = await PunktAsync("Geländespiel");
        await AblegenAsync(drittes);

        var eintraege = await BibliothekAsync();

        Assert.Equal(["Geländespiel", "lagerfeuer", "Lagerfeuer"], eintraege.Select(e => Text(e, "titel")));
        Assert.Equal(eintraege.Select(Id), (await OrgBibliothekAsync()).Select(Id));
    }

    // ---------- Übernehmen ----------

    [Fact]
    public async Task Uebernehmen_legt_einen_neuen_freien_Punkt_mit_Material_gebraucht_und_offenen_Aufgaben_an_ohne_Verknuepfung()
    {
        var eintrag = await EintragAsync(await GelaendespielAsync(), "Geländespiel");

        using var antwort = await UebernehmenAsync(eintrag, new { tag = "2027-08-05", beginn = "10:00" });

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(ProgrammPfad(_s.X), antwort.Headers.Location?.OriginalString);
        var neu = await JsonAsync(antwort);
        var punkt = Id(neu);
        Assert.Equal(
            ("Geländespiel", "2027-08-05", "10:00", JsonValueKind.Null, false, false),
            (Text(neu, "titel"), Text(neu, "tag"), Text(neu, "beginn"), neu.GetProperty("uebernommenVon").ValueKind, Wahr(neu, "meins"), Wahr(neu, "inBibliothek")));
        using var materialAntwort = await _s.Miriam.GetAsync($"{ProgrammPfad(_s.X)}/material", Abbruch);
        var material = (await JsonAsync(materialAntwort)).GetProperty("material").EnumerateArray().Where(m => m.GetProperty("programmpunktId").GetGuid() == punkt).ToList();
        Assert.Equal(
            [("Fahnen", (string?)null, "Gebraucht", JsonValueKind.Null, JsonValueKind.Null), ("Seile", "4 × 10 m", "Gebraucht", JsonValueKind.Null, JsonValueKind.Null)],
            material.Select(m => (Text(m, "bezeichnung"), Text(m, "menge"), Text(m, "status"), m.GetProperty("bringtMit").ValueKind, m.GetProperty("reservierungId").ValueKind)));
        using var aufgabenAntwort = await _s.Miriam.GetAsync($"{ProgrammPfad(_s.X)}/aufgaben", Abbruch);
        var aufgabe = Assert.Single((await JsonAsync(aufgabenAntwort)).EnumerateArray(), a => a.GetProperty("programmpunktId").ValueKind == JsonValueKind.String && a.GetProperty("programmpunktId").GetGuid() == punkt);
        Assert.Equal(
            ("Fahnen verstecken", "Waehrend", JsonValueKind.Null, false, false),
            (Text(aufgabe, "titel"), Text(aufgabe, "phase"), aufgabe.GetProperty("uebernommenVon").ValueKind, Wahr(aufgabe, "erledigt"), Wahr(aufgabe, "vorlage")));
        Assert.Equal(1L, await AnzahlAsync("bibliotheks_eintrag"));
        Assert.Equal(1L, await AnzahlAsync("programmpunkt", "\"BibliotheksEintragId\" IS NOT NULL"));

        // Der übernommene Punkt ist nicht verknüpft: Ablegen legt eine zweite Fassung an.
        using var ablegen = await AblegenAsync(punkt);
        Assert.Equal(HttpStatusCode.OK, ablegen.StatusCode);
        Assert.Equal(2, (await BibliothekAsync()).Count);
    }

    [Fact]
    public async Task Uebernehmen_ohne_Uhrzeit_und_Feldfehler_tag_fehlt_ausserhalb_beginn_falsch_Eintrag_aus_B_und_unbekannt_404()
    {
        var eintrag = await EintragAsync(await PunktAsync("Lagerfeuer"), "Lagerfeuer");
        var ausB = await EintragAsync(await PunktAsync("Nachtwanderung", _s.Z, "2027-09-02"), "Nachtwanderung", _s.Z);

        using var ohneUhrzeit = await UebernehmenAsync(eintrag, new { tag = "2027-08-13" }, _s.Tobias);
        Assert.Equal(HttpStatusCode.Created, ohneUhrzeit.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(ohneUhrzeit)).GetProperty("beginn").ValueKind);

        foreach (var (koerper, feld) in new (object Koerper, string Feld)[]
                 {
                     (new { beginn = "10:00" }, "tag"),
                     (new { tag = "2027-08-14", beginn = "10:00" }, "tag"),
                     (new { tag = "2027-08-05", beginn = "25:99" }, "beginn"),
                 })
        {
            using var fehler = await UebernehmenAsync(eintrag, koerper);
            Assert.Equal(HttpStatusCode.BadRequest, fehler.StatusCode);
            Assert.Equal([feld], await FehlerFelderAsync(fehler));
        }

        using var fremd = await UebernehmenAsync(ausB, new { tag = "2027-08-05" });
        using var unbekannt = await UebernehmenAsync(Guid.CreateVersion7(), new { tag = "2027-08-05" });
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (fremd.StatusCode, unbekannt.StatusCode));
        Assert.Single(await PunkteAsync(), p => Text(p, "titel") == "Lagerfeuer" && Text(p, "tag") == "2027-08-13");
        Assert.DoesNotContain(await PunkteAsync(), p => Text(p, "titel") == "Nachtwanderung");
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Abgeschlossen_lesen_200_ablegen_und_uebernehmen_409_freizeit_abgeschlossen()
    {
        var eintrag = await EintragAsync(await PunktAsync("Lagerfeuer"), "Lagerfeuer");
        var punkt = await PunktAsync("Geländespiel");
        await GelaufenAsync(_s.X);

        Assert.Single(await BibliothekAsync());
        using var ablegen = await AblegenAsync(punkt);
        using var uebernehmen = await UebernehmenAsync(eintrag, new { tag = "2027-08-05" });

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (ablegen.StatusCode, uebernehmen.StatusCode));
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(ablegen)));
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(uebernehmen)));
        Assert.Equal(1L, await AnzahlAsync("bibliotheks_eintrag"));
    }

    // ---------- Org-Ansicht ----------

    [Fact]
    public async Task Org_Andrea_liest_200_entfernt_204_danach_ist_der_Punkt_nicht_mehr_verknuepft_und_Ablegen_legt_neu_an_unbekannt_und_aus_B_404()
    {
        var punkt = await GelaendespielAsync();
        var eintrag = await EintragAsync(punkt, "Geländespiel");
        var ausB = await EintragAsync(await PunktAsync("Nachtwanderung", _s.Z, "2027-09-02"), "Nachtwanderung", _s.Z);

        var liste = Assert.Single(await OrgBibliothekAsync());
        Assert.Equal((eintrag, 2, 1), (Id(liste), liste.GetProperty("material").GetArrayLength(), liste.GetProperty("aufgaben").GetArrayLength()));

        using var unbekannt = await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{Guid.CreateVersion7()}");
        using var fremd = await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{ausB}");
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (unbekannt.StatusCode, fremd.StatusCode));

        using var entfernt = await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{eintrag}");
        Assert.Equal(HttpStatusCode.NoContent, entfernt.StatusCode);
        Assert.Empty(await OrgBibliothekAsync());
        Assert.False(Wahr(Assert.Single(await PunkteAsync(), p => Id(p) == punkt), "inBibliothek"));
        Assert.Single(await OrgBibliothekAsync(_s.Lea, _s.B));

        var neu = await EintragAsync(punkt, "Geländespiel");
        Assert.NotEqual(eintrag, neu);
    }

    // ---------- Punkt gelöscht, Frist ----------

    [Fact]
    public async Task Wird_der_abgelegte_Programmpunkt_geloescht_bleibt_der_Eintrag()
    {
        var punkt = await GelaendespielAsync();
        await EintragAsync(punkt, "Geländespiel");

        using var geloescht = await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{ProgrammPfad(_s.X)}/punkte/{punkt}");

        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        var eintrag = Assert.Single(await BibliothekAsync());
        Assert.Equal(2, eintrag.GetProperty("material").GetArrayLength());
    }

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Punkte_von_X_der_Eintrag_bleibt()
    {
        var punkt = await GelaendespielAsync();
        await EintragAsync(punkt, "Geländespiel");

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "ProgrammLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("programmpunkt", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await AnzahlAsync("bibliotheks_eintrag"));
        Assert.Equal("Geländespiel", Text(Assert.Single(await OrgBibliothekAsync()), "titel"));
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Rechte_Freizeit_Leitung_und_Team_2xx_Org_Admin_ohne_Rolle_und_fremde_Organisation_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var punkt = await PunktAsync("Lagerfeuer");
        var eintrag = await EintragAsync(await PunktAsync("Geländespiel"), "Geländespiel");
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        var status = new List<string>();
        foreach (var (client, freizeit) in faelle)
        {
            using var ablegen = await AblegenAsync(punkt, client, freizeit);
            using var lesen = await client.GetAsync(BibliothekPfad(freizeit), Abbruch);
            using var uebernehmen = await UebernehmenAsync(eintrag, new { tag = "2027-08-05" }, client, freizeit);
            status.Add($"{(int)ablegen.StatusCode} {(int)lesen.StatusCode} {(int)uebernehmen.StatusCode}");
        }

        Assert.Equal(["200 200 201", "200 200 201", "404 404 404", "404 404 404", "401 401 401"], status);
        Assert.Equal(2, (await PunkteAsync()).Count(p => Text(p, "titel") == "Geländespiel" && Text(p, "tag") == "2027-08-05"));
        Assert.Equal(2, (await BibliothekAsync()).Count);
    }

    [Fact]
    public async Task Rechte_ein_Punkt_oder_Eintrag_einer_anderen_Freizeit_oder_Organisation_ist_unter_X_404_auch_fuer_die_Leitung_beider()
    {
        var inZ = await PunktAsync("Nachtwanderung", _s.Z, "2027-09-02");
        var ausX = await EintragAsync(await PunktAsync("Lagerfeuer"), "Lagerfeuer");

        using var ablegen = await AblegenAsync(inZ);
        using var uebernehmenInZ = await UebernehmenAsync(ausX, new { tag = "2027-09-02" }, freizeit: _s.Z);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (ablegen.StatusCode, uebernehmenInZ.StatusCode));
        Assert.False(Wahr(Assert.Single(await PunkteAsync(_s.Z), p => Id(p) == inZ), "inBibliothek"));
        Assert.Equal(1L, await AnzahlAsync("bibliotheks_eintrag"));
        Assert.DoesNotContain(await PunkteAsync(_s.Z), p => Text(p, "titel") == "Lagerfeuer");
    }

    [Fact]
    public async Task Rechte_Org_Org_Admin_2xx_Org_Ueberblick_403_Org_Admin_von_B_und_ohne_Org_Rolle_404_ohne_Sitzung_401()
    {
        const string ueberblickMail = "vorstand@example.org";
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, ueberblickMail, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, ueberblickMail);
        var eintrag = await EintragAsync(await PunktAsync("Lagerfeuer"), "Lagerfeuer");

        async Task<string> AlleAsync(HttpClient client)
        {
            using var get = await client.GetAsync(OrgPfad(_s.A), Abbruch);
            using var delete = await SendenAsync(client, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{eintrag}");
            return $"{(int)get.StatusCode} {(int)delete.StatusCode}";
        }

        Assert.Equal("403 403", await AlleAsync(ueberblick));
        foreach (var client in new[] { _s.Lea, _s.Miriam, _s.Tobias, _s.Katrin })
        {
            Assert.Equal("404 404", await AlleAsync(client));
        }

        Assert.Equal("401 401", await AlleAsync(_s.Anonym));
        Assert.Equal(1L, await AnzahlAsync("bibliotheks_eintrag"));
        Assert.Equal("200 204", await AlleAsync(_s.Andrea));
        Assert.Equal(0L, await AnzahlAsync("bibliotheks_eintrag"));
    }

    [Fact]
    public async Task Org_Admin_bekommt_programmbibliothek_verwalten_Org_Ueberblick_nicht()
    {
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, OrgHelfer.Jonas, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, OrgHelfer.Jonas);

        static async Task<List<string?>> ErlaubtAsync(HttpClient client, Guid org) =>
            [.. (await JsonAsync(await client.GetAsync($"/api/organisationen/{org}", Abbruch))).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString())];

        Assert.Equal(Aktionsschluessel.ProgrammbibliothekVerwalten, (await ErlaubtAsync(_s.Andrea, _s.A))[^1]);
        Assert.DoesNotContain(Aktionsschluessel.ProgrammbibliothekVerwalten, await ErlaubtAsync(ueberblick, _s.A));
    }
}
