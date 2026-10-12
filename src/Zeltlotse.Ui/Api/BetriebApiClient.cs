using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Betrieb;

namespace Zeltlotse.Ui.Api;

/// <summary>
/// Typisierter Client der Endpunkte des Hosts (AD-29, handgeschrieben): Betrieb unter <c>/api/betrieb</c> und die Datenauskunft unter
/// <c>/api/ich/daten</c>.
/// </summary>
public sealed class BetriebApiClient(HttpClient http)
{
    /// <summary><c>GET /api/betrieb/antiforgery</c>: das Token für ändernde Aufrufe; das Cookie setzt der Server.</summary>
    public async Task<string> AntiforgeryTokenHolenAsync(CancellationToken abbruch = default)
    {
        var antwort = await http.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", ApiJson.Optionen, abbruch)
                      ?? throw new HttpRequestException("Leere Antwort beim Holen des Antiforgery-Tokens.");
        return antwort.Token;
    }

    /// <summary>
    /// <c>GET /api/ich/daten</c> (FR-14): die Datenauskunft der eigenen Person; <c>null</c> bei <c>401</c> (Anmeldung älter als 10
    /// Minuten), dann führt der Sitzungs-Handler zur Anmeldung mit Rückweg.
    /// </summary>
    public async Task<DatenauskunftAntwort?> DatenauskunftLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync("/api/ich/daten", abbruch);
        if (antwort.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<DatenauskunftAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Datenauskunft.");
    }

    /// <summary><c>POST /api/betrieb/fehler</c>: meldet einen unerwarteten Fehler im Browser (nur Fehlerart, Seite, Trace-ID).</summary>
    public async Task FehlerMeldenAsync(BrowserFehler meldung, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/betrieb/fehler", meldung, ApiJson.Optionen, abbruch);
        antwort.EnsureSuccessStatusCode();
    }
}
