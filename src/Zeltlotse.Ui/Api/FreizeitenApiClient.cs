using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Ui.Api;

/// <summary>Typisierter Client des Moduls Freizeiten (AD-29, handgeschrieben); bisher nur der Freizeit-Kontext.</summary>
public sealed class FreizeitenApiClient(HttpClient http)
{
    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/kontext</c>: der Kontext bei <c>200</c>; <c>null</c> („nicht verfügbar“) bei
    /// <c>403</c> und <c>404</c>. Andere Antworten enden in einer <see cref="HttpRequestException"/>.
    /// </summary>
    public async Task<FreizeitKontext?> KontextLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync($"/api/freizeiten/{freizeitId}/kontext", abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<FreizeitKontext>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Freizeit-Kontexts.");
    }
}
