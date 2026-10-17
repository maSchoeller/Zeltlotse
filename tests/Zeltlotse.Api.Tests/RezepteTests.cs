using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 4-1, Rezepte mit Skalierung (CAP-28, FR-46, AD-16, AD-17, AD-19, AD-20, AD-30) gegen echtes PostgreSQL: Liste und Rezept mit hochgerechneten
/// Mengen, Anlegen mit Feldfehlern, Autosave mit Normalisierung und Konfliktregel (auch gleichzeitig), Löschen samt Zutaten (auch gleichzeitig), die
/// Pflichtfälle der Freizeit-Route für alle acht Routen, der Schalter Verpflegung, die abgeschlossene Freizeit, der Lösch-Lauf und die Personenzahl
/// aus Freizeiten. Freizeit X (Organisation A, Org-Admin Andrea, Leitung Miriam, Team Tobias, bestätigte Mitarbeitende) hat Verpflegung an; Freizeit
/// Z gehört Organisation B (Leitung Miriam).
/// </summary>
public sealed class RezepteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres);
        await SchaltenAsync(_s.X, an: true);
        await SchaltenAsync(_s.Z, an: true);
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string ListePfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/kueche/rezepte";

    private static string RezeptPfad(Guid freizeit, Guid rezept) => $"{ListePfad(freizeit)}/{rezept}";

    private static string ZutatPfad(Guid freizeit, Guid rezept, Guid zutat) => $"{RezeptPfad(freizeit, rezept)}/zutaten/{zutat}";

    /// <summary>Schaltet Verpflegung mit dem aktuellen Stand als altem Wert (Miriam ist Leitung von X und Z).</summary>
    private async Task SchaltenAsync(Guid freizeit, bool an)
    {
        using var stand = await _s.Miriam.GetAsync($"/api/freizeiten/{freizeit}/module", Abbruch);
        var json = await JsonAsync(stand);
        var koerper = new Dictionary<string, object?>
        {
            ["version"] = json.GetProperty("version").GetUInt32(),
            ["alterWert"] = json.GetProperty("verpflegung").GetBoolean() ? "true" : "false",
            ["verpflegung"] = an ? "true" : "false",
        };
        using var antwort = await SendenAsync(_s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/module", koerper);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private Task<HttpResponseMessage> AnlegenAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, ListePfad(freizeit ?? _s.X), koerper);

    /// <summary>Legt ein Rezept an und liefert die Antwort.</summary>
    private async Task<JsonElement> RezeptNeuAsync(string name = "Pfannkuchen", string basis = "4", HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await AnlegenAsync(new { name, basisPersonen = basis }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> ZutatAnlegenAsync(Guid rezept, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{RezeptPfad(freizeit ?? _s.X, rezept)}/zutaten", koerper);

    /// <summary>Fügt eine Zutat hinzu und liefert die Antwort.</summary>
    private async Task<JsonElement> ZutatNeuAsync(Guid rezept, string bezeichnung, string? menge = null, string? einheit = null, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await ZutatAnlegenAsync(rezept, new { bezeichnung, menge, einheit }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<JsonElement> ListeAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(ListePfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<JsonElement> RezeptAsync(Guid rezept, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(RezeptPfad(freizeit ?? _s.X, rezept), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> RezeptPatchAsync(Guid rezept, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, RezeptPfad(freizeit ?? _s.X, rezept), koerper);

    private Task<HttpResponseMessage> ZutatPatchAsync(Guid rezept, Guid zutat, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, ZutatPfad(freizeit ?? _s.X, rezept, zutat), koerper);

    private Task<HttpResponseMessage> RezeptLoeschenAsync(Guid rezept, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, RezeptPfad(freizeit ?? _s.X, rezept));

    private Task<HttpResponseMessage> ZutatLoeschenAsync(Guid rezept, Guid zutat, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, ZutatPfad(freizeit ?? _s.X, rezept, zutat));

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM kueche.{tabelle} WHERE {bedingung}", parameter);

    /// <summary>Gibt <paramref name="anzahl"/> weiteren (erfundenen) Personen in X die Rolle Team, damit die Personenzahl steigt.</summary>
    private Task TeamAuffuellenAsync(int anzahl) =>
        AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"FreizeitId\", \"PersonId\", \"OrganisationId\", \"Rolle\", \"VergebenAm\") SELECT @f, gen_random_uuid(), @o, 2, now() FROM generate_series(1, @n)",
            ("f", _s.X), ("o", _s.A), ("n", anzahl));

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static uint Version(JsonElement json) => json.GetProperty("version").GetUInt32();

    private static string? Text(JsonElement json, string feld) => json.GetProperty(feld).GetString();

    private static decimal? Zahl(JsonElement json, string feld) => json.GetProperty(feld).ValueKind == JsonValueKind.Null ? null : json.GetProperty(feld).GetDecimal();

    private static async Task FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
    }

    // ---------- Liste und Rezept ----------

    [Fact]
    public async Task Liste_ohne_Rezepte_ist_leer_und_nennt_die_Personenzahl()
    {
        var liste = await ListeAsync(_s.Tobias);

        Assert.Equal(["personenzahl", "rezepte"], Namen(liste));
        Assert.Equal(2, liste.GetProperty("personenzahl").GetInt32());
        Assert.Empty(liste.GetProperty("rezepte").EnumerateArray());
    }

    [Fact]
    public async Task Liste_nach_Name_mit_Basis_Zutatenzahl_und_Vereinigung_der_Allergene()
    {
        var pfannkuchen = await RezeptNeuAsync("Pfannkuchen", "4");
        var chili = await RezeptNeuAsync("Chili sin Carne", "6");
        var mehl = await ZutatNeuAsync(Id(pfannkuchen), "Mehl", "500", "g");
        var milch = await ZutatNeuAsync(Id(pfannkuchen), "Milch", "0,5", "l");
        await ZutatNeuAsync(Id(pfannkuchen), "Salz");
        await ZutatPatchAsync(Id(pfannkuchen), Id(mehl), Koerper(Version(mehl), null, "allergene", "gluten"));
        await ZutatPatchAsync(Id(pfannkuchen), Id(milch), Koerper(Version(milch), null, "allergene", "milch,gluten"));

        var liste = await ListeAsync(_s.Tobias);

        var rezepte = liste.GetProperty("rezepte").EnumerateArray().ToList();
        Assert.Equal(
            ["Chili sin Carne 6 0 -", "Pfannkuchen 4 3 gluten,milch"],
            rezepte.Select(r => $"{Text(r, "name")} {r.GetProperty("basisPersonen").GetInt32()} {r.GetProperty("zutaten").GetInt32()} {Text(r, "allergene") ?? "-"}"));
        Assert.Equal(["allergene", "basisPersonen", "id", "name", "zutaten"], Namen(rezepte[0]));
        Assert.Equal(Id(chili), Id(rezepte[0]));

        // Deutsch und ohne Groß-/Kleinschreibung sortiert: Umlaut wie Grundbuchstabe, klein zwischen den großen.
        await RezeptNeuAsync("apfelmus", "4");
        await RezeptNeuAsync("Äpfel", "4");
        Assert.Equal(
            ["Äpfel", "apfelmus", "Chili sin Carne", "Pfannkuchen"],
            (await ListeAsync()).GetProperty("rezepte").EnumerateArray().Select(r => Text(r, "name")));
    }

    [Fact]
    public async Task Rezept_rechnet_jede_Menge_auf_die_Personenzahl_hoch_Zutat_ohne_Menge_bleibt_ohne()
    {
        // Matrix: Basis 3, 300 g, 60 Personen → 6000. X hat Miriam und Tobias; 58 weitere im Team.
        var chili = await RezeptNeuAsync("Chili sin Carne", "3");
        var bohnen = await ZutatNeuAsync(Id(chili), "Kidneybohnen", "300", "g");
        var salz = await ZutatNeuAsync(Id(chili), "Salz");
        Assert.Equal((300m, 200m), (Zahl(bohnen, "menge"), Zahl(bohnen, "hochgerechneteMenge")));
        await TeamAuffuellenAsync(58);

        var rezept = await RezeptAsync(Id(chili), _s.Tobias);

        Assert.Equal(["basisPersonen", "id", "name", "personenzahl", "version", "zubereitung", "zutaten"], Namen(rezept));
        Assert.Equal(60, rezept.GetProperty("personenzahl").GetInt32());
        var zutaten = rezept.GetProperty("zutaten").EnumerateArray().ToList();
        Assert.Equal([Id(bohnen), Id(salz)], zutaten.Select(Id));
        Assert.Equal(["allergene", "bezeichnung", "einheit", "hochgerechneteMenge", "id", "menge", "version"], Namen(zutaten[0]));
        Assert.Equal((300m, "g", 6000m), (Zahl(zutaten[0], "menge"), Text(zutaten[0], "einheit"), Zahl(zutaten[0], "hochgerechneteMenge")));
        Assert.Equal("6000", zutaten[0].GetProperty("hochgerechneteMenge").GetRawText());
        Assert.Equal(("Salz", (decimal?)null, (decimal?)null, (string?)null), (Text(zutaten[1], "bezeichnung"), Zahl(zutaten[1], "menge"), Zahl(zutaten[1], "hochgerechneteMenge"), Text(zutaten[1], "einheit")));
        Assert.Equal(2, (await ListeAsync()).GetProperty("rezepte")[0].GetProperty("zutaten").GetInt32());
    }

    [Fact]
    public async Task Ungerade_Hochrechnung_Basis_4_Menge_1_5_bei_6_Personen_ergibt_2_25()
    {
        var pfannkuchen = await RezeptNeuAsync("Pfannkuchen", "4");
        await ZutatNeuAsync(Id(pfannkuchen), "Mehl", "1,5", "kg");
        await TeamAuffuellenAsync(4);

        var zutat = Assert.Single((await RezeptAsync(Id(pfannkuchen))).GetProperty("zutaten").EnumerateArray());

        Assert.Equal((1.5m, 2.25m), (Zahl(zutat, "menge"), Zahl(zutat, "hochgerechneteMenge")));
    }

    [Fact]
    public async Task Rezept_einer_anderen_Freizeit_oder_unbekannt_ist_404()
    {
        var inZ = await RezeptNeuAsync("Pfannkuchen", freizeit: _s.Z);

        using var unterX = await _s.Miriam.GetAsync(RezeptPfad(_s.X, Id(inZ)), Abbruch);
        using var unbekannt = await _s.Miriam.GetAsync(RezeptPfad(_s.X, Guid.CreateVersion7()), Abbruch);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (unterX.StatusCode, unbekannt.StatusCode));
        Assert.Empty((await ListeAsync()).GetProperty("rezepte").EnumerateArray());
        Assert.Single((await ListeAsync(freizeit: _s.Z)).GetProperty("rezepte").EnumerateArray());
    }

    // ---------- Anlegen ----------

    [Fact]
    public async Task Rezept_anlegen_201_getrimmt_mit_Version_ohne_Zutaten_und_Location()
    {
        using var antwort = await AnlegenAsync(new { name = "  Chili sin Carne ", basisPersonen = " 4 " }, _s.Tobias);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var rezept = await JsonAsync(antwort);
        Assert.Equal(RezeptPfad(_s.X, Id(rezept)), antwort.Headers.Location?.OriginalString);
        Assert.Equal(("Chili sin Carne", 4, JsonValueKind.Null, 2), (Text(rezept, "name"), rezept.GetProperty("basisPersonen").GetInt32(), rezept.GetProperty("zubereitung").ValueKind, rezept.GetProperty("personenzahl").GetInt32()));
        Assert.Empty(rezept.GetProperty("zutaten").EnumerateArray());
        Assert.NotEqual(0u, Version(rezept));
    }

    [Fact]
    public async Task Rezept_anlegen_Pflichtfelder_und_ungueltige_Basis_400_am_Feld()
    {
        var faelle = new (object Koerper, string Feld)[]
        {
            (new { basisPersonen = "4" }, "name"),
            (new { name = "  ", basisPersonen = "4" }, "name"),
            (new { name = new string('a', 201), basisPersonen = "4" }, "name"),
            (new { name = "Chili" }, "basisPersonen"),
            (new { name = "Chili", basisPersonen = "0" }, "basisPersonen"),
            (new { name = "Chili", basisPersonen = "-2" }, "basisPersonen"),
            (new { name = "Chili", basisPersonen = "abc" }, "basisPersonen"),
            (new { name = "Chili", basisPersonen = "1001" }, "basisPersonen"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await AnlegenAsync(koerper);
            await FeldfehlerAsync(antwort, feld);
        }

        Assert.Equal(0L, await AnzahlAsync("rezept"));
    }

    [Fact]
    public async Task Zutat_anlegen_201_Menge_normalisiert_ohne_Menge_moeglich_Feldfehler_400_fremdes_Rezept_404()
    {
        var rezept = await RezeptNeuAsync();

        var mehl = await ZutatNeuAsync(Id(rezept), " Mehl ", "1.5", " kg ", _s.Tobias);
        Assert.Equal(("Mehl", 1.5m, "kg", JsonValueKind.Null), (Text(mehl, "bezeichnung"), Zahl(mehl, "menge"), Text(mehl, "einheit"), mehl.GetProperty("allergene").ValueKind));
        Assert.Equal(0.75m, Zahl(mehl, "hochgerechneteMenge"));
        var salz = await ZutatNeuAsync(Id(rezept), "Salz", " ", "");
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (salz.GetProperty("menge").ValueKind, salz.GetProperty("einheit").ValueKind));

        var faelle = new (object Koerper, string Feld)[]
        {
            (new { menge = "1" }, "bezeichnung"),
            (new { bezeichnung = " " }, "bezeichnung"),
            (new { bezeichnung = "Mehl", menge = "0" }, "menge"),
            (new { bezeichnung = "Mehl", menge = "-1" }, "menge"),
            (new { bezeichnung = "Mehl", menge = "1,2345" }, "menge"),
            (new { bezeichnung = "Mehl", menge = "x" }, "menge"),
            (new { bezeichnung = "Mehl", menge = "1.000" }, "menge"),
            (new { bezeichnung = "Mehl", einheit = new string('e', 31) }, "einheit"),
        };
        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await ZutatAnlegenAsync(Id(rezept), koerper);
            await FeldfehlerAsync(antwort, feld);
        }

        var inZ = await RezeptNeuAsync("Chili", freizeit: _s.Z);
        using var fremd = await ZutatAnlegenAsync(Id(inZ), new { bezeichnung = "Mehl" });
        using var unbekannt = await ZutatAnlegenAsync(Guid.CreateVersion7(), new { bezeichnung = "Mehl" });
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (fremd.StatusCode, unbekannt.StatusCode));
        Assert.Equal(2L, await AnzahlAsync("zutat"));
    }

    // ---------- Bearbeiten (Autosave) ----------

    [Fact]
    public async Task Rezept_Felder_aendern_200_normalisiert_anderer_Altwert_409_konflikt_anderes_Feld_wird_angewendet()
    {
        var rezept = await RezeptNeuAsync("Pfannkuchen", "4");
        var alteVersion = Version(rezept);

        using var basis = await RezeptPatchAsync(Id(rezept), Koerper(alteVersion, "4", "basisPersonen", " 6 "), _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, basis.StatusCode);
        var json = await JsonAsync(basis);
        Assert.Equal(["basisPersonen", "version"], Namen(json));
        Assert.Equal("6", Text(json, "basisPersonen"));
        Assert.NotEqual(alteVersion, Version(json));

        using var konflikt = await RezeptPatchAsync(Id(rezept), Koerper(alteVersion, "4", "basisPersonen", "8"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        using var zubereitung = await RezeptPatchAsync(Id(rezept), Koerper(alteVersion, null, "zubereitung", "Alles verrühren und ausbacken."));
        Assert.Equal("Alles verrühren und ausbacken.", Text(await JsonAsync(zubereitung), "zubereitung"));
        using var name = await RezeptPatchAsync(Id(rezept), Koerper(alteVersion, "Pfannkuchen", "name", " Eierkuchen "));
        Assert.Equal("Eierkuchen", Text(await JsonAsync(name), "name"));

        var gespeichert = await RezeptAsync(Id(rezept));
        Assert.Equal(("Eierkuchen", 6, "Alles verrühren und ausbacken."), (Text(gespeichert, "name"), gespeichert.GetProperty("basisPersonen").GetInt32(), Text(gespeichert, "zubereitung")));
    }

    [Fact]
    public async Task Rezept_ungueltige_Werte_und_Koerper_400_am_Feld()
    {
        var rezept = await RezeptNeuAsync();

        foreach (var (feld, wert) in new[] { ("basisPersonen", "0"), ("basisPersonen", "-2"), ("basisPersonen", "abc"), ("basisPersonen", "1001"), ("basisPersonen", ""), ("name", " "), ("zubereitung", new string('z', 8001)) })
        {
            using var antwort = await RezeptPatchAsync(Id(rezept), Koerper(Version(rezept), null, feld, wert));
            await FeldfehlerAsync(antwort, feld);
        }

        using var zwei = await RezeptPatchAsync(Id(rezept), new Dictionary<string, object?> { ["version"] = Version(rezept), ["name"] = "A", ["zubereitung"] = "B" });
        await FeldfehlerAsync(zwei, "feld");
        using var unbekannt = await RezeptPatchAsync(Id(rezept), Koerper(Version(rezept), null, "zutaten", "x"));
        await FeldfehlerAsync(unbekannt, "feld");
        using var ohneVersion = await RezeptPatchAsync(Id(rezept), new Dictionary<string, object?> { ["name"] = "A" });
        await FeldfehlerAsync(ohneVersion, "version");
        Assert.Equal(("Pfannkuchen", 4), (Text(await RezeptAsync(Id(rezept)), "name"), (await RezeptAsync(Id(rezept))).GetProperty("basisPersonen").GetInt32()));
    }

    [Fact]
    public async Task Zutat_Menge_1_5_wird_als_1_Komma_5_gespeichert_und_der_naechste_alte_Wert_passt()
    {
        var rezept = await RezeptNeuAsync();
        var mehl = await ZutatNeuAsync(Id(rezept), "Mehl");

        using var punkt = await ZutatPatchAsync(Id(rezept), Id(mehl), Koerper(Version(mehl), null, "menge", "1.5"));
        Assert.Equal(HttpStatusCode.OK, punkt.StatusCode);
        var json = await JsonAsync(punkt);
        Assert.Equal(["menge", "version"], Namen(json));
        Assert.Equal("1,5", Text(json, "menge"));

        using var komma = await ZutatPatchAsync(Id(rezept), Id(mehl), Koerper(Version(json), "1,5", "menge", "2,500"));
        Assert.Equal("2,5", Text(await JsonAsync(komma), "menge"));
        using var leer = await ZutatPatchAsync(Id(rezept), Id(mehl), Koerper(0, "2,5", "menge", ""));
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(leer)).GetProperty("menge").ValueKind);
        using var konflikt = await ZutatPatchAsync(Id(rezept), Id(mehl), Koerper(0, "2,5", "menge", "3"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));
    }

    [Fact]
    public async Task Zutat_Allergene_normalisiert_unbekanntes_Allergen_400_Bezeichnung_und_Einheit_aendern()
    {
        var rezept = await RezeptNeuAsync();
        var zutat = await ZutatNeuAsync(Id(rezept), "Senfsoße", "100", "ml");

        using var allergene = await ZutatPatchAsync(Id(rezept), Id(zutat), Koerper(Version(zutat), null, "allergene", "senf,milch,milch"));
        Assert.Equal("milch,senf", Text(await JsonAsync(allergene), "allergene"));
        using var unbekannt = await ZutatPatchAsync(Id(rezept), Id(zutat), Koerper(Version(zutat), "milch,senf", "allergene", "milch,kaese"));
        await FeldfehlerAsync(unbekannt, "allergene");
        using var bezeichnung = await ZutatPatchAsync(Id(rezept), Id(zutat), Koerper(Version(zutat), "Senfsoße", "bezeichnung", " Senfsauce "));
        Assert.Equal("Senfsauce", Text(await JsonAsync(bezeichnung), "bezeichnung"));
        using var einheit = await ZutatPatchAsync(Id(rezept), Id(zutat), Koerper(Version(zutat), "ml", "einheit", ""));
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(einheit)).GetProperty("einheit").ValueKind);
        foreach (var (feld, wert) in new[] { ("menge", "0"), ("menge", "-1"), ("menge", "1,2345"), ("menge", "x"), ("menge", "1.000"), ("menge", "12.500"), ("menge", "1.000.000"), ("bezeichnung", ""), ("einheit", new string('e', 31)) })
        {
            using var antwort = await ZutatPatchAsync(Id(rezept), Id(zutat), Koerper(Version(zutat), null, feld, wert));
            await FeldfehlerAsync(antwort, feld);
        }

        var gespeichert = Assert.Single((await RezeptAsync(Id(rezept))).GetProperty("zutaten").EnumerateArray());
        Assert.Equal(("Senfsauce", 100m, (string?)null, "milch,senf"), (Text(gespeichert, "bezeichnung"), Zahl(gespeichert, "menge")!.Value, Text(gespeichert, "einheit"), Text(gespeichert, "allergene")));
    }

    [Fact]
    public async Task Zutat_eines_anderen_Rezepts_oder_einer_anderen_Freizeit_ist_404_und_bleibt_unveraendert()
    {
        var pfannkuchen = await RezeptNeuAsync("Pfannkuchen");
        var chili = await RezeptNeuAsync("Chili");
        var mehl = await ZutatNeuAsync(Id(pfannkuchen), "Mehl", "500", "g");
        var inZ = await RezeptNeuAsync("Pfannkuchen", freizeit: _s.Z);
        var mehlInZ = await ZutatNeuAsync(Id(inZ), "Mehl", "500", "g", freizeit: _s.Z);

        using var falschesRezept = await ZutatPatchAsync(Id(chili), Id(mehl), Koerper(0, "500", "menge", "600"));
        using var falschLoeschen = await ZutatLoeschenAsync(Id(chili), Id(mehl));
        using var fremdeFreizeit = await ZutatPatchAsync(Id(inZ), Id(mehlInZ), Koerper(0, "500", "menge", "600"));
        using var fremdLoeschen = await ZutatLoeschenAsync(Id(inZ), Id(mehlInZ));
        using var rezeptFremd = await RezeptPatchAsync(Id(inZ), Koerper(0, "Pfannkuchen", "name", "Anders"));
        using var rezeptFremdLoeschen = await RezeptLoeschenAsync(Id(inZ));

        Assert.All(
            new[] { falschesRezept, falschLoeschen, fremdeFreizeit, fremdLoeschen, rezeptFremd, rezeptFremdLoeschen },
            a => Assert.Equal(HttpStatusCode.NotFound, a.StatusCode));
        Assert.Equal(500m, Zahl(Assert.Single((await RezeptAsync(Id(pfannkuchen))).GetProperty("zutaten").EnumerateArray()), "menge"));
        var z = await RezeptAsync(Id(inZ), freizeit: _s.Z);
        Assert.Equal(("Pfannkuchen", 500m), (Text(z, "name"), Zahl(z.GetProperty("zutaten")[0], "menge")!.Value));
    }

    // ---------- Gleichzeitig ----------

    [Fact]
    public async Task Zwei_gleichzeitige_PATCH_desselben_Felds_mit_gleichem_Altwert_genau_einer_gewinnt()
    {
        var rezept = await RezeptNeuAsync("Pfannkuchen", "4");
        var mehl = await ZutatNeuAsync(Id(rezept), "Mehl", "500", "g");

        var rezeptAntworten = await Task.WhenAll(
            RezeptPatchAsync(Id(rezept), Koerper(Version(rezept), "4", "basisPersonen", "6"), _s.Tobias),
            RezeptPatchAsync(Id(rezept), Koerper(Version(rezept), "4", "basisPersonen", "8")));
        var zutatAntworten = await Task.WhenAll(
            ZutatPatchAsync(Id(rezept), Id(mehl), Koerper(Version(mehl), "500", "menge", "600"), _s.Tobias),
            ZutatPatchAsync(Id(rezept), Id(mehl), Koerper(Version(mehl), "500", "menge", "700")));

        foreach (var antworten in new[] { rezeptAntworten, zutatAntworten })
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], antworten.Select(a => a.StatusCode).Order());
            Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antworten.Single(a => a.StatusCode == HttpStatusCode.Conflict))));
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }
        }
    }

    [Fact]
    public async Task Zutat_und_Rezept_gleichzeitig_loeschen_kein_500_und_danach_ist_alles_weg()
    {
        for (var runde = 0; runde < 5; runde++)
        {
            var rezept = await RezeptNeuAsync($"Pfannkuchen {runde}");
            var mehl = await ZutatNeuAsync(Id(rezept), "Mehl", "500", "g");
            await ZutatNeuAsync(Id(rezept), "Milch", "1", "l");

            var antworten = await Task.WhenAll(ZutatLoeschenAsync(Id(rezept), Id(mehl), _s.Tobias), RezeptLoeschenAsync(Id(rezept)));

            Assert.Equal(HttpStatusCode.NoContent, antworten[1].StatusCode);
            Assert.Contains(antworten[0].StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound });
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }
        }

        Assert.Equal((0L, 0L), (await AnzahlAsync("rezept"), await AnzahlAsync("zutat")));
    }

    [Fact]
    public async Task Zutat_anlegen_waehrend_das_Rezept_geloescht_wird_ergibt_201_oder_404_nie_500()
    {
        for (var runde = 0; runde < 5; runde++)
        {
            var rezept = await RezeptNeuAsync($"Chili {runde}");

            var antworten = await Task.WhenAll(ZutatAnlegenAsync(Id(rezept), new { bezeichnung = "Bohnen" }, _s.Tobias), RezeptLoeschenAsync(Id(rezept)));

            Assert.Contains(antworten[0].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.NotFound });
            Assert.Equal(HttpStatusCode.NoContent, antworten[1].StatusCode);
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }
        }

        Assert.Equal((0L, 0L), (await AnzahlAsync("rezept"), await AnzahlAsync("zutat")));
    }

    // ---------- Löschen ----------

    [Fact]
    public async Task Rezept_loeschen_204_nimmt_die_Zutaten_mit_Zutat_loeschen_204_nochmal_404()
    {
        var weg = await RezeptNeuAsync("Chili");
        await ZutatNeuAsync(Id(weg), "Bohnen", "300", "g");
        await ZutatNeuAsync(Id(weg), "Sellerie", "1", "Stange");
        var bleibt = await RezeptNeuAsync("Pfannkuchen");
        var mehl = await ZutatNeuAsync(Id(bleibt), "Mehl", "500", "g");
        var milch = await ZutatNeuAsync(Id(bleibt), "Milch", "1", "l");

        using var rezeptWeg = await RezeptLoeschenAsync(Id(weg), _s.Tobias);
        using var rezeptNochmal = await RezeptLoeschenAsync(Id(weg));
        using var zutatWeg = await ZutatLoeschenAsync(Id(bleibt), Id(mehl), _s.Tobias);
        using var zutatNochmal = await ZutatLoeschenAsync(Id(bleibt), Id(mehl));

        Assert.Equal(
            (HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.NoContent, HttpStatusCode.NotFound),
            (rezeptWeg.StatusCode, rezeptNochmal.StatusCode, zutatWeg.StatusCode, zutatNochmal.StatusCode));
        Assert.Equal([Id(milch)], (await RezeptAsync(Id(bleibt))).GetProperty("zutaten").EnumerateArray().Select(Id));
        Assert.Equal(1L, await AnzahlAsync("zutat"));
        Assert.Equal(1L, await AnzahlAsync("rezept"));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufene_Freizeit_jede_Schreibaktion_409_freizeit_abgeschlossen_Lesen_200()
    {
        var rezept = await RezeptNeuAsync("Pfannkuchen", "4");
        var mehl = await ZutatNeuAsync(Id(rezept), "Mehl", "500", "g");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        var antworten = new List<HttpResponseMessage>
        {
            await AnlegenAsync(new { name = "Chili", basisPersonen = "4" }),
            await RezeptPatchAsync(Id(rezept), Koerper(0, "4", "basisPersonen", "6")),
            await RezeptLoeschenAsync(Id(rezept), _s.Tobias),
            await ZutatAnlegenAsync(Id(rezept), new { bezeichnung = "Milch" }),
            await ZutatPatchAsync(Id(rezept), Id(mehl), Koerper(0, "500", "menge", "600")),
            await ZutatLoeschenAsync(Id(rezept), Id(mehl), _s.Tobias),
        };

        foreach (var antwort in antworten)
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            antwort.Dispose();
        }

        Assert.Single((await ListeAsync(_s.Tobias)).GetProperty("rezepte").EnumerateArray());
        var unveraendert = await RezeptAsync(Id(rezept), _s.Tobias);
        Assert.Equal((4, 500m), (unveraendert.GetProperty("basisPersonen").GetInt32(), Zahl(unveraendert.GetProperty("zutaten")[0], "menge")!.Value));
    }

    // ---------- Rechte und Schalter ----------

    /// <summary>Alle acht Routen für einen Client unter einer Freizeit; Rezept und Zutat gehören zu X.</summary>
    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid rezept, Guid zutat) =>
    [
        () => client.GetAsync(ListePfad(freizeit), Abbruch),
        () => AnlegenAsync(new { name = "Chili", basisPersonen = "4" }, client, freizeit),
        () => client.GetAsync(RezeptPfad(freizeit, rezept), Abbruch),
        () => RezeptPatchAsync(rezept, Koerper(0, null, "zubereitung", "Kochen."), client, freizeit),
        () => ZutatAnlegenAsync(rezept, new { bezeichnung = "Salz" }, client, freizeit),
        () => ZutatPatchAsync(rezept, zutat, Koerper(0, null, "allergene", "gluten"), client, freizeit),
        () => ZutatLoeschenAsync(rezept, zutat, client, freizeit),
        () => RezeptLoeschenAsync(rezept, client, freizeit),
    ];

    private async Task<List<HttpStatusCode>> StatusAllerRoutenAsync(HttpClient client, Guid freizeit, Guid rezept, Guid zutat)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, rezept, zutat))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_2xx_auf_allen_acht_Routen()
    {
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            var rezept = await RezeptNeuAsync();
            var zutat = await ZutatNeuAsync(Id(rezept), "Mehl", "500", "g");

            var status = await StatusAllerRoutenAsync(client, _s.X, Id(rezept), Id(zutat));

            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NoContent],
                status);
        }
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_unbekannte_Freizeit_404_ohne_Sitzung_401_auf_allen_acht_Routen()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var rezept = await RezeptNeuAsync("Pfannkuchen", "4");
        var zutat = await ZutatNeuAsync(Id(rezept), "Mehl", "500", "g");
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
            Assert.All(await StatusAllerRoutenAsync(client, freizeit, Id(rezept), Id(zutat)), s => Assert.Equal(erwartet, s));
        }

        // Leitung von Z (Organisation B) mit Verpflegung an: Rezept und Zutat aus X bleiben 404.
        Assert.All((await StatusAllerRoutenAsync(_s.Miriam, _s.Z, Id(rezept), Id(zutat))).Skip(2), s => Assert.Equal(HttpStatusCode.NotFound, s));
        Assert.Equal((1L, 1L), (await AnzahlAsync("rezept", "\"FreizeitId\" = @f", ("f", _s.X)), await AnzahlAsync("zutat", "\"FreizeitId\" = @f", ("f", _s.X))));
        var unveraendert = await RezeptAsync(Id(rezept));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (unveraendert.GetProperty("zubereitung").ValueKind, unveraendert.GetProperty("zutaten")[0].GetProperty("allergene").ValueKind));
    }

    [Fact]
    public async Task Schalter_aus_jede_Route_404_fuer_Leitung_und_Team_ohne_Sitzung_401_nach_dem_Einschalten_sind_die_Daten_wieder_da()
    {
        var rezept = await RezeptNeuAsync("Pfannkuchen", "4");
        var zutat = await ZutatNeuAsync(Id(rezept), "Mehl", "500", "g");
        await SchaltenAsync(_s.X, an: false);

        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.All(await StatusAllerRoutenAsync(client, _s.X, Id(rezept), Id(zutat)), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        // Rolle vor Schalter: ohne Rolle bleibt es 404, ohne Sitzung 401.
        Assert.All(await StatusAllerRoutenAsync(_s.Andrea, _s.X, Id(rezept), Id(zutat)), s => Assert.Equal(HttpStatusCode.NotFound, s));
        Assert.All(await StatusAllerRoutenAsync(_s.Anonym, _s.X, Id(rezept), Id(zutat)), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal((1L, 1L), (await AnzahlAsync("rezept"), await AnzahlAsync("zutat")));

        await SchaltenAsync(_s.X, an: true);
        var wieder = await RezeptAsync(Id(rezept), _s.Tobias);
        Assert.Equal(("Pfannkuchen", "Mehl"), (Text(wieder, "name"), Text(wieder.GetProperty("zutaten")[0], "bezeichnung")));
    }

    // ---------- Lösch-Lauf ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_Rezepte_und_Zutaten_der_abgelaufenen_Freizeit_die_andere_bleibt()
    {
        var inX = await RezeptNeuAsync("Pfannkuchen");
        await ZutatNeuAsync(Id(inX), "Mehl", "500", "g");
        var inZ = await RezeptNeuAsync("Chili", freizeit: _s.Z);
        await ZutatNeuAsync(Id(inZ), "Bohnen", "300", "g", freizeit: _s.Z);

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "KuecheLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal((0L, 0L), (await AnzahlAsync("rezept", "\"FreizeitId\" = @f", ("f", _s.X)), await AnzahlAsync("zutat", "\"FreizeitId\" = @f", ("f", _s.X))));
        Assert.Equal((1L, 1L), (await AnzahlAsync("rezept", "\"FreizeitId\" = @f", ("f", _s.Z)), await AnzahlAsync("zutat", "\"FreizeitId\" = @f", ("f", _s.Z))));
    }

    // ---------- Personenzahl (Freizeiten.Contracts) ----------

    private async Task<int> PersonenzahlAsync(Guid freizeit, Guid organisation)
    {
        await using var scope = _s.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await scope.ServiceProvider.GetRequiredService<IFreizeitPersonenzahlLesen>().AnzahlAsync(freizeit, Abbruch);
    }

    [Fact]
    public async Task Personenzahl_bestaetigte_Teilnehmende_und_Rollen_jede_Person_einmal_eingegangen_abgesagt_und_Mitarbeitende_ohne_Rolle_zaehlen_nicht()
    {
        // Miriam (Leitung) und Tobias (Team, bestätigte Mitarbeitende: zählt einmal).
        Assert.Equal(2, await PersonenzahlAsync(_s.X, _s.A));

        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var mia = await _s.KindAnmeldenAsync(_s.Mia);
        Assert.Equal(2, await PersonenzahlAsync(_s.X, _s.A));

        await _s.BestaetigenAsync(emma);
        await _s.AbsagenAsync(mia);
        await _s.ZustellenAsync();
        Assert.Equal(3, await PersonenzahlAsync(_s.X, _s.A));
        Assert.Equal(3, (await ListeAsync(_s.Tobias)).GetProperty("personenzahl").GetInt32());

        // Emma bekommt zusätzlich eine Team-Rolle: sie zählt weiter einmal.
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"FreizeitId\", \"PersonId\", \"OrganisationId\", \"Rolle\", \"VergebenAm\") VALUES (@f, @p, @o, 2, now())",
            ("f", _s.X), ("p", _s.Emma), ("o", _s.A));
        Assert.Equal(3, await PersonenzahlAsync(_s.X, _s.A));

        // Ohne seine Team-Rolle zählt Tobias' bestätigte Mitarbeitenden-Anmeldung nicht.
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM freizeiten.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p", ("f", _s.X), ("p", _s.TobiasId));
        Assert.Equal(2, await PersonenzahlAsync(_s.X, _s.A));

        // Fremder Mandant und unbekannte Freizeit: 0.
        Assert.Equal(0, await PersonenzahlAsync(_s.X, _s.B));
        Assert.Equal(0, await PersonenzahlAsync(Guid.CreateVersion7(), _s.A));
    }

    [Fact]
    public async Task Personenzahl_bestaetigte_Anmeldungen_geloeschter_Personen_zaehlen_einzeln()
    {
        foreach (var kind in new[] { _s.Emma, _s.Mia })
        {
            await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(kind));
        }

        await AusfuehrenAsync(
            _s.Verbindung,
            "UPDATE freizeiten.anmeldung SET \"PersonId\" = @g WHERE \"FreizeitId\" = @f AND \"PersonId\" = ANY(@k)",
            ("g", GeloeschtePerson.Id), ("f", _s.X), ("k", new[] { _s.Emma, _s.Mia }));

        Assert.Equal(4, await PersonenzahlAsync(_s.X, _s.A));
    }
}
