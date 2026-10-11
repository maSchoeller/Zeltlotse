using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Public.Transport;

/// <summary>
/// Transport der öffentlichen Seiten (AD-5, AD-25): setzt an jedem API-Aufruf die Dienst-Kennung
/// <c>X-Zeltlotse-Dienst</c> und die IP des Besuchers der laufenden Anfrage (<c>X-Zeltlotse-Besucher-Ip</c>),
/// damit die Zugriffsbegrenzung je Besucher zählt. Ohne laufende Anfrage wirft er, statt ohne Besucher-IP zu senden.
/// </summary>
public sealed class PublicTransportHandler(IHttpContextAccessor anfrage, PublicDienstKennung kennung) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var besucher = anfrage.HttpContext?.Connection.RemoteIpAddress
                       ?? throw new InvalidOperationException(
                           "Die öffentlichen Seiten rufen die API nur aus einer laufenden Anfrage mit bekannter Besucher-IP auf.");

        request.Headers.Remove(Kopfzeilen.Dienst);
        request.Headers.Remove(Kopfzeilen.BesucherIp);
        request.Headers.Add(Kopfzeilen.Dienst, kennung.Geheimnis);
        request.Headers.Add(Kopfzeilen.BesucherIp, (besucher.IsIPv4MappedToIPv6 ? besucher.MapToIPv4() : besucher).ToString());
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Das Geheimnis der Dienst-Kennung <c>PublicSeiten</c>, vom Host per DI übergeben; nie loggen.</summary>
public sealed class PublicDienstKennung(string geheimnis)
{
    public string Geheimnis { get; } = geheimnis;

    public override string ToString() => nameof(PublicDienstKennung);
}
