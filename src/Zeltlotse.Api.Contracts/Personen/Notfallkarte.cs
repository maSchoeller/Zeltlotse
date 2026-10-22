namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>
/// Story 6-5: Ein Eintrag von <c>GET /api/freizeiten/{freizeitId}/personen/notfallkarte</c> (Leitung und Team, FR-65, AD-18): eine
/// bestätigte Person mit gültiger Freigabe und nur den nötigen Notfallfragen (<c>allergien</c>, <c>medikamente</c>, <c>notfallkontakte</c>,
/// <c>krankheiten</c>), soweit die Freigabe sie enthält, in Katalogreihenfolge (unbeantwortet mit <c>wert: null</c>). Die Liste ist nach
/// Nachname, dann Vorname sortiert. Die Web-App legt sie verschlüsselt für den Offline-Fall ab.
/// </summary>
public sealed record NotfallkartePerson(Guid PersonId, string Vorname, string Nachname, IReadOnlyList<FreizeitpassFrage> Fragen);
