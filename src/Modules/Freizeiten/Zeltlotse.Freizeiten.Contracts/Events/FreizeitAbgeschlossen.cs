namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Die Freizeit ist abgeschlossen (Wechsel auf die Phase „gelaufen“, Tabelle Ereignis-Fluss): Sicherheit archiviert die Vorfall-Notizen,
/// Kanäle beenden die Gast-Abos. Datensatz im Umschlag ist die Freizeit, die Organisation die der Freizeit. Story 2-10 legt den Typ und den
/// Empfänger in Sicherheit an; Story 3-8 löst es aus, wenn die Leitung die Freizeit abschließt (<c>POST …/abschliessen</c>).
/// </summary>
public sealed record FreizeitAbgeschlossen(Guid FreizeitId);
