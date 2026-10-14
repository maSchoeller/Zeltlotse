namespace Zeltlotse.Inventar.Contracts.Events;

/// <summary>
/// Story 3-3: Eine Reservierung im Inventar, die an einem Material eines Programmpunkts hängt, ist entstanden oder aufgehoben (FR-37, Tabelle
/// Ereignis-Fluss): Programm setzt damit den Materialstatus. Datensatz im Umschlag ist die Reservierung; die Folgenummer ordnet Reservieren vor
/// Aufheben. Ohne Material gibt es kein Ereignis.
/// </summary>
public sealed record ReservierungGeaendert(Guid ReservierungId, Guid FreizeitId, Guid ArtikelId, Guid? MaterialId, ReservierungArt Art);

/// <summary>Was mit der Reservierung geschehen ist. Werte sind gespeichert; nur anhängen.</summary>
public enum ReservierungArt
{
    Reserviert = 1,
    Aufgehoben = 2,
}
