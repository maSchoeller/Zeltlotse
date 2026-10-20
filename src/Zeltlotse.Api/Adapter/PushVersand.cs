using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Api.Adapter;

/// <summary>
/// Story 6-1: Web-Push-Adapter für <see cref="IPushVersand"/> (AD-21, AD-23): der einzige Ort mit Code für das Push-Protokoll, über Lib.Net.Http.WebPush
/// mit dem eigenen VAPID-Schlüssel (<see cref="VapidSchluessel"/>), ohne Firebase und ohne Hersteller-SDK. Die Kontakt-Adresse (VAPID „sub“) ist
/// <c>Zeltlotse:OeffentlicheAdresse</c>, wenn sie <c>https</c> ist. Vor jedem Senden prüft er den Endpunkt noch einmal gegen die bekannten Push-Dienste
/// (Schutz gegen SSRF) und folgt keinen Weiterleitungen. Antwortet der Push-Dienst mit 404 oder 410, ist das Ziel ungültig; jeder andere Fehler wirft.
/// Inhalt, Endpunkt und Schlüssel erscheinen nie in Logs (AD-22).
/// </summary>
internal sealed class WebPushVersand(IHttpClientFactory clients, VapidSchluessel vapid, IConfiguration konfiguration) : IPushVersand
{
    public const string ClientName = "Zeltlotse.Push";

    /// <summary>So lange hält der Push-Dienst einen Push für ein ausgeschaltetes Gerät bereit.</summary>
    private static readonly int Lebensdauer = (int)TimeSpan.FromDays(1).TotalSeconds;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PushErgebnis> SendenAsync(PushZiel ziel, PushInhalt inhalt, CancellationToken abbruch = default)
    {
        if (!PushEndpunkte.IstErlaubt(ziel.Endpunkt))
        {
            return PushErgebnis.ZielUngueltig;
        }

        var authentifizierung = new VapidAuthentication(vapid.Oeffentlich, vapid.Privat);
        var adresse = OeffentlicheAdresse.Lesen(konfiguration);
        if (adresse.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            authentifizierung.Subject = adresse;
        }

        var client = new PushServiceClient(clients.CreateClient(ClientName)) { DefaultAuthentication = authentifizierung };
        var abo = new PushSubscription { Endpoint = ziel.Endpunkt };
        abo.SetKey(PushEncryptionKeyName.P256DH, ziel.P256dh);
        abo.SetKey(PushEncryptionKeyName.Auth, ziel.Auth);
        var nachricht = new PushMessage(JsonSerializer.Serialize(new { titel = inhalt.Titel, text = inhalt.Text, url = inhalt.Url }, Json))
        {
            TimeToLive = Lebensdauer,
            Urgency = PushMessageUrgency.Normal,
        };

        try
        {
            await client.RequestPushMessageDeliveryAsync(abo, nachricht, abbruch);
            return PushErgebnis.Zugestellt;
        }
        catch (PushServiceClientException fehler) when (fehler.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushErgebnis.ZielUngueltig;
        }
    }
}

internal static class PushRegistrierung
{
    /// <summary>So lange wartet der Versand höchstens auf einen Push-Dienst; die Ziele gehen nacheinander hinaus.</summary>
    public static readonly TimeSpan Wartezeit = TimeSpan.FromSeconds(10);

    /// <summary>Story 6-1: der Web-Push-Adapter mit eigenem HTTP-Client ohne Weiterleitungen und mit kurzer Wartezeit.</summary>
    public static IServiceCollection AddPushVersand(this IServiceCollection services)
    {
        services.AddHttpClient(WebPushVersand.ClientName)
            .ConfigureHttpClient(client => client.Timeout = Wartezeit)
            .ConfigurePrimaryHttpMessageHandler(PrimaerHandler);
        services.AddSingleton<IPushVersand, WebPushVersand>();
        return services;
    }

    /// <summary>Folgt keinen Weiterleitungen (Schutz gegen SSRF) und schickt keine Cookies.</summary>
    internal static SocketsHttpHandler PrimaerHandler() => new() { AllowAutoRedirect = false, UseCookies = false };
}
