namespace Zeltlotse.Personen.Contracts.Freizeitpass;

/// <summary>
/// Story 4-4: Zweckgebundene Leseschnittstelle „Sonderkost für die Küche“ (AD-8, AD-10, FR-52; Pfeil Küche → Personen). Lesen ohne Menschen: Sie liefert
/// nur abgeleitete Merkmale (die gewählten Schlüssel der Frage <see cref="Fragenkatalog.Ernaehrung"/>), nie Antworttexte, „Sonstiges“ oder Kennungen von
/// Personen, und protokolliert den Zweck ohne lesende Person.
/// </summary>
public interface IFreizeitpassSonderkost
{
    /// <summary>
    /// Anonyme Sonderkost-Profile der übergebenen Personen dieser Freizeit, nach denselben Regeln wie <see cref="IFreizeitpassAllergene.ProfileAsync"/>:
    /// je Person mit gültiger Freigabe der Stufe Team, deren Fragen <see cref="Fragenkatalog.Ernaehrung"/> enthalten, die gewählten Schlüssel in
    /// Katalogreihenfolge. Leere Profile fehlen, die Profile sind sortiert. Je gelesener Person ein Eintrag im Einsicht-Protokoll (Zweck
    /// „Sonderkost-Abgleich der Küche“, ohne lesende Person, höchstens einmal am Tag).
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> ProfileAsync(Guid freizeitId, IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default);
}
