using System.Text.RegularExpressions;

namespace Zeltlotse.SharedKernel.Dateien;

/// <summary>
/// Dateiablage im S3-kompatiblen Speicher (AD-15), unter <c>&lt;modul-präfix&gt;/&lt;kennung&gt;</c>.
/// Der Adapter steht im API-Host; nur dort gibt es anbieterspezifischen Code (AD-23).
/// Besonders schützenswerte Dateien werden vorher mit <c>IVerschluesselung</c> verschlüsselt.
/// </summary>
public interface IDateiSpeicher
{
    Task SpeichernAsync(string modulPraefix, Guid kennung, byte[] inhalt, string contentType, CancellationToken abbruch = default);

    /// <summary>Die Datei oder <c>null</c>, wenn es sie nicht gibt.</summary>
    Task<GespeicherteDatei?> LesenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default);

    /// <summary>Löscht endgültig; eine fehlende Datei ist kein Fehler.</summary>
    Task LoeschenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default);
}

public sealed record GespeicherteDatei(byte[] Inhalt, string ContentType);

/// <summary>Bildet den Objekt-Schlüssel; der Präfix ist das Schema des besitzenden Moduls.</summary>
public static partial class DateiSchluessel
{
    public static string Bilden(string modulPraefix, Guid kennung)
    {
        if (string.IsNullOrEmpty(modulPraefix) || !PraefixMuster().IsMatch(modulPraefix))
        {
            throw new ArgumentException("Der Modul-Präfix besteht nur aus Kleinbuchstaben, Ziffern und Bindestrich.", nameof(modulPraefix));
        }

        return $"{modulPraefix}/{kennung:D}";
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex PraefixMuster();
}
