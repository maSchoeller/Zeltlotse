using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>Datenklassen für Logs und Telemetrie (AD-22).</summary>
public static class Datenklassen
{
    /// <summary>Namen, Mail-Adressen, Freitext und alles andere mit Personenbezug.</summary>
    public static DataClassification Personenbezogen { get; } = new("Zeltlotse", "Personenbezogen");
}

/// <summary>
/// Markiert einen Parameter einer <c>[LoggerMessage]</c>-Methode als personenbezogen: Im Log steht dann nur sein HMAC-Wert (AD-22).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PersonenbezogenAttribute() : DataClassificationAttribute(Datenklassen.Personenbezogen);

/// <summary>Schwärzung personenbezogener Log-Parameter mit dem HMAC-Redaktor (AD-22).</summary>
public static class ProtokollSchwaerzung
{
    /// <summary>Konfigurationsschlüssel; der Wert ist Base64 mit mindestens 32 Bytes und liegt in der Geheimnisverwaltung.</summary>
    public const string Konfigurationsschluessel = "Zeltlotse:Protokoll:HmacSchluessel";

    private const int MindestLaenge = 32;

    /// <summary>Kennung des Schlüssels im geschwärzten Wert; bei einer Rotation hochzählen.</summary>
    private const int SchluesselKennung = 1;

    private static readonly string Fehlermeldung =
        $"Der HMAC-Schlüssel für Logs ({Konfigurationsschluessel}) fehlt oder ist kein Base64-Wert mit mindestens {MindestLaenge} Bytes.";

    /// <summary>
    /// Registriert den HMAC-Redaktor für <see cref="Datenklassen.Personenbezogen"/> und schaltet die Schwärzung
    /// im Logging ein. Der Schlüssel wird beim ersten Logger geprüft; <see cref="SchluesselPruefen"/> prüft ihn beim Start.
    /// </summary>
    public static IServiceCollection AddProtokollSchwaerzung(this IServiceCollection services)
    {
#pragma warning disable EXTEXP0002 // HMAC-Redaktor ist als experimentell markiert; AD-22 verlangt ihn ausdrücklich.
        services.AddRedaction(redaktion => redaktion.SetHmacRedactor(_ => { }, new DataClassificationSet(Datenklassen.Personenbezogen)));
#pragma warning restore EXTEXP0002
        services.AddOptions<HmacRedactorOptions>().Configure<IConfiguration>((optionen, konfiguration) =>
        {
            optionen.KeyId = SchluesselKennung;
            optionen.Key = SchluesselLesen(konfiguration);
        });
        services.AddLogging(logging => logging.EnableRedaction());
        return services;
    }

    /// <summary>Bricht ab, wenn der Schlüssel fehlt oder zu kurz ist; die Meldung nennt den Wert nie.</summary>
    public static void SchluesselPruefen(IConfiguration konfiguration) => _ = SchluesselLesen(konfiguration);

    private static string SchluesselLesen(IConfiguration konfiguration)
    {
        var wert = konfiguration[Konfigurationsschluessel]?.Trim();
        if (string.IsNullOrEmpty(wert))
        {
            throw new InvalidOperationException(Fehlermeldung);
        }

        var puffer = new byte[(wert.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(wert, puffer, out var laenge) || laenge < MindestLaenge)
        {
            throw new InvalidOperationException(Fehlermeldung);
        }

        // Kanonisch neu kodieren: Der Redaktor prüft streng (keine gesetzten Füllbits), erzeugte Geheimnisse sind das nicht immer.
        return Convert.ToBase64String(puffer, 0, laenge);
    }
}
