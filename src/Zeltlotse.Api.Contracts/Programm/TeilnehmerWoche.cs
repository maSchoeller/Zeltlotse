namespace Zeltlotse.Api.Contracts.Programm;

/// <summary>
/// Story 6-4 (FR-64, NS-10): Antwort von <c>GET /api/teilnehmer/programm</c> (Gast-Sitzung): die Wochenübersicht nur mit Tag, Uhrzeit und Titel, sortiert
/// wie der Wochenraster. Ohne Kennung, Version, Übernahme oder Details.
/// </summary>
public sealed record TeilnehmerWoche(DateOnly ErsterTag, DateOnly LetzterTag, IReadOnlyList<TeilnehmerPunkt> Punkte);

/// <summary>Story 6-4: Ein Programmpunkt in der Wochenübersicht der Teilnehmenden.</summary>
/// <param name="Beginn">Uhrzeit als <c>HH:mm</c> oder <c>null</c>.</param>
public sealed record TeilnehmerPunkt(DateOnly Tag, string? Beginn, string Titel);
