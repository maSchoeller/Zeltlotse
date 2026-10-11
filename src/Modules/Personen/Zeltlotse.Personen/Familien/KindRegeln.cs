using System.Globalization;
using Zeltlotse.Api.Contracts.Personen;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Feldregeln eines Kinderprofils, gemeinsam für „Kind anlegen“ und „Kind bearbeiten“ (NFR-12: Pflicht sind nur Vorname und
/// Geburtsdatum). Jede Regel liefert den Fehlertext für das Feld oder <c>null</c>.
/// </summary>
internal static class KindRegeln
{
    /// <summary>Frühestes erlaubtes Geburtsjahr; davor gilt ein Datum als Tippfehler.</summary>
    public const int FruehestesJahr = 1900;

    public static string? Vorname(string? vorname) =>
        string.IsNullOrWhiteSpace(vorname) ? "Vorname fehlt."
        : vorname.Trim().Length > KindFelder.MaximaleLaenge ? $"Der Vorname ist zu lang (höchstens {KindFelder.MaximaleLaenge} Zeichen)."
        : null;

    public static string? Nachname(string? nachname) =>
        nachname is not null && nachname.Trim().Length > KindFelder.MaximaleLaenge
            ? $"Der Nachname ist zu lang (höchstens {KindFelder.MaximaleLaenge} Zeichen)."
            : null;

    /// <param name="mitVolljaehrigkeit">Beim Anlegen gilt die 18-Grenze; nach einer Änderung darf ein Kind volljährig sein.</param>
    public static string? Geburtsdatum(DateOnly? geburtsdatum, DateOnly heute, bool mitVolljaehrigkeit) =>
        geburtsdatum switch
        {
            null => "Geburtsdatum fehlt.",
            { Year: < FruehestesJahr } => "Das Geburtsdatum ist ungültig.",
            { } tag when tag > heute => "Das Geburtsdatum liegt in der Zukunft.",
            { } tag when mitVolljaehrigkeit && Volljaehrigkeit.Ist(tag, heute) => "Kinder ab 18 legen ein eigenes Konto an.",
            _ => null,
        };

    /// <summary>Das Geburtsdatum als Text im <see cref="KindFelder.Datumsformat"/> (PATCH, AD-19).</summary>
    public static string? Geburtsdatum(string? wert, DateOnly heute)
    {
        if (string.IsNullOrWhiteSpace(wert))
        {
            return "Geburtsdatum fehlt.";
        }

        return DateOnly.TryParseExact(wert.Trim(), KindFelder.Datumsformat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag)
            ? Geburtsdatum(tag, heute, mitVolljaehrigkeit: false)
            : "Das Geburtsdatum ist ungültig.";
    }
}
