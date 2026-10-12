using System.Globalization;

namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Vertrag <em>Datenauskunft</em> (AD-16, FR-14): Jedes Modul mit Personenbezug liefert die gespeicherten Daten einer Person als
/// Abschnitte mit Feld und Wert. Gelesen wird nur über den Lese-Kontext; den Mandantenfilter umgeht die Auskunft mit
/// <c>IgnoreQueryFilters()</c>, aber immer mit Filter auf die Person (AD-6). Der API-Host setzt die Abschnitte aller Module in der
/// Reihenfolge ihrer Registrierung zusammen; leere Abschnitte entfallen.
/// </summary>
public interface IDatenauskunft
{
    Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch);
}

/// <summary>Ein Abschnitt der Auskunft, z. B. „Profil“, mit seinen Zeilen.</summary>
public sealed record AuskunftAbschnitt(string Titel, IReadOnlyList<AuskunftZeile> Zeilen);

/// <summary>Eine Zeile der Auskunft: Feld und Wert als Text, Datumswerte deutsch formatiert (<see cref="AuskunftFormat"/>).</summary>
public sealed record AuskunftZeile(string Feld, string Wert);

/// <summary>Einheitliche Darstellung der Werte in der Auskunft: Datum als TT.MM.JJJJ (Europe/Berlin), ja/nein.</summary>
public static class AuskunftFormat
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static string Datum(DateOnly tag) => tag.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string Datum(DateTimeOffset zeitpunkt) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(zeitpunkt, Berlin).DateTime).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Zeitpunkt als „TT.MM.JJJJ HH:mm“ in Europe/Berlin.</summary>
    public static string Zeitpunkt(DateTimeOffset zeitpunkt) => TimeZoneInfo.ConvertTime(zeitpunkt, Berlin).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string JaNein(bool wert) => wert ? "ja" : "nein";

    /// <summary>„Vorname Nachname“; ohne beide „Name fehlt“.</summary>
    public static string Name(string? vorname, string? nachname)
    {
        var name = string.Join(' ', new[] { vorname, nachname }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        return name.Length == 0 ? "Name fehlt" : name;
    }
}
