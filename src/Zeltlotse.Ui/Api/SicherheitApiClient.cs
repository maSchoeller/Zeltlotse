using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Sicherheit;

namespace Zeltlotse.Ui.Api;

/// <summary>Typisierter Client des Moduls Sicherheit (AD-29, handgeschrieben). Story 2-8: die Sicherheitskette einer Freizeit.</summary>
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
}
