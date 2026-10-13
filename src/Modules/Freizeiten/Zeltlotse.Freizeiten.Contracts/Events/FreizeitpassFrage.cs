namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Eine Frage des Freizeitpasses, wie sie <see cref="AnmeldungEingegangen"/> und <see cref="AnmeldungBestaetigt"/> tragen (AD-4, AD-8,
/// Story 2-5): Ereignisse tragen keine Texte, darum steht die Katalog-Kennung als Enum. Der Name eines Mitglieds ist die
/// PascalCase-Form der Kennung (<c>zecken-entfernen</c> → <see cref="ZeckenEntfernen"/>); ein Test hält die Zuordnung zum Katalog
/// eineindeutig. Werte sind gespeichert (Outbox); nur anhängen.
/// </summary>
public enum FreizeitpassFrage
{
    Allergien = 1,
    Medikamente = 2,
    Notfallkontakte = 3,
    Ernaehrung = 4,
    Krankheiten = 5,
    Krankenversicherung = 6,
    Tetanus = 7,
    Schwimmen = 8,
    ZeckenEntfernen = 9,
    Hinweise = 10,
}
