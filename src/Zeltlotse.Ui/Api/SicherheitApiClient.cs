using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 2-9: Ergebnis von <see cref="SicherheitApiClient.GelesenAsync"/>.</summary>
public enum SchutzkonzeptGelesenErgebnis
{
    /// <summary>Bestätigt (auch, wenn es schon bestätigt war).</summary>
    Erfolg,

    /// <summary><c>409 konflikt</c>: Das Schutzkonzept wurde inzwischen ausgetauscht oder entfernt.</summary>
    Konflikt,

    /// <summary>Jede andere Antwort, z. B. eine abgeschlossene Freizeit oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>
/// Typisierter Client des Moduls Sicherheit (AD-29, handgeschrieben). Story 2-8: die Sicherheitskette einer Freizeit. Story 2-9: das
/// Schutzkonzept einer Freizeit.
/// </summary>
public sealed class SicherheitApiClient(HttpClient http)
{
    /// <summary>Pfad der Sicherheitskette einer Freizeit, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string SicherheitskettePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/sicherheit/sicherheitskette";

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c> (Leitung und Team, FR-29): die Kette mit Version oder <c>null</c>
    /// bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<Sicherheitskette?> SicherheitsketteLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(SicherheitskettePfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Sicherheitskette>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Sicherheitskette.");
    }

    /// <summary>Story 2-9: Pfad des Schutzkonzepts einer Freizeit, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string SchutzkonzeptPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/sicherheit/schutzkonzept";

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept</c> (Leitung und Team, FR-30): das Schutzkonzept mit dem Team oder
    /// <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<Schutzkonzept?> SchutzkonzeptLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(SchutzkonzeptPfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Schutzkonzept>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Schutzkonzepts.");
    }

    /// <summary>
    /// <c>POST …/schutzkonzept/gelesen {dokumentId}</c>: bestätigt, ausgetauscht (<c>409 konflikt</c>) oder ein anderer Fehler (auch
    /// <c>409 freizeit-abgeschlossen</c>, <c>403</c>/<c>404</c> und <c>5xx</c>).
    /// </summary>
    public async Task<SchutzkonzeptGelesenErgebnis> GelesenAsync(Guid freizeitId, Guid dokumentId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{SchutzkonzeptPfad(freizeitId)}/gelesen", new SchutzkonzeptGelesen(dokumentId), ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            return SchutzkonzeptGelesenErgebnis.Erfolg;
        }

        return antwort.StatusCode == HttpStatusCode.Conflict && await CodeAsync(antwort, abbruch) == Problemcodes.Konflikt
            ? SchutzkonzeptGelesenErgebnis.Konflikt
            : SchutzkonzeptGelesenErgebnis.Fehler;
    }

    /// <summary>Der Problemcode einer ProblemDetails-Antwort oder <c>null</c>.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
