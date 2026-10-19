using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Fragen;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.KuecheTestHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 4-2, Einkaufsliste mit Allergen-Abgleich (CAP-29, FR-47 bis FR-49, AD-8, AD-13, AD-16, AD-17, AD-19, AD-20, AD-30) gegen echtes PostgreSQL:
/// die berechnete Liste, Hand-Mengen (bleiben bei geänderter Personenzahl, zurücknehmen, Konflikt), Hand-Posten mit Feldfehlern und Autosave (auch
/// gleichzeitig löschen), der Abgleich mit Ersatzportionen und Protokoll ohne lesende Person, die Pflichtfälle der Freizeit-Route für die fünf Routen,
/// der Schalter Verpflegung, die abgeschlossene Freizeit, der Lösch-Lauf und die neuen Leseschnittstellen von Freizeiten und Personen. Aufbau wie
/// <see cref="RezepteTests"/>; X fragt für Teilnehmende Allergien ab, Emma hat „milch“ im Freizeitpass.
/// </summary>
public sealed class EinkaufslisteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly DateOnly Dienstag = new(2027, 8, 3);

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres);
        await VerpflegungAsync(_s, _s.X);
        await VerpflegungAsync(_s, _s.Z);
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string PostenPfad(Guid freizeit) => $"{KuechePfad(freizeit)}/einkaufsliste/posten";

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static Guid PostenId(JsonElement json) => json.GetProperty("postenId").GetGuid();

    private async Task<Guid> MahlzeitAsync(DateOnly tag, string titel, Guid? freizeit = null)
    {
        using var antwort = await _s.Miriam.GetAsync($"{KuechePfad(freizeit ?? _s.X)}/essensplan", Abbruch);
        return (await JsonAsync(antwort)).GetProperty("tage").EnumerateArray()
            .Single(t => t.GetProperty("tag").GetString() == tag.ToString("yyyy-MM-dd"))
            .GetProperty("mahlzeiten").EnumerateArray()
            .Single(m => m.GetProperty("titel").GetString() == titel)
            .GetProperty("programmpunktId").GetGuid();
    }

    private async Task<Guid> GerichtAsync(DateOnly tag, string mahlzeit, Guid rezept, Guid? freizeit = null)
    {
        using var antwort = await PostAsync(_s.Miriam, $"{KuechePfad(freizeit ?? _s.X)}/gerichte", new { programmpunktId = await MahlzeitAsync(tag, mahlzeit, freizeit), rezeptId = rezept });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return Id(await JsonAsync(antwort));
    }

    /// <summary>Pfannkuchen (Basis 4) mit Mehl 400 g (Gluten), Milch 0,75 l (Milch) und Salz ohne Menge; liefert Rezept und Mehl.</summary>
    private async Task<(Guid Rezept, Guid Mehl)> PfannkuchenAsync(Guid? freizeit = null)
    {
        var rezept = Id(await RezeptAsync(_s, "Pfannkuchen", "4", freizeit));
        var mehl = Id(await ZutatAsync(_s, rezept, "Mehl", "400", "g", "gluten", freizeit));
        await ZutatAsync(_s, rezept, "Milch", "0,75", "l", "milch", freizeit);
        await ZutatAsync(_s, rezept, "Salz", freizeit: freizeit);
        return (rezept, mehl);
    }

    private Task<HttpResponseMessage> PostenAnlegenAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, PostenPfad(freizeit ?? _s.X), koerper);

    private async Task<JsonElement> PostenNeuAsync(string bezeichnung, string? menge = null, string? einheit = null, string? tag = null)
    {
        using var antwort = await PostenAnlegenAsync(new { bezeichnung, menge, einheit, tag });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> PostenPatchAsync(Guid posten, string feld, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, $"{PostenPfad(freizeit ?? _s.X)}/{posten}", new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = alterWert, [feld] = wert });

    private Task<HttpResponseMessage> PostenLoeschenAsync(Guid posten, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, $"{PostenPfad(freizeit ?? _s.X)}/{posten}");

    private static async Task FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
    }

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM kueche.{tabelle} WHERE {bedingung}", parameter);

    private Task<long> ProtokollAsync(Guid person) =>
        WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @p AND \"Zweck\" = 3 AND \"LesendePersonId\" IS NULL", ("p", person));

    /// <summary>Mia mit Gluten im Freizeitpass, angemeldet und bestätigt; Emma (Milch) nur angemeldet.</summary>
    private async Task MiaBestaetigtEmmaEingegangenAsync()
    {
        await _s.AntwortAsync(_s.Mia, "allergien", "gluten");
        await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(_s.Mia));
        await _s.KindAnmeldenAsync(_s.Emma);
        await _s.ZustellenAsync();
    }

    // ---------- Liste ----------

    [Fact]
    public async Task Vormerken_Pfannkuchen_an_Dienstag_Fruehstueck_bei_8_Personen_ergibt_Mehl_800_g()
    {
        var (pfannkuchen, mehl) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await TeamAuffuellenAsync(_s, 6);

        var liste = await EinkaufslisteAsync(_s, _s.Tobias);

        Assert.Equal(["allergene", "allergienAbgefragt", "personenzahl", "tage"], Namen(liste));
        Assert.Equal(8, liste.GetProperty("personenzahl").GetInt32());
        var tag = Assert.Single(liste.GetProperty("tage").EnumerateArray());
        Assert.Equal(["ersatzportionen", "posten", "tag"], Namen(tag));
        Assert.Equal("2027-08-03", tag.GetProperty("tag").GetString());
        Assert.Equal(["Mehl 800 g Pfannkuchen Frühstück", "Milch 1.5 l Pfannkuchen Frühstück", "Salz - - Pfannkuchen Frühstück"], Posten(liste));
        var posten = tag.GetProperty("posten")[0];
        Assert.Equal(["bezeichnung", "einheit", "gerichtId", "mahlzeitName", "menge", "postenId", "rezeptName", "version", "vonHand", "zutatId"], Namen(posten));
        Assert.Equal((gericht, mehl, JsonValueKind.Null), (posten.GetProperty("gerichtId").GetGuid(), posten.GetProperty("zutatId").GetGuid(), posten.GetProperty("postenId").ValueKind));
    }

    [Fact]
    public async Task Ohne_Gerichte_und_Posten_ist_die_Liste_leer()
    {
        var liste = await EinkaufslisteAsync(_s);

        Assert.Empty(liste.GetProperty("tage").EnumerateArray());
        Assert.Equal((2, true), (liste.GetProperty("personenzahl").GetInt32(), liste.GetProperty("allergienAbgefragt").GetBoolean()));
    }

    // ---------- Hand-Menge ----------

    [Fact]
    public async Task Hand_Menge_ersetzt_die_berechnete_Menge_und_bleibt_bei_geaenderter_Personenzahl_anderer_Altwert_409_leer_zurueck()
    {
        var (pfannkuchen, mehl) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await TeamAuffuellenAsync(_s, 6);

        using var gesetzt = await HandMengeAsync(_s, gericht, mehl, "800", "1", _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, gesetzt.StatusCode);
        var json = await JsonAsync(gesetzt);
        Assert.Equal(["menge", "version"], Namen(json));
        Assert.Equal("1", json.GetProperty("menge").GetString());
        Assert.Equal("Mehl 1 g Pfannkuchen Frühstück von Hand", Posten(await EinkaufslisteAsync(_s))[0]);

        // Personenzahl ändert sich: die Hand-Menge bleibt, die anderen Posten rechnen neu.
        await TeamAuffuellenAsync(_s, 4);
        var liste = await EinkaufslisteAsync(_s);
        Assert.Equal(["Mehl 1 g Pfannkuchen Frühstück von Hand", "Milch 2.25 l Pfannkuchen Frühstück"], Posten(liste).Take(2));
        Assert.NotEqual(0u, liste.GetProperty("tage")[0].GetProperty("posten")[0].GetProperty("version").GetUInt32());

        using var konflikt = await HandMengeAsync(_s, gericht, mehl, "800", "2");
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        using var zurueck = await HandMengeAsync(_s, gericht, mehl, "1", "");
        Assert.Equal(HttpStatusCode.OK, zurueck.StatusCode);
        Assert.Equal("1200", (await JsonAsync(zurueck)).GetProperty("menge").GetString());
        Assert.Equal("Mehl 1200 g Pfannkuchen Frühstück", Posten(await EinkaufslisteAsync(_s))[0]);
        Assert.Equal(0L, await AnzahlAsync("menge_von_hand"));
    }

    [Fact]
    public async Task Hand_Menge_ungueltig_400_Zutat_eines_anderen_Rezepts_oder_Gericht_einer_anderen_Freizeit_404()
    {
        var (pfannkuchen, mehl) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var chili = Id(await RezeptAsync(_s, "Chili sin Carne", "4"));
        var bohnen = Id(await ZutatAsync(_s, chili, "Kidneybohnen", "500", "g"));
        var (inZ, mehlInZ) = await PfannkuchenAsync(_s.Z);
        var gerichtInZ = await GerichtAsync(new DateOnly(2027, 9, 1), "Frühstück", inZ, _s.Z);

        foreach (var menge in new[] { "0", "x", "1.000", "-1" })
        {
            using var antwort = await HandMengeAsync(_s, gericht, mehl, "200", menge);
            await FeldfehlerAsync(antwort, "menge");
        }

        using var fremdeZutat = await HandMengeAsync(_s, gericht, bohnen, "250", "1");
        using var fremdesGericht = await HandMengeAsync(_s, gerichtInZ, mehlInZ, "200", "1");
        using var unbekannt = await HandMengeAsync(_s, Guid.CreateVersion7(), mehl, "200", "1");
        Assert.All(new[] { fremdeZutat, fremdesGericht, unbekannt }, a => Assert.Equal(HttpStatusCode.NotFound, a.StatusCode));
        Assert.Equal(0L, await AnzahlAsync("menge_von_hand"));
    }

    // ---------- Hand-Posten ----------

    [Fact]
    public async Task Hand_Posten_anlegen_201_stehen_am_Tag_ohne_Tag_zuletzt_Feldfehler_400()
    {
        var (pfannkuchen, _) = await PfannkuchenAsync();
        await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);

        var aepfel = await PostenNeuAsync(" Äpfel ", "3", "kg", "2027-08-03");
        await PostenNeuAsync("Kaffee");

        Assert.Equal(["bezeichnung", "einheit", "gerichtId", "mahlzeitName", "menge", "postenId", "rezeptName", "version", "vonHand", "zutatId"], Namen(aepfel));
        Assert.Equal(("Äpfel", 3m, true), (aepfel.GetProperty("bezeichnung").GetString(), aepfel.GetProperty("menge").GetDecimal(), aepfel.GetProperty("vonHand").GetBoolean()));
        var liste = await EinkaufslisteAsync(_s);
        Assert.Equal(["2027-08-03", null], liste.GetProperty("tage").EnumerateArray().Select(t => t.GetProperty("tag").GetString()));
        Assert.Equal(
            ["Mehl 200 g Pfannkuchen Frühstück", "Milch 0.38 l Pfannkuchen Frühstück", "Salz - - Pfannkuchen Frühstück", "Äpfel 3 kg - - von Hand", "Kaffee - - - - von Hand"],
            Posten(liste));

        foreach (var (koerper, feld) in new (object, string)[]
                 {
                     (new { menge = "1" }, "bezeichnung"),
                     (new { bezeichnung = " " }, "bezeichnung"),
                     (new { bezeichnung = new string('b', 201) }, "bezeichnung"),
                     (new { bezeichnung = "Äpfel", tag = "2027-08-14" }, "tag"),
                     (new { bezeichnung = "Äpfel", tag = "2027-08-01" }, "tag"),
                     (new { bezeichnung = "Äpfel", tag = "morgen" }, "tag"),
                     (new { bezeichnung = "Äpfel", menge = "x" }, "menge"),
                     (new { bezeichnung = "Äpfel", einheit = new string('e', 31) }, "einheit"),
                 })
        {
            using var antwort = await PostenAnlegenAsync(koerper);
            await FeldfehlerAsync(antwort, feld);
        }

        Assert.Equal(2L, await AnzahlAsync("einkaufsposten"));
    }

    [Fact]
    public async Task Hand_Posten_Autosave_normalisiert_Konflikt_409_Tag_ausserhalb_400_Loeschen_204_nochmal_404()
    {
        var posten = PostenId(await PostenNeuAsync("Äpfel", "3", "kg", "2027-08-03"));

        using var menge = await PostenPatchAsync(posten, "menge", "3", "2.5", _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, menge.StatusCode);
        var json = await JsonAsync(menge);
        Assert.Equal(["menge", "version"], Namen(json));
        Assert.Equal("2,5", json.GetProperty("menge").GetString());
        using var tag = await PostenPatchAsync(posten, "tag", "2027-08-03", "2027-08-13");
        Assert.Equal("2027-08-13", (await JsonAsync(tag)).GetProperty("tag").GetString());
        using var ohneTag = await PostenPatchAsync(posten, "tag", "2027-08-13", "");
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(ohneTag)).GetProperty("tag").ValueKind);
        using var bezeichnung = await PostenPatchAsync(posten, "bezeichnung", "Äpfel", " Birnen ");
        Assert.Equal("Birnen", (await JsonAsync(bezeichnung)).GetProperty("bezeichnung").GetString());
        using var einheit = await PostenPatchAsync(posten, "einheit", "kg", "Stück");
        Assert.Equal("Stück", (await JsonAsync(einheit)).GetProperty("einheit").GetString());

        using var konflikt = await PostenPatchAsync(posten, "menge", "3", "4");
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));
        foreach (var (feld, wert) in new[] { ("tag", "2027-07-31"), ("tag", "x"), ("menge", "0"), ("bezeichnung", "") })
        {
            using var antwort = await PostenPatchAsync(posten, feld, null, wert);
            await FeldfehlerAsync(antwort, feld);
        }

        Assert.Equal(["Birnen 2.5 Stück - - von Hand"], Posten(await EinkaufslisteAsync(_s)));
        using var weg = await PostenLoeschenAsync(posten, _s.Tobias);
        using var nochmal = await PostenLoeschenAsync(posten);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (weg.StatusCode, nochmal.StatusCode));
    }

    [Fact]
    public async Task Hand_Posten_einer_anderen_Freizeit_404_gleichzeitig_loeschen_204_und_404_nie_500()
    {
        using var inZAntwort = await PostenAnlegenAsync(new { bezeichnung = "Brot" }, freizeit: _s.Z);
        var inZ = PostenId(await JsonAsync(inZAntwort));
        using var fremdPatch = await PostenPatchAsync(inZ, "bezeichnung", "Brot", "Brötchen");
        using var fremdLoeschen = await PostenLoeschenAsync(inZ);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (fremdPatch.StatusCode, fremdLoeschen.StatusCode));

        for (var runde = 0; runde < 5; runde++)
        {
            var posten = PostenId(await PostenNeuAsync($"Äpfel {runde}"));

            var antworten = await Task.WhenAll(PostenLoeschenAsync(posten, _s.Tobias), PostenLoeschenAsync(posten));

            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], antworten.Select(a => a.StatusCode).Order());
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }
        }

        Assert.Equal(1L, await AnzahlAsync("einkaufsposten"));
    }

    // ---------- Abgleich ----------

    [Fact]
    public async Task Abgleich_Mia_bestaetigt_mit_Gluten_ergibt_eine_Ersatzportion_Emma_nur_eingegangen_zaehlt_nicht_Protokoll_einmal_am_Tag()
    {
        await MiaBestaetigtEmmaEingegangenAsync();
        var (pfannkuchen, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);

        var liste = await EinkaufslisteAsync(_s, _s.Tobias);
        await EinkaufslisteAsync(_s);

        Assert.True(liste.GetProperty("allergienAbgefragt").GetBoolean());
        Assert.Equal(["gluten 1"], liste.GetProperty("allergene").EnumerateArray().Select(a => $"{a.GetProperty("allergen").GetString()} {a.GetProperty("anzahl").GetInt32()}"));
        var ersatz = Assert.Single(liste.GetProperty("tage")[0].GetProperty("ersatzportionen").EnumerateArray());
        Assert.Equal(["ersatzportionen", "gerichtId", "mahlzeitName", "rezeptName"], Namen(ersatz));
        Assert.Equal((gericht, "Pfannkuchen", "Frühstück"), (ersatz.GetProperty("gerichtId").GetGuid(), ersatz.GetProperty("rezeptName").GetString(), ersatz.GetProperty("mahlzeitName").GetString()));
        Assert.Equal(1, ersatz.GetProperty("ersatzportionen").GetProperty("anzahl").GetInt32());
        Assert.Equal(["gluten"], ersatz.GetProperty("ersatzportionen").GetProperty("allergene").EnumerateArray().Select(a => a.GetString()));

        using var plan = await _s.Tobias.GetAsync($"{KuechePfad(_s.X)}/essensplan", Abbruch);
        var imPlan = (await JsonAsync(plan)).GetProperty("tage")[1].GetProperty("mahlzeiten")[0].GetProperty("gerichte")[0].GetProperty("ersatzportionen");
        Assert.Equal(1, imPlan.GetProperty("anzahl").GetInt32());

        // Protokoll: genau ein Eintrag für Mia (ohne lesende Person), keiner für Emma; Katrin sieht ihn im Zugriffsprotokoll.
        Assert.Equal((1L, 0L), (await ProtokollAsync(_s.Mia), await ProtokollAsync(_s.Emma)));
        var eintrag = Assert.Single((await _s.Katrin.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>("/api/ich/zugriffsprotokoll", Abbruch))!);
        Assert.Equal((_s.Mia, ZugriffZweck.AllergeneFuerKueche, (Guid?)null, (string?)null, _s.X), (eintrag.BetroffenePersonId, eintrag.Zweck, eintrag.LesendePersonId, eintrag.LesenderName, eintrag.FreizeitId));
        Assert.Equal(_s.A, eintrag.OrganisationId);
    }

    [Fact]
    public async Task Abgleich_abgelaufene_Freigabe_zaehlt_nicht()
    {
        await MiaBestaetigtEmmaEingegangenAsync();
        var (pfannkuchen, _) = await PfannkuchenAsync();
        await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await AusfuehrenAsync(_s.Verbindung, "UPDATE personen.freigabe SET \"GueltigBis\" = @b WHERE \"PersonId\" = @p", ("b", _s.Factory.Uhr.GetUtcNow().AddMinutes(-1)), ("p", _s.Mia));

        var liste = await EinkaufslisteAsync(_s);

        Assert.Empty(liste.GetProperty("allergene").EnumerateArray());
        Assert.Empty(liste.GetProperty("tage")[0].GetProperty("ersatzportionen").EnumerateArray());
        Assert.Equal(0L, await ProtokollAsync(_s.Mia));
    }

    [Fact]
    public async Task Freizeit_fragt_keine_Allergien_ab_kein_Abgleich_und_kein_Protokoll()
    {
        await MiaBestaetigtEmmaEingegangenAsync();
        var (pfannkuchen, _) = await PfannkuchenAsync();
        await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await _s.FragenAsync(_s.X, "notfallkontakte");

        var liste = await EinkaufslisteAsync(_s);
        using var plan = await _s.Miriam.GetAsync($"{KuechePfad(_s.X)}/essensplan", Abbruch);

        Assert.False(liste.GetProperty("allergienAbgefragt").GetBoolean());
        Assert.Empty(liste.GetProperty("allergene").EnumerateArray());
        Assert.Empty(liste.GetProperty("tage")[0].GetProperty("ersatzportionen").EnumerateArray());
        var essensplan = await JsonAsync(plan);
        Assert.False(essensplan.GetProperty("allergienAbgefragt").GetBoolean());
        Assert.Equal(0, essensplan.GetProperty("tage")[1].GetProperty("mahlzeiten")[0].GetProperty("gerichte")[0].GetProperty("ersatzportionen").GetProperty("anzahl").GetInt32());
        Assert.Equal(0L, await ProtokollAsync(_s.Mia));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufene_Freizeit_Schreiben_409_freizeit_abgeschlossen_Liste_200()
    {
        var (pfannkuchen, mehl) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var posten = PostenId(await PostenNeuAsync("Äpfel"));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        foreach (var antwort in new[]
                 {
                     await HandMengeAsync(_s, gericht, mehl, "200", "1"),
                     await PostenAnlegenAsync(new { bezeichnung = "Brot" }),
                     await PostenPatchAsync(posten, "bezeichnung", "Äpfel", "Birnen", _s.Tobias),
                     await PostenLoeschenAsync(posten),
                 })
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            antwort.Dispose();
        }

        Assert.Equal(4, Posten(await EinkaufslisteAsync(_s, _s.Tobias)).Count);
    }

    // ---------- Rechte und Schalter ----------

    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid gericht, Guid zutat, Guid posten) =>
    [
        () => client.GetAsync($"{KuechePfad(freizeit)}/einkaufsliste", Abbruch),
        () => HandMengeAsync(_s, gericht, zutat, "200", "1", client, freizeit),
        () => PostenAnlegenAsync(new { bezeichnung = "Brot" }, client, freizeit),
        () => PostenPatchAsync(posten, "einheit", null, "Stück", client, freizeit),
        () => PostenLoeschenAsync(posten, client, freizeit),
    ];

    private async Task<List<HttpStatusCode>> StatusAsync(HttpClient client, Guid freizeit, Guid gericht, Guid zutat, Guid posten)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, gericht, zutat, posten))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_2xx_auf_allen_fuenf_Routen()
    {
        var (pfannkuchen, mehl) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            (await HandMengeAsync(_s, gericht, mehl, "1", "")).Dispose();
            var posten = PostenId(await PostenNeuAsync("Äpfel"));

            var status = await StatusAsync(client, _s.X, gericht, mehl, posten);

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent], status);
        }
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_unbekannte_Freizeit_404_ohne_Sitzung_401_Schalter_aus_404()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var (pfannkuchen, mehl) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var posten = PostenId(await PostenNeuAsync("Äpfel"));
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
            Assert.All(await StatusAsync(client, freizeit, gericht, mehl, posten), s => Assert.Equal(erwartet, s));
        }

        await VerpflegungAsync(_s, _s.X, an: false);
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.All(await StatusAsync(client, _s.X, gericht, mehl, posten), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.Equal((0L, 1L), (await AnzahlAsync("menge_von_hand"), await AnzahlAsync("einkaufsposten")));
    }

    // ---------- Lösch-Lauf ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_Gerichte_Hand_Mengen_und_Hand_Posten_der_abgelaufenen_Freizeit_die_andere_bleibt()
    {
        var (pfannkuchen, mehl) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        (await HandMengeAsync(_s, gericht, mehl, "200", "1")).Dispose();
        await PostenNeuAsync("Äpfel");
        var (inZ, mehlInZ) = await PfannkuchenAsync(_s.Z);
        var gerichtInZ = await GerichtAsync(new DateOnly(2027, 9, 1), "Frühstück", inZ, _s.Z);
        // In Z ist nur Miriam: 400 g für 4 sind 100 g für 1.
        using (var handInZ = await HandMengeAsync(_s, gerichtInZ, mehlInZ, "100", "1", freizeit: _s.Z))
        {
            Assert.Equal(HttpStatusCode.OK, handInZ.StatusCode);
        }

        (await PostenAnlegenAsync(new { bezeichnung = "Brot" }, freizeit: _s.Z)).Dispose();

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "KuecheLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        foreach (var tabelle in new[] { "gericht", "menge_von_hand", "einkaufsposten", "rezept", "zutat" })
        {
            Assert.Equal(0L, await AnzahlAsync(tabelle, "\"FreizeitId\" = @f", ("f", _s.X)));
            Assert.NotEqual(0L, await AnzahlAsync(tabelle, "\"FreizeitId\" = @f", ("f", _s.Z)));
        }
    }

    // ---------- Leseschnittstellen von Freizeiten und Personen ----------

    private async Task<T> MitMandantAsync<T>(Guid organisation, Func<IServiceProvider, Task<T>> lesen)
    {
        await using var scope = _s.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await lesen(scope.ServiceProvider);
    }

    [Fact]
    public async Task PersonenAsync_dieselben_Personen_wie_die_Personenzahl_jede_einmal_ohne_geloeschte()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        await _s.BestaetigenAsync(emma);
        await _s.KindAnmeldenAsync(_s.Mia);
        await _s.ZustellenAsync();
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"FreizeitId\", \"PersonId\", \"OrganisationId\", \"Rolle\", \"VergebenAm\") VALUES (@f, @p, @o, 2, now())",
            ("f", _s.X), ("p", _s.Emma), ("o", _s.A));

        var personen = await MitMandantAsync(_s.A, d => d.GetRequiredService<IFreizeitPersonenzahlLesen>().PersonenAsync(_s.X, Abbruch));

        Assert.Equal(new[] { _s.MiriamId, _s.TobiasId, _s.Emma }.Order(), personen.Order());
        Assert.Equal(3, await MitMandantAsync(_s.A, d => d.GetRequiredService<IFreizeitPersonenzahlLesen>().AnzahlAsync(_s.X, Abbruch)));

        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.anmeldung SET \"PersonId\" = @g WHERE \"Id\" = @a", ("g", GeloeschtePerson.Id), ("a", emma));
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM freizeiten.freizeit_rolle WHERE \"PersonId\" = @p", ("p", _s.Emma));
        Assert.Equal(new[] { _s.MiriamId, _s.TobiasId }.Order(), (await MitMandantAsync(_s.A, d => d.GetRequiredService<IFreizeitPersonenzahlLesen>().PersonenAsync(_s.X, Abbruch))).Order());
        Assert.Empty(await MitMandantAsync(_s.B, d => d.GetRequiredService<IFreizeitPersonenzahlLesen>().PersonenAsync(_s.X, Abbruch)));
    }

    [Fact]
    public async Task FragtAbAsync_fuer_Teilnehmende_oder_Mitarbeitende_sonst_nicht_fremder_Mandant_nicht()
    {
        Task<bool> FragtAbAsync(Guid freizeit, string kennung, Guid organisation) =>
            MitMandantAsync(organisation, d => d.GetRequiredService<IFreizeitFragenLesen>().FragtAbAsync(freizeit, kennung, Abbruch));

        Assert.True(await FragtAbAsync(_s.X, "allergien", _s.A));
        Assert.False(await FragtAbAsync(_s.X, "tetanus", _s.A));
        Assert.False(await FragtAbAsync(_s.X, "allergien", _s.B));
        Assert.False(await FragtAbAsync(Guid.CreateVersion7(), "allergien", _s.A));

        await _s.FragenAsync(_s.X, "notfallkontakte");
        Assert.False(await FragtAbAsync(_s.X, "allergien", _s.A));
        var auswahl = await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/fragen", Abbruch));
        using var mitarbeitende = await SendenAsync(
            _s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{_s.X}/fragen",
            new Dictionary<string, object?> { ["version"] = auswahl.GetProperty("version").GetUInt32(), ["alterWert"] = auswahl.GetProperty("mitarbeitende").GetString(), ["mitarbeitende"] = "allergien" });
        Assert.Equal(HttpStatusCode.OK, mitarbeitende.StatusCode);
        Assert.True(await FragtAbAsync(_s.X, "allergien", _s.A));
    }

    private Task<IReadOnlyList<IReadOnlyList<string>>> ProfileAsync(params Guid[] personen) =>
        MitMandantAsync(_s.A, d => d.GetRequiredService<IFreizeitpassAllergene>().ProfileAsync(_s.X, personen, Abbruch));

    [Fact]
    public async Task Allergen_Profile_nur_bestaetigt_gueltig_freigegeben_sortiert_ohne_leere_Protokoll_einmal_am_Tag_ohne_lesende_Person()
    {
        await _s.AntwortAsync(_s.Mia, "allergien", "milch,gluten");
        await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(_s.Mia));
        await _s.KindAnmeldenAsync(_s.Emma);
        var lena = await FamilienHelfer.KindAsync(_s.Katrin, "Lena", new DateOnly(2015, 3, 1));
        await _s.AntwortAsync(lena, "allergien", "eier");
        await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(lena));
        var ohne = await FamilienHelfer.KindAsync(_s.Katrin, "Paul", new DateOnly(2015, 6, 1));
        await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(ohne));
        await _s.ZustellenAsync();

        // Emma ist nur eingegangen (Stufe Leitung), Paul hat keine Antwort, Tobias keine Freigabe als Teilnehmer; fremde Kennungen fallen weg.
        var profile = await ProfileAsync(_s.Mia, _s.Emma, lena, ohne, _s.TobiasId, Guid.CreateVersion7());
        await ProfileAsync(_s.Mia, lena);

        Assert.Equal([["eier"], ["gluten", "milch"]], profile.Select(p => p.ToList()));
        Assert.Equal((1L, 1L, 0L, 0L), (await ProtokollAsync(_s.Mia), await ProtokollAsync(lena), await ProtokollAsync(_s.Emma), await ProtokollAsync(ohne)));
        Assert.Equal(_s.A, await WertAsync<Guid>(_s.Verbindung, "SELECT \"OrganisationId\" FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @p", ("p", _s.Mia)));

        // Am nächsten Tag ein neuer Eintrag.
        _s.Factory.Uhr.Advance(TimeSpan.FromDays(1));
        await ProfileAsync(_s.Mia);
        Assert.Equal(2L, await ProtokollAsync(_s.Mia));
    }

    [Fact]
    public async Task Allergen_Profile_Frage_nicht_freigegeben_abgelaufen_oder_Schluessel_vernichtet_die_Person_faellt_weg()
    {
        await _s.AntwortAsync(_s.Mia, "allergien", "gluten");
        var lena = await FamilienHelfer.KindAsync(_s.Katrin, "Lena", new DateOnly(2015, 3, 1));
        await _s.AntwortAsync(lena, "allergien", "eier");
        foreach (var kind in new[] { _s.Mia, _s.Emma, lena })
        {
            await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(kind));
        }

        await _s.ZustellenAsync();
        Assert.Equal(3, (await ProfileAsync(_s.Mia, _s.Emma, lena)).Count);

        await AusfuehrenAsync(_s.Verbindung, "UPDATE personen.freigabe SET \"Fragen\" = '{notfallkontakte}' WHERE \"PersonId\" = @p", ("p", _s.Mia));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE personen.freigabe SET \"GueltigBis\" = @b WHERE \"PersonId\" = @p", ("b", _s.Factory.Uhr.GetUtcNow()), ("p", _s.Emma));
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM personen.personen_schluessel WHERE \"PersonId\" = @p", ("p", lena));

        Assert.Empty(await ProfileAsync(_s.Mia, _s.Emma, lena));
    }
}
