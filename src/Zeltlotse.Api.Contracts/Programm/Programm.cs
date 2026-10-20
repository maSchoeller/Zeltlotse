using System.Globalization;
using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Programm;

/// <summary>Story 3-1: feste Mahlzeit eines Freizeittages (FR-34); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Mahlzeit>))]
public enum Mahlzeit
{
    /// <summary>Frühstück, anfangs um 08:00 Uhr.</summary>
    Fruehstueck = 1,

    /// <summary>Mittagessen, anfangs um 12:30 Uhr.</summary>
    Mittagessen = 2,

    /// <summary>Abendessen, anfangs um 18:30 Uhr.</summary>
    Abendessen = 3,
}

/// <summary>
/// Story 3-1: Antwort von <c>GET /api/freizeiten/{freizeitId}/programm</c> (Leitung und Team, FR-34): der Wochenraster der Freizeit mit allen
/// Programmpunkten, sortiert nach Tag, Beginn (ohne Uhrzeit zuletzt) und Titel. Frühstück, Mittag- und Abendessen stehen an jedem Tag.
/// </summary>
public sealed record Wochenraster(DateOnly ErsterTag, DateOnly LetzterTag, IReadOnlyList<Programmpunkt> Punkte);

/// <summary>Story 3-1: Ein Programmpunkt im Wochenraster (FR-34, FR-35).</summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
/// <param name="Beginn">Uhrzeit als <c>HH:mm</c> oder <c>null</c> (ohne Uhrzeit steht der Punkt am Ende des Tages).</param>
/// <param name="Mahlzeit">Gesetzt bei den festen Mahlzeiten; sie lassen sich nicht löschen.</param>
/// <param name="UebernommenVon">Die Person, die den Punkt übernommen hat; den Namen setzt der Client zusammen (AD-2).</param>
/// <param name="Meins">Die angemeldete Person hat den Punkt übernommen.</param>
/// <param name="InBibliothek">Story 3-6: Der Punkt ist mit einem noch vorhandenen Eintrag der Programmbibliothek verknüpft (abgelegt).</param>
/// <param name="MitgeteilterBeginn">
/// Story 6-2 (FR-62): Gesetzt heißt „Geändert, noch nicht mitgeteilt“: die zuletzt mitgeteilte Uhrzeit als <c>HH:mm</c> (der Wert für „statt …“).
/// Bei einem Punkt, der vorher ohne Uhrzeit war, entsteht kein Hinweis.
/// </param>
public sealed record Programmpunkt(
    Guid Id,
    uint Version,
    DateOnly Tag,
    string? Beginn,
    string Titel,
    Mahlzeit? Mahlzeit,
    Guid? UebernommenVon,
    bool Meins,
    bool InBibliothek = false,
    string? MitgeteilterBeginn = null);

/// <summary>
/// Story 6-2: Körper von <c>POST …/programm/punkte/{punktId}/mitgeteilt</c> („Ja, informieren“ nach dem Senden in „Alle“ oder „Ausblenden“).
/// </summary>
/// <param name="Beginn">Die mitgeteilte Uhrzeit als <c>HH:mm</c>, leer heißt ohne Uhrzeit; der Hinweis verschwindet nur, wenn sie noch aktuell ist.</param>
public sealed record ProgrammpunktMitgeteilt(string? Beginn);

/// <summary>Story 3-1: Körper von <c>POST /api/freizeiten/{freizeitId}/programm/punkte</c>: nur der Titel ist nötig (NS-5), dazu der Tag.</summary>
/// <param name="Titel">1 bis <see cref="ProgrammpunktFelder.TitelMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="Tag">Ein Tag der Freizeit.</param>
/// <param name="Beginn">Uhrzeit als <c>HH:mm</c>; leer heißt ohne Uhrzeit.</param>
public sealed record ProgrammpunktNeu(string? Titel, DateOnly? Tag, string? Beginn);

/// <summary>Story 3-1: Bearbeitbare Felder eines Programmpunkts für <c>PATCH …/programm/punkte/{punktId}</c> (AD-19).</summary>
public enum ProgrammpunktFeld
{
    Titel,
    Beginn,
}

/// <summary>
/// Story 3-1: JSON-Namen und Grenzen der Felder eines Programmpunkts. <c>PATCH …/programm/punkte/{punktId}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "titel"|"beginn": neu }</c> mit genau einem Feld als Text (<c>beginn</c> als <c>HH:mm</c>, leer = keine
/// Uhrzeit), Antwort <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>.
/// </summary>
public static class ProgrammpunktFelder
{
    public const string Titel = "titel";

    public const string Tag = "tag";

    public const string Beginn = "beginn";

    public const int TitelMaximaleLaenge = 120;

    /// <summary>Format der Uhrzeit in der API.</summary>
    public const string UhrzeitFormat = "HH:mm";

    public static string JsonName(ProgrammpunktFeld feld) => feld switch
    {
        ProgrammpunktFeld.Titel => Titel,
        ProgrammpunktFeld.Beginn => Beginn,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Liest eine Uhrzeit im Format <c>HH:mm</c>; leer oder nur Leerzeichen heißt ohne Uhrzeit (<c>true</c> mit <c>null</c>).</summary>
    public static bool UhrzeitLesen(string? text, out TimeOnly? uhrzeit)
    {
        uhrzeit = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (TimeOnly.TryParseExact(text.Trim(), UhrzeitFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var gelesen))
        {
            uhrzeit = gelesen;
            return true;
        }

        return false;
    }

    /// <summary>Eine Uhrzeit im Format <c>HH:mm</c> oder <c>null</c>.</summary>
    public static string? UhrzeitText(TimeOnly? uhrzeit) => uhrzeit?.ToString(UhrzeitFormat, CultureInfo.InvariantCulture);
}
