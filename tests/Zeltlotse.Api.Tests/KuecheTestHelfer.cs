using System.Net;
using System.Text.Json;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Story 4-2: Gemeinsame Schritte der Küche-Tests über die echten Endpunkte (Miriam ist Leitung von X und Z).</summary>
internal static class KuecheTestHelfer
{
    public static string KuechePfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/kueche";

    /// <summary>Schaltet Verpflegung mit dem aktuellen Stand als altem Wert.</summary>
    public static async Task VerpflegungAsync(FreizeitpassSzenario s, Guid freizeit, bool an = true)
    {
        using var stand = await s.Miriam.GetAsync($"/api/freizeiten/{freizeit}/module", Abbruch);
        var json = await JsonAsync(stand);
        var koerper = new Dictionary<string, object?>
        {
            ["version"] = json.GetProperty("version").GetUInt32(),
            ["alterWert"] = json.GetProperty("verpflegung").GetBoolean() ? "true" : "false",
            ["verpflegung"] = an ? "true" : "false",
        };
        using var antwort = await SendenAsync(s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/module", koerper);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    public static async Task<JsonElement> RezeptAsync(FreizeitpassSzenario s, string name, string basis, Guid? freizeit = null)
    {
        using var antwort = await PostAsync(s.Miriam, $"{KuechePfad(freizeit ?? s.X)}/rezepte", new { name, basisPersonen = basis });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    public static async Task<JsonElement> ZutatAsync(
        FreizeitpassSzenario s, Guid rezept, string bezeichnung, string? menge = null, string? einheit = null, string? allergene = null, Guid? freizeit = null)
    {
        var pfad = $"{KuechePfad(freizeit ?? s.X)}/rezepte/{rezept}/zutaten";
        using var antwort = await PostAsync(s.Miriam, pfad, new { bezeichnung, menge, einheit });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var zutat = await JsonAsync(antwort);
        if (allergene is not null)
        {
            using var patch = await SendenAsync(
                s.Miriam, HttpMethod.Patch, $"{pfad}/{zutat.GetProperty("id").GetGuid()}",
                new Dictionary<string, object?> { ["version"] = zutat.GetProperty("version").GetUInt32(), ["alterWert"] = null, ["allergene"] = allergene });
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        }

        return zutat;
    }

    public static Task<HttpResponseMessage> HandMengeAsync(
        FreizeitpassSzenario s, Guid gericht, Guid zutat, string? alterWert, string? menge, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(
            client ?? s.Miriam, HttpMethod.Patch, $"{KuechePfad(freizeit ?? s.X)}/einkaufsliste/gerichte/{gericht}/zutaten/{zutat}",
            new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = alterWert, ["menge"] = menge });

    public static async Task<JsonElement> EinkaufslisteAsync(FreizeitpassSzenario s, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? s.Miriam).GetAsync($"{KuechePfad(freizeit ?? s.X)}/einkaufsliste", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    /// <summary>Alle Posten der Liste über alle Tage: „Bezeichnung Menge Einheit Rezept Mahlzeit[ von Hand]“, fehlende Werte als „-“.</summary>
    public static List<string> Posten(JsonElement liste) =>
        [.. liste.GetProperty("tage").EnumerateArray().SelectMany(t => t.GetProperty("posten").EnumerateArray()).Select(p =>
            $"{p.GetProperty("bezeichnung").GetString()} {(p.GetProperty("menge").ValueKind == JsonValueKind.Null ? "-" : p.GetProperty("menge").GetRawText())} "
            + $"{p.GetProperty("einheit").GetString() ?? "-"} {p.GetProperty("rezeptName").GetString() ?? "-"} {p.GetProperty("mahlzeitName").GetString() ?? "-"}"
            + (p.GetProperty("vonHand").GetBoolean() ? " von Hand" : ""))];

    /// <summary>Gibt <paramref name="anzahl"/> weiteren (erfundenen) Personen in X die Rolle Team, damit die Personenzahl steigt.</summary>
    public static Task TeamAuffuellenAsync(FreizeitpassSzenario s, int anzahl) =>
        AusfuehrenAsync(
            s.Verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"FreizeitId\", \"PersonId\", \"OrganisationId\", \"Rolle\", \"VergebenAm\") SELECT @f, gen_random_uuid(), @o, 2, now() FROM generate_series(1, @n)",
            ("f", s.X), ("o", s.A), ("n", anzahl));
}
