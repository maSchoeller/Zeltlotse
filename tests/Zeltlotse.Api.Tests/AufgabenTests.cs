using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using OrgRolle = Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-5, Aufgaben und Aufgaben-Vorlagen (CAP-23, FR-39, FR-40, AD-16, AD-19, AD-30) gegen echtes PostgreSQL: Vorlagen der Organisation
/// fließen beim ersten Laden genau einmal ein (auch gleichzeitig, nicht nach dem Abschluss), Anlegen mit Feldfehlern und am Programmpunkt,
/// Autosave mit Konfliktregel, „Ich übernehme“ und „Abgeben“ (auch gleichzeitig), Erledigt und Wieder offen, „Als Vorlage speichern“, die
/// abgeschlossene Freizeit, die Vorlagen der Organisation, die Pflichtfälle beider Routenarten, Löschen mit Programmpunkt, Person und Frist
/// sowie die Datenauskunft. Freizeit X (Organisation A, Org-Admin Andrea, Leitung Miriam, Team Tobias) geht vom 02. bis 13.08.2027, Freizeit Z
/// gehört Organisation B (Org-Admin Lea, Leitung Miriam).
/// </summary>
public sealed class AufgabenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/programm/aufgaben";

    private static string AufgabePfad(Guid freizeit, Guid aufgabe) => $"{Pfad(freizeit)}/{aufgabe}";

    private static string VorlagenPfad(Guid org) => $"/api/organisationen/{org}/aufgaben-vorlagen";

    private async Task<List<JsonElement>> AufgabenAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(Pfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    private Task<HttpResponseMessage> AnlegenAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, Pfad(freizeit ?? _s.X), koerper);

    /// <summary>Legt eine Aufgabe an und liefert die Antwort.</summary>
    private async Task<JsonElement> NeuAsync(string titel, string? phase = null, Guid? punkt = null, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await AnlegenAsync(new { titel, phase, programmpunktId = punkt }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> PatchAsync(Guid aufgabe, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, AufgabePfad(freizeit ?? _s.X, aufgabe), koerper);

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private Task<HttpResponseMessage> UebergangAsync(HttpClient client, Guid aufgabe, string uebergang, Guid? freizeit = null) =>
        PostAsync(client, $"{AufgabePfad(freizeit ?? _s.X, aufgabe)}/{uebergang}");

    private Task<HttpResponseMessage> LoeschenAsync(Guid aufgabe, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, AufgabePfad(freizeit ?? _s.X, aufgabe));

    private async Task<JsonElement> VorlageNeuAsync(string titel, string? phase = null, HttpClient? client = null, Guid? org = null)
    {
        using var antwort = await PostAsync(client ?? _s.Andrea, VorlagenPfad(org ?? _s.A), new { titel, phase });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<List<JsonElement>> VorlagenAsync(HttpClient? client = null, Guid? org = null)
    {
        using var antwort = await (client ?? _s.Andrea).GetAsync(VorlagenPfad(org ?? _s.A), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    /// <summary>Legt einen Programmpunkt in X an und liefert seine Kennung.</summary>
    private async Task<Guid> PunktAsync(string titel, Guid? freizeit = null, string tag = "2027-08-02")
    {
        using var antwort = await PostAsync(_s.Miriam, $"/api/freizeiten/{freizeit ?? _s.X}/programm/punkte", new { titel, tag, beginn = "20:00" });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return Id(await JsonAsync(antwort));
    }

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM programm.{tabelle} WHERE {bedingung}", parameter);

    private Task GelaufenAsync(Guid freizeit) =>
        AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", freizeit));

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static string? Text(JsonElement element, string feld) => element.GetProperty(feld).GetString();

    private static bool Wahr(JsonElement element, string feld) => element.GetProperty(feld).GetBoolean();

    // ---------- Vorlagen fließen ein ----------

    [Fact]
    public async Task Vorlagen_der_Organisation_stehen_beim_ersten_Laden_als_Aufgaben_da_ein_zweites_Laden_kopiert_nichts_spaetere_Vorlagen_nicht()
    {
        await VorlageNeuAsync("Zeltplatz-Vertrag prüfen");
        await VorlageNeuAsync("Zeltplatz übergeben", "Nachbereitung");
        await VorlageNeuAsync("Andere Organisation", client: _s.Lea, org: _s.B);

        var aufgaben = await AufgabenAsync(_s.Tobias);

        Assert.Equal(["Vorbereitung Zeltplatz-Vertrag prüfen", "Nachbereitung Zeltplatz übergeben"], aufgaben.Select(a => $"{Text(a, "phase")} {Text(a, "titel")}"));
        Assert.Equal(["erledigt", "id", "meins", "phase", "programmpunktId", "titel", "uebernommenVon", "version", "vorlage"], Namen(aufgaben[0]));
        Assert.All(aufgaben, a => Assert.True(Wahr(a, "vorlage")));
        Assert.All(aufgaben, a => Assert.Equal(JsonValueKind.Null, a.GetProperty("uebernommenVon").ValueKind));
        Assert.All(aufgaben, a => Assert.False(Wahr(a, "erledigt")));

        await VorlageNeuAsync("Später angelegt");
        var zweites = await AufgabenAsync();
        Assert.Equal(aufgaben.Select(Id), zweites.Select(Id));
        Assert.Equal(2L, await AnzahlAsync("aufgabe"));
        Assert.Equal(1L, await AnzahlAsync("aufgaben_vorlagen_uebernahme"));

        // Eine gelöschte Aufgabe aus einer Vorlage kommt nicht wieder.
        using var geloescht = await LoeschenAsync(Id(aufgaben[0]));
        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.Equal(["Zeltplatz übergeben"], (await AufgabenAsync()).Select(a => Text(a, "titel")));
    }

    [Fact]
    public async Task Gleichzeitige_erste_Aufrufe_kopieren_jede_Vorlage_genau_einmal()
    {
        await VorlageNeuAsync("Zeltplatz-Vertrag prüfen");
        await VorlageNeuAsync("Erste-Hilfe-Kasten auffüllen");
        await VorlageNeuAsync("Zeltplatz besenrein übergeben", "Nachbereitung");

        var antworten = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? _s.Miriam : _s.Tobias).GetAsync(Pfad(_s.X), Abbruch)));

        foreach (var antwort in antworten)
        {
            using (antwort)
            {
                Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            }
        }

        Assert.Equal(3L, await AnzahlAsync("aufgabe"));
        Assert.Equal(3, (await AufgabenAsync()).Count);
    }

    [Fact]
    public async Task Abgeschlossene_Freizeit_uebernimmt_keine_Vorlagen_Laden_200_jede_Schreibaktion_409_freizeit_abgeschlossen()
    {
        var aufgabe = await NeuAsync("Fotos sammeln", "Nachbereitung");
        await VorlageNeuAsync("Zeltplatz-Vertrag prüfen", client: _s.Lea, org: _s.B);
        await GelaufenAsync(_s.X);
        await GelaufenAsync(_s.Z);

        Assert.Empty(await AufgabenAsync(freizeit: _s.Z));
        Assert.Equal(0L, await AnzahlAsync("aufgaben_vorlagen_uebernahme", "\"FreizeitId\" = @f", ("f", _s.Z)));
        var antworten = new List<HttpResponseMessage>
        {
            await AnlegenAsync(new { titel = "Abbau" }),
            await PatchAsync(Id(aufgabe), Koerper(0, "Nachbereitung", "phase", "Waehrend")),
            await LoeschenAsync(Id(aufgabe)),
        };
        foreach (var uebergang in new[] { "uebernehmen", "abgeben", "erledigt", "offen", "vorlage" })
        {
            antworten.Add(await UebergangAsync(_s.Tobias, Id(aufgabe), uebergang));
        }

        foreach (var antwort in antworten)
        {
            using (antwort)
            {
                Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
                Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            }
        }

        var unveraendert = Assert.Single(await AufgabenAsync(_s.Tobias));
        Assert.Equal(("Fotos sammeln", "Nachbereitung", false, false), (Text(unveraendert, "titel"), Text(unveraendert, "phase"), Wahr(unveraendert, "erledigt"), Wahr(unveraendert, "vorlage")));
        Assert.Equal(1L, await AnzahlAsync("aufgaben_vorlage"));
    }

    // ---------- Anlegen ----------

    [Fact]
    public async Task Anlegen_201_getrimmt_unbesetzt_offen_am_Programmpunkt_ohne_Phase_Vorbereitung_Sortierung_nach_Phase_und_Titel()
    {
        var lagerfeuer = await PunktAsync("Lagerfeuer");

        using var antwort = await AnlegenAsync(new { titel = " Andacht vorbereiten ", phase = "waehrend", programmpunktId = lagerfeuer }, _s.Tobias);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(Pfad(_s.X), antwort.Headers.Location?.OriginalString);
        var aufgabe = await JsonAsync(antwort);
        Assert.Equal(("Andacht vorbereiten", "Waehrend", lagerfeuer), (Text(aufgabe, "titel"), Text(aufgabe, "phase"), aufgabe.GetProperty("programmpunktId").GetGuid()));
        Assert.Equal((JsonValueKind.Null, false, false, false), (aufgabe.GetProperty("uebernommenVon").ValueKind, Wahr(aufgabe, "meins"), Wahr(aufgabe, "erledigt"), Wahr(aufgabe, "vorlage")));
        Assert.NotEqual(0u, aufgabe.GetProperty("version").GetUInt32());
        var ohnePhase = await NeuAsync("Zelte zählen");
        Assert.Equal(("Vorbereitung", JsonValueKind.Null), (Text(ohnePhase, "phase"), ohnePhase.GetProperty("programmpunktId").ValueKind));
        await NeuAsync("Fotos sammeln", "Nachbereitung");
        await NeuAsync("Anmeldungen prüfen", "Vorbereitung");

        Assert.Equal(["Anmeldungen prüfen", "Zelte zählen", "Andacht vorbereiten", "Fotos sammeln"], (await AufgabenAsync()).Select(a => Text(a, "titel")));
    }

    [Fact]
    public async Task Anlegen_Feldfehler_Titel_leer_oder_zu_lang_Phase_unbekannt_400_Programmpunkt_unbekannt_oder_fremd_404()
    {
        var inZ = await PunktAsync("Lagerfeuer", _s.Z, "2027-09-01");
        var faelle = new (object Koerper, string Feld)[]
        {
            (new { titel = "  " }, "titel"),
            (new { titel = (string?)null }, "titel"),
            (new { titel = new string('a', 121) }, "titel"),
            (new { titel = "Andacht", phase = 9 }, "phase"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await AnlegenAsync(koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        using var fremd = await AnlegenAsync(new { titel = "Andacht", programmpunktId = inZ });
        using var unbekannt = await AnlegenAsync(new { titel = "Andacht", programmpunktId = Guid.CreateVersion7() });
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (fremd.StatusCode, unbekannt.StatusCode));
        Assert.Equal(0L, await AnzahlAsync("aufgabe"));
        using var genau = await AnlegenAsync(new { titel = new string('a', 120), phase = 3 });
        Assert.Equal(HttpStatusCode.Created, genau.StatusCode);
    }

    // ---------- Bearbeiten (Autosave) ----------

    [Fact]
    public async Task Phase_aendern_200_mit_Version_anderer_Altwert_409_konflikt_anderes_Feld_wird_angewendet()
    {
        var aufgabe = await NeuAsync("Andacht vorbereiten");
        var alteVersion = aufgabe.GetProperty("version").GetUInt32();

        using var erste = await PatchAsync(Id(aufgabe), Koerper(alteVersion, "Vorbereitung", "phase", "Waehrend"), _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, erste.StatusCode);
        var json = await JsonAsync(erste);
        Assert.Equal(["phase", "version"], Namen(json));
        Assert.Equal("Waehrend", Text(json, "phase"));
        Assert.NotEqual(alteVersion, json.GetProperty("version").GetUInt32());

        using var konflikt = await PatchAsync(Id(aufgabe), Koerper(alteVersion, "Vorbereitung", "phase", "Nachbereitung"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        using var anderesFeld = await PatchAsync(Id(aufgabe), Koerper(alteVersion, "Andacht vorbereiten", "titel", " Andacht am Lagerfeuer "));
        Assert.Equal(HttpStatusCode.OK, anderesFeld.StatusCode);
        Assert.Equal("Andacht am Lagerfeuer", Text(await JsonAsync(anderesFeld), "titel"));
        var gespeichert = Assert.Single(await AufgabenAsync());
        Assert.Equal(("Andacht am Lagerfeuer", "Waehrend"), (Text(gespeichert, "titel"), Text(gespeichert, "phase")));
    }

    [Fact]
    public async Task Bearbeiten_Feldfehler_Titel_leer_Phase_unbekannt_zwei_Felder_unbekanntes_Feld_400_unbekannt_404()
    {
        var aufgabe = await NeuAsync("Andacht vorbereiten");
        var faelle = new (object Koerper, string Feld)[]
        {
            (Koerper(0, "Andacht vorbereiten", "titel", " "), "titel"),
            (Koerper(0, "Andacht vorbereiten", "titel", new string('a', 121)), "titel"),
            (Koerper(0, "Vorbereitung", "phase", "Während"), "phase"),
            (Koerper(0, "Vorbereitung", "phase", "2"), "phase"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["titel"] = "a", ["phase"] = "Waehrend" }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["erledigt"] = "true" }, "feld"),
            (new Dictionary<string, object?> { ["alterWert"] = "Vorbereitung", ["phase"] = "Waehrend" }, "version"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await PatchAsync(Id(aufgabe), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        using var unbekannt = await PatchAsync(Guid.CreateVersion7(), Koerper(0, "x", "titel", "y"));
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
    }

    // ---------- Übernehmen, Abgeben, Erledigt ----------

    [Fact]
    public async Task Ich_uebernehme_200_Meins_wiederholt_200_eine_andere_Person_409_Abgeben_nur_selbst()
    {
        var aufgabe = await NeuAsync("Andacht vorbereiten", "Waehrend");

        using var uebernommen = await UebergangAsync(_s.Tobias, Id(aufgabe), "uebernehmen");
        Assert.Equal(HttpStatusCode.OK, uebernommen.StatusCode);
        var json = await JsonAsync(uebernommen);
        Assert.Equal((_s.TobiasId, true), (json.GetProperty("uebernommenVon").GetGuid(), Wahr(json, "meins")));
        using var nochmal = await UebergangAsync(_s.Tobias, Id(aufgabe), "uebernehmen");
        Assert.Equal(HttpStatusCode.OK, nochmal.StatusCode);

        // Die Leitung teilt nichts zu und nimmt nichts weg.
        using var miriam = await UebergangAsync(_s.Miriam, Id(aufgabe), "uebernehmen");
        using var miriamAbgeben = await UebergangAsync(_s.Miriam, Id(aufgabe), "abgeben");
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (miriam.StatusCode, miriamAbgeben.StatusCode));
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(miriam)));
        var beiMiriam = Assert.Single(await AufgabenAsync());
        Assert.Equal((_s.TobiasId, false), (beiMiriam.GetProperty("uebernommenVon").GetGuid(), Wahr(beiMiriam, "meins")));

        using var abgegeben = await UebergangAsync(_s.Tobias, Id(aufgabe), "abgeben");
        Assert.Equal(HttpStatusCode.OK, abgegeben.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(abgegeben)).GetProperty("uebernommenVon").ValueKind);
        using var freiAbgeben = await UebergangAsync(_s.Tobias, Id(aufgabe), "abgeben");
        Assert.Equal(HttpStatusCode.OK, freiAbgeben.StatusCode);
    }

    [Fact]
    public async Task Gleichzeitiges_Uebernehmen_genau_einer_gewinnt()
    {
        var aufgabe = await NeuAsync("Erste-Hilfe-Kasten auffüllen");

        var antworten = await Task.WhenAll(UebergangAsync(_s.Tobias, Id(aufgabe), "uebernehmen"), UebergangAsync(_s.Miriam, Id(aufgabe), "uebernehmen"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], antworten.Select(a => a.StatusCode).Order());
        var gewinner = antworten[0].StatusCode == HttpStatusCode.OK ? _s.TobiasId : _s.MiriamId;
        foreach (var antwort in antworten)
        {
            antwort.Dispose();
        }

        Assert.Equal(gewinner, Assert.Single(await AufgabenAsync()).GetProperty("uebernommenVon").GetGuid());
    }

    [Fact]
    public async Task Erledigt_und_offen_darf_jede_Person_wiederholt_die_Uebernahme_bleibt()
    {
        var aufgabe = await NeuAsync("Zeltplatz-Vertrag prüfen");
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(aufgabe), "uebernehmen")).StatusCode);

        using var erledigt = await UebergangAsync(_s.Miriam, Id(aufgabe), "erledigt");
        using var nochmal = await UebergangAsync(_s.Miriam, Id(aufgabe), "erledigt");
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (erledigt.StatusCode, nochmal.StatusCode));
        var json = await JsonAsync(erledigt);
        Assert.Equal((true, _s.TobiasId, false), (Wahr(json, "erledigt"), json.GetProperty("uebernommenVon").GetGuid(), Wahr(json, "meins")));

        using var offen = await UebergangAsync(_s.Miriam, Id(aufgabe), "offen");
        Assert.Equal(HttpStatusCode.OK, offen.StatusCode);
        Assert.False(Wahr(await JsonAsync(offen), "erledigt"));
        var danach = Assert.Single(await AufgabenAsync(_s.Tobias));
        Assert.Equal((false, true), (Wahr(danach, "erledigt"), Wahr(danach, "meins")));
    }

    // ---------- Als Vorlage speichern ----------

    [Fact]
    public async Task Als_Vorlage_zweimal_eine_neue_Vorlage_in_A_nach_dem_Loeschen_der_Vorlage_vorlage_false_erneut_eine_neue()
    {
        var aufgabe = await NeuAsync("Fotos sammeln", "Nachbereitung");

        using var erste = await UebergangAsync(_s.Tobias, Id(aufgabe), "vorlage");
        using var zweite = await UebergangAsync(_s.Tobias, Id(aufgabe), "vorlage");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (erste.StatusCode, zweite.StatusCode));
        Assert.True(Wahr(await JsonAsync(erste), "vorlage"));
        Assert.True(Wahr(await JsonAsync(zweite), "vorlage"));
        var vorlage = Assert.Single(await VorlagenAsync());
        Assert.Equal(("Fotos sammeln", "Nachbereitung"), (Text(vorlage, "titel"), Text(vorlage, "phase")));
        Assert.Empty(await VorlagenAsync(_s.Lea, _s.B));

        using var geloescht = await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{VorlagenPfad(_s.A)}/{Id(vorlage)}");
        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        var ohne = Assert.Single(await AufgabenAsync());
        Assert.Equal(("Fotos sammeln", false), (Text(ohne, "titel"), Wahr(ohne, "vorlage")));

        using var erneut = await UebergangAsync(_s.Miriam, Id(aufgabe), "vorlage");
        Assert.True(Wahr(await JsonAsync(erneut), "vorlage"));
        Assert.NotEqual(Id(vorlage), Id(Assert.Single(await VorlagenAsync())));
    }

    [Fact]
    public async Task Gleichzeitiges_Speichern_als_Vorlage_legt_genau_eine_Vorlage_an()
    {
        var aufgabe = await NeuAsync("Fotos sammeln", "Nachbereitung");

        var antworten = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => UebergangAsync(i % 2 == 0 ? _s.Miriam : _s.Tobias, Id(aufgabe), "vorlage")));

        foreach (var antwort in antworten)
        {
            using (antwort)
            {
                Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            }
        }

        Assert.Equal(1L, await AnzahlAsync("aufgaben_vorlage"));
    }

    // ---------- Vorlagen der Organisation ----------

    [Fact]
    public async Task Org_Vorlagen_anlegen_201_bearbeiten_200_konflikt_409_loeschen_204_Feldfehler_400_unbekannt_404()
    {
        using var antwort = await PostAsync(_s.Andrea, VorlagenPfad(_s.A), new { titel = " Zeltplatz besenrein übergeben ", phase = "Nachbereitung" });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(VorlagenPfad(_s.A), antwort.Headers.Location?.OriginalString);
        var vorlage = await JsonAsync(antwort);
        Assert.Equal(["id", "phase", "titel", "version"], Namen(vorlage));
        Assert.Equal(("Zeltplatz besenrein übergeben", "Nachbereitung"), (Text(vorlage, "titel"), Text(vorlage, "phase")));
        await VorlageNeuAsync("Zeltplatz-Vertrag prüfen");
        await VorlageNeuAsync("Erste-Hilfe-Kasten auffüllen");
        Assert.Equal(
            ["Erste-Hilfe-Kasten auffüllen", "Zeltplatz-Vertrag prüfen", "Zeltplatz besenrein übergeben"],
            (await VorlagenAsync()).Select(v => Text(v, "titel")));

        var version = vorlage.GetProperty("version").GetUInt32();
        var pfad = $"{VorlagenPfad(_s.A)}/{Id(vorlage)}";
        using var patch = await SendenAsync(_s.Andrea, HttpMethod.Patch, pfad, Koerper(version, "Nachbereitung", "phase", "Waehrend"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal("Waehrend", Text(await JsonAsync(patch), "phase"));
        using var konflikt = await SendenAsync(_s.Andrea, HttpMethod.Patch, pfad, Koerper(version, "Nachbereitung", "phase", "Vorbereitung"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));
        using var titel = await SendenAsync(_s.Andrea, HttpMethod.Patch, pfad, Koerper(version, "Zeltplatz besenrein übergeben", "titel", "Zeltplatz übergeben"));
        Assert.Equal(HttpStatusCode.OK, titel.StatusCode);

        foreach (var (koerper, feld) in new (object Koerper, string Feld)[] { (new { titel = "" }, "titel"), (new { titel = "x", phase = 7 }, "phase") })
        {
            using var fehler = await PostAsync(_s.Andrea, VorlagenPfad(_s.A), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, fehler.StatusCode);
            Assert.True((await ProblemAsync(fehler)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        using var leer = await SendenAsync(_s.Andrea, HttpMethod.Patch, pfad, Koerper(0, "Zeltplatz übergeben", "titel", " "));
        Assert.Equal(HttpStatusCode.BadRequest, leer.StatusCode);
        using var unbekanntPatch = await SendenAsync(_s.Andrea, HttpMethod.Patch, $"{VorlagenPfad(_s.A)}/{Guid.CreateVersion7()}", Koerper(0, "x", "titel", "y"));
        using var unbekanntDelete = await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{VorlagenPfad(_s.A)}/{Guid.CreateVersion7()}");
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (unbekanntPatch.StatusCode, unbekanntDelete.StatusCode));
        using var loeschen = await SendenAsync(_s.Andrea, HttpMethod.Delete, pfad);
        Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        Assert.Equal(2, (await VorlagenAsync()).Count);
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Rechte_Freizeit_Leitung_und_Team_2xx_Org_Admin_ohne_Rolle_und_fremde_Organisation_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        var status = new List<string>();
        foreach (var (client, freizeit) in faelle)
        {
            using var get = await client.GetAsync(Pfad(freizeit), Abbruch);
            using var post = await AnlegenAsync(new { titel = "Andacht" }, client, freizeit);
            var aufgabe = post.StatusCode == HttpStatusCode.Created ? Id(await JsonAsync(post)) : Id(await NeuAsync("Fremdzugriff"));
            var codes = new List<int> { (int)get.StatusCode, (int)post.StatusCode };
            foreach (var aufruf in new Func<Task<HttpResponseMessage>>[]
                     {
                         () => PatchAsync(aufgabe, Koerper(0, "Vorbereitung", "phase", "Waehrend"), client, freizeit),
                         () => UebergangAsync(client, aufgabe, "uebernehmen", freizeit),
                         () => UebergangAsync(client, aufgabe, "abgeben", freizeit),
                         () => UebergangAsync(client, aufgabe, "erledigt", freizeit),
                         () => UebergangAsync(client, aufgabe, "offen", freizeit),
                         () => UebergangAsync(client, aufgabe, "vorlage", freizeit),
                         () => LoeschenAsync(aufgabe, client, freizeit),
                     })
            {
                using var antwort = await aufruf();
                codes.Add((int)antwort.StatusCode);
            }

            status.Add(string.Join(' ', codes));
        }

        Assert.Equal(
            [
                "200 201 200 200 200 200 200 200 204",
                "200 201 200 200 200 200 200 200 204",
                "404 404 404 404 404 404 404 404 404",
                "404 404 404 404 404 404 404 404 404",
                "401 401 401 401 401 401 401 401 401",
            ],
            status);
        // Nur die drei Aufgaben „Fremdzugriff“ bleiben, unverändert.
        var rest = await AufgabenAsync();
        Assert.Equal(3, rest.Count);
        Assert.All(rest, a => Assert.Equal(("Fremdzugriff", "Vorbereitung", JsonValueKind.Null), (Text(a, "titel"), Text(a, "phase"), a.GetProperty("uebernommenVon").ValueKind)));
    }

    [Fact]
    public async Task Rechte_eine_Aufgabe_einer_anderen_Freizeit_ist_unter_X_404_auch_fuer_die_Leitung_beider()
    {
        var inZ = await NeuAsync("Andacht", freizeit: _s.Z);

        var antworten = new List<HttpResponseMessage>
        {
            await PatchAsync(Id(inZ), Koerper(0, "Andacht", "titel", "Anders")),
            await LoeschenAsync(Id(inZ)),
        };
        foreach (var uebergang in new[] { "uebernehmen", "abgeben", "erledigt", "offen", "vorlage" })
        {
            antworten.Add(await UebergangAsync(_s.Miriam, Id(inZ), uebergang));
        }

        Assert.All(antworten, a => Assert.Equal(HttpStatusCode.NotFound, a.StatusCode));
        antworten.ForEach(a => a.Dispose());
        var unveraendert = Assert.Single(await AufgabenAsync(freizeit: _s.Z));
        Assert.Equal(("Andacht", false, false), (Text(unveraendert, "titel"), Wahr(unveraendert, "erledigt"), Wahr(unveraendert, "vorlage")));
        Assert.Equal(0L, await AnzahlAsync("aufgaben_vorlage"));
    }

    [Fact]
    public async Task Rechte_Org_Vorlagen_Org_Admin_2xx_Org_Ueberblick_403_Org_Admin_von_B_und_ohne_Org_Rolle_404_ohne_Sitzung_401()
    {
        const string ueberblickMail = "vorstand@example.org";
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, ueberblickMail, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, ueberblickMail);
        var vorlage = await VorlageNeuAsync("Zeltplatz-Vertrag prüfen");

        async Task<string> AlleAsync(HttpClient client)
        {
            using var get = await client.GetAsync(VorlagenPfad(_s.A), Abbruch);
            using var post = await PostAsync(client, VorlagenPfad(_s.A), new { titel = "Erste-Hilfe-Kasten auffüllen" });
            var ziel = post.StatusCode == HttpStatusCode.Created ? Id(await JsonAsync(post)) : Id(vorlage);
            using var patch = await SendenAsync(client, HttpMethod.Patch, $"{VorlagenPfad(_s.A)}/{ziel}", Koerper(0, "Vorbereitung", "phase", "Waehrend"));
            using var delete = await SendenAsync(client, HttpMethod.Delete, $"{VorlagenPfad(_s.A)}/{ziel}");
            return $"{(int)get.StatusCode} {(int)post.StatusCode} {(int)patch.StatusCode} {(int)delete.StatusCode}";
        }

        Assert.Equal("200 201 200 204", await AlleAsync(_s.Andrea));
        Assert.Equal("403 403 403 403", await AlleAsync(ueberblick));
        foreach (var client in new[] { _s.Lea, _s.Miriam, _s.Tobias, _s.Katrin })
        {
            Assert.Equal("404 404 404 404", await AlleAsync(client));
        }

        Assert.Equal("401 401 401 401", await AlleAsync(_s.Anonym));
        var uebrig = Assert.Single(await VorlagenAsync());
        Assert.Equal(("Zeltplatz-Vertrag prüfen", "Vorbereitung"), (Text(uebrig, "titel"), Text(uebrig, "phase")));
    }

    [Fact]
    public async Task Org_Admin_bekommt_aufgaben_vorlagen_verwalten_Org_Ueberblick_nicht()
    {
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, OrgHelfer.Jonas, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, OrgHelfer.Jonas);

        static async Task<List<string?>> ErlaubtAsync(HttpClient client, Guid org) =>
            [.. (await JsonAsync(await client.GetAsync($"/api/organisationen/{org}", Abbruch))).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString())];

        Assert.Contains(Aktionsschluessel.AufgabenVorlagenVerwalten, await ErlaubtAsync(_s.Andrea, _s.A));
        Assert.DoesNotContain(Aktionsschluessel.AufgabenVorlagenVerwalten, await ErlaubtAsync(ueberblick, _s.A));
    }

    // ---------- Löschen ----------

    [Fact]
    public async Task Wird_der_Programmpunkt_geloescht_verschwindet_seine_Aufgabe_die_anderen_bleiben()
    {
        var lagerfeuer = await PunktAsync("Lagerfeuer");
        await NeuAsync("Andacht vorbereiten", "Waehrend", lagerfeuer);
        await NeuAsync("Fotos sammeln", "Nachbereitung");

        using var geloescht = await SendenAsync(_s.Miriam, HttpMethod.Delete, $"/api/freizeiten/{_s.X}/programm/punkte/{lagerfeuer}");

        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.Equal(["Fotos sammeln"], (await AufgabenAsync()).Select(a => Text(a, "titel")));
    }

    [Fact]
    public async Task Person_geloescht_ihre_Uebernahme_traegt_den_Platzhalter_und_die_Aufgabe_ist_wieder_frei()
    {
        var aufgabe = await NeuAsync("Andacht vorbereiten", "Waehrend");
        var andere = await NeuAsync("Fotos sammeln", "Nachbereitung");
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(aufgabe), "uebernehmen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Miriam, Id(andere), "uebernehmen")).StatusCode);

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_s.Tobias, "/api/ich/profil/loeschen")).StatusCode);
        await _s.ZustellenAsync();

        Assert.Equal(0L, await AnzahlAsync("aufgabe", "\"PersonId\" = @p", ("p", _s.TobiasId)));
        Assert.Equal(1L, await AnzahlAsync("aufgabe", "\"PersonId\" = @p", ("p", GeloeschtePerson.Id)));
        var aufgaben = await AufgabenAsync();
        Assert.Equal(JsonValueKind.Null, Assert.Single(aufgaben, a => Id(a) == Id(aufgabe)).GetProperty("uebernommenVon").ValueKind);
        Assert.Equal(_s.MiriamId, Assert.Single(aufgaben, a => Id(a) == Id(andere)).GetProperty("uebernommenVon").GetGuid());
        using var miriam = await UebergangAsync(_s.Miriam, Id(aufgabe), "uebernehmen");
        Assert.True(Wahr(await JsonAsync(miriam), "meins"));
    }

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_Aufgaben_und_Uebernahme_Zeile_der_abgelaufenen_Freizeit_Vorlagen_und_andere_Freizeiten_bleiben()
    {
        await VorlageNeuAsync("Zeltplatz-Vertrag prüfen");
        await NeuAsync("Fotos sammeln", "Nachbereitung");
        await NeuAsync("Andacht", freizeit: _s.Z);
        Assert.Equal(2, (await AufgabenAsync()).Count);
        Assert.Single(await AufgabenAsync(freizeit: _s.Z));

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "ProgrammLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("aufgabe", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(0L, await AnzahlAsync("aufgaben_vorlagen_uebernahme", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await AnzahlAsync("aufgabe", "\"FreizeitId\" = @f", ("f", _s.Z)));
        Assert.Equal(1L, await AnzahlAsync("aufgaben_vorlagen_uebernahme", "\"FreizeitId\" = @f", ("f", _s.Z)));
        Assert.Equal(1L, await AnzahlAsync("aufgaben_vorlage"));
    }

    [Fact]
    public async Task Auskunft_Tobias_sieht_seine_uebernommenen_Aufgaben_im_Abschnitt_Programm()
    {
        var andacht = await NeuAsync("Andacht vorbereiten", "Waehrend");
        var vertrag = await NeuAsync("Zeltplatz-Vertrag prüfen");
        await NeuAsync("Fotos sammeln", "Nachbereitung");
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(andacht), "uebernehmen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(vertrag), "uebernehmen")).StatusCode);

        var auskunft = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        Assert.Equal(
            [new DatenauskunftZeile("Aufgabe übernommen", "Zeltplatz-Vertrag prüfen"), new DatenauskunftZeile("Aufgabe übernommen", "Andacht vorbereiten")],
            Assert.Single(auskunft, a => a.Titel == "Programm").Zeilen);
        var miriam = (await _s.Miriam.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(miriam, a => a.Titel == "Programm");
    }
}
