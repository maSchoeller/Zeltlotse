namespace Zeltlotse.Ui.Transport;

/// <summary>
/// Meldet jeden API-Aufruf der Web-App an <see cref="IVerbindung"/>: Netzfehler und Zeitüberschreitung (die Ausnahme geht
/// weiter an den Aufrufer) sowie Erfolg. Erfolg heißt: Der Server hat geantwortet, auch mit einem Fehlerstatus (auch als
/// <see cref="HttpRequestException"/> mit Statuscode); die Verbindung steht. Das Zeitlimit liegt hier statt am
/// <see cref="HttpClient"/>, damit sich eine Zeitüberschreitung vom Abbruch durch den Aufrufer unterscheiden lässt.
/// </summary>
public sealed class VerbindungsHandler(IVerbindung verbindung, TimeSpan? zeitlimit = null) : DelegatingHandler
{
    /// <summary>Vorgabe für das Zeitlimit eines Aufrufs.</summary>
    public static readonly TimeSpan VorgabeZeitlimit = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _zeitlimit = zeitlimit ?? VorgabeZeitlimit;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var zeit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        zeit.CancelAfter(_zeitlimit);
        HttpResponseMessage antwort;
        try
        {
            antwort = await base.SendAsync(request, zeit.Token);
        }
        catch (HttpRequestException fehler) when (fehler.StatusCode is null)
        {
            await verbindung.NetzfehlerMeldenAsync();
            throw;
        }
        catch (HttpRequestException)
        {
            // Der Server hat geantwortet (z. B. 5xx beim Holen des Antiforgery-Tokens): kein Netzfehler.
            await verbindung.ErfolgMeldenAsync();
            throw;
        }
        catch (OperationCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            await verbindung.NetzfehlerMeldenAsync();
            throw new TaskCanceledException("Zeitüberschreitung beim API-Aufruf.", new TimeoutException(fehler.Message, fehler));
        }

        await verbindung.ErfolgMeldenAsync();
        return antwort;
    }
}
