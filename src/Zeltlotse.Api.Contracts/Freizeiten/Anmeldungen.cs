using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>Art einer Anmeldung (FR-16): über den Anmeldelink für Teilnehmende oder für Mitarbeitende; im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnmeldungArt>))]
public enum AnmeldungArt
{
    Teilnehmende,
    Mitarbeitende,
}

/// <summary>Status einer Anmeldung (FR-20); bis Story 2-3 gibt es nur „eingegangen“. Im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnmeldungStatus>))]
public enum AnmeldungStatus
{
    Eingegangen,
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
public sealed record AnmeldungZeile(
    Guid AnmeldungId,
    Guid PersonId,
    string FreizeitName,
    DateOnly ErsterTag,
    DateOnly LetzterTag,
    AnmeldungArt Art,
    AnmeldungStatus Status);

/// <summary>
/// Antwort von <c>GET /api/freizeiten/gast/{gastToken}</c> (anonym, Gast-Link, AD-11): die Anmeldungen der Gast-Familie mit
/// Übersichtsfeldern und wie lange der Gast-Link gilt.
/// </summary>
/// <param name="GueltigBis">Zeitpunkt, an dem der Gast-Link endet (Ende des Freigabefensters, vor der ersten Freigabe 1 Tag ab dem Anlegen).</param>
public sealed record GastLinkAnsicht(DateTimeOffset GueltigBis, IReadOnlyList<GastLinkAnmeldung> Anmeldungen);

/// <summary>Eine Anmeldung auf dem Gast-Link: Vorname des Kindes, Freizeit, Zeitraum und Status.</summary>
public sealed record GastLinkAnmeldung(string Vorname, string FreizeitName, DateOnly ErsterTag, DateOnly LetzterTag, AnmeldungStatus Status);
