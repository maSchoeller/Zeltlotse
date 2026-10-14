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

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-2, Material am Programmpunkt (CAP-20, FR-36, FR-69, AD-16, AD-19, AD-28, AD-30) gegen echtes PostgreSQL: die Materialliste aller
/// Programmpunkte, Anlegen mit Feldfehlern, Autosave mit Konfliktregel (auch gleichzeitig), Status setzen, „Ich bringe das mit“ und Zurücknehmen
/// (auch gleichzeitig), Löschen, Mitlöschen mit dem Programmpunkt, abgeschlossene Freizeit, die Pflichtfälle der Freizeit-Route für alle sieben
/// Routen, Löschen mit der Person und nach der Frist sowie die Datenauskunft. Freizeit X geht vom 02. bis 13.08.2027.
/// </summary>
public sealed class MaterialTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/programm";

    private static string ListePfad(Guid freizeit) => $"{Pfad(freizeit)}/material";

    private static string MaterialPfad(Guid freizeit, Guid material) => $"{ListePfad(freizeit)}/{material}";

    /// <summary>Legt einen Programmpunkt an und liefert seine Kennung.</summary>
    private async Task<Guid> PunktAsync(string titel, string tag = "2027-08-03", string? beginn = null, Guid? freizeit = null)
    {
        using var antwort = await PostAsync(_s.Miriam, $"{Pfad(freizeit ?? _s.X)}/punkte", new { titel, tag, beginn });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> AnlegenAsync(Guid punkt, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{Pfad(freizeit ?? _s.X)}/punkte/{punkt}/material", koerper);

    /// <summary>Legt ein Material an und liefert die Antwort.</summary>
    private async Task<JsonElement> NeuAsync(Guid punkt, string bezeichnung, string? menge = null, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await AnlegenAsync(punkt, new { bezeichnung, menge }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<List<JsonElement>> ListeAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(ListePfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["material"], Namen(json));
        return [.. json.GetProperty("material").EnumerateArray()];
    }

    private Task<HttpResponseMessage> PatchAsync(Guid material, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, MaterialPfad(freizeit ?? _s.X, material), koerper);

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private Task<HttpResponseMessage> StatusAsync(Guid material, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{MaterialPfad(freizeit ?? _s.X, material)}/status", koerper);

    private Task<HttpResponseMessage> UebergangAsync(HttpClient client, Guid material, string uebergang, Guid? freizeit = null) =>
        PostAsync(client, $"{MaterialPfad(freizeit ?? _s.X, material)}/{uebergang}");

    private Task<HttpResponseMessage> LoeschenAsync(Guid material, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, MaterialPfad(freizeit ?? _s.X, material));

    private Task<long> AnzahlAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM programm.material WHERE {bedingung}", parameter);

    private static Guid Id(JsonElement material) => material.GetProperty("id").GetGuid();

    private static uint Version(JsonElement material) => material.GetProperty("version").GetUInt32();

    private static string? Text(JsonElement material, string feld) => material.GetProperty(feld).GetString();

    // ---------- Liste ----------

    [Fact]
    public async Task Liste_ohne_Material_ist_leer()
    {
        Assert.Empty(await ListeAsync(_s.Tobias));
    }

    [Fact]
    public async Task Liste_zeigt_das_Material_aller_Programmpunkte_mit_Punkt_sortiert_wie_der_Raster_dann_nach_Bezeichnung()
    {
        var gelaendespiel = await PunktAsync("Geländespiel", "2027-08-03", "14:30");
        var lagerfeuer = await PunktAsync("Lagerfeuer", "2027-08-02", "20:00");
        var ohneZeit = await PunktAsync("Abbau", "2027-08-02");
        await NeuAsync(gelaendespiel, "Taschenlampen", "10 Stück");
        await NeuAsync(ohneZeit, "Werkzeug");
        await NeuAsync(gelaendespiel, "Fahnen", "8 Stück");
        await NeuAsync(lagerfeuer, "Feuerholz", "2 Kisten");

        var liste = await ListeAsync(_s.Tobias);

        Assert.Equal(
            [
                "2027-08-02 20:00 Lagerfeuer Feuerholz 2 Kisten Gebraucht",
                "2027-08-02 - Abbau Werkzeug - Gebraucht",
                "2027-08-03 14:30 Geländespiel Fahnen 8 Stück Gebraucht",
                "2027-08-03 14:30 Geländespiel Taschenlampen 10 Stück Gebraucht",
            ],
            liste.Select(m => $"{Text(m, "tag")} {Text(m, "beginn") ?? "-"} {Text(m, "programmpunkt")} {Text(m, "bezeichnung")} {Text(m, "menge") ?? "-"} {Text(m, "status")}"));
        Assert.Equal(
            ["beginn", "bezeichnung", "bringtMit", "id", "meins", "menge", "programmpunkt", "programmpunktId", "reservierungId", "status", "tag", "version"],
            Namen(liste[0]));
        Assert.Equal(lagerfeuer, liste[0].GetProperty("programmpunktId").GetGuid());
    }

    // ---------- Anlegen ----------

    [Fact]
    public async Task Anlegen_201_mit_Status_gebraucht_Version_getrimmt_und_ohne_weitere_Eingabe_in_der_Liste()
    {
        var punkt = await PunktAsync("Geländespiel", beginn: "14:30");

        using var antwort = await AnlegenAsync(punkt, new { bezeichnung = "  Seile ", menge = " 4 × 10 m " }, _s.Tobias);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(ListePfad(_s.X), antwort.Headers.Location?.OriginalString);
        var material = await JsonAsync(antwort);
        Assert.Equal(("Seile", "4 × 10 m", "Gebraucht"), (Text(material, "bezeichnung"), Text(material, "menge"), Text(material, "status")));
        Assert.Equal((punkt, "Geländespiel", "2027-08-03", "14:30"), (
            material.GetProperty("programmpunktId").GetGuid(), Text(material, "programmpunkt"), Text(material, "tag"), Text(material, "beginn")));
        Assert.Equal((JsonValueKind.Null, false), (material.GetProperty("bringtMit").ValueKind, material.GetProperty("meins").GetBoolean()));
        Assert.NotEqual(0u, Version(material));
        var ohneMenge = await NeuAsync(punkt, "Fahnen", "  ");
        Assert.Equal(JsonValueKind.Null, ohneMenge.GetProperty("menge").ValueKind);

        Assert.Equal([Id(ohneMenge), Id(material)], (await ListeAsync()).Select(Id));
    }

    [Fact]
    public async Task Anlegen_Feldfehler_Bezeichnung_leer_oder_zu_lang_Menge_zu_lang_400_unbekannter_Punkt_404()
    {
        var punkt = await PunktAsync("Geländespiel");
        var faelle = new (object Koerper, string Feld)[]
        {
            (new { bezeichnung = "  " }, "bezeichnung"),
            (new { menge = "2" }, "bezeichnung"),
            (new { bezeichnung = new string('a', 121) }, "bezeichnung"),
            (new { bezeichnung = "Seile", menge = new string('m', 41) }, "menge"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await AnlegenAsync(punkt, koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        using var unbekannt = await AnlegenAsync(Guid.CreateVersion7(), new { bezeichnung = "Seile" });
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        var inZ = await PunktAsync("Lagerfeuer", "2027-09-01", freizeit: _s.Z);
        using var andereFreizeit = await AnlegenAsync(inZ, new { bezeichnung = "Seile" });
        Assert.Equal(HttpStatusCode.NotFound, andereFreizeit.StatusCode);
        Assert.Equal(0L, await AnzahlAsync());
    }

    // ---------- Bearbeiten (Autosave) ----------

    [Fact]
    public async Task Menge_aendern_200_mit_Version_anderer_Altwert_409_konflikt_anderes_Feld_wird_angewendet()
    {
        var material = await NeuAsync(await PunktAsync("Geländespiel"), "Seile", "4 × 10 m");
        var alteVersion = Version(material);

        using var erste = await PatchAsync(Id(material), Koerper(alteVersion, "4 × 10 m", "menge", " 6 × 10 m "), _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, erste.StatusCode);
        var json = await JsonAsync(erste);
        Assert.Equal(["menge", "version"], Namen(json));
        Assert.Equal("6 × 10 m", Text(json, "menge"));
        Assert.NotEqual(alteVersion, json.GetProperty("version").GetUInt32());

        using var konflikt = await PatchAsync(Id(material), Koerper(alteVersion, "4 × 10 m", "menge", "8 × 10 m"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        using var anderesFeld = await PatchAsync(Id(material), Koerper(alteVersion, "Seile", "bezeichnung", " Kletterseile "));
        Assert.Equal(HttpStatusCode.OK, anderesFeld.StatusCode);
        Assert.Equal("Kletterseile", Text(await JsonAsync(anderesFeld), "bezeichnung"));

        using var leeren = await PatchAsync(Id(material), Koerper(0, "6 × 10 m", "menge", ""));
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(leeren)).GetProperty("menge").ValueKind);
        var gespeichert = Assert.Single(await ListeAsync());
        Assert.Equal(("Kletterseile", null, "Gebraucht"), (Text(gespeichert, "bezeichnung"), Text(gespeichert, "menge"), Text(gespeichert, "status")));
    }

    [Fact]
    public async Task Gleichzeitige_Aenderungen_an_Bezeichnung_und_Menge_gelingen_beide()
    {
        var material = await NeuAsync(await PunktAsync("Geländespiel"), "Seile", "4 × 10 m");
        var version = Version(material);
        var (bezeichnung, menge) = ("Seile", "4 × 10 m");

        for (var runde = 0; runde < 5; runde++)
        {
            // Beide kennen dieselbe Version; jede Anfrage ändert ein anderes Feld (AD-19: kein Konflikt).
            var neueBezeichnung = $"Seile {runde}";
            var neueMenge = $"{runde} × 10 m";
            var antworten = await Task.WhenAll(
                Task.Run(() => PatchAsync(Id(material), Koerper(version, bezeichnung, "bezeichnung", neueBezeichnung))),
                Task.Run(() => PatchAsync(Id(material), Koerper(version, menge, "menge", neueMenge))));
            foreach (var antwort in antworten)
            {
                using (antwort)
                {
                    Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
                }
            }

            (bezeichnung, menge) = (neueBezeichnung, neueMenge);
            var gespeichert = Assert.Single(await ListeAsync());
            Assert.Equal((bezeichnung, menge), (Text(gespeichert, "bezeichnung"), Text(gespeichert, "menge")));
            version = Version(gespeichert);
        }
    }

    [Fact]
    public async Task Bearbeiten_Feldfehler_Bezeichnung_leer_Menge_zu_lang_Status_kein_Feld_zwei_Felder_ohne_Version_400()
    {
        var material = await NeuAsync(await PunktAsync("Geländespiel"), "Seile");
        var faelle = new (object Koerper, string Feld)[]
        {
            (Koerper(0, "Seile", "bezeichnung", " "), "bezeichnung"),
            (Koerper(0, "Seile", "bezeichnung", new string('a', 121)), "bezeichnung"),
            (Koerper(0, null, "menge", new string('m', 41)), "menge"),
            (Koerper(0, "Gebraucht", "status", "Eingepackt"), "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["bezeichnung"] = "a", ["menge"] = "2" }, "feld"),
            (new Dictionary<string, object?> { ["alterWert"] = "Seile", ["bezeichnung"] = "Taue" }, "version"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await PatchAsync(Id(material), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        Assert.Equal("Gebraucht", Text(Assert.Single(await ListeAsync()), "status"));
    }

    // ---------- Status ----------

    [Fact]
    public async Task Status_setzen_200_mit_neuem_Status_und_neuer_Version_in_jeden_Zustand_fehlt_oder_unbekannt_400_am_Feld_status()
    {
        var material = await NeuAsync(await PunktAsync("Geländespiel"), "Seile");
        var version = Version(material);

        var bisher = "Gebraucht";
        foreach (var status in new[] { "Eingepackt", "Gebraucht", "Besorgt", "Vorhanden", "Vorhanden" })
        {
            using var antwort = await StatusAsync(Id(material), new { status }, _s.Tobias);
            Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            var json = await JsonAsync(antwort);
            Assert.Equal((status, "Seile", "Geländespiel"), (Text(json, "status"), Text(json, "bezeichnung"), Text(json, "programmpunkt")));
            // Derselbe Zustand lässt alles, wie es ist, auch die Version.
            Assert.Equal(status == bisher, version == Version(json));
            (version, bisher) = (Version(json), status);
        }

        foreach (var koerper in new object[] { new { }, new { status = (string?)null }, new { status = "Verloren" }, new { status = "eingepackt" }, new { status = 4 }, new { status = "1" } })
        {
            using var antwort = await StatusAsync(Id(material), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty("status", out _), JsonSerializer.Serialize(koerper));
        }

        using var unbekannt = await StatusAsync(Guid.CreateVersion7(), new { status = "Eingepackt" });
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        Assert.Equal("Vorhanden", Text(Assert.Single(await ListeAsync()), "status"));
    }

    // ---------- Ich bringe das mit ----------

    [Fact]
    public async Task Mitbringen_200_Meins_wiederholt_200_eine_andere_Person_409_nicht_mitbringen_nur_selbst_Status_bleibt()
    {
        var material = await NeuAsync(await PunktAsync("Geländespiel"), "Taschenlampen", "10 Stück");

        using var mitgebracht = await UebergangAsync(_s.Miriam, Id(material), "mitbringen");
        Assert.Equal(HttpStatusCode.OK, mitgebracht.StatusCode);
        var json = await JsonAsync(mitgebracht);
        Assert.Equal((_s.MiriamId, true, "Gebraucht"), (json.GetProperty("bringtMit").GetGuid(), json.GetProperty("meins").GetBoolean(), Text(json, "status")));
        using var nochmal = await UebergangAsync(_s.Miriam, Id(material), "mitbringen");
        Assert.Equal(HttpStatusCode.OK, nochmal.StatusCode);

        using var tobias = await UebergangAsync(_s.Tobias, Id(material), "mitbringen");
        Assert.Equal(HttpStatusCode.Conflict, tobias.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(tobias)));
        using var tobiasNicht = await UebergangAsync(_s.Tobias, Id(material), "nicht-mitbringen");
        Assert.Equal(HttpStatusCode.Conflict, tobiasNicht.StatusCode);
        var beiTobias = Assert.Single(await ListeAsync(_s.Tobias));
        Assert.Equal((_s.MiriamId, false), (beiTobias.GetProperty("bringtMit").GetGuid(), beiTobias.GetProperty("meins").GetBoolean()));

        using var zurueck = await UebergangAsync(_s.Miriam, Id(material), "nicht-mitbringen");
        Assert.Equal(HttpStatusCode.OK, zurueck.StatusCode);
        var frei = await JsonAsync(zurueck);
        Assert.Equal((JsonValueKind.Null, false), (frei.GetProperty("bringtMit").ValueKind, frei.GetProperty("meins").GetBoolean()));
        using var freiNicht = await UebergangAsync(_s.Miriam, Id(material), "nicht-mitbringen");
        Assert.Equal(HttpStatusCode.OK, freiNicht.StatusCode);
        using var jetztTobias = await UebergangAsync(_s.Tobias, Id(material), "mitbringen");
        Assert.Equal(HttpStatusCode.OK, jetztTobias.StatusCode);
    }

    [Fact]
    public async Task Gleichzeitiges_Mitbringen_genau_einer_gewinnt()
    {
        var material = await NeuAsync(await PunktAsync("Geländespiel"), "Taschenlampen");

        var antworten = await Task.WhenAll(UebergangAsync(_s.Tobias, Id(material), "mitbringen"), UebergangAsync(_s.Miriam, Id(material), "mitbringen"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], antworten.Select(a => a.StatusCode).Order());
        var gewinner = antworten[0].StatusCode == HttpStatusCode.OK ? _s.TobiasId : _s.MiriamId;
        foreach (var antwort in antworten)
        {
            antwort.Dispose();
        }

        Assert.Equal(gewinner, Assert.Single(await ListeAsync()).GetProperty("bringtMit").GetGuid());
    }

    // ---------- Löschen ----------

    [Fact]
    public async Task Loeschen_204_unbekannt_404()
    {
        var punkt = await PunktAsync("Geländespiel");
        var material = await NeuAsync(punkt, "Seile");
        var bleibt = await NeuAsync(punkt, "Fahnen");

        using var geloescht = await LoeschenAsync(Id(material), _s.Tobias);
        using var nochmal = await LoeschenAsync(Id(material));

        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nochmal.StatusCode);
        Assert.Equal([Id(bleibt)], (await ListeAsync()).Select(Id));
    }

    [Fact]
    public async Task Ein_geloeschter_Programmpunkt_nimmt_sein_Material_mit()
    {
        var weg = await PunktAsync("Geländespiel");
        var bleibt = await PunktAsync("Lagerfeuer", "2027-08-02");
        await NeuAsync(weg, "Seile");
        await NeuAsync(weg, "Fahnen");
        var feuerholz = await NeuAsync(bleibt, "Feuerholz");

        using var antwort = await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{Pfad(_s.X)}/punkte/{weg}");

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Equal([Id(feuerholz)], (await ListeAsync()).Select(Id));
        Assert.Equal(1L, await AnzahlAsync());
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Abgeschlossene_Freizeit_jede_Schreibaktion_409_freizeit_abgeschlossen_Liste_200()
    {
        var punkt = await PunktAsync("Geländespiel");
        var material = await NeuAsync(punkt, "Seile", "4 × 10 m");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        var antworten = new List<HttpResponseMessage>
        {
            await AnlegenAsync(punkt, new { bezeichnung = "Fahnen" }),
            await PatchAsync(Id(material), Koerper(0, "4 × 10 m", "menge", "5 × 10 m")),
            await StatusAsync(Id(material), new { status = "Eingepackt" }),
            await UebergangAsync(_s.Tobias, Id(material), "mitbringen"),
            await UebergangAsync(_s.Tobias, Id(material), "nicht-mitbringen"),
            await LoeschenAsync(Id(material)),
        };

        foreach (var antwort in antworten)
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            antwort.Dispose();
        }

        var unveraendert = Assert.Single(await ListeAsync(_s.Tobias));
        Assert.Equal(("4 × 10 m", "Gebraucht", JsonValueKind.Null), (Text(unveraendert, "menge"), Text(unveraendert, "status"), unveraendert.GetProperty("bringtMit").ValueKind));
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Rechte_Leitung_und_Team_200_Org_Admin_ohne_Rolle_fremde_Organisation_und_Leitung_nur_von_Z_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var punkt = await PunktAsync("Geländespiel");
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (leitungZ, _s.X), (_s.Anonym, _s.X) };

        var lesen = new List<HttpStatusCode>();
        var anlegen = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in faelle)
        {
            using var get = await client.GetAsync(ListePfad(freizeit), Abbruch);
            lesen.Add(get.StatusCode);
            using var post = await AnlegenAsync(punkt, new { bezeichnung = "Seile" }, client, freizeit);
            anlegen.Add(post.StatusCode);
        }

        HttpStatusCode[] erwartet(HttpStatusCode ok) =>
            [ok, ok, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized];
        Assert.Equal(erwartet(HttpStatusCode.OK), lesen);
        Assert.Equal(erwartet(HttpStatusCode.Created), anlegen);
        Assert.Equal(2L, await AnzahlAsync());
    }

    [Fact]
    public async Task Rechte_Bearbeiten_Loeschen_Status_Mitbringen_Org_Admin_ohne_Rolle_und_fremde_Organisation_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var material = await NeuAsync(await PunktAsync("Geländespiel"), "Seile", "4 × 10 m");
        var faelle = new[] { (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        foreach (var (client, freizeit) in faelle)
        {
            var status = new List<HttpStatusCode>();
            foreach (var aufruf in new Func<Task<HttpResponseMessage>>[]
                     {
                         () => PatchAsync(Id(material), Koerper(0, "4 × 10 m", "menge", "5 × 10 m"), client, freizeit),
                         () => LoeschenAsync(Id(material), client, freizeit),
                         () => StatusAsync(Id(material), new { status = "Eingepackt" }, client, freizeit),
                         () => UebergangAsync(client, Id(material), "mitbringen", freizeit),
                         () => UebergangAsync(client, Id(material), "nicht-mitbringen", freizeit),
                     })
            {
                using var antwort = await aufruf();
                status.Add(antwort.StatusCode);
            }

            var erwartet = client == _s.Anonym ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound;
            Assert.All(status, s => Assert.Equal(erwartet, s));
        }

        var unveraendert = Assert.Single(await ListeAsync());
        Assert.Equal(("4 × 10 m", "Gebraucht", JsonValueKind.Null), (Text(unveraendert, "menge"), Text(unveraendert, "status"), unveraendert.GetProperty("bringtMit").ValueKind));
    }

    [Fact]
    public async Task Rechte_ein_Material_einer_anderen_Freizeit_ist_unter_X_404_auch_fuer_die_Leitung_beider()
    {
        var inZ = await NeuAsync(await PunktAsync("Lagerfeuer", "2027-09-01", freizeit: _s.Z), "Feuerholz", freizeit: _s.Z);

        using var patch = await PatchAsync(Id(inZ), Koerper(0, "Feuerholz", "bezeichnung", "Anders"));
        using var loeschen = await LoeschenAsync(Id(inZ));
        using var status = await StatusAsync(Id(inZ), new { status = "Eingepackt" });
        using var mitbringen = await UebergangAsync(_s.Miriam, Id(inZ), "mitbringen");
        using var nicht = await UebergangAsync(_s.Miriam, Id(inZ), "nicht-mitbringen");

        Assert.All(new[] { patch, loeschen, status, mitbringen, nicht }, a => Assert.Equal(HttpStatusCode.NotFound, a.StatusCode));
        Assert.Empty(await ListeAsync());
        var unveraendert = Assert.Single(await ListeAsync(freizeit: _s.Z));
        Assert.Equal(("Feuerholz", "Gebraucht"), (Text(unveraendert, "bezeichnung"), Text(unveraendert, "status")));
        using var unterZ = await UebergangAsync(_s.Miriam, Id(inZ), "mitbringen", _s.Z);
        Assert.Equal(HttpStatusCode.OK, unterZ.StatusCode);
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Person_geloescht_ihr_Mitbringen_traegt_den_Platzhalter_und_Ich_bringe_das_mit_ist_wieder_moeglich()
    {
        var punkt = await PunktAsync("Geländespiel");
        var taschenlampen = await NeuAsync(punkt, "Taschenlampen");
        var fahnen = await NeuAsync(punkt, "Fahnen");
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(taschenlampen), "mitbringen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Miriam, Id(fahnen), "mitbringen")).StatusCode);

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_s.Tobias, "/api/ich/profil/loeschen")).StatusCode);
        await _s.ZustellenAsync();

        Assert.Equal(0L, await AnzahlAsync("\"PersonId\" = @p", ("p", _s.TobiasId)));
        Assert.Equal(1L, await AnzahlAsync("\"PersonId\" = @p", ("p", GeloeschtePerson.Id)));
        var liste = await ListeAsync();
        Assert.Equal(JsonValueKind.Null, Assert.Single(liste, m => Id(m) == Id(taschenlampen)).GetProperty("bringtMit").ValueKind);
        Assert.Equal(_s.MiriamId, Assert.Single(liste, m => Id(m) == Id(fahnen)).GetProperty("bringtMit").GetGuid());
        using var miriam = await UebergangAsync(_s.Miriam, Id(taschenlampen), "mitbringen");
        Assert.Equal(HttpStatusCode.OK, miriam.StatusCode);
        Assert.True((await JsonAsync(miriam)).GetProperty("meins").GetBoolean());
    }

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_das_Material_der_abgelaufenen_Freizeit_das_andere_bleibt()
    {
        await NeuAsync(await PunktAsync("Geländespiel"), "Seile");
        await NeuAsync(await PunktAsync("Lagerfeuer", "2027-09-01", freizeit: _s.Z), "Feuerholz", freizeit: _s.Z);

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "ProgrammLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    [Fact]
    public async Task Auskunft_Tobias_sieht_im_Abschnitt_Programm_das_Material_das_er_mitbringt()
    {
        var gelaendespiel = await PunktAsync("Geländespiel", "2027-08-03", "14:30");
        var lagerfeuer = await PunktAsync("Lagerfeuer", "2027-08-02", "20:00");
        var taschenlampen = await NeuAsync(gelaendespiel, "Taschenlampen");
        var feuerholz = await NeuAsync(lagerfeuer, "Feuerholz");
        await NeuAsync(lagerfeuer, "Stockbrot-Teig");
        var keine = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(keine, a => a.Titel == "Programm");
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(taschenlampen), "mitbringen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UebergangAsync(_s.Tobias, Id(feuerholz), "mitbringen")).StatusCode);

        var auskunft = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        Assert.Equal(
            [new DatenauskunftZeile("Bringt Material mit", "02.08.2027 Lagerfeuer: Feuerholz"), new DatenauskunftZeile("Bringt Material mit", "03.08.2027 Geländespiel: Taschenlampen")],
            Assert.Single(auskunft, a => a.Titel == "Programm").Zeilen);
        var miriam = (await _s.Miriam.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(miriam, a => a.Titel == "Programm");
    }
}
