using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>Art einer Anmeldung (FR-16): über den Anmeldelink für Teilnehmende oder für Mitarbeitende; im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnmeldungArt>))]
public enum AnmeldungArt
{
    Teilnehmende,
    Mitarbeitende,
}

/// <summary>Status einer Anmeldung (FR-20, FR-21): eingegangen, von der Leitung bestätigt oder abgesagt. Im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnmeldungStatus>))]
public enum AnmeldungStatus
{
    Eingegangen,
    Bestaetigt,
    Abgesagt,
}

/// <summary>
/// JSON-Namen der Felder der Anmeldungs-Endpunkte (Fehlerschlüssel in ProblemDetails, AD-27) und der Grenzwert der
/// Teilnahmebedingungen.
/// </summary>
public static class AnmeldungFelder
{
    public const string TeilnahmebedingungenBestaetigt = "teilnahmebedingungenBestaetigt";

    public const string PersonId = "personId";

    public const string Teilnahmebedingungen = "teilnahmebedingungen";

    public const int TeilnahmebedingungenMaximaleLaenge = 4000;
}

/// <summary>
/// Körper von <c>POST /api/freizeiten/oeffentlich/{token}/anmeldungen</c> (anonym, Gast-Anmeldung, FR-17): das Gast-Token aus
/// <c>POST /api/personen/oeffentlich/gaeste</c> und das Kind der Gast-Familie. Nur über den Anmeldelink für Teilnehmende.
/// </summary>
public sealed record GastAnmeldungAbsenden(string? GastToken, Guid KindPersonId, bool TeilnahmebedingungenBestaetigt)
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => nameof(GastAnmeldungAbsenden);
}

/// <summary>
/// Körper von <c>POST /api/ich/anmeldungen</c> (mit Konto, FR-16, FR-19): der Anmeldelink und die angemeldete Person selbst
/// (<see cref="PersonId"/> leer oder die eigene Kennung) oder bei Teilnehmenden ein Kind unter 18 der eigenen Familie.
/// </summary>
public sealed record AnmeldungAbsenden(string? AnmeldelinkToken, Guid? PersonId, bool TeilnahmebedingungenBestaetigt)
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(AnmeldungAbsenden)}({PersonId})";
}

/// <summary>Antwort <c>201</c> der beiden Anmelde-Endpunkte.</summary>
public sealed record AnmeldungAngelegt(Guid AnmeldungId);

/// <summary>
/// Eine Zeile von <c>GET /api/ich/anmeldungen</c> (AD-26): nur Übersichtsfelder, über Organisationen hinweg, für die Person und
/// die Kinder ihrer Familie.
/// </summary>
/// <param name="KontaktMail">Mail-Adresse der Leitung für Absagen (Story 2-3, FR-21), wenn die Leitung sie eingetragen hat.</param>
/// <param name="KontaktTelefon">Telefon der Leitung für Absagen (Story 2-3, FR-21), wenn eingetragen.</param>
public sealed record AnmeldungZeile(
    Guid AnmeldungId,
    Guid PersonId,
    string FreizeitName,
    DateOnly ErsterTag,
    DateOnly LetzterTag,
    AnmeldungArt Art,
    AnmeldungStatus Status,
    string? KontaktMail = null,
    string? KontaktTelefon = null);

/// <summary>
/// Antwort von <c>GET /api/freizeiten/gast/{gastToken}</c> (anonym, Gast-Link, AD-11): die Anmeldungen der Gast-Familie mit
/// Übersichtsfeldern und wie lange der Gast-Link gilt.
/// </summary>
/// <param name="GueltigBis">Zeitpunkt, an dem der Gast-Link endet (Ende des Freigabefensters, vor der ersten Freigabe 1 Tag ab dem Anlegen).</param>
public sealed record GastLinkAnsicht(DateTimeOffset GueltigBis, IReadOnlyList<GastLinkAnmeldung> Anmeldungen);

/// <summary>
/// Eine Anmeldung auf dem Gast-Link: Vorname des Kindes, Freizeit, Zeitraum, Status und der Kontakt der Leitung für Absagen
/// (Story 2-3, FR-21), wenn eingetragen.
/// </summary>
public sealed record GastLinkAnmeldung(
    string Vorname,
    string FreizeitName,
    DateOnly ErsterTag,
    DateOnly LetzterTag,
    AnmeldungStatus Status,
    string? KontaktMail = null,
    string? KontaktTelefon = null);

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/anmeldungen</c> (Leitung, FR-20, FR-21, FR-67): die Höchstzahl, die bestätigten
/// Teilnehmenden (Mitarbeitende zählen nicht) und alle Anmeldungen, nach Eingang sortiert.
/// </summary>
public sealed record AnmeldungenUebersicht(int? HoechstzahlTeilnehmende, int BestaetigteTeilnehmende, IReadOnlyList<LeitungAnmeldungZeile> Anmeldungen);

/// <summary>
/// Eine Anmeldung in der Liste der Leitung: Name der Person (bei einer gelöschten Person der Vorname „Gelöschte Person“), Art,
/// Status, Eingang und, nach Bestätigung oder Absage, wann entschieden wurde.
/// </summary>
public sealed record LeitungAnmeldungZeile(
    Guid AnmeldungId,
    Guid PersonId,
    string? Vorname,
    string? Nachname,
    AnmeldungArt Art,
    AnmeldungStatus Status,
    DateTimeOffset EingegangenAm,
    DateTimeOffset? EntschiedenAm);
