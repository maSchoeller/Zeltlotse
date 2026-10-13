using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>Antworttyp einer Frage des Freizeitpasses (AD-10); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FrageArt>))]
public enum FrageArt
{
    /// <summary>Freier Text.</summary>
    Freitext,

    /// <summary>Kalendertag <c>yyyy-MM-dd</c>.</summary>
    Datum,

    /// <summary><c>ja</c> oder <c>nein</c> (die Optionen stehen in der Frage).</summary>
    JaNein,

    /// <summary>Mehrfachauswahl, Schlüssel mit Komma getrennt.</summary>
    Auswahl,
}

/// <summary>
/// Antwort von <c>GET /api/ich/freizeitpass/{personId}</c> (AD-26) und <c>GET /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId}</c>
/// (Gast-Link, AD-11): je Frage des festen Katalogs (in Anzeigereihenfolge) die Antwort der Person. Welche Fragen eine Freizeit abfragt,
/// liefert Freizeiten an der Anmeldung; der Client zeigt nur diese (AD-2, NFR-7).
/// </summary>
/// <param name="Vorname">Vorname der Person, für die Überschrift.</param>
public sealed record FreizeitpassAnsicht(Guid PersonId, string? Vorname, IReadOnlyList<FreizeitpassFrage> Fragen);

/// <summary>Eine Frage des Freizeitpasses mit der Antwort der Person.</summary>
/// <param name="Kennung">Stabile Kennung der Frage, z. B. <c>allergien</c>.</param>
/// <param name="Frage">Text der Frage.</param>
/// <param name="Optionen">Optionen bei <see cref="FrageArt.Auswahl"/> und <see cref="FrageArt.JaNein"/>, sonst leer.</param>
/// <param name="MitSonstiges">Die Frage hat zusätzlich das Freitextfeld „Sonstiges“.</param>
/// <param name="Wert">Gespeicherter Wert im Wire-Format oder <c>null</c> (keine Angabe).</param>
/// <param name="Sonstiges">Gespeicherter Text „Sonstiges“ oder <c>null</c>.</param>
/// <param name="Version">Version der Antwort (AD-19); <c>0</c>, solange es keine Antwort gibt.</param>
/// <param name="Veraltet">Beantwortet und zuletzt vor mehr als 12 Monaten bestätigt: „Stimmt das noch?“ (FR-23).</param>
public sealed record FreizeitpassFrage(
    string Kennung,
    string Frage,
    FrageArt Art,
    IReadOnlyList<FreizeitpassOption> Optionen,
    bool MitSonstiges,
    string? Wert,
    string? Sonstiges,
    uint Version,
    bool Veraltet);

/// <summary>Eine Option einer Frage: gespeicherter Schlüssel und Anzeigetext.</summary>
public sealed record FreizeitpassOption(string Wert, string Text);

/// <summary>Bearbeitbare Felder einer Antwort für <c>PATCH /api/ich/freizeitpass/{personId}/antworten/{kennung}</c> (AD-19).</summary>
public enum FreizeitpassFeld
{
    Wert,
    Sonstiges,
}

/// <summary>
/// JSON-Namen der Felder einer Antwort: Körper <c>{ "version": n, "alterWert": …, "wert"|"sonstiges": neu }</c>, Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>. Freitext höchstens <see cref="FreitextMaximaleLaenge"/> Zeichen,
/// „Sonstiges“ höchstens <see cref="SonstigesMaximaleLaenge"/>.
/// </summary>
public static class FreizeitpassFelder
{
    public const string Wert = "wert";

    public const string Sonstiges = "sonstiges";

    public const int FreitextMaximaleLaenge = 2000;

    public const int SonstigesMaximaleLaenge = 500;

    /// <summary>Format eines Datums im Wire-Format.</summary>
    public const string Datumsformat = "yyyy-MM-dd";

    public static string JsonName(FreizeitpassFeld feld) => feld switch
    {
        FreizeitpassFeld.Wert => Wert,
        FreizeitpassFeld.Sonstiges => Sonstiges,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Fehlerschlüssel eines Felds im Formular des Gasts: <c>&lt;kennung&gt;.wert</c> bzw. <c>&lt;kennung&gt;.sonstiges</c>.</summary>
    public static string FormularFehler(string kennung, FreizeitpassFeld feld) => $"{kennung}.{JsonName(feld)}";
}

/// <summary>
/// Körper von <c>POST /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId}</c> (Gast-Link, klassisches Formular): alle
/// Antworten der Seite. Geschrieben werden nur geänderte Werte; eine unveränderte Antwort wird nur bestätigt, wenn ihre Kennung in
/// <see cref="Bestaetigt"/> steht („Stimmt noch“).
/// </summary>
public sealed record FreizeitpassFormular(IReadOnlyList<FreizeitpassFormularAntwort> Antworten, IReadOnlyList<string>? Bestaetigt = null);

/// <summary>Eine Antwort im Formular des Gasts im Wire-Format.</summary>
public sealed record FreizeitpassFormularAntwort(string Kennung, string? Wert, string? Sonstiges = null)
{
    /// <summary>Keine Antworttexte ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(FreizeitpassFormularAntwort)}({Kennung})";
}
