using System.Net;

namespace Zeltlotse.SharedKernel.Push;

/// <summary>
/// Story 6-1: Welche Adressen als Push-Endpunkt gelten (Schutz gegen SSRF, also dagegen, dass der Server Anfragen an beliebige Adressen schickt): nur
/// <c>https</c> ohne Port-Angabe und ohne Anmeldedaten, mit einem Host-Namen (keine IP-Adresse, nicht <c>localhost</c>), der einer der bekannten
/// Push-Dienste der Browser ist oder auf ihn endet. Die Kanäle prüfen beim Speichern eines Abos, der Versand im API-Host vor jedem Senden.
/// </summary>
public static class PushEndpunkte
{
    /// <summary>Die bekannten Push-Dienste (Chrome/Edge/Android, Firefox, Safari, Windows).</summary>
    public static IReadOnlyList<string> BekannteDienste { get; } =
        ["fcm.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com"];

    /// <summary>Längste erlaubte Adresse.</summary>
    public const int MaximaleLaenge = 1000;

    public static bool IstErlaubt(string? endpunkt)
    {
        if (string.IsNullOrWhiteSpace(endpunkt) || endpunkt.Length > MaximaleLaenge
            || !Uri.TryCreate(endpunkt, UriKind.Absolute, out var adresse)
            || adresse.Scheme != Uri.UriSchemeHttps
            || !adresse.IsDefaultPort
            || !string.IsNullOrEmpty(adresse.UserInfo)
            || adresse.HostNameType != UriHostNameType.Dns
            || IPAddress.TryParse(adresse.Host, out _))
        {
            return false;
        }

        var host = adresse.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host != "localhost"
               && BekannteDienste.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }
}
