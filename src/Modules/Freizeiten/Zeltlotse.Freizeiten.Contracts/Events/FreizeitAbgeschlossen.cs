namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Die Freizeit ist abgeschlossen (Wechsel auf die Phase „gelaufen“, Tabelle Ereignis-Fluss): Sicherheit archiviert die Vorfall-Notizen,
/// Kanäle beenden die Gast-Abos. Datensatz im Umschlag ist die Freizeit, die Organisation die der Freizeit. Story 2-10 legt den Typ und den
/// Empfänger in Sicherheit an; auslösen wird es die Story „Freizeit abschließen“.
/// </summary>
public sealed record FreizeitAbgeschlossen(Guid FreizeitId);
