namespace Zeltlotse.Sicherheit.Contracts.Events;

/// <summary>
/// Story 3-4: Im Vorfall-Protokoll einer Freizeit ist eine Entnahme aus dem Erste-Hilfe-Kasten eingetragen (FR-32, Tabelle Ereignis-Fluss): Inventar
/// legt daraus einen Eintrag auf der Schadens- und Verlustliste an. Datensatz im Umschlag ist der Vorfall-Eintrag. Ohne Text und ohne Person
/// (AD-4): Der Text bleibt verschlüsselt in Sicherheit.
/// </summary>
public sealed record ErsteHilfeEntnommen(Guid VorfallEintragId, Guid FreizeitId, DateTimeOffset ErfasstAm);
