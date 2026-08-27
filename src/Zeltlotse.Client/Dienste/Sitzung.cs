using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Zeltlotse.Core.Konten.Contracts;

namespace Zeltlotse.Client.Dienste;

/// <summary>
/// Hält die Anmeldung. Das Zugriffstoken lebt ausschließlich hier im
/// Arbeitsspeicher — nichts davon landet im Browserspeicher. Über einen
/// Neuladen der Seite trägt allein das HttpOnly-Cookie beim Server.
/// </summary>
public sealed class Sitzung(HttpClient http)
{
    private string? _zugriffstoken;

    public AngemeldeterNutzerDto? Nutzer { get; private set; }

    public bool IstAngemeldet => Nutzer is not null;

    public event Action? Geaendert;

    /// <summary>
    /// Beim Start: „Bin ich angemeldet?" Eine 401-Antwort ist hier die
    /// <em>richtige</em> Antwort und keine abgelaufene Sitzung — deshalb läuft
    /// dieser Weg bewusst ohne Wiederholungslogik.
    /// </summary>
    public async Task<bool> WiederherstellenAsync()
    {
        try
        {
            return await ErneuernAsync();
        }
        catch (HttpRequestException)
        {
            // Server nicht erreichbar: „nicht angemeldet" ist die einzig
            // sichere Deutung — beide Formen des Misserfolgs führen hierher.
            return false;
        }
    }

    public async Task<string?> AnmeldenAsync(string email, string kennwort)
    {
        var antwort = await SendenAsync(HttpMethod.Post, "/api/auth/anmelden",
            new AnmeldungAnfrage(email, kennwort));

        if (antwort.StatusCode == HttpStatusCode.Unauthorized)
        {
            return "E-Mail-Adresse oder Kennwort stimmen nicht.";
        }

        if (!antwort.IsSuccessStatusCode)
        {
            return await FehlertextAsync(antwort);
        }

        await UebernehmenAsync(antwort);

        return null;
    }

    public async Task<string?> EinrichtenAsync(string name, string email, string kennwort)
    {
        var antwort = await SendenAsync(HttpMethod.Post, "/api/einrichtung",
            new EinrichtungAnfrage(name, email, kennwort));

        if (!antwort.IsSuccessStatusCode)
        {
            return await FehlertextAsync(antwort);
        }

        await UebernehmenAsync(antwort);

        return null;
    }

    public async Task<string?> EinladungEinloesenAsync(string token, string kennwort)
    {
        var antwort = await SendenAsync(HttpMethod.Post, "/api/einladungen/einloesen",
            new EinladungEinloesen(token, kennwort));

        if (!antwort.IsSuccessStatusCode)
        {
            return await FehlertextAsync(antwort);
        }

        await UebernehmenAsync(antwort);

        return null;
    }

    public async Task AbmeldenAsync()
    {
        try
        {
            await SendenAsync(HttpMethod.Post, "/api/auth/abmelden", null);
        }
        catch (HttpRequestException)
        {
            // Lokal abmelden gelingt auch, wenn der Server gerade nicht antwortet.
        }

        _zugriffstoken = null;
        Nutzer = null;
        Geaendert?.Invoke();
    }

    public async Task NutzerNeuLadenAsync()
    {
        if (_zugriffstoken is null)
        {
            return;
        }

        var anfrage = Vorbereiten(HttpMethod.Get, "/api/ich");
        var antwort = await http.SendAsync(anfrage);

        if (antwort.IsSuccessStatusCode)
        {
            Nutzer = await antwort.Content.ReadFromJsonAsync<AngemeldeterNutzerDto>();
            Geaendert?.Invoke();
        }
    }

    /// <summary>Holt ein frisches Zugriffstoken über das Erneuerungs-Cookie.</summary>
    public async Task<bool> ErneuernAsync()
    {
        var antwort = await SendenAsync(HttpMethod.Post, "/api/auth/erneuern", null);

        if (!antwort.IsSuccessStatusCode)
        {
            _zugriffstoken = null;
            Nutzer = null;
            return false;
        }

        await UebernehmenAsync(antwort);

        return true;
    }

    public HttpRequestMessage Vorbereiten(HttpMethod verfahren, string pfad, object? rumpf = null)
    {
        var anfrage = new HttpRequestMessage(verfahren, pfad);

        // Ohne diese Kennung weist der Server cookie-gestützte Aufrufe ab.
        anfrage.Headers.Add("X-Zeltlotse-Anfrage", "1");
        anfrage.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        if (_zugriffstoken is not null)
        {
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _zugriffstoken);
        }

        if (rumpf is not null)
        {
            anfrage.Content = JsonContent.Create(rumpf, rumpf.GetType());
        }

        return anfrage;
    }

    private Task<HttpResponseMessage> SendenAsync(HttpMethod verfahren, string pfad, object? rumpf)
        => http.SendAsync(Vorbereiten(verfahren, pfad, rumpf));

    private async Task UebernehmenAsync(HttpResponseMessage antwort)
    {
        var anmeldung = await antwort.Content.ReadFromJsonAsync<AnmeldungAntwort>();
        _zugriffstoken = anmeldung?.Zugriffstoken;

        await NutzerNeuLadenAsync();

        Geaendert?.Invoke();
    }

    private static async Task<string> FehlertextAsync(HttpResponseMessage antwort)
    {
        try
        {
            var inhalt = await antwort.Content.ReadFromJsonAsync<Fehlerantwort>();

            if (!string.IsNullOrWhiteSpace(inhalt?.Fehler))
            {
                return inhalt.Fehler;
            }
        }
        catch (Exception)
        {
            // Antwort ohne verwertbaren Rumpf — dann eben der allgemeine Satz.
        }

        return "Das hat nicht geklappt. Versuche es noch einmal.";
    }

    private sealed record Fehlerantwort(string? Fehler);
}
