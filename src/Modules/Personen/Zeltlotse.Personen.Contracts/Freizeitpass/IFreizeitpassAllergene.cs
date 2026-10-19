namespace Zeltlotse.Personen.Contracts.Freizeitpass;

/// <summary>
/// Story 4-2: Zweckgebundene Leseschnittstelle „Allergene für die Küche“ (AD-8, AD-10, FR-48; Pfeil Küche → Personen). Lesen ohne Menschen: Sie liefert
/// nur abgeleitete Merkmale (die gewählten Allergen-Schlüssel), nie Antworttexte, „Sonstiges“ oder Kennungen von Personen, und protokolliert den
/// Zweck ohne lesende Person.
/// </summary>
public interface IFreizeitpassAllergene
{
    /// <summary>
    /// Anonyme Allergen-Profile der übergebenen Personen dieser Freizeit: je Person mit gültiger Freigabe der Stufe Team (bestätigt, nicht
    /// abgelaufen), deren Fragen <see cref="Fragenkatalog.Allergien"/> enthalten, die gewählten Allergen-Schlüssel in Katalogreihenfolge. Leere Profile
    /// fehlen, die Profile sind sortiert (ohne Rückschluss auf die Reihenfolge der Personen). Personen prüft jede Kennung selbst; Kennungen ohne
    /// passende Freigabe fallen still weg, ebenso Personen, deren Schlüssel vernichtet ist. Je gelesener Person ein Eintrag im Einsicht-Protokoll
    /// (Zweck „Allergen-Abgleich der Küche“, ohne lesende Person, höchstens einmal am Tag).
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> ProfileAsync(Guid freizeitId, IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default);
}
