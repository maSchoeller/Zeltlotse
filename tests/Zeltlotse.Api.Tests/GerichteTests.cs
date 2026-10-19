using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Programm.Contracts.Mahlzeiten;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 4-2, Gerichte vormerken und tauschen (CAP-29, FR-47, FR-50, AD-13, AD-17, AD-19, AD-20, AD-30) gegen echtes PostgreSQL: der Essensplan (legt
/// fehlende Mahlzeiten an), Vormerken mit Feldfehlern, Entfernen, die Tausch-Vorschau (schreibt nichts) und der Tausch mit Version (auch gleichzeitig),
/// Hand-Mengen beim Tausch, Cascade beim Rezept-Löschen, die Pflichtfälle der Freizeit-Route für die fünf Routen, der Schalter Verpflegung, die
/// abgeschlossene Freizeit und die Leseschnittstelle der Mahlzeiten aus Programm. Aufbau wie <see cref="RezepteTests"/>: Freizeit X (02.–13.08.2027,
/// Leitung Miriam, Team Tobias) und Z (Organisation B) haben Verpflegung an.
/// </summary>
public sealed class GerichteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly DateOnly Dienstag = new(2027, 8, 3);

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres);
        await KuecheTestHelfer.VerpflegungAsync(_s, _s.X);
        await KuecheTestHelfer.VerpflegungAsync(_s, _s.Z);
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Kueche(Guid freizeit) => $"/api/freizeiten/{freizeit}/kueche";

    private static string GerichtPfad(Guid freizeit, Guid gericht) => $"{Kueche(freizeit)}/gerichte/{gericht}";

    private async Task<JsonElement> EssensplanAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync($"{Kueche(freizeit ?? _s.X)}/essensplan", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> VormerkenAsync(Guid? programmpunkt, Guid? rezept, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{Kueche(freizeit ?? _s.X)}/gerichte", new { programmpunktId = programmpunkt, rezeptId = rezept });

    private async Task<JsonElement> GerichtNeuAsync(Guid programmpunkt, Guid rezept, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await VormerkenAsync(programmpunkt, rezept, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> VorschauAsync(Guid gericht, Guid? rezept, HttpClient? client = null, Guid? freizeit = null) =>
        (client ?? _s.Miriam).GetAsync($"{GerichtPfad(freizeit ?? _s.X, gericht)}/tausch{(rezept is { } r ? $"?rezeptId={r}" : "")}", Abbruch);

    private Task<HttpResponseMessage> TauschenAsync(Guid gericht, uint version, Guid? rezept, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{GerichtPfad(freizeit ?? _s.X, gericht)}/tauschen", new { version, rezeptId = rezept });

    private Task<HttpResponseMessage> EntfernenAsync(Guid gericht, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, GerichtPfad(freizeit ?? _s.X, gericht));

    /// <summary>Die Mahlzeit eines Tags aus dem Essensplan, z. B. „Frühstück“.</summary>
    private async Task<Guid> MahlzeitAsync(DateOnly tag, string titel, Guid? freizeit = null) =>
        (await EssensplanAsync(freizeit: freizeit)).GetProperty("tage").EnumerateArray()
        .Single(t => t.GetProperty("tag").GetString() == tag.ToString("yyyy-MM-dd"))
        .GetProperty("mahlzeiten").EnumerateArray()
        .Single(m => m.GetProperty("titel").GetString() == titel)
        .GetProperty("programmpunktId").GetGuid();

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM kueche.{tabelle} WHERE {bedingung}", parameter);

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static uint Version(JsonElement json) => json.GetProperty("version").GetUInt32();

    private static async Task FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
    }

    private static List<string> Zeilen(JsonElement zeilen) =>
        [.. zeilen.EnumerateArray().Select(z =>
            $"{z.GetProperty("bezeichnung").GetString()} {(z.GetProperty("menge").ValueKind == JsonValueKind.Null ? "-" : z.GetProperty("menge").GetRawText())} "
            + $"{z.GetProperty("einheit").GetString() ?? "-"}{(z.GetProperty("vonHand").GetBoolean() ? " von Hand" : "")}{(z.GetProperty("geaendert").GetBoolean() ? " *" : "")}")];

    // ---------- Essensplan ----------

    [Fact]
    public async Task Essensplan_legt_fehlende_Mahlzeiten_an_und_zeigt_drei_je_Tag_mit_Rezeptauswahl()
    {
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM programm.programmpunkt"));
        await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");

        var plan = await EssensplanAsync(_s.Tobias);

        Assert.Equal(["allergienAbgefragt", "personenzahl", "rezepte", "tage"], Namen(plan));
        Assert.Equal((2, true), (plan.GetProperty("personenzahl").GetInt32(), plan.GetProperty("allergienAbgefragt").GetBoolean()));
        var tage = plan.GetProperty("tage").EnumerateArray().ToList();
        Assert.Equal(12, tage.Count);
        Assert.Equal(["2027-08-02", "2027-08-03"], tage.Take(2).Select(t => t.GetProperty("tag").GetString()));
        var mahlzeiten = tage[1].GetProperty("mahlzeiten").EnumerateArray().ToList();
        Assert.Equal(
            ["Frühstück 08:00 Fruehstueck", "Mittagessen 12:30 Mittagessen", "Abendessen 18:30 Abendessen"],
            mahlzeiten.Select(m => $"{m.GetProperty("titel").GetString()} {m.GetProperty("beginn").GetString()} {m.GetProperty("art").GetString()}"));
        Assert.Equal(["art", "beginn", "gerichte", "programmpunktId", "titel"], Namen(mahlzeiten[0]));
        Assert.All(tage, t => Assert.Equal(3, t.GetProperty("mahlzeiten").GetArrayLength()));
        Assert.Equal(["Chili sin Carne", "Pfannkuchen"], plan.GetProperty("rezepte").EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(36L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM programm.programmpunkt WHERE \"Mahlzeit\" IS NOT NULL"));

        // Ein zweiter Abruf legt nichts doppelt an.
        await EssensplanAsync();
        Assert.Equal(36L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM programm.programmpunkt"));
    }

    [Fact]
    public async Task Vormerken_201_das_Gericht_steht_an_der_Mahlzeit_mit_Rezeptname_und_Version()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var fruehstueck = await MahlzeitAsync(Dienstag, "Frühstück");

        using var antwort = await VormerkenAsync(fruehstueck, Id(pfannkuchen), _s.Tobias);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var gericht = await JsonAsync(antwort);
        Assert.Equal(["id", "programmpunktId", "rezeptId", "version"], Namen(gericht));
        Assert.Equal((fruehstueck, Id(pfannkuchen)), (gericht.GetProperty("programmpunktId").GetGuid(), gericht.GetProperty("rezeptId").GetGuid()));
        Assert.NotEqual(0u, Version(gericht));
        var mahlzeit = (await EssensplanAsync()).GetProperty("tage")[1].GetProperty("mahlzeiten")[0];
        var imPlan = Assert.Single(mahlzeit.GetProperty("gerichte").EnumerateArray());
        Assert.Equal(["ersatzportionen", "id", "rezeptId", "rezeptName", "version"], Namen(imPlan));
        Assert.Equal((Id(gericht), "Pfannkuchen", Version(gericht)), (Id(imPlan), imPlan.GetProperty("rezeptName").GetString(), Version(imPlan)));
        Assert.Equal(0, imPlan.GetProperty("ersatzportionen").GetProperty("anzahl").GetInt32());
    }

    [Fact]
    public async Task Vormerken_ohne_Mahlzeit_Punkt_oder_mit_fremdem_Rezept_400_am_Feld()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var inZ = await KuecheTestHelfer.RezeptAsync(_s, "Chili", "4", _s.Z);
        var fruehstueck = await MahlzeitAsync(Dienstag, "Frühstück");
        var mahlzeitInZ = await MahlzeitAsync(new DateOnly(2027, 9, 1), "Frühstück", _s.Z);
        using var normal = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/programm/punkte", new { titel = "Geländespiel", tag = "2027-08-03", beginn = "14:00" });
        Assert.Equal(HttpStatusCode.Created, normal.StatusCode);
        var gelaendespiel = Id(await JsonAsync(normal));

        foreach (var (punkt, rezept, feld) in new (Guid?, Guid?, string)[]
                 {
                     (gelaendespiel, Id(pfannkuchen), "programmpunktId"),
                     (mahlzeitInZ, Id(pfannkuchen), "programmpunktId"),
                     (Guid.CreateVersion7(), Id(pfannkuchen), "programmpunktId"),
                     (null, Id(pfannkuchen), "programmpunktId"),
                     (fruehstueck, Id(inZ), "rezeptId"),
                     (fruehstueck, Guid.CreateVersion7(), "rezeptId"),
                     (fruehstueck, null, "rezeptId"),
                 })
        {
            using var antwort = await VormerkenAsync(punkt, rezept);
            await FeldfehlerAsync(antwort, feld);
        }

        Assert.Equal(0L, await AnzahlAsync("gericht"));
    }

    [Fact]
    public async Task Entfernen_204_nochmal_404_ein_Gericht_einer_anderen_Freizeit_404()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var gericht = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Frühstück"), Id(pfannkuchen));
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili", "4", _s.Z);
        var inZ = await GerichtNeuAsync(await MahlzeitAsync(new DateOnly(2027, 9, 1), "Mittagessen", _s.Z), Id(chili), freizeit: _s.Z);

        using var fremd = await EntfernenAsync(Id(inZ));
        using var weg = await EntfernenAsync(Id(gericht), _s.Tobias);
        using var nochmal = await EntfernenAsync(Id(gericht));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NoContent, HttpStatusCode.NotFound), (fremd.StatusCode, weg.StatusCode, nochmal.StatusCode));
        Assert.Equal(1L, await AnzahlAsync("gericht"));
    }

    // ---------- Tausch ----------

    [Fact]
    public async Task Tausch_Vorschau_zeigt_Vorher_und_Nachher_und_schreibt_nichts_gleiches_Rezept_ohne_Aenderung()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var mehl = await KuecheTestHelfer.ZutatAsync(_s, Id(pfannkuchen), "Mehl", "400", "g", "gluten");
        await KuecheTestHelfer.ZutatAsync(_s, Id(pfannkuchen), "Salz");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "2");
        await KuecheTestHelfer.ZutatAsync(_s, Id(chili), "Kidneybohnen", "250", "g");
        await KuecheTestHelfer.ZutatAsync(_s, Id(chili), "Zwiebeln", "1", "Stück");
        var gericht = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Frühstück"), Id(pfannkuchen));
        using (var hand = await KuecheTestHelfer.HandMengeAsync(_s, Id(gericht), Id(mehl), "200", "1"))
        {
            Assert.Equal(HttpStatusCode.OK, hand.StatusCode);
        }

        using var antwort = await VorschauAsync(Id(gericht), Id(chili), _s.Tobias);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var vorschau = await JsonAsync(antwort);
        Assert.Equal(["gerichtId", "nachher", "neuesRezept", "rezept", "version", "vorher"], Namen(vorschau));
        // Die Hand-Menge hat dem Gericht eine neue Version gegeben; die Vorschau trägt die aktuelle.
        var aktuell = (await EssensplanAsync()).GetProperty("tage")[1].GetProperty("mahlzeiten")[0].GetProperty("gerichte")[0];
        Assert.NotEqual(Version(gericht), Version(aktuell));
        Assert.Equal((Id(gericht), Version(aktuell)), (vorschau.GetProperty("gerichtId").GetGuid(), Version(vorschau)));
        Assert.Equal(("Pfannkuchen", "Chili sin Carne"), (vorschau.GetProperty("rezept").GetProperty("name").GetString(), vorschau.GetProperty("neuesRezept").GetProperty("name").GetString()));
        Assert.Equal(["Mehl 1 g von Hand *", "Salz - - *"], Zeilen(vorschau.GetProperty("vorher")));
        Assert.Equal(["Kidneybohnen 250 g *", "Zwiebeln 1 Stück *"], Zeilen(vorschau.GetProperty("nachher")));
        Assert.Equal(["bezeichnung", "einheit", "geaendert", "menge", "vonHand"], Namen(vorschau.GetProperty("nachher")[0]));

        // Nichts geschrieben: das Gericht hat weiter Pfannkuchen und seine Hand-Menge.
        Assert.Equal(Id(pfannkuchen), await WertAsync<Guid>(_s.Verbindung, "SELECT \"RezeptId\" FROM kueche.gericht WHERE \"Id\" = @g", ("g", Id(gericht))));
        Assert.Equal(1L, await AnzahlAsync("menge_von_hand"));

        using var gleich = await VorschauAsync(Id(gericht), Id(pfannkuchen));
        var ohneAenderung = await JsonAsync(gleich);
        Assert.Equal(["Mehl 1 g von Hand", "Salz - -"], Zeilen(ohneAenderung.GetProperty("vorher")));
        Assert.Equal(["Mehl 1 g von Hand", "Salz - -"], Zeilen(ohneAenderung.GetProperty("nachher")));
    }

    [Fact]
    public async Task Tausch_Vorschau_unbekanntes_Rezept_400_fremdes_Gericht_404()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var gericht = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Frühstück"), Id(pfannkuchen));
        var inZ = await KuecheTestHelfer.RezeptAsync(_s, "Chili", "4", _s.Z);

        using var ohne = await VorschauAsync(Id(gericht), null);
        using var fremdesRezept = await VorschauAsync(Id(gericht), Id(inZ));
        using var unbekanntesGericht = await VorschauAsync(Guid.CreateVersion7(), Id(pfannkuchen));
        using var fremdeFreizeit = await VorschauAsync(Id(gericht), Id(inZ), freizeit: _s.Z);

        await FeldfehlerAsync(ohne, "rezeptId");
        await FeldfehlerAsync(fremdesRezept, "rezeptId");
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (unbekanntesGericht.StatusCode, fremdeFreizeit.StatusCode));
    }

    [Fact]
    public async Task Tauschen_uebernimmt_genau_die_Vorschau_Hand_Menge_wandert_auf_die_gleiche_Zutat_die_uebrigen_entfallen()
    {
        var gemuese = await KuecheTestHelfer.RezeptAsync(_s, "Gemüsepfanne", "4");
        var zwiebelnAlt = await KuecheTestHelfer.ZutatAsync(_s, Id(gemuese), "Zwiebeln", "1", "kg");
        var paprikaAlt = await KuecheTestHelfer.ZutatAsync(_s, Id(gemuese), "Paprika", "4", "Stück");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        var zwiebelnNeu = await KuecheTestHelfer.ZutatAsync(_s, Id(chili), "zwiebeln", "2", "kg");
        await KuecheTestHelfer.ZutatAsync(_s, Id(chili), "Paprika", "2", "Stk.");
        var gericht = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Mittagessen"), Id(gemuese));
        (await KuecheTestHelfer.HandMengeAsync(_s, Id(gericht), Id(zwiebelnAlt), "0,5", "3")).Dispose();
        (await KuecheTestHelfer.HandMengeAsync(_s, Id(gericht), Id(paprikaAlt), "2", "6")).Dispose();
        var vorschau = await JsonAsync(await VorschauAsync(Id(gericht), Id(chili)));
        // Die Zwiebeln bleiben gleich (Hand-Menge gewandert), die Paprika ist neu (andere Einheit, ohne Hand-Menge).
        Assert.Equal(["zwiebeln 3 kg von Hand", "Paprika 1 Stk. *"], Zeilen(vorschau.GetProperty("nachher")));
        Assert.Equal(["Zwiebeln 3 kg von Hand", "Paprika 6 Stück von Hand *"], Zeilen(vorschau.GetProperty("vorher")));

        using var antwort = await TauschenAsync(Id(gericht), Version(vorschau), Id(chili), _s.Tobias);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var getauscht = await JsonAsync(antwort);
        Assert.Equal((Id(chili), Id(gericht)), (getauscht.GetProperty("rezeptId").GetGuid(), Id(getauscht)));
        Assert.NotEqual(Version(gericht), Version(getauscht));
        var posten = KuecheTestHelfer.Posten(await KuecheTestHelfer.EinkaufslisteAsync(_s));
        Assert.Equal(["zwiebeln 3 kg Chili sin Carne Mittagessen von Hand", "Paprika 1 Stk. Chili sin Carne Mittagessen"], posten);
        Assert.Equal(1L, await AnzahlAsync("menge_von_hand"));
        Assert.Equal(1L, await AnzahlAsync("menge_von_hand", "\"ZutatId\" = @z", ("z", Id(zwiebelnNeu))));
    }

    [Fact]
    public async Task Tauschen_mit_veralteter_Version_409_konflikt_und_nichts_geaendert()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        var nudeln = await KuecheTestHelfer.RezeptAsync(_s, "Nudeln", "4");
        var gericht = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Frühstück"), Id(pfannkuchen));
        using (var erst = await TauschenAsync(Id(gericht), Version(gericht), Id(chili)))
        {
            Assert.Equal(HttpStatusCode.OK, erst.StatusCode);
        }

        using var veraltet = await TauschenAsync(Id(gericht), Version(gericht), Id(nudeln), _s.Tobias);

        Assert.Equal(HttpStatusCode.Conflict, veraltet.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(veraltet)));
        Assert.Equal(Id(chili), await WertAsync<Guid>(_s.Verbindung, "SELECT \"RezeptId\" FROM kueche.gericht WHERE \"Id\" = @g", ("g", Id(gericht))));
        using var fremd = await TauschenAsync(Id(gericht), Version(gericht), Guid.CreateVersion7());
        await FeldfehlerAsync(fremd, "rezeptId");
        using var unbekannt = await TauschenAsync(Guid.CreateVersion7(), 1, Id(chili));
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
    }

    [Fact]
    public async Task Hand_Menge_nach_der_Vorschau_macht_die_Vorschau_veraltet_Tauschen_409_konflikt_und_nichts_geaendert()
    {
        var gemuese = await KuecheTestHelfer.RezeptAsync(_s, "Gemüsepfanne", "4");
        var zwiebeln = await KuecheTestHelfer.ZutatAsync(_s, Id(gemuese), "Zwiebeln", "1", "kg");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        await KuecheTestHelfer.ZutatAsync(_s, Id(chili), "Zwiebeln", "2", "kg");
        var gericht = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Mittagessen"), Id(gemuese));
        var vorschau = await JsonAsync(await VorschauAsync(Id(gericht), Id(chili)));

        using (var hand = await KuecheTestHelfer.HandMengeAsync(_s, Id(gericht), Id(zwiebeln), "0,5", "3"))
        {
            Assert.Equal(HttpStatusCode.OK, hand.StatusCode);
        }

        using var antwort = await TauschenAsync(Id(gericht), Version(vorschau), Id(chili));

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        Assert.Equal(Id(gemuese), await WertAsync<Guid>(_s.Verbindung, "SELECT \"RezeptId\" FROM kueche.gericht WHERE \"Id\" = @g", ("g", Id(gericht))));
        Assert.Equal(1L, await AnzahlAsync("menge_von_hand", "\"ZutatId\" = @z", ("z", Id(zwiebeln))));

        // Auch das Zurücknehmen der Hand-Menge macht eine Vorschau veraltet.
        var neueVorschau = await JsonAsync(await VorschauAsync(Id(gericht), Id(chili)));
        (await KuecheTestHelfer.HandMengeAsync(_s, Id(gericht), Id(zwiebeln), "3", "")).Dispose();
        using var wieder = await TauschenAsync(Id(gericht), Version(neueVorschau), Id(chili));
        Assert.Equal(HttpStatusCode.Conflict, wieder.StatusCode);
    }

    [Fact]
    public async Task Zwei_gleichzeitige_Tausch_POST_mit_gleicher_Version_genau_einer_gewinnt()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        var nudeln = await KuecheTestHelfer.RezeptAsync(_s, "Nudeln", "4");
        for (var runde = 0; runde < 3; runde++)
        {
            var gericht = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Abendessen"), Id(pfannkuchen));

            var antworten = await Task.WhenAll(TauschenAsync(Id(gericht), Version(gericht), Id(chili), _s.Tobias), TauschenAsync(Id(gericht), Version(gericht), Id(nudeln)));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], antworten.Select(a => a.StatusCode).Order());
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }
        }
    }

    [Fact]
    public async Task Ein_geloeschtes_Rezept_nimmt_seine_Gerichte_und_deren_Hand_Mengen_mit()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var mehl = await KuecheTestHelfer.ZutatAsync(_s, Id(pfannkuchen), "Mehl", "400", "g");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        var g1 = await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Frühstück"), Id(pfannkuchen));
        await GerichtNeuAsync(await MahlzeitAsync(new DateOnly(2027, 8, 4), "Frühstück"), Id(pfannkuchen));
        await GerichtNeuAsync(await MahlzeitAsync(Dienstag, "Mittagessen"), Id(chili));
        (await KuecheTestHelfer.HandMengeAsync(_s, Id(g1), Id(mehl), "200", "1")).Dispose();

        using var loeschen = await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{Kueche(_s.X)}/rezepte/{Id(pfannkuchen)}");

        Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        Assert.Equal((1L, 0L), (await AnzahlAsync("gericht"), await AnzahlAsync("menge_von_hand")));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufene_Freizeit_Schreiben_409_freizeit_abgeschlossen_Essensplan_und_Vorschau_200()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        var fruehstueck = await MahlzeitAsync(Dienstag, "Frühstück");
        var gericht = await GerichtNeuAsync(fruehstueck, Id(pfannkuchen));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        foreach (var antwort in new[]
                 {
                     await VormerkenAsync(fruehstueck, Id(chili)),
                     await TauschenAsync(Id(gericht), Version(gericht), Id(chili), _s.Tobias),
                     await EntfernenAsync(Id(gericht)),
                 })
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            antwort.Dispose();
        }

        Assert.Single((await EssensplanAsync(_s.Tobias)).GetProperty("tage")[1].GetProperty("mahlzeiten")[0].GetProperty("gerichte").EnumerateArray());
        using var vorschau = await VorschauAsync(Id(gericht), Id(chili), _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, vorschau.StatusCode);
    }

    // ---------- Rechte und Schalter ----------

    /// <summary>Die fünf Routen der Gerichte für einen Client unter einer Freizeit; Mahlzeit, Rezepte und Gericht gehören zu X.</summary>
    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid mahlzeit, Guid rezept, Guid anderes, Guid gericht, uint version) =>
    [
        () => client.GetAsync($"{Kueche(freizeit)}/essensplan", Abbruch),
        () => VormerkenAsync(mahlzeit, rezept, client, freizeit),
        () => VorschauAsync(gericht, anderes, client, freizeit),
        () => TauschenAsync(gericht, version, anderes, client, freizeit),
        () => EntfernenAsync(gericht, client, freizeit),
    ];

    private async Task<List<HttpStatusCode>> StatusAsync(HttpClient client, Guid freizeit, Guid mahlzeit, Guid rezept, Guid anderes, Guid gericht, uint version)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, mahlzeit, rezept, anderes, gericht, version))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_2xx_auf_allen_fuenf_Routen()
    {
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        var mahlzeit = await MahlzeitAsync(Dienstag, "Frühstück");
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            var gericht = await GerichtNeuAsync(mahlzeit, Id(pfannkuchen));

            var status = await StatusAsync(client, _s.X, mahlzeit, Id(pfannkuchen), Id(chili), Id(gericht), Version(gericht));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NoContent], status);
        }
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_unbekannte_Freizeit_404_ohne_Sitzung_401_Schalter_aus_404()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var pfannkuchen = await KuecheTestHelfer.RezeptAsync(_s, "Pfannkuchen", "4");
        var chili = await KuecheTestHelfer.RezeptAsync(_s, "Chili sin Carne", "4");
        var mahlzeit = await MahlzeitAsync(Dienstag, "Frühstück");
        var gericht = await GerichtNeuAsync(mahlzeit, Id(pfannkuchen));
        var faelle = new (HttpClient Client, Guid Freizeit, HttpStatusCode Erwartet)[]
        {
            (_s.Andrea, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, fremde, HttpStatusCode.NotFound),
            (sara, fremde, HttpStatusCode.NotFound),
            (leitungZ, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
            (_s.Anonym, _s.X, HttpStatusCode.Unauthorized),
        };

        foreach (var (client, freizeit, erwartet) in faelle)
        {
            Assert.All(await StatusAsync(client, freizeit, mahlzeit, Id(pfannkuchen), Id(chili), Id(gericht), Version(gericht)), s => Assert.Equal(erwartet, s));
        }

        await KuecheTestHelfer.VerpflegungAsync(_s, _s.X, an: false);
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.All(await StatusAsync(client, _s.X, mahlzeit, Id(pfannkuchen), Id(chili), Id(gericht), Version(gericht)), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.Equal(1L, await AnzahlAsync("gericht"));
        Assert.Equal(Id(pfannkuchen), await WertAsync<Guid>(_s.Verbindung, "SELECT \"RezeptId\" FROM kueche.gericht"));
    }

    // ---------- Leseschnittstelle Mahlzeiten (Programm.Contracts) ----------

    private async Task<IReadOnlyList<FreizeitMahlzeit>> MahlzeitenAsync(Guid freizeit, Guid organisation)
    {
        await using var scope = _s.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await scope.ServiceProvider.GetRequiredService<IFreizeitMahlzeitenLesen>().MahlzeitenAsync(freizeit, Abbruch);
    }

    [Fact]
    public async Task Mahlzeiten_legt_fehlende_an_liefert_sie_nach_Tag_und_Uhrzeit_ohne_normale_Programmpunkte_fremder_Mandant_leer()
    {
        var mahlzeiten = await MahlzeitenAsync(_s.Z, _s.B);

        Assert.Equal(9, mahlzeiten.Count);
        Assert.Equal(
            [
                (new DateOnly(2027, 9, 1), (TimeOnly?)new TimeOnly(8, 0), "Frühstück", FreizeitMahlzeitArt.Fruehstueck),
                (new DateOnly(2027, 9, 1), new TimeOnly(12, 30), "Mittagessen", FreizeitMahlzeitArt.Mittagessen),
                (new DateOnly(2027, 9, 1), new TimeOnly(18, 30), "Abendessen", FreizeitMahlzeitArt.Abendessen),
            ],
            mahlzeiten.Take(3).Select(m => (m.Tag, m.Beginn, m.Titel, m.MahlzeitArt)));
        Assert.Equal(new DateOnly(2027, 9, 3), mahlzeiten[^1].Tag);
        Assert.Equal(9L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM programm.programmpunkt WHERE \"FreizeitId\" = @z", ("z", _s.Z)));

        using var normal = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.Z}/programm/punkte", new { titel = "Bachwanderung", tag = "2027-09-02", beginn = "10:00" });
        Assert.Equal(HttpStatusCode.Created, normal.StatusCode);
        Assert.Equal(9, (await MahlzeitenAsync(_s.Z, _s.B)).Count);
        Assert.Empty(await MahlzeitenAsync(_s.Z, _s.A));
        Assert.Empty(await MahlzeitenAsync(Guid.CreateVersion7(), _s.B));
    }
}
