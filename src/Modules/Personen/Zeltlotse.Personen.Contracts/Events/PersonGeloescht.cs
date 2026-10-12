namespace Zeltlotse.Personen.Contracts.Events;

/// <summary>
/// Eine Person ist gelöscht (FR-14, AD-16, Tabelle Ereignis-Fluss): Personen hat Profil, Schlüssel und alle personeneigenen
/// Daten schon endgültig gelöscht. Jedes Modul mit Personenbezug wendet daraufhin seine Lösch-Klasse an (eigene Zeilen löschen,
/// Bezüge in Daten anderer durch den Platzhalter ersetzen). Datensatz im Umschlag ist die Person.
/// </summary>
public sealed record PersonGeloescht(Guid PersonId);
