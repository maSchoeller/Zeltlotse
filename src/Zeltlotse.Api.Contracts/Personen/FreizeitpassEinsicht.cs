using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>
/// Was Leitung oder Team vom Freizeitpass einer Person sehen (Story 2-5, FR-24, AD-8); im JSON als camelCase-Wort.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FreizeitpassEinsichtZustand>))]
public enum FreizeitpassEinsichtZustand
{
    /// <summary>Die Fragen der Freigabe mit den Antworten der Person.</summary>
    [JsonStringEnumMemberName("sichtbar")]
    Sichtbar,

    /// <summary>Die Anmeldung ist noch nicht bestätigt; das Team sieht den Pass erst danach (FR-24 b). Keine Fragen.</summary>
    [JsonStringEnumMemberName("abBestaetigung")]
    AbBestaetigung,

    /// <summary>Das Freigabefenster ist zu Ende oder die Anmeldung abgesagt. Keine Fragen.</summary>
    [JsonStringEnumMemberName("beendet")]
    Beendet,
}

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass/{personId}</c> (Leitung und Team, Story 2-5, FR-24, AD-8): der
/// Freizeitpass einer Person im Kontext der Freizeit. Bei <see cref="FreizeitpassEinsichtZustand.Sichtbar"/> nur die Fragen der Freigabe
/// in Katalogreihenfolge (unbeantwortet mit <c>wert: null</c>), sonst keine.
/// </summary>
/// <param name="EinsichtBis">Ende des Freigabefensters (bei einer Absage deren Zeitpunkt).</param>
/// <param name="BeendetAm">Nur bei <see cref="FreizeitpassEinsichtZustand.Beendet"/>: wann die Einsicht endete.</param>
public sealed record FreizeitpassEinsicht(
    Guid PersonId,
    string? Vorname,
    string? Nachname,
    FreizeitpassEinsichtZustand Zustand,
    DateTimeOffset EinsichtBis,
    DateTimeOffset? BeendetAm,
    IReadOnlyList<FreizeitpassFrage> Fragen);
