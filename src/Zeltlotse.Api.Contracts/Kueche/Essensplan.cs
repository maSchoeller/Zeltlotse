using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Kueche;

/// <summary>Story 4-2: Art einer Mahlzeit im Essensplan (die festen Mahlzeiten des Wochenrasters); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MahlzeitArt>))]
public enum MahlzeitArt
{
    Fruehstueck = 1,
    Mittagessen = 2,
    Abendessen = 3,
}

/// <summary>
/// Story 4-2: Antwort von <c>GET /api/freizeiten/{freizeitId}/kueche/essensplan</c> (Leitung und Team, Modul Verpflegung, CAP-29, FR-47): je Tag
/// der Freizeit die festen Mahlzeiten mit ihren vorgemerkten Gerichten, die Rezepte zur Auswahl (nach Name) und ob die Freizeit Allergien abfragt.
/// </summary>
/// <param name="Personenzahl">Wie <see cref="Rezeptliste.Personenzahl"/>.</param>
/// <param name="AllergienAbgefragt">
/// Die Freizeit fragt die Allergie-Frage für Teilnehmende oder Mitarbeitende ab; sonst gibt es keinen Abgleich und alle Ersatzportionen sind 0.
/// </param>
public sealed record Essensplan(int Personenzahl, bool AllergienAbgefragt, IReadOnlyList<EssensplanTag> Tage, IReadOnlyList<RezeptWahl> Rezepte);

/// <summary>Story 4-2: Ein Tag des Essensplans mit seinen Mahlzeiten in Tagesreihenfolge.</summary>
public sealed record EssensplanTag(DateOnly Tag, IReadOnlyList<EssensplanMahlzeit> Mahlzeiten);

/// <summary>Story 4-2: Eine Mahlzeit (Programmpunkt des Moduls Programm) mit den vorgemerkten Gerichten in der Reihenfolge des Vormerkens.</summary>
/// <param name="ProgrammpunktId">Kennung des Mahlzeiten-Programmpunkts; mit ihr wird ein Gericht vorgemerkt.</param>
/// <param name="Beginn">Uhrzeit als <c>HH:mm</c> oder <c>null</c>.</param>
/// <param name="Titel">Titel im Wochenraster, z. B. „Frühstück“.</param>
/// <param name="Restetag">Story 4-4, FR-53: Die Küche hat die Mahlzeit als Restetag markiert (reine Markierung, ändert nichts am Plan).</param>
public sealed record EssensplanMahlzeit(Guid ProgrammpunktId, string? Beginn, string Titel, MahlzeitArt Art, IReadOnlyList<Gericht> Gerichte, bool Restetag = false);

/// <summary>Story 4-2: Ein vorgemerktes Gericht: ein Rezept an einer Mahlzeit, mit dem Allergen-Abgleich.</summary>
/// <param name="Version">Versionsnummer für den Tausch (veraltet → <c>409 konflikt</c>).</param>
public sealed record Gericht(Guid Id, uint Version, Guid RezeptId, string RezeptName, Ersatzportionen Ersatzportionen);

/// <summary>
/// Story 4-2, FR-48: Ersatzportionen eines Gerichts: so viele bestätigte Personen haben im Freizeitpass ein Allergen, das das Rezept enthält
/// (jede Person zählt einmal), zusätzlich zur Hauptmenge.
/// </summary>
/// <param name="Allergene">Die getroffenen Allergene des Rezepts als Schlüssel in Katalogreihenfolge; ohne Ersatzportion leer.</param>
public sealed record Ersatzportionen(int Anzahl, IReadOnlyList<string> Allergene);

/// <summary>Story 4-2: Ein Rezept zur Auswahl beim Vormerken und Tauschen.</summary>
public sealed record RezeptWahl(Guid Id, string Name);

/// <summary>Story 4-2: Körper von <c>POST …/kueche/gerichte</c>: beide Kennungen sind Pflicht.</summary>
/// <param name="ProgrammpunktId">Eine Mahlzeit dieser Freizeit; sonst <c>400</c> am Feld <c>programmpunktId</c>.</param>
/// <param name="RezeptId">Ein Rezept dieser Freizeit; sonst <c>400</c> am Feld <c>rezeptId</c>.</param>
public sealed record GerichtNeu(Guid? ProgrammpunktId, Guid? RezeptId);

/// <summary>Story 4-2: Antwort von <c>POST …/kueche/gerichte</c> und <c>POST …/gerichte/{gerichtId}/tauschen</c>.</summary>
public sealed record GerichtStand(Guid Id, uint Version, Guid ProgrammpunktId, Guid RezeptId);

/// <summary>Story 4-2: Körper von <c>POST …/kueche/gerichte/{gerichtId}/tauschen</c>: die Version aus der Vorschau und das neue Rezept.</summary>
/// <param name="Version">Ist das Gericht inzwischen geändert (z. B. schon getauscht), <c>409 konflikt</c>.</param>
public sealed record GerichtTauschen(uint Version, Guid? RezeptId);

/// <summary>
/// Story 4-2, FR-50: Antwort von <c>GET …/kueche/gerichte/{gerichtId}/tausch?rezeptId=…</c>: die Posten des Gerichts vorher und genau die, die
/// „Übernehmen“ speichert, nachher; die Vorschau schreibt nichts. Hand-Mengen wandern auf die Zutat des neuen Rezepts mit gleicher Bezeichnung (ohne
/// Groß-/Kleinschreibung) und gleicher Einheit, die übrigen entfallen.
/// </summary>
/// <param name="Version">Version des Gerichts für <c>POST …/tauschen</c>.</param>
/// <param name="Vorher">Story 4-3: nur die noch nicht gekauften Posten des bisherigen Rezepts.</param>
/// <param name="SchonGekauft">Story 4-3, FR-50: so viele Posten des bisherigen Rezepts sind schon gekauft und stehen nicht im Vergleich.</param>
public sealed record Tauschvorschau(
    Guid GerichtId,
    uint Version,
    RezeptWahl Rezept,
    RezeptWahl NeuesRezept,
    IReadOnlyList<TauschZeile> Vorher,
    IReadOnlyList<TauschZeile> Nachher,
    int SchonGekauft);

/// <summary>Story 4-2: Eine Zeile der Tausch-Vorschau.</summary>
/// <param name="Menge">Hochgerechnete Menge oder die Hand-Menge; ohne Menge <c>null</c>.</param>
/// <param name="VonHand">Die Menge ist von Hand gesetzt.</param>
/// <param name="Geaendert">Auf der anderen Seite gibt es keine gleiche Zeile (Bezeichnung, Menge, Einheit, von Hand).</param>
public sealed record TauschZeile(string Bezeichnung, decimal? Menge, string? Einheit, bool VonHand, bool Geaendert);
