using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Ui.Api;

namespace Zeltlotse.Ui.Transport;

/// <summary>
/// Schutz gegen gefälschte Anfragen aus Sicht der Web-App (AD-25): Beim ersten ändernden Aufruf holt der Handler das
/// Antiforgery-Token einmal je App-Start über den <see cref="BetriebApiClient"/> und setzt <c>X-Zeltlotse-Antiforgery</c>.
/// Antwortet der Server mit <c>400</c> und <c>code=antiforgery-ungueltig</c> (Token abgelaufen), holt er es einmal neu
/// und wiederholt die Anfrage; ein zweites <c>400</c> geht an den Aufrufer.
/// </summary>
public sealed class AntiforgeryHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _sperre = new(1, 1);
    private string? _token;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IstAendernd(request.Method))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // Der Körper muss für eine Wiederholung noch einmal lesbar sein.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
        }

        var token = await TokenAsync(verwerfen: null, request, cancellationToken);
        TokenSetzen(request, token);
        var antwort = await base.SendAsync(request, cancellationToken);
        if (!await IstTokenUngueltigAsync(antwort, cancellationToken))
        {
            return antwort;
        }

        antwort.Dispose();
        token = await TokenAsync(verwerfen: token, request, cancellationToken);
        TokenSetzen(request, token);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> TokenAsync(string? verwerfen, HttpRequestMessage request, CancellationToken abbruch)
    {
        await _sperre.WaitAsync(abbruch);
        try
        {
            if (_token is null || _token == verwerfen)
            {
                // Abruf über den typisierten Client, aber am Rest der Kette vorbei (ein GET braucht kein Token).
                using var client = new HttpClient(InnerHandler!, disposeHandler: false) { BaseAddress = Basis(request) };
                _token = await new BetriebApiClient(client).AntiforgeryTokenHolenAsync(abbruch);
            }

            return _token;
        }
        finally
        {
            _sperre.Release();
        }
    }

    private static Uri? Basis(HttpRequestMessage request) =>
        request.RequestUri is { IsAbsoluteUri: true } uri ? new Uri(uri.GetLeftPart(UriPartial.Authority)) : null;

    private static void TokenSetzen(HttpRequestMessage request, string token)
    {
        request.Headers.Remove(Kopfzeilen.Antiforgery);
        request.Headers.Add(Kopfzeilen.Antiforgery, token);
    }

    private static async Task<bool> IstTokenUngueltigAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode != HttpStatusCode.BadRequest || antwort.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return false;
        }

        // Puffert den Körper; der Aufrufer kann ihn danach weiter lesen.
        await antwort.Content.LoadIntoBufferAsync(abbruch);
        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var code)
                   && code.ValueKind == JsonValueKind.String
                   && code.GetString() == Problemcodes.AntiforgeryUngueltig;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IstAendernd(HttpMethod methode) =>
        methode == HttpMethod.Post || methode == HttpMethod.Put || methode == HttpMethod.Patch || methode == HttpMethod.Delete;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sperre.Dispose();
        }

        base.Dispose(disposing);
    }
}
