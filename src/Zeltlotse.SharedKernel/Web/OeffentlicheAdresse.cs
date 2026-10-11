using Microsoft.Extensions.Configuration;

namespace Zeltlotse.SharedKernel.Web;

/// <summary>
/// Die öffentliche Adresse von Zeltlotse (Pflichtkonfiguration <see cref="Schluessel"/>, absolute URL), z. B. für Links
/// in Mails. Der API-Host prüft sie beim Start.
/// </summary>
public static class OeffentlicheAdresse
{
    public const string Schluessel = "Zeltlotse:OeffentlicheAdresse";

    /// <summary>Die Adresse ohne Schrägstrich am Ende; wirft mit dem Schlüssel in der Meldung, wenn sie fehlt oder nicht absolut ist.</summary>
    public static string Lesen(IConfiguration konfiguration)
    {
        var wert = konfiguration[Schluessel]?.Trim();
        if (string.IsNullOrEmpty(wert)
            || !Uri.TryCreate(wert, UriKind.Absolute, out var adresse)
            || adresse.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(adresse.Query)
            || !string.IsNullOrEmpty(adresse.Fragment))
        {
            throw new InvalidOperationException($"{Schluessel} fehlt oder ist keine absolute http(s)-Adresse.");
        }

        return wert.TrimEnd('/');
    }
}
