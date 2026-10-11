using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Ui.Transport;

/// <summary>
/// Vergleicht <c>X-Zeltlotse-Version</c> jeder Antwort mit der eigenen Version (<see cref="ZeltlotseVersion.Aktuell"/>)
/// und meldet eine Abweichung an <see cref="NeueVersion"/> (AD-5). Antworten ohne die Kopfzeile ändern nichts.
/// </summary>
public sealed class VersionsHandler(NeueVersion neueVersion, string? eigeneVersion = null) : DelegatingHandler
{
    private readonly string _eigeneVersion = eigeneVersion ?? ZeltlotseVersion.Aktuell;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var antwort = await base.SendAsync(request, cancellationToken);
        if (antwort.Headers.TryGetValues(Kopfzeilen.Version, out var werte)
            && werte.FirstOrDefault() is { Length: > 0 } version
            && !string.Equals(version, _eigeneVersion, StringComparison.Ordinal))
        {
            neueVersion.Melden();
        }

        return antwort;
    }
}
