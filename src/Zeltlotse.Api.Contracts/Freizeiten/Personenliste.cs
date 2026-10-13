namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>
/// Eine Zeile von <c>GET /api/freizeiten/{freizeitId}/personenliste</c> (Leitung und Team, Story 2-5, FR-24): eine eingegangene oder
/// bestätigte Anmeldung der Freizeit mit Namen (bei einer gelöschten Person der Vorname „Gelöschte Person“), Art und Status. Abgesagte
/// fehlen. Sortiert nach Art (Teilnehmende zuerst), Nachname, Vorname. Den Freizeitpass liefert Personen (AD-2).
/// </summary>
public sealed record PersonenlisteZeile(
    Guid AnmeldungId,
    Guid PersonId,
    string? Vorname,
    string? Nachname,
    AnmeldungArt Art,
    AnmeldungStatus Status);
