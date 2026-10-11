using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Betrieb;

namespace Zeltlotse.Ui.Api;

/// <summary>Typisierter Client der Betriebs-Endpunkte des Hosts unter <c>/api/betrieb</c> (AD-29, handgeschrieben).</summary>
public sealed class BetriebApiClient(HttpClient http)
{
    /// <summary><c>GET /api/betrieb/antiforgery</c>: das Token für ändernde Aufrufe; das Cookie setzt der Server.</summary>
    public async Task<string> AntiforgeryTokenHolenAsync(CancellationToken abbruch = default)
    {
        var antwort = await http.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", ApiJson.Optionen, abbruch)
                      ?? throw new HttpRequestException("Leere Antwort beim Holen des Antiforgery-Tokens.");
        return antwort.Token;
    }

    /// <summary><c>POST /api/betrieb/fehler</c>: meldet einen unerwarteten Fehler im Browser (nur Fehlerart, Seite, Trace-ID).</summary>
    public async Task FehlerMeldenAsync(BrowserFehler meldung, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/betrieb/fehler", meldung, ApiJson.Optionen, abbruch);
        antwort.EnsureSuccessStatusCode();
    }
}
