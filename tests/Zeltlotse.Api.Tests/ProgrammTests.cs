using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-1, Wochenraster und Programmpunkte (CAP-19, FR-34, FR-35, AD-16, AD-19, AD-30) gegen echtes PostgreSQL: Laden mit den festen
/// Mahlzeiten (auch gleichzeitig nichts doppelt), Anlegen mit Feldfehlern, Autosave mit Konfliktregel, Löschen (Mahlzeiten nicht), „Ich übernehme“
/// und „Abgeben“ (auch gleichzeitig), abgeschlossene Freizeit, die Pflichtfälle der Freizeit-Route, Löschen mit der Person und nach der Frist
/// sowie die Datenauskunft. Story 6-2: „Geändert, noch nicht mitgeteilt“ (<c>mitgeteilterBeginn</c>), „mitgeteilt“ und dass eine Verschiebung selbst
/// nichts verschickt (AD-13). Freizeit X geht vom 02. bis 13.08.2027 (12 Tage).
/// </summary>
public sealed class ProgrammTests(PostgresContainer postgres) : IAsyncLifetime
{
    private readonly PushAufzeichnung _push = new();
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() =>
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres, zusatzDienste: s => s.Replace(ServiceDescriptor.Singleton<IPushVersand>(_push)));

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/programm";

    private static string PunktPfad(Guid freizeit, Guid punkt) => $"{Pfad(freizeit)}/punkte/{punkt}";

    private async Task<JsonElement> RasterAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(Pfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<List<JsonElement>> PunkteAsync(HttpClient? client = null, Guid? freizeit = null) =>
        [.. (await RasterAsync(client, freizeit)).GetProperty("punkte").EnumerateArray()];

    private Task<HttpResponseMessage> AnlegenAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{Pfad(freizeit ?? _s.X)}/punkte", koerper);

    /// <summary>Legt einen Programmpunkt an und liefert die Antwort.</summary>
    private async Task<JsonElement> NeuAsync(string titel, string tag = "2027-08-02", string? beginn = null, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await AnlegenAsync(new { titel, tag, beginn }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> PatchAsync(Guid punkt, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, PunktPfad(freizeit ?? _s.X, punkt), koerper);

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private Task<HttpResponseMessage> UebergangAsync(HttpClient client, Guid punkt, string uebergang, Guid? freizeit = null) =>
        PostAsync(client, $"{PunktPfad(freizeit ?? _s.X, punkt)}/{uebergang}");

    private Task<HttpResponseMessage> LoeschenAsync(Guid punkt, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, PunktPfad(freizeit ?? _s.X, punkt));

    private Task<long> AnzahlAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM programm.programmpunkt WHERE {bedingung}", parameter);

    private static Guid Id(JsonElement punkt) => punkt.GetProperty("id").GetGuid();

    private static string? Text(JsonElement punkt, string feld) => punkt.GetProperty(feld).GetString();

    private Task<HttpResponseMessage> MitgeteiltAsync(Guid punkt, string? beginn, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{PunktPfad(freizeit ?? _s.X, punkt)}/mitgeteilt", new { beginn });

    /// <summary>Story 6-2: Verschiebt einen Punkt über den Autosave (PATCH der Uhrzeit).</summary>
    private async Task VerschiebenAsync(Guid punkt, string? alt, string? neu, HttpClient? client = null)
    {
        using var antwort = await PatchAsync(punkt, Koerper(0, alt, "beginn", neu), client);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private async Task<string?> MitgeteilterBeginnAsync(Guid punkt, HttpClient? client = null) =>
        Text(Assert.Single(await PunkteAsync(client), p => Id(p) == punkt), "mitgeteilterBeginn");

    private async Task<long> OutboxAsync() =>
        await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM programm.outbox")
        + await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM kanaele.outbox");

    // ---------- Laden ----------

    [Fact]
    public async Task Laden_legt_Fruehstueck_Mittag_und_Abendessen_an_jedem_Tag_an_ein_zweiter_Aufruf_nichts_doppelt()
    {
        var raster = await RasterAsync(_s.Tobias);

        Assert.Equal(["ersterTag", "letzterTag", "punkte"], Namen(raster));
        Assert.Equal(("2027-08-02", "2027-08-13"), (Text(raster, "ersterTag"), Text(raster, "letzterTag")));
        var punkte = raster.GetProperty("punkte").EnumerateArray().ToList();
        Assert.Equal(36, punkte.Count);
        Assert.Equal(["beginn", "id", "inBibliothek", "mahlzeit", "meins", "mitgeteilterBeginn", "tag", "titel", "uebernommenVon", "version"], Namen(punkte[0]));
        Assert.Equal(
            ["2027-08-02 08:00 Frühstück Fruehstueck", "2027-08-02 12:30 Mittagessen Mittagessen", "2027-08-02 18:30 Abendessen Abendessen", "2027-08-03 08:00 Frühstück Fruehstueck"],
            punkte.Take(4).Select(p => $"{Text(p, "tag")} {Text(p, "beginn")} {Text(p, "titel")} {Text(p, "mahlzeit")}"));
        Assert.Equal("2027-08-13", Text(punkte[^1], "tag"));
        Assert.All(punkte, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("uebernommenVon").ValueKind));
        Assert.All(punkte, p => Assert.False(p.GetProperty("meins").GetBoolean()));

        var zweiter = await PunkteAsync();
        Assert.Equal(punkte.Select(Id), zweiter.Select(Id));
        Assert.Equal(36L, await AnzahlAsync());
    }

    [Fact]
    public async Task Gleichzeitige_erste_Aufrufe_legen_jede_Mahlzeit_genau_einmal_an()
    {
        var antworten = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? _s.Miriam : _s.Tobias).GetAsync(Pfad(_s.X), Abbruch)));

        Assert.All(antworten, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
        foreach (var antwort in antworten)
        {
            Assert.Equal(36, (await JsonAsync(antwort)).GetProperty("punkte").GetArrayLength());
            antwort.Dispose();
        }

        Assert.Equal(36L, await AnzahlAsync());
        Assert.Equal(12L, await AnzahlAsync("\"Mahlzeit\" = 2"));
    }

    // ---------- Anlegen ----------

    [Fact]
    public async Task Anlegen_201_mit_Version_getrimmtem_Titel_und_Sortierung_nach_Uhrzeit_ohne_Uhrzeit_zuletzt()
    {
        using var antwort = await AnlegenAsync(new { titel = "  Lagerfeuer ", tag = "2027-08-02", beginn = "20:00" }, _s.Tobias);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var punkt = await JsonAsync(antwort);
        Assert.Equal(Pfad(_s.X), antwort.Headers.Location?.OriginalString);
        Assert.Equal(("Lagerfeuer", "20:00", "2027-08-02"), (Text(punkt, "titel"), Text(punkt, "beginn"), Text(punkt, "tag")));
        Assert.Equal(JsonValueKind.Null, punkt.GetProperty("mahlzeit").ValueKind);
        Assert.Equal(JsonValueKind.Null, punkt.GetProperty("uebernommenVon").ValueKind);
        Assert.NotEqual(0u, punkt.GetProperty("version").GetUInt32());
        await NeuAsync("Abbau");
        await NeuAsync("Anreise und Zeltaufbau", beginn: "15:00");

        var tag = (await PunkteAsync()).Where(p => Text(p, "tag") == "2027-08-02").Select(p => Text(p, "titel"));

        Assert.Equal(["Frühstück", "Mittagessen", "Anreise und Zeltaufbau", "Abendessen", "Lagerfeuer", "Abbau"], tag);
    }

    [Fact]
    public async Task Anlegen_Feldfehler_Titel_leer_oder_zu_lang_Tag_fehlt_oder_ausserhalb_Uhrzeit_falsch_400()
    {
        var faelle = new (object Koerper, string Feld)[]
        {
            (new { titel = "  ", tag = "2027-08-02" }, "titel"),
            (new { titel = new string('a', 121), tag = "2027-08-02" }, "titel"),
            (new { titel = "Lagerfeuer" }, "tag"),
            (new { titel = "Lagerfeuer", tag = "2027-08-14" }, "tag"),
            (new { titel = "Lagerfeuer", tag = "2027-08-01" }, "tag"),
            (new { titel = "Lagerfeuer", tag = "2027-08-02", beginn = "8 Uhr" }, "beginn"),
            (new { titel = "Lagerfeuer", tag = "2027-08-02", beginn = "25:00" }, "beginn"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await AnlegenAsync(koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        Assert.Equal(0L, await AnzahlAsync());
    }

    // ---------- Bearbeiten (Autosave) ----------

    [Fact]
    public async Task Uhrzeit_aendern_200_mit_Version_anderer_Altwert_409_konflikt_anderes_Feld_wird_angewendet()
    {
        var punkt = await NeuAsync("Geländespiel", "2027-08-03", "14:00");
        var alteVersion = punkt.GetProperty("version").GetUInt32();

        using var erste = await PatchAsync(Id(punkt), Koerper(alteVersion, "14:00", "beginn", "14:30"), _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, erste.StatusCode);
        var json = await JsonAsync(erste);
        Assert.Equal(["beginn", "version"], Namen(json));
        Assert.Equal("14:30", Text(json, "beginn"));
        Assert.NotEqual(alteVersion, json.GetProperty("version").GetUInt32());

        using var konflikt = await PatchAsync(Id(punkt), Koerper(alteVersion, "14:00", "beginn", "15:00"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        using var anderesFeld = await PatchAsync(Id(punkt), Koerper(alteVersion, "Geländespiel", "titel", " Geländespiel Dienstag "));
        Assert.Equal(HttpStatusCode.OK, anderesFeld.StatusCode);
        Assert.Equal("Geländespiel Dienstag", Text(await JsonAsync(anderesFeld), "titel"));

        using var leeren = await PatchAsync(Id(punkt), Koerper(0, "14:30", "beginn", ""));
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(leeren)).GetProperty("beginn").ValueKind);
        var gespeichert = Assert.Single(await PunkteAsync(), p => Id(p) == Id(punkt));
        Assert.Equal(("Geländespiel Dienstag", null), (Text(gespeichert, "titel"), Text(gespeichert, "beginn")));
    }

    [Fact]
    public async Task Gleichzeitige_Aenderungen_an_Titel_und_Uhrzeit_gelingen_beide()
    {
        var punkt = await NeuAsync("Geländespiel", "2027-08-03", "14:00");
        var version = punkt.GetProperty("version").GetUInt32();
        var titel = "Geländespiel";
        var beginn = "14:00";

        for (var runde = 0; runde < 5; runde++)
        {
            // Beide Felder kennen dieselbe Version; jedes ändert ein anderes Feld (AD-19: kein Konflikt).
            var neuerTitel = $"Geländespiel {runde}";
            var neuerBeginn = $"14:{runde}0";
            var antworten = await Task.WhenAll(
                Task.Run(() => PatchAsync(Id(punkt), Koerper(version, titel, "titel", neuerTitel))),
                Task.Run(() => PatchAsync(Id(punkt), Koerper(version, beginn, "beginn", neuerBeginn))));
            foreach (var antwort in antworten)
            {
                using (antwort)
                {
                    Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
                }
            }

            (titel, beginn) = (neuerTitel, neuerBeginn);
            var gespeichert = Assert.Single(await PunkteAsync(), p => Id(p) == Id(punkt));
            Assert.Equal((titel, beginn), (Text(gespeichert, "titel"), Text(gespeichert, "beginn")));
            version = gespeichert.GetProperty("version").GetUInt32();
        }
    }

    [Fact]
    public async Task Mahlzeit_Uhrzeit_und_Titel_lassen_sich_aendern()
    {
        var mittag = (await PunkteAsync()).First(p => Text(p, "mahlzeit") == "Mittagessen");

        using var antwort = await PatchAsync(Id(mittag), Koerper(mittag.GetProperty("version").GetUInt32(), "12:30", "beginn", "13:30"));

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var danach = Assert.Single(await PunkteAsync(), p => Id(p) == Id(mittag));
        Assert.Equal(("13:30", "Mittagessen"), (Text(danach, "beginn"), Text(danach, "mahlzeit")));
        // Erneutes Sicherstellen der Mahlzeiten (fehlende Mahlzeit, neues Laden) überschreibt die Änderung nicht.
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM programm.programmpunkt WHERE \"Mahlzeit\" = 3 AND \"Tag\" = '2027-08-13'");
        var neu = await PunkteAsync();
        Assert.Equal(36, neu.Count);
        Assert.Equal("13:30", Text(Assert.Single(neu, p => Id(p) == Id(mittag)), "beginn"));
    }

    [Fact]
    public async Task Bearbeiten_Feldfehler_Titel_leer_Uhrzeit_falsch_zwei_Felder_kein_Feld_unbekanntes_Feld_400()
    {
        var punkt = await NeuAsync("Lagerfeuer", beginn: "20:00");
        var faelle = new (object Koerper, string Feld)[]
        {
            (Koerper(0, "Lagerfeuer", "titel", " "), "titel"),
            (Koerper(0, "Lagerfeuer", "titel", new string('a', 121)), "titel"),
            (Koerper(0, "20:00", "beginn", "20 Uhr"), "beginn"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["titel"] = "a", ["beginn"] = "10:00" }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["tag"] = "2027-08-03" }, "feld"),
            (new Dictionary<string, object?> { ["alterWert"] = "Lagerfeuer", ["titel"] = "Feuer" }, "version"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = 5, ["titel"] = "Feuer" }, "alterWert"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await PatchAsync(Id(punkt), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }
    }

    // ---------- Löschen ----------

    [Fact]
    public async Task Loeschen_204_Mahlzeit_409_konflikt_unbekannt_404()
    {
        var punkt = await NeuAsync("Lagerfeuer");
        var abend = (await PunkteAsync()).First(p => Text(p, "mahlzeit") == "Abendessen");

        using var geloescht = await LoeschenAsync(Id(punkt), _s.Tobias);
        using var mahlzeit = await LoeschenAsync(Id(abend));
        using var unbekannt = await LoeschenAsync(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, mahlzeit.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(mahlzeit)));
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        var punkte = await PunkteAsync();
        Assert.DoesNotContain(punkte, p => Id(p) == Id(punkt));
        Assert.Contains(punkte, p => Id(p) == Id(abend));
    }

    // ---------- Übernehmen und Abgeben ----------

    [Fact]
    public async Task Ich_uebernehme_200_Meins_wiederholt_200_eine_andere_Person_409_Abgeben_nur_selbst()
    {
        var punkt = await NeuAsync("Lagerfeuer", beginn: "20:00");

        using var uebernommen = await UebergangAsync(_s.Tobias, Id(punkt), "uebernehmen");
        Assert.Equal(HttpStatusCode.OK, uebernommen.StatusCode);
        var json = await JsonAsync(uebernommen);
        Assert.Equal((_s.TobiasId, true), (json.GetProperty("uebernommenVon").GetGuid(), json.GetProperty("meins").GetBoolean()));
        Assert.Equal("Lagerfeuer", Text(json, "titel"));
        using var nochmal = await UebergangAsync(_s.Tobias, Id(punkt), "uebernehmen");
        Assert.Equal(HttpStatusCode.OK, nochmal.StatusCode);

        // Die Leitung teilt nichts zu und nimmt nichts weg.
        using var miriam = await UebergangAsync(_s.Miriam, Id(punkt), "uebernehmen");
        Assert.Equal(HttpStatusCode.Conflict, miriam.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(miriam)));
        using var miriamAbgeben = await UebergangAsync(_s.Miriam, Id(punkt), "abgeben");
        Assert.Equal(HttpStatusCode.Conflict, miriamAbgeben.StatusCode);
        var beiMiriam = Assert.Single(await PunkteAsync(_s.Miriam), p => Id(p) == Id(punkt));
        Assert.Equal((_s.TobiasId, false), (beiMiriam.GetProperty("uebernommenVon").GetGuid(), beiMiriam.GetProperty("meins").GetBoolean()));

        using var abgegeben = await UebergangAsync(_s.Tobias, Id(punkt), "abgeben");
        Assert.Equal(HttpStatusCode.OK, abgegeben.StatusCode);
        var frei = await JsonAsync(abgegeben);
        Assert.Equal((JsonValueKind.Null, false), (frei.GetProperty("uebernommenVon").ValueKind, frei.GetProperty("meins").GetBoolean()));
        using var freiAbgeben = await UebergangAsync(_s.Tobias, Id(punkt), "abgeben");
        Assert.Equal(HttpStatusCode.OK, freiAbgeben.StatusCode);
        using var jetztMiriam = await UebergangAsync(_s.Miriam, Id(punkt), "uebernehmen");
        Assert.Equal(HttpStatusCode.OK, jetztMiriam.StatusCode);
    }

    [Fact]
    public async Task Gleichzeitiges_Uebernehmen_genau_einer_gewinnt()
    {
        var punkt = await NeuAsync("Geländespiel", "2027-08-03", "14:30");

        var antworten = await Task.WhenAll(UebergangAsync(_s.Tobias, Id(punkt), "uebernehmen"), UebergangAsync(_s.Miriam, Id(punkt), "uebernehmen"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], antworten.Select(a => a.StatusCode).Order());
        var gewinner = antworten[0].StatusCode == HttpStatusCode.OK ? _s.TobiasId : _s.MiriamId;
        foreach (var antwort in antworten)
        {
            antwort.Dispose();
        }

        Assert.Equal(gewinner, Assert.Single(await PunkteAsync(), p => Id(p) == Id(punkt)).GetProperty("uebernommenVon").GetGuid());
    }

    // ---------- Story 6-2: „Geändert, noch nicht mitgeteilt“ ----------

    [Fact]
    public async Task Verschieben_merkt_die_alte_Uhrzeit_erneut_verschieben_behaelt_sie_zurueckschieben_leert_sie()
    {
        var punkt = Id(await NeuAsync("Mittagessen am Bach", "2027-08-03", "13:00"));
        Assert.Null(await MitgeteilterBeginnAsync(punkt));

        await VerschiebenAsync(punkt, "13:00", "13:30", _s.Tobias);
        Assert.Equal("13:00", await MitgeteilterBeginnAsync(punkt));
        // Auch für andere Teammitglieder und nach dem Neuladen.
        Assert.Equal("13:00", await MitgeteilterBeginnAsync(punkt, _s.Tobias));

        await VerschiebenAsync(punkt, "13:30", "14:00");
        Assert.Equal("13:00", await MitgeteilterBeginnAsync(punkt));

        await VerschiebenAsync(punkt, "14:00", "13:00");
        Assert.Null(await MitgeteilterBeginnAsync(punkt));
    }

    [Fact]
    public async Task Erste_Uhrzeit_kein_Hinweis_Uhrzeit_entfernt_merkt_die_alte_Titel_aendert_nichts()
    {
        var ohne = Id(await NeuAsync("Lagerfeuer"));
        await VerschiebenAsync(ohne, null, "15:00");
        Assert.Null(await MitgeteilterBeginnAsync(ohne));

        var punkt = Id(await NeuAsync("Geländespiel", "2027-08-03", "13:00"));
        await VerschiebenAsync(punkt, "13:00", "");
        Assert.Equal("13:00", await MitgeteilterBeginnAsync(punkt));

        using var titel = await PatchAsync(punkt, Koerper(0, "Geländespiel", "titel", "Schnitzeljagd"));
        Assert.Equal(HttpStatusCode.OK, titel.StatusCode);
        Assert.Equal("13:00", await MitgeteilterBeginnAsync(punkt));
        using var titelOhne = await PatchAsync(ohne, Koerper(0, "Lagerfeuer", "titel", "Feuer"));
        Assert.Equal(HttpStatusCode.OK, titelOhne.StatusCode);
        Assert.Null(await MitgeteilterBeginnAsync(ohne));
    }

    [Fact]
    public async Task Verschieben_verschickt_nichts_keine_Nachricht_kein_Push_kein_Auftrag_in_den_Outboxen_erst_Ja_informieren_schickt_einen_Push()
    {
        // Tobias hat Push eingeschaltet; Miriam verschiebt.
        var p256dh = Convert.ToBase64String([0x04, .. Enumerable.Range(1, 64).Select(i => (byte)i)]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = Convert.ToBase64String([.. Enumerable.Range(1, 16).Select(i => (byte)i)]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using (var abo = await PostAsync(_s.Tobias, $"/api/freizeiten/{_s.X}/kanaele/push/abo", new { endpunkt = "https://fcm.googleapis.com/fcm/send/tobias", p256dh, auth }))
        {
            Assert.Equal(HttpStatusCode.NoContent, abo.StatusCode);
        }

        var mittag = (await PunkteAsync()).First(p => Text(p, "mahlzeit") == "Mittagessen");
        await _s.ZustellenAsync();
        var outboxVorher = await OutboxAsync();

        await VerschiebenAsync(Id(mittag), "12:30", "13:30");
        await _s.ZustellenAsync();

        Assert.Equal("12:30", await MitgeteilterBeginnAsync(Id(mittag)));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM kanaele.nachricht"));
        Assert.Equal(outboxVorher, await OutboxAsync());
        Assert.Empty(_push.Gesendet);

        // Erst „Ja, informieren“ (Senden in „Alle“, bestätigt) schickt genau einen Push; „mitgeteilt“ nimmt den Hinweis weg.
        using (var senden = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/kanaele/alle/nachrichten",
                   new { text = "Mittagessen am Mo 02.08.: 13:30 statt 12:30", bestaetigt = true }))
        {
            Assert.Equal(HttpStatusCode.Created, senden.StatusCode);
        }

        using (var mitgeteilt = await MitgeteiltAsync(Id(mittag), "13:30"))
        {
            Assert.Equal(HttpStatusCode.NoContent, mitgeteilt.StatusCode);
        }

        await _s.ZustellenAsync();
        Assert.Equal("Mittagessen am Mo 02.08.: 13:30 statt 12:30", Assert.Single(_push.Gesendet).Inhalt.Text);
        Assert.Null(await MitgeteilterBeginnAsync(Id(mittag)));
    }

    [Fact]
    public async Task Mitgeteilt_204_auch_ohne_Hinweis_inzwischen_verschoben_409_konflikt_ungueltige_Uhrzeit_400_unbekannt_404()
    {
        var punkt = Id(await NeuAsync("Geländespiel", "2027-08-03", "13:00"));
        await VerschiebenAsync(punkt, "13:00", "13:30");

        using (var erledigt = await MitgeteiltAsync(punkt, "13:30", _s.Tobias))
        {
            Assert.Equal(HttpStatusCode.NoContent, erledigt.StatusCode);
        }

        Assert.Null(await MitgeteilterBeginnAsync(punkt));
        using (var nochmal = await MitgeteiltAsync(punkt, "13:30"))
        {
            Assert.Equal(HttpStatusCode.NoContent, nochmal.StatusCode);
        }

        // Ohne offenen Hinweis ist auch eine veraltete Uhrzeit erledigt (z. B. „Ausblenden“ aus einer alten Ansicht).
        using (var veraltet = await MitgeteiltAsync(punkt, "13:00"))
        {
            Assert.Equal(HttpStatusCode.NoContent, veraltet.StatusCode);
        }

        await VerschiebenAsync(punkt, "13:30", "14:00");
        using (var konflikt = await MitgeteiltAsync(punkt, "13:30"))
        {
            Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
            Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));
        }

        Assert.Equal("13:30", await MitgeteilterBeginnAsync(punkt));

        using (var ungueltig = await MitgeteiltAsync(punkt, "25:99"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, ungueltig.StatusCode);
            Assert.True((await ProblemAsync(ungueltig)).GetProperty("errors").TryGetProperty("beginn", out _));
        }

        // Ohne Uhrzeit passt zu einem Punkt ohne Uhrzeit.
        await VerschiebenAsync(punkt, "14:00", "");
        using (var ohne = await MitgeteiltAsync(punkt, null))
        {
            Assert.Equal(HttpStatusCode.NoContent, ohne.StatusCode);
        }

        Assert.Null(await MitgeteilterBeginnAsync(punkt));
        using var unbekannt = await MitgeteiltAsync(Guid.CreateVersion7(), "13:00");
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
    }

    [Fact]
    public async Task Mitgeteilt_Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_andere_Freizeit_404_ohne_Sitzung_401_abgeschlossen_409()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var punkt = Id(await NeuAsync("Geländespiel", "2027-08-03", "13:00"));
        await VerschiebenAsync(punkt, "13:00", "13:30");
        var inZ = Id(await NeuAsync("Lagerfeuer", "2027-09-01", "20:00", freizeit: _s.Z));

        var faelle = new (HttpClient Client, Guid Freizeit, Guid Punkt, HttpStatusCode Erwartet)[]
        {
            (_s.Andrea, _s.X, punkt, HttpStatusCode.NotFound),
            (_s.Miriam, fremde, punkt, HttpStatusCode.NotFound),
            (_s.Miriam, _s.X, inZ, HttpStatusCode.NotFound),
            (_s.Anonym, _s.X, punkt, HttpStatusCode.Unauthorized),
        };
        foreach (var (client, freizeit, ziel, erwartet) in faelle)
        {
            using var antwort = await MitgeteiltAsync(ziel, "13:30", client, freizeit);
            Assert.Equal(erwartet, antwort.StatusCode);
        }

        Assert.Equal("13:00", await MitgeteilterBeginnAsync(punkt));

        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));
        using var abgeschlossen = await MitgeteiltAsync(punkt, "13:30", _s.Tobias);
        Assert.Equal(HttpStatusCode.Conflict, abgeschlossen.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(abgeschlossen)));
        Assert.Equal("13:00", await MitgeteilterBeginnAsync(punkt));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Abgeschlossene_Freizeit_jede_Schreibaktion_409_freizeit_abgeschlossen_Laden_200_mit_Mahlzeiten()
    {
        var punkt = await NeuAsync("Lagerfeuer", beginn: "20:00");
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM programm.programmpunkt WHERE \"Mahlzeit\" IS NOT NULL");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        var antworten = new List<HttpResponseMessage>
        {
            await AnlegenAsync(new { titel = "Abbau", tag = "2027-08-13" }),
            await PatchAsync(Id(punkt), Koerper(0, "20:00", "beginn", "21:00")),
            await LoeschenAsync(Id(punkt)),
            await UebergangAsync(_s.Tobias, Id(punkt), "uebernehmen"),
            await UebergangAsync(_s.Tobias, Id(punkt), "abgeben"),
        };

        foreach (var antwort in antworten)
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            antwort.Dispose();
        }

        // Die Mahlzeiten entstehen auch nach dem Abschluss beim Laden (kein Benutzer-Command); sonst bleibt alles, wie es war.
        var punkte = await PunkteAsync(_s.Tobias);
        Assert.Equal(37, punkte.Count);
        var lagerfeuer = Assert.Single(punkte, p => Id(p) == Id(punkt));
        Assert.Equal(("20:00", JsonValueKind.Null), (Text(lagerfeuer, "beginn"), lagerfeuer.GetProperty("uebernommenVon").ValueKind));
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Rechte_Leitung_und_Team_200_Org_Admin_ohne_Rolle_fremde_Organisation_und_Leitung_nur_von_Z_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (leitungZ, _s.X), (_s.Anonym, _s.X) };

        var lesen = new List<HttpStatusCode>();
        var anlegen = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in faelle)
        {
            using var get = await client.GetAsync(Pfad(freizeit), Abbruch);
            lesen.Add(get.StatusCode);
            using var post = await AnlegenAsync(new { titel = "Lagerfeuer", tag = "2027-08-02" }, client, freizeit);
            anlegen.Add(post.StatusCode);
        }

        HttpStatusCode[] erwartet(HttpStatusCode ok) =>
            [ok, ok, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized];
        Assert.Equal(erwartet(HttpStatusCode.OK), lesen);
        Assert.Equal(erwartet(HttpStatusCode.Created), anlegen);
        Assert.Equal(2L, await AnzahlAsync("\"Mahlzeit\" IS NULL"));
        Assert.Equal(HttpStatusCode.OK, (await leitungZ.GetAsync(Pfad(_s.Z), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Rechte_Bearbeiten_Loeschen_Uebernehmen_Abgeben_Org_Admin_ohne_Rolle_und_fremde_Organisation_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var punkt = await NeuAsync("Lagerfeuer", beginn: "20:00");
        var faelle = new[] { (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        foreach (var (client, freizeit) in faelle)
        {
            var status = new List<HttpStatusCode>();
            foreach (var aufruf in new Func<Task<HttpResponseMessage>>[]
                     {
                         () => PatchAsync(Id(punkt), Koerper(0, "20:00", "beginn", "21:00"), client, freizeit),
                         () => LoeschenAsync(Id(punkt), client, freizeit),
                         () => UebergangAsync(client, Id(punkt), "uebernehmen", freizeit),
                         () => UebergangAsync(client, Id(punkt), "abgeben", freizeit),
                     })
            {
                using var antwort = await aufruf();
                status.Add(antwort.StatusCode);
            }

            var erwartet = client == _s.Anonym ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound;
            Assert.All(status, s => Assert.Equal(erwartet, s));
        }

        var unveraendert = Assert.Single(await PunkteAsync(), p => Id(p) == Id(punkt));
        Assert.Equal(("20:00", JsonValueKind.Null), (Text(unveraendert, "beginn"), unveraendert.GetProperty("uebernommenVon").ValueKind));
    }

    [Fact]
    public async Task Rechte_ein_Punkt_einer_anderen_Freizeit_ist_unter_X_404_auch_fuer_die_Leitung_beider()
    {
        var inZ = await NeuAsync("Lagerfeuer", "2027-09-01", freizeit: _s.Z);

        using var patch = await PatchAsync(Id(inZ), Koerper(0, "Lagerfeuer", "titel", "Anders"));
        using var loeschen = await LoeschenAsync(Id(inZ));
        using var uebernehmen = await UebergangAsync(_s.Miriam, Id(inZ), "uebernehmen");
        using var abgeben = await UebergangAsync(_s.Miriam, Id(inZ), "abgeben");

        Assert.All(new[] { patch, loeschen, uebernehmen, abgeben }, a => Assert.Equal(HttpStatusCode.NotFound, a.StatusCode));
        var unveraendert = Assert.Single(await PunkteAsync(freizeit: _s.Z), p => Id(p) == Id(inZ));
        Assert.Equal(("Lagerfeuer", JsonValueKind.Null), (Text(unveraendert, "titel"), unveraendert.GetProperty("uebernommenVon").ValueKind));
        using var unterZ = await UebergangAsync(_s.Miriam, Id(inZ), "uebernehmen", _s.Z);
        Assert.Equal(HttpStatusCode.OK, unterZ.StatusCode);
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Person_geloescht_ihre_Uebernahme_traegt_den_Platzhalter_und_der_Punkt_ist_wieder_frei()
    {
        var punkt = await NeuAsync("Lagerfeuer", beginn: "20:00");
        var anderer = await NeuAsync("Abbau", "2027-08-13");
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(punkt), "uebernehmen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Miriam, Id(anderer), "uebernehmen")).StatusCode);

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_s.Tobias, "/api/ich/profil/loeschen")).StatusCode);
        await _s.ZustellenAsync();

        Assert.Equal(0L, await AnzahlAsync("\"PersonId\" = @p", ("p", _s.TobiasId)));
        var punkte = await PunkteAsync();
        Assert.Equal(1L, await AnzahlAsync("\"PersonId\" = @p", ("p", GeloeschtePerson.Id)));
        // Der Platzhalter gilt als frei: die API zeigt den Punkt ohne Übernahme, und eine andere Person kann ihn übernehmen.
        Assert.Equal(JsonValueKind.Null, Assert.Single(punkte, p => Id(p) == Id(punkt)).GetProperty("uebernommenVon").ValueKind);
        Assert.Equal(_s.MiriamId, Assert.Single(punkte, p => Id(p) == Id(anderer)).GetProperty("uebernommenVon").GetGuid());
        using var miriam = await UebergangAsync(_s.Miriam, Id(punkt), "uebernehmen");
        Assert.Equal(HttpStatusCode.OK, miriam.StatusCode);
        Assert.True((await JsonAsync(miriam)).GetProperty("meins").GetBoolean());
    }

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Programmpunkte_der_abgelaufenen_Freizeit_die_anderen_bleiben()
    {
        await NeuAsync("Lagerfeuer");
        await NeuAsync("Lagerfeuer", "2027-09-01", freizeit: _s.Z);
        await RasterAsync();
        await RasterAsync(freizeit: _s.Z);
        var inZ = await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.Z));
        Assert.Equal(1L + (3 * 3), inZ);

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "ProgrammLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(inZ, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    [Fact]
    public async Task Auskunft_Tobias_sieht_den_Abschnitt_Programm_mit_seinen_Uebernahmen()
    {
        var lagerfeuer = await NeuAsync("Lagerfeuer", beginn: "20:00");
        var abbau = await NeuAsync("Abbau", "2027-08-13");
        var keine = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(keine, a => a.Titel == "Programm");
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(abbau), "uebernehmen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(lagerfeuer), "uebernehmen")).StatusCode);

        var auskunft = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        Assert.Equal(
            [new DatenauskunftZeile("Programmpunkt übernommen", "02.08.2027 20:00: Lagerfeuer"), new DatenauskunftZeile("Programmpunkt übernommen", "13.08.2027: Abbau")],
            Assert.Single(auskunft, a => a.Titel == "Programm").Zeilen);
        var miriam = (await _s.Miriam.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(miriam, a => a.Titel == "Programm");
    }
}
