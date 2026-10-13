namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass-stand</c> (nur Leitung, Story 2-6, FR-25, FR-27): wer in dieser
/// Freizeit offene Angaben im Freizeitpass hat, sortiert nach Nachname, dann Vorname. Ohne Antworten und ohne Fragen; nur Zahlen.
/// </summary>
public sealed record FreizeitpassStand(IReadOnlyList<FreizeitpassStandPerson> Personen);

/// <summary>
/// Eine Person mit offenen Angaben: <see cref="Gefragt"/> ist die Zahl der Fragen ihrer Freigabe, <see cref="Offen"/> die Zahl davon, die
/// unbeantwortet oder älter als 12 Monate sind. Gleich viele heißt: Der Freizeitpass fehlt noch.
/// </summary>
/// <param name="ErinnertAm">Die letzte Erinnerung zum Freizeitpass in dieser Freizeit an diese Person, sonst <c>null</c>.</param>
public sealed record FreizeitpassStandPerson(Guid PersonId, string? Vorname, string? Nachname, int Gefragt, int Offen, DateTimeOffset? ErinnertAm);

/// <summary>Körper von <c>POST /api/freizeiten/{freizeitId}/personen/freizeitpass/erinnerungen</c> (nur Leitung, Story 2-6, FR-25).</summary>
public sealed record FreizeitpassErinnerungSenden(Guid? PersonId);

/// <summary>Antwort <c>200</c> von <c>POST …/personen/freizeitpass/erinnerungen</c>: wann erinnert wurde.</summary>
public sealed record FreizeitpassErinnert(DateTimeOffset ErinnertAm);
