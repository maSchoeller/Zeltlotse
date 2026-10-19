namespace Zeltlotse.Programm.Contracts.Mahlzeiten;

/// <summary>Story 4-2: Art einer festen Mahlzeit; eigenes Enum der Modul-Verträge (AD-5: keine API-Typen zwischen Modulen).</summary>
public enum FreizeitMahlzeitArt
{
    Fruehstueck = 1,
    Mittagessen = 2,
    Abendessen = 3,
}

/// <summary>Story 4-2: Eine feste Mahlzeit einer Freizeit, ein Programmpunkt des Wochenrasters.</summary>
/// <param name="Beginn">Uhrzeit; ohne Uhrzeit <c>null</c>.</param>
/// <param name="Titel">Titel im Wochenraster, z. B. „Frühstück“.</param>
public sealed record FreizeitMahlzeit(Guid ProgrammpunktId, DateOnly Tag, TimeOnly? Beginn, string Titel, FreizeitMahlzeitArt MahlzeitArt);

/// <summary>
/// Story 4-2: Lesende Schnittstelle von Programm (AD-2, Pfeil Küche → Programm): die festen Mahlzeiten einer Freizeit, an denen die Küche Gerichte
/// vormerkt. Läuft unter dem Mandantenfilter: Freizeiten anderer Organisationen haben keine Mahlzeiten.
/// </summary>
public interface IFreizeitMahlzeitenLesen
{
    /// <summary>
    /// Alle Mahlzeiten der Freizeit nach Tag, Uhrzeit und Art. Fehlen Mahlzeiten (z. B. weil der Wochenraster nie geöffnet wurde), legt Programm sie
    /// vorher an (wie beim Laden des Wochenrasters). Unbekannte oder fremde Freizeit: leer.
    /// </summary>
    Task<IReadOnlyList<FreizeitMahlzeit>> MahlzeitenAsync(Guid freizeitId, CancellationToken abbruch = default);
}
