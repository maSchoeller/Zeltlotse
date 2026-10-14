using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Inventar;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 3-3: Ergebnis einer schreibenden Aktion im Inventar.</summary>
public enum InventarAktionErgebnis
{
    Erfolg,

    /// <summary><c>409 konflikt</c>: z. B. ein Artikel mit Reservierungen lässt sich nicht löschen.</summary>
    Konflikt,

    /// <summary><c>409 freizeit-abgeschlossen</c>: Die Freizeit ist nur noch lesbar.</summary>
    Abgeschlossen,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 3-3: Ergebnis von „Artikel hinzufügen“; bei Erfolg der Artikel, sonst die Fehler je Feld.</summary>
public sealed record ArtikelAktion(InventarAktionErgebnis Ergebnis, InventarArtikel? Artikel, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>Story 3-3: Ergebnis von „Reservieren“; bei Erfolg die Reservierung mit Konflikthinweisen, sonst die Fehler je Feld.</summary>
public sealed record ReservierungAktion(InventarAktionErgebnis Ergebnis, Reservierung? Reservierung, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>Story 3-4: Ergebnis von „Eintragen“ auf der Schadens- und Verlustliste; bei Erfolg der Eintrag, sonst die Fehler je Feld.</summary>
public sealed record SchadenAktion(InventarAktionErgebnis Ergebnis, SchadenEintrag? Eintrag, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>
/// Typisierter Client des Moduls Inventar (AD-29, handgeschrieben), Story 3-3: das Inventar der Organisation (lesen, Artikel hinzufügen und
/// löschen; den Autosave von Bezeichnung und Menge übernimmt der Autosave-Baustein mit <see cref="ArtikelPfad"/>) und das Inventar in der Freizeit
/// (lesen, reservieren, aufheben). Story 3-4: die Schadens- und Verlustliste der Freizeit (lesen, eintragen, entfernen; die Bezeichnung speichert
/// der Autosave-Baustein mit <see cref="SchadenPfad"/>) und die Anschaffungswünsche der Organisation (lesen, erledigt). <c>403</c> und <c>404</c>
/// beim Laden heißen „nicht verfügbar“ (<c>null</c>).
/// </summary>
public sealed class InventarApiClient(HttpClient http)
{
    /// <summary>Pfad des Inventars einer Organisation.</summary>
    public static string OrgInventarPfad(Guid orgId) => $"/api/organisationen/{orgId}/inventar";

    /// <summary>Pfad eines Artikels, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string ArtikelPfad(Guid orgId, Guid artikelId) => $"{OrgInventarPfad(orgId)}/{artikelId}";

    /// <summary>Pfad des Inventars in einer Freizeit.</summary>
    public static string FreizeitInventarPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/inventar";

    /// <summary><c>GET /api/organisationen/{orgId}/inventar</c> (Org-Admin, Org-Überblick): das Inventar oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<OrgInventar?> OrgInventarAsync(Guid orgId, CancellationToken abbruch = default) => LadenAsync<OrgInventar>(OrgInventarPfad(orgId), abbruch);

    /// <summary><c>GET /api/freizeiten/{freizeitId}/inventar</c> (Leitung und Team): das Inventar oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<FreizeitInventar?> FreizeitInventarAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitInventar>(FreizeitInventarPfad(freizeitId), abbruch);

    /// <summary><c>POST …/organisationen/{orgId}/inventar</c>: angelegt mit dem Artikel, Feldfehler (<c>400</c>) oder ein anderer Fehler.</summary>
    public async Task<ArtikelAktion> ArtikelAnlegenAsync(Guid orgId, InventarArtikelNeu artikel, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(OrgInventarPfad(orgId), artikel, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new ArtikelAktion(InventarAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        if (!antwort.IsSuccessStatusCode)
        {
            return new ArtikelAktion(await FehlerAsync(antwort, abbruch), null, Leer);
        }

        var angelegt = await antwort.Content.ReadFromJsonAsync<InventarArtikel>(ApiJson.Optionen, abbruch)
                       ?? throw new HttpRequestException("Leere Antwort zum Artikel.");
        return new ArtikelAktion(InventarAktionErgebnis.Erfolg, angelegt, Leer);
    }

    /// <summary><c>DELETE …/inventar/{artikelId}</c>: gelöscht, noch reserviert (<c>409 konflikt</c>) oder ein anderer Fehler.</summary>
    public async Task<InventarAktionErgebnis> ArtikelLoeschenAsync(Guid orgId, Guid artikelId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(ArtikelPfad(orgId, artikelId), abbruch);
        return antwort.IsSuccessStatusCode ? InventarAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>
    /// <c>POST …/freizeiten/{freizeitId}/inventar/{artikelId}/reservierungen</c>: reserviert mit Konflikthinweisen, Feldfehler (<c>400</c>, z. B. am
    /// Feld <c>bis</c>), abgeschlossene Freizeit oder ein anderer Fehler.
    /// </summary>
    public async Task<ReservierungAktion> ReservierenAsync(Guid freizeitId, Guid artikelId, ReservierungNeu reservierung, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{FreizeitInventarPfad(freizeitId)}/{artikelId}/reservierungen", reservierung, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new ReservierungAktion(InventarAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        if (!antwort.IsSuccessStatusCode)
        {
            return new ReservierungAktion(await FehlerAsync(antwort, abbruch), null, Leer);
        }

        var angelegt = await antwort.Content.ReadFromJsonAsync<Reservierung>(ApiJson.Optionen, abbruch)
                       ?? throw new HttpRequestException("Leere Antwort zur Reservierung.");
        return new ReservierungAktion(InventarAktionErgebnis.Erfolg, angelegt, Leer);
    }

    /// <summary><c>DELETE …/freizeiten/{freizeitId}/inventar/reservierungen/{reservierungId}</c>: aufgehoben, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<InventarAktionErgebnis> AufhebenAsync(Guid freizeitId, Guid reservierungId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync($"{FreizeitInventarPfad(freizeitId)}/reservierungen/{reservierungId}", abbruch);
        return antwort.IsSuccessStatusCode ? InventarAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Story 3-4: Pfad der Schadens- und Verlustliste einer Freizeit.</summary>
    public static string SchaedenPfad(Guid freizeitId) => $"{FreizeitInventarPfad(freizeitId)}/schaeden";

    /// <summary>Story 3-4: Pfad eines Eintrags, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string SchadenPfad(Guid freizeitId, Guid eintragId) => $"{SchaedenPfad(freizeitId)}/{eintragId}";

    /// <summary>Story 3-4: Pfad der Anschaffungswünsche einer Organisation.</summary>
    public static string AnschaffungswuenschePfad(Guid orgId) => $"{OrgInventarPfad(orgId)}/anschaffungswuensche";

    /// <summary>
    /// Story 3-4: <c>GET /api/freizeiten/{freizeitId}/inventar/schaeden</c> (Leitung und Team): die Einträge, neueste zuerst, oder <c>null</c> bei
    /// <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<List<SchadenEintrag>?> SchaedenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<List<SchadenEintrag>>(SchaedenPfad(freizeitId), abbruch);

    /// <summary>Story 3-4: <c>POST …/inventar/schaeden</c>: eingetragen mit dem Eintrag, Feldfehler (<c>400</c>) oder ein anderer Fehler.</summary>
    public async Task<SchadenAktion> SchadenEintragenAsync(Guid freizeitId, SchadenNeu eintrag, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(SchaedenPfad(freizeitId), eintrag, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new SchadenAktion(InventarAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        if (!antwort.IsSuccessStatusCode)
        {
            return new SchadenAktion(await FehlerAsync(antwort, abbruch), null, Leer);
        }

        var angelegt = await antwort.Content.ReadFromJsonAsync<SchadenEintrag>(ApiJson.Optionen, abbruch)
                       ?? throw new HttpRequestException("Leere Antwort zum Eintrag.");
        return new SchadenAktion(InventarAktionErgebnis.Erfolg, angelegt, Leer);
    }

    /// <summary>Story 3-4: <c>DELETE …/inventar/schaeden/{eintragId}</c>: entfernt oder ein Fehler (z. B. schon entfernt).</summary>
    public async Task<InventarAktionErgebnis> SchadenEntfernenAsync(Guid freizeitId, Guid eintragId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(SchadenPfad(freizeitId, eintragId), abbruch);
        return antwort.IsSuccessStatusCode ? InventarAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>
    /// Story 3-4: <c>GET /api/organisationen/{orgId}/inventar/anschaffungswuensche</c> (Org-Admin, Org-Überblick): je abgeschlossener Freizeit ihre
    /// Einträge, oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<List<AnschaffungswunschFreizeit>?> AnschaffungswuenscheAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<List<AnschaffungswunschFreizeit>>(AnschaffungswuenschePfad(orgId), abbruch);

    /// <summary>Story 3-4: <c>DELETE …/inventar/anschaffungswuensche/{eintragId}</c> (Org-Admin): erledigt oder ein Fehler.</summary>
    public async Task<InventarAktionErgebnis> ErledigtAsync(Guid orgId, Guid eintragId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync($"{AnschaffungswuenschePfad(orgId)}/{eintragId}", abbruch);
        return antwort.IsSuccessStatusCode ? InventarAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    private static readonly IReadOnlyDictionary<string, string> Leer = new Dictionary<string, string>();

    private async Task<T?> LadenAsync<T>(string pfad, CancellationToken abbruch)
        where T : class
    {
        using var antwort = await http.GetAsync(pfad, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<T>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException($"Leere Antwort von {pfad}.");
    }

    /// <summary>Ordnet eine Fehlerantwort zu: <c>409</c> nach Problemcode, alles andere ist ein Fehler.</summary>
    private static async Task<InventarAktionErgebnis> FehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode != HttpStatusCode.Conflict)
        {
            return InventarAktionErgebnis.Fehler;
        }

        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            var code = problem.RootElement.ValueKind == JsonValueKind.Object
                       && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var wert)
                       && wert.ValueKind == JsonValueKind.String
                ? wert.GetString()
                : null;
            return code switch
            {
                Problemcodes.Konflikt => InventarAktionErgebnis.Konflikt,
                Problemcodes.FreizeitAbgeschlossen => InventarAktionErgebnis.Abgeschlossen,
                _ => InventarAktionErgebnis.Fehler,
            };
        }
        catch (JsonException)
        {
            return InventarAktionErgebnis.Fehler;
        }
    }
}
