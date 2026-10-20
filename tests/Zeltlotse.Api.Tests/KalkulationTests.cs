using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 5-1, Kalkulation und Beitragsrechner (CAP-32, FR-54 bis FR-57, AD-16, AD-17, AD-19, AD-20, AD-30) gegen echtes PostgreSQL: die Kalkulation
/// einer neuen Freizeit, das Ergebnis nach der Rechenregel, Autosave der Planzahlen, Kategorien und Einnahmen mit Normalisierung, Feldfehlern und
/// Konfliktregel (auch der gleichzeitige erste PATCH), Einnahmen anlegen und löschen, die Pflichtfälle der Freizeit-Route für alle sechs Routen, der
/// Schalter Finanzen, die abgeschlossene Freizeit und der Lösch-Lauf. Freizeit X (Organisation A, 02.08.–13.08.2027, 12 Tage; Org-Admin Andrea,
/// Leitung Miriam, Team Tobias) hat Finanzen an; Freizeit Z gehört Organisation B (3 Tage, Leitung Miriam).
/// </summary>
public sealed class KalkulationTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres);
        await SchaltenAsync(_s.X, an: true);
        await SchaltenAsync(_s.Z, an: true);
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/finanzen/kalkulation";

    private static string KategoriePfad(Guid freizeit, string kategorie) => $"{Pfad(freizeit)}/kategorien/{kategorie}";

    private static string EinnahmenPfad(Guid freizeit) => $"{Pfad(freizeit)}/einnahmen";

    private static string EinnahmePfad(Guid freizeit, Guid einnahme) => $"{EinnahmenPfad(freizeit)}/{einnahme}";

    /// <summary>Schaltet Finanzen mit dem aktuellen Stand als altem Wert (Miriam ist Leitung von X und Z).</summary>
    private async Task SchaltenAsync(Guid freizeit, bool an)
    {
        using var stand = await _s.Miriam.GetAsync($"/api/freizeiten/{freizeit}/module", Abbruch);
        var json = await JsonAsync(stand);
        var koerper = new Dictionary<string, object?>
        {
            ["version"] = json.GetProperty("version").GetUInt32(),
            ["alterWert"] = json.GetProperty("finanzen").GetBoolean() ? "true" : "false",
            ["finanzen"] = an ? "true" : "false",
        };
        using var antwort = await SendenAsync(_s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/module", koerper);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private async Task<JsonElement> KalkulationAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(Pfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> PlanzahlAsync(string feld, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, Pfad(freizeit ?? _s.X), Koerper(0, alterWert, feld, wert));

    private Task<HttpResponseMessage> KategorieAsync(
        string kategorie, string feld, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, KategoriePfad(freizeit ?? _s.X, kategorie), Koerper(0, alterWert, feld, wert));

    private Task<HttpResponseMessage> EinnahmeAnlegenAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, EinnahmenPfad(freizeit ?? _s.X), koerper);

    private async Task<JsonElement> EinnahmeNeuAsync(string bezeichnung, string? betrag, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await EinnahmeAnlegenAsync(new { bezeichnung, betrag }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> EinnahmePatchAsync(Guid einnahme, string feld, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, EinnahmePfad(freizeit ?? _s.X, einnahme), Koerper(0, alterWert, feld, wert));

    private Task<HttpResponseMessage> EinnahmeLoeschenAsync(Guid einnahme, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, EinnahmePfad(freizeit ?? _s.X, einnahme));

    private async Task OkAsync(Task<HttpResponseMessage> aufruf)
    {
        using var antwort = await aufruf;
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM finanzen.{tabelle} WHERE {bedingung}", parameter);

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static decimal? Zahl(JsonElement json, string feld) => json.GetProperty(feld).ValueKind == JsonValueKind.Null ? null : json.GetProperty(feld).GetDecimal();

    private static JsonElement Kategorie(JsonElement kalkulation, string name) =>
        kalkulation.GetProperty("kategorien").EnumerateArray().Single(k => k.GetProperty("kategorie").GetString() == name);

    private static async Task FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
    }

    // ---------- Lesen und Rechnen ----------

    [Fact]
    public async Task Neue_Freizeit_Planzahlen_0_sechs_Kategorien_mit_0_Euro_Version_0_keine_Einnahmen_kein_Beitrag()
    {
        var kalkulation = await KalkulationAsync(_s.Tobias);

        Assert.Equal(["einnahmen", "ergebnis", "kategorien", "mitarbeitende", "tage", "teilnehmende", "version"], Namen(kalkulation));
        Assert.Equal((0u, 0, 0, 12), (kalkulation.GetProperty("version").GetUInt32(), kalkulation.GetProperty("teilnehmende").GetInt32(),
            kalkulation.GetProperty("mitarbeitende").GetInt32(), kalkulation.GetProperty("tage").GetInt32()));
        Assert.Equal(
            ["Unterkunft 0 0 0 0", "Verpflegung 0 0 0 0", "Fahrt 0 0 0 0", "ProgrammUndMaterial 0 0 0 0", "Mitarbeitende 0 0 0 0", "Sonstiges 0 0 0 0"],
            kalkulation.GetProperty("kategorien").EnumerateArray().Select(k =>
                $"{k.GetProperty("kategorie").GetString()} {k.GetProperty("version").GetUInt32()} {k.GetProperty("fixkosten").GetDecimal()} {k.GetProperty("proPersonUndTag").GetDecimal()} {k.GetProperty("kosten").GetDecimal()}"));
        Assert.Empty(kalkulation.GetProperty("einnahmen").EnumerateArray());
        var ergebnis = kalkulation.GetProperty("ergebnis");
        Assert.Equal(["einnahmen", "gesamtkosten", "teilnehmerbeitrag"], Namen(ergebnis));
        Assert.Equal((0m, 0m, (decimal?)null), (ergebnis.GetProperty("gesamtkosten").GetDecimal(), ergebnis.GetProperty("einnahmen").GetDecimal(), Zahl(ergebnis, "teilnehmerbeitrag")));
        Assert.Equal(0L, await AnzahlAsync("kalkulation"));
        Assert.Equal(3, (await KalkulationAsync(freizeit: _s.Z)).GetProperty("tage").GetInt32());
    }

    [Fact]
    public async Task Beitrag_rechnen_40_TN_10_MA_12_Tage_Verpflegung_7_Unterkunft_2000_plus_10_Einnahme_1200_ergibt_275()
    {
        await OkAsync(PlanzahlAsync("teilnehmende", "0", "40"));
        await OkAsync(PlanzahlAsync("mitarbeitende", "0", "10", _s.Tobias));
        await OkAsync(KategorieAsync("Verpflegung", "proPersonUndTag", "0", "7"));
        await OkAsync(KategorieAsync("unterkunft", "fixkosten", "0", "2000", _s.Tobias));
        await OkAsync(KategorieAsync("Unterkunft", "proPersonUndTag", "0", "10"));
        await EinnahmeNeuAsync("Zuschuss Landesjugendplan", "1200");

        var kalkulation = await KalkulationAsync(_s.Tobias);

        Assert.Equal((40, 10), (kalkulation.GetProperty("teilnehmende").GetInt32(), kalkulation.GetProperty("mitarbeitende").GetInt32()));
        Assert.NotEqual(0u, kalkulation.GetProperty("version").GetUInt32());
        Assert.Equal(4200m, Kategorie(kalkulation, "Verpflegung").GetProperty("kosten").GetDecimal());
        Assert.Equal((2000m, 10m, 8000m), (Kategorie(kalkulation, "Unterkunft").GetProperty("fixkosten").GetDecimal(),
            Kategorie(kalkulation, "Unterkunft").GetProperty("proPersonUndTag").GetDecimal(), Kategorie(kalkulation, "Unterkunft").GetProperty("kosten").GetDecimal()));
        Assert.Equal(0m, Kategorie(kalkulation, "Fahrt").GetProperty("kosten").GetDecimal());
        Assert.NotEqual(0u, Kategorie(kalkulation, "Unterkunft").GetProperty("version").GetUInt32());
        var ergebnis = kalkulation.GetProperty("ergebnis");
        Assert.Equal((12200m, 1200m, (decimal?)275.00m), (ergebnis.GetProperty("gesamtkosten").GetDecimal(), ergebnis.GetProperty("einnahmen").GetDecimal(), Zahl(ergebnis, "teilnehmerbeitrag")));
        Assert.Equal(1L, await AnzahlAsync("kalkulation"));
        Assert.Equal(2L, await AnzahlAsync("kalkulation_kategorie"));
    }

    [Fact]
    public async Task Rundung_und_Einnahmen_ueber_den_Kosten()
    {
        await OkAsync(PlanzahlAsync("teilnehmende", "0", "3"));
        await OkAsync(KategorieAsync("Sonstiges", "fixkosten", "0", "1000"));
        Assert.Equal(333.33m, Zahl((await KalkulationAsync()).GetProperty("ergebnis"), "teilnehmerbeitrag"));

        await EinnahmeNeuAsync("Rabatt der Organisation", "1500");
        Assert.Equal(0m, Zahl((await KalkulationAsync()).GetProperty("ergebnis"), "teilnehmerbeitrag"));
    }

    // ---------- Autosave ----------

    [Fact]
    public async Task Betrag_mit_Komma_wird_als_7_50_gespeichert_und_als_7_5_geliefert_der_naechste_alte_Wert_passt()
    {
        using var erster = await KategorieAsync("Verpflegung", "proPersonUndTag", "0", "7,50");
        var json = await JsonAsync(erster);
        Assert.Equal(HttpStatusCode.OK, erster.StatusCode);
        Assert.Equal(["proPersonUndTag", "version"], Namen(json));
        Assert.Equal("7,5", json.GetProperty("proPersonUndTag").GetString());
        Assert.Equal(7.5m, await WertAsync<decimal>(_s.Verbindung, "SELECT \"ProPersonUndTag\" FROM finanzen.kalkulation_kategorie"));

        using var zweiter = await KategorieAsync("Verpflegung", "proPersonUndTag", "7,5", "8.25");
        Assert.Equal("8,25", (await JsonAsync(zweiter)).GetProperty("proPersonUndTag").GetString());

        using var planzahl = await PlanzahlAsync("teilnehmende", "0", " 40 ");
        Assert.Equal("40", (await JsonAsync(planzahl)).GetProperty("teilnehmende").GetString());
    }

    [Theory]
    [InlineData("1.250")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("1,234")]
    [InlineData("1000001")]
    [InlineData("")]
    public async Task Ungueltiger_Betrag_400_am_Feld_und_keine_Aenderung(string wert)
    {
        using var kategorie = await KategorieAsync("Fahrt", "fixkosten", "0", wert);
        await FeldfehlerAsync(kategorie, "fixkosten");
        var einnahme = await EinnahmeNeuAsync("Zuschuss", "100");
        using var patch = await EinnahmePatchAsync(Id(einnahme), "betrag", "100", wert);
        await FeldfehlerAsync(patch, "betrag");
        using var anlegen = await EinnahmeAnlegenAsync(new { bezeichnung = "Spende", betrag = wert });
        if (wert.Length > 0)
        {
            await FeldfehlerAsync(anlegen, "betrag");
        }

        Assert.Equal(0L, await AnzahlAsync("kalkulation_kategorie"));
        Assert.Equal(100m, (await KalkulationAsync()).GetProperty("einnahmen")[0].GetProperty("betrag").GetDecimal());
    }

    [Theory]
    [InlineData("1001")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("x")]
    [InlineData(null)]
    public async Task Ungueltige_Planzahl_400_am_Feld(string? wert)
    {
        using var antwort = await PlanzahlAsync("mitarbeitende", "0", wert);

        await FeldfehlerAsync(antwort, "mitarbeitende");
        Assert.Equal(0L, await AnzahlAsync("kalkulation"));
    }

    [Fact]
    public async Task Koerper_ohne_oder_mit_zwei_Feldern_oder_unbekanntem_Feld_400()
    {
        foreach (var koerper in new object[]
                 {
                     new { version = 0, alterWert = "0" },
                     new { version = 0, alterWert = "0", teilnehmende = "1", mitarbeitende = "2" },
                     new { version = 0, alterWert = "0", tage = "3" },
                 })
        {
            using var antwort = await SendenAsync(_s.Miriam, HttpMethod.Patch, Pfad(_s.X), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        }

        using var kategorie = await SendenAsync(_s.Miriam, HttpMethod.Patch, KategoriePfad(_s.X, "Fahrt"), new { version = 0, alterWert = "0", kosten = "3" });
        Assert.Equal(HttpStatusCode.BadRequest, kategorie.StatusCode);
    }

    [Fact]
    public async Task Veralteter_alter_Wert_409_konflikt_ein_anderes_Feld_wird_angewendet()
    {
        await OkAsync(PlanzahlAsync("teilnehmende", "0", "40"));
        await OkAsync(KategorieAsync("Fahrt", "fixkosten", "0", "300"));
        var einnahme = await EinnahmeNeuAsync("Zuschuss", "100");

        using var planzahl = await PlanzahlAsync("teilnehmende", "0", "50", _s.Tobias);
        using var kategorie = await KategorieAsync("Fahrt", "fixkosten", "0", "400", _s.Tobias);
        using var betrag = await EinnahmePatchAsync(Id(einnahme), "betrag", "50", "200", _s.Tobias);
        using var bezeichnung = await EinnahmePatchAsync(Id(einnahme), "bezeichnung", "Spende", "Zuschuss Land", _s.Tobias);

        foreach (var antwort in new[] { planzahl, kategorie, betrag, bezeichnung })
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        }

        // Ein anderes Feld derselben Zeile wird trotz alter Version angewendet.
        await OkAsync(PlanzahlAsync("mitarbeitende", "0", "10", _s.Tobias));
        await OkAsync(KategorieAsync("Fahrt", "proPersonUndTag", "0", "1"));
        var kalkulation = await KalkulationAsync();
        Assert.Equal((40, 10), (kalkulation.GetProperty("teilnehmende").GetInt32(), kalkulation.GetProperty("mitarbeitende").GetInt32()));
        Assert.Equal((300m, 1m), (Kategorie(kalkulation, "Fahrt").GetProperty("fixkosten").GetDecimal(), Kategorie(kalkulation, "Fahrt").GetProperty("proPersonUndTag").GetDecimal()));
        Assert.Equal(("Zuschuss", 100m), (kalkulation.GetProperty("einnahmen")[0].GetProperty("bezeichnung").GetString(), kalkulation.GetProperty("einnahmen")[0].GetProperty("betrag").GetDecimal()));
    }

    [Fact]
    public async Task Gleichzeitig_zwei_erste_PATCH_auf_dieselbe_neue_Kategorie_und_die_neue_Kalkulation_genau_eine_Zeile_nie_500()
    {
        foreach (var kategorie in new[] { "Unterkunft", "Fahrt", "Sonstiges" })
        {
            var antworten = await Task.WhenAll(KategorieAsync(kategorie, "fixkosten", "0", "100", _s.Tobias), KategorieAsync(kategorie, "fixkosten", "0", "100"));
            var status = antworten.Select(a => a.StatusCode).ToList();
            Assert.Contains(HttpStatusCode.OK, status);
            Assert.All(status, s => Assert.Contains(s, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }

            Assert.Equal(1L, await AnzahlAsync("kalkulation_kategorie", "\"Kategorie\" = @k", ("k", (int)Enum.Parse<Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie>(kategorie))));
        }

        var planzahlen = await Task.WhenAll(PlanzahlAsync("teilnehmende", "0", "40", _s.Tobias), PlanzahlAsync("mitarbeitende", "0", "10"));
        Assert.All(planzahlen, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
        foreach (var antwort in planzahlen)
        {
            antwort.Dispose();
        }

        Assert.Equal(1L, await AnzahlAsync("kalkulation"));
        Assert.Equal(3L, await AnzahlAsync("kalkulation_kategorie"));
    }

    [Fact]
    public async Task Unbekannte_Kategorie_404()
    {
        foreach (var kategorie in new[] { "xyz", "7", "Kosten" })
        {
            using var antwort = await KategorieAsync(kategorie, "fixkosten", "0", "100");
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }

        Assert.Equal(0L, await AnzahlAsync("kalkulation_kategorie"));
    }

    // ---------- Einnahmen ----------

    [Fact]
    public async Task Einnahme_anlegen_201_ohne_Betrag_0_bearbeiten_loeschen_204_nochmal_404()
    {
        var zuschuss = await EinnahmeNeuAsync("  Zuschuss Landesjugendplan ", null, _s.Tobias);
        Assert.Equal(["betrag", "bezeichnung", "id", "version"], Namen(zuschuss));
        Assert.Equal(("Zuschuss Landesjugendplan", 0m), (zuschuss.GetProperty("bezeichnung").GetString(), zuschuss.GetProperty("betrag").GetDecimal()));
        Assert.NotEqual(0u, zuschuss.GetProperty("version").GetUInt32());
        var rabatt = await EinnahmeNeuAsync("Rabatt der Organisation", "250,5");

        using var betrag = await EinnahmePatchAsync(Id(zuschuss), "betrag", "0", "1200,00");
        Assert.Equal("1200", (await JsonAsync(betrag)).GetProperty("betrag").GetString());
        using var bezeichnung = await EinnahmePatchAsync(Id(zuschuss), "bezeichnung", "Zuschuss Landesjugendplan", " Zuschuss Land ", _s.Tobias);
        Assert.Equal("Zuschuss Land", (await JsonAsync(bezeichnung)).GetProperty("bezeichnung").GetString());

        var kalkulation = await KalkulationAsync();
        Assert.Equal(
            ["Zuschuss Land 1200", "Rabatt der Organisation 250.5"],
            kalkulation.GetProperty("einnahmen").EnumerateArray().Select(e => $"{e.GetProperty("bezeichnung").GetString()} {e.GetProperty("betrag").GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        Assert.Equal(1450.5m, kalkulation.GetProperty("ergebnis").GetProperty("einnahmen").GetDecimal());

        using var weg = await EinnahmeLoeschenAsync(Id(rabatt), _s.Tobias);
        using var nochmal = await EinnahmeLoeschenAsync(Id(rabatt));
        using var patchWeg = await EinnahmePatchAsync(Id(rabatt), "betrag", "250,5", "1");
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (weg.StatusCode, nochmal.StatusCode, patchWeg.StatusCode));
        Assert.Equal(1L, await AnzahlAsync("einnahmeposten"));
    }

    [Fact]
    public async Task Einnahme_anlegen_ohne_oder_mit_zu_langer_Bezeichnung_400_am_Feld()
    {
        using var ohne = await EinnahmeAnlegenAsync(new { bezeichnung = " ", betrag = "1" });
        await FeldfehlerAsync(ohne, "bezeichnung");
        using var lang = await EinnahmeAnlegenAsync(new { bezeichnung = new string('x', 201) });
        await FeldfehlerAsync(lang, "bezeichnung");
        var einnahme = await EinnahmeNeuAsync("Zuschuss", "1");
        using var leer = await EinnahmePatchAsync(Id(einnahme), "bezeichnung", "Zuschuss", "");
        await FeldfehlerAsync(leer, "bezeichnung");
        Assert.Equal(1L, await AnzahlAsync("einnahmeposten"));
    }

    [Fact]
    public async Task Einnahme_einer_anderen_Freizeit_404()
    {
        var inZ = await EinnahmeNeuAsync("Zuschuss", "100", freizeit: _s.Z);

        using var patch = await EinnahmePatchAsync(Id(inZ), "betrag", "100", "1");
        using var loeschen = await EinnahmeLoeschenAsync(Id(inZ));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (patch.StatusCode, loeschen.StatusCode));
        Assert.Equal(100m, (await KalkulationAsync(freizeit: _s.Z)).GetProperty("einnahmen")[0].GetProperty("betrag").GetDecimal());
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufene_Freizeit_jede_Schreibaktion_409_freizeit_abgeschlossen_Lesen_200()
    {
        await OkAsync(PlanzahlAsync("teilnehmende", "0", "40"));
        var einnahme = await EinnahmeNeuAsync("Zuschuss", "100");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        var antworten = new List<HttpResponseMessage>
        {
            await PlanzahlAsync("teilnehmende", "40", "50"),
            await KategorieAsync("Fahrt", "fixkosten", "0", "1", _s.Tobias),
            await EinnahmeAnlegenAsync(new { bezeichnung = "Spende" }),
            await EinnahmePatchAsync(Id(einnahme), "betrag", "100", "200"),
            await EinnahmeLoeschenAsync(Id(einnahme), _s.Tobias),
        };

        foreach (var antwort in antworten)
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            antwort.Dispose();
        }

        var kalkulation = await KalkulationAsync(_s.Tobias);
        Assert.Equal((40, 100m), (kalkulation.GetProperty("teilnehmende").GetInt32(), kalkulation.GetProperty("einnahmen")[0].GetProperty("betrag").GetDecimal()));
        Assert.Equal(0L, await AnzahlAsync("kalkulation_kategorie"));
    }

    // ---------- Rechte und Schalter ----------

    /// <summary>Alle sechs Routen für einen Client unter einer Freizeit; die Einnahme gehört zu X.</summary>
    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid einnahme) =>
    [
        () => client.GetAsync(Pfad(freizeit), Abbruch),
        () => SendenAsync(client, HttpMethod.Patch, Pfad(freizeit), Koerper(0, "0", "mitarbeitende", "1")),
        () => SendenAsync(client, HttpMethod.Patch, KategoriePfad(freizeit, "Programmundmaterial"), Koerper(0, "0", "fixkosten", "1")),
        () => EinnahmeAnlegenAsync(new { bezeichnung = "Spende", betrag = "5" }, client, freizeit),
        () => SendenAsync(client, HttpMethod.Patch, EinnahmePfad(freizeit, einnahme), Koerper(0, "Zuschuss", "bezeichnung", "Zuschuss Land")),
        () => EinnahmeLoeschenAsync(einnahme, client, freizeit),
    ];

    private async Task<List<HttpStatusCode>> StatusAllerRoutenAsync(HttpClient client, Guid freizeit, Guid einnahme)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, einnahme))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_2xx_auf_allen_sechs_Routen()
    {
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            await AusfuehrenAsync(_s.Verbindung, "DELETE FROM finanzen.kalkulation; DELETE FROM finanzen.kalkulation_kategorie");
            var einnahme = await EinnahmeNeuAsync("Zuschuss", "100");

            var status = await StatusAllerRoutenAsync(client, _s.X, Id(einnahme));

            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent],
                status);
        }
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_unbekannte_Freizeit_404_ohne_Sitzung_401_auf_allen_sechs_Routen()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kasse@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var einnahme = await EinnahmeNeuAsync("Zuschuss", "100");
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
            Assert.All(await StatusAllerRoutenAsync(client, freizeit, Id(einnahme)), s => Assert.Equal(erwartet, s));
        }

        // Leitung von Z (Organisation B) mit Finanzen an: die Einnahme aus X bleibt 404.
        Assert.All((await StatusAllerRoutenAsync(_s.Miriam, _s.Z, Id(einnahme))).Skip(4), s => Assert.Equal(HttpStatusCode.NotFound, s));
        Assert.Equal(1L, await AnzahlAsync("einnahmeposten", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(0L, await AnzahlAsync("kalkulation", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal("Zuschuss", (await KalkulationAsync()).GetProperty("einnahmen")[0].GetProperty("bezeichnung").GetString());
    }

    [Fact]
    public async Task Schalter_aus_jede_Route_404_fuer_Leitung_und_Team_ohne_Sitzung_401_nach_dem_Einschalten_sind_die_Daten_wieder_da()
    {
        await OkAsync(PlanzahlAsync("teilnehmende", "0", "40"));
        var einnahme = await EinnahmeNeuAsync("Zuschuss", "100");
        await SchaltenAsync(_s.X, an: false);

        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.All(await StatusAllerRoutenAsync(client, _s.X, Id(einnahme)), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        // Rolle vor Schalter: ohne Rolle bleibt es 404, ohne Sitzung 401.
        Assert.All(await StatusAllerRoutenAsync(_s.Andrea, _s.X, Id(einnahme)), s => Assert.Equal(HttpStatusCode.NotFound, s));
        Assert.All(await StatusAllerRoutenAsync(_s.Anonym, _s.X, Id(einnahme)), s => Assert.Equal(HttpStatusCode.Unauthorized, s));

        await SchaltenAsync(_s.X, an: true);
        var wieder = await KalkulationAsync(_s.Tobias);
        Assert.Equal((40, "Zuschuss"), (wieder.GetProperty("teilnehmende").GetInt32(), wieder.GetProperty("einnahmen")[0].GetProperty("bezeichnung").GetString()));
    }

    // ---------- Lösch-Lauf ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Kalkulation_der_abgelaufenen_Freizeit_die_andere_bleibt()
    {
        foreach (var freizeit in new[] { _s.X, _s.Z })
        {
            await OkAsync(PlanzahlAsync("teilnehmende", "0", "40", freizeit: freizeit));
            await OkAsync(KategorieAsync("Fahrt", "fixkosten", "0", "300", freizeit: freizeit));
            await EinnahmeNeuAsync("Zuschuss", "100", freizeit: freizeit);
        }

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "FinanzenLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        foreach (var (freizeit, erwartet) in new[] { (_s.X, 0L), (_s.Z, 1L) })
        {
            foreach (var tabelle in new[] { "kalkulation", "kalkulation_kategorie", "einnahmeposten" })
            {
                Assert.Equal(erwartet, await AnzahlAsync(tabelle, "\"FreizeitId\" = @f", ("f", freizeit)));
            }
        }
    }
}
