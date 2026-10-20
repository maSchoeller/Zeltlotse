using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Zeltlotse.SharedKernel.Push;

/// <summary>
/// Story 6-1: Das eigene VAPID-Schlüsselpaar für Web-Push (AD-21). Aus der Geheimnisverwaltung kommt nur der private P-256-Schlüssel: 32 Bytes als
/// Base64. Der öffentliche Schlüssel wird daraus abgeleitet; so gibt es kein zweites Geheimnis, das nicht dazu passt. Beide liegen hier base64url-kodiert
/// vor, wie Browser und Push-Dienste sie erwarten (öffentlich: unkomprimierter Punkt mit 65 Bytes).
/// </summary>
public sealed class VapidSchluessel
{
    /// <summary>Konfigurationsschlüssel; der Wert ist Base64 mit genau 32 Bytes.</summary>
    public const string Konfigurationsschluessel = "Zeltlotse:Push:VapidSchluessel";

    private const int SchluesselLaenge = 32;

    private static readonly string Fehlermeldung =
        $"Der VAPID-Schlüssel ({Konfigurationsschluessel}) fehlt oder ist kein Base64-Wert mit genau {SchluesselLaenge} Bytes für P-256.";

    private VapidSchluessel(string oeffentlich, string privat)
    {
        Oeffentlich = oeffentlich;
        Privat = privat;
    }

    /// <summary>Der öffentliche Schlüssel (base64url, unkomprimierter P-256-Punkt); der Browser braucht ihn zum Abonnieren.</summary>
    public string Oeffentlich { get; }

    /// <summary>Der private Schlüssel (base64url); nur der Push-Adapter im API-Host liest ihn.</summary>
    public string Privat { get; }

    /// <summary>Liest und prüft den Schlüssel; die Meldung nennt den Wert nie.</summary>
    public static VapidSchluessel AusKonfiguration(IConfiguration konfiguration) => AusBase64(konfiguration[Konfigurationsschluessel]);

    /// <summary>Prüft den Base64-Wert und leitet den öffentlichen Schlüssel ab; die Meldung nennt den Wert nie.</summary>
    public static VapidSchluessel AusBase64(string? wert)
    {
        if (string.IsNullOrWhiteSpace(wert))
        {
            throw new InvalidOperationException(Fehlermeldung);
        }

        var puffer = new byte[(wert.Length * 3 / 4) + 3];
        try
        {
            if (!Convert.TryFromBase64String(wert.Trim(), puffer, out var laenge) || laenge != SchluesselLaenge)
            {
                throw new InvalidOperationException(Fehlermeldung);
            }

            var privat = puffer[..SchluesselLaenge];
            try
            {
                using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = privat });
                var punkt = ec.ExportParameters(includePrivateParameters: false).Q;
                byte[] oeffentlich = [0x04, .. punkt.X!, .. punkt.Y!];
                return new VapidSchluessel(Base64Url(oeffentlich), Base64Url(privat));
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException(Fehlermeldung);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privat);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(puffer);
        }
    }

    /// <summary>Nie einen Schlüssel ausgeben.</summary>
    public override string ToString() => nameof(VapidSchluessel);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
