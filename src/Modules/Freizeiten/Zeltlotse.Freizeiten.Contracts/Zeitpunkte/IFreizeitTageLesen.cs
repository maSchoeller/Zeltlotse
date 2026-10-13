namespace Zeltlotse.Freizeiten.Contracts.Zeitpunkte;

/// <summary>
/// Story 3-1: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Programm → Freizeiten): die Tage einer Freizeit, z. B. für den Wochenraster
/// und die festen Mahlzeiten. Läuft unter dem Mandantenfilter: Freizeiten anderer Organisationen fehlen im Ergebnis.
/// </summary>
public interface IFreizeitTageLesen
{
    /// <summary>Je vorhandener Freizeit erster und letzter Tag; unbekannte Kennungen fehlen (Menge statt Einzelabruf).</summary>
    Task<IReadOnlyDictionary<Guid, FreizeitTage>> TageAsync(IReadOnlyCollection<Guid> freizeitIds, CancellationToken abbruch = default);
}

/// <summary>Erster und letzter Tag einer Freizeit (beide eingeschlossen); nach dem Anlegen unveränderlich.</summary>
public sealed record FreizeitTage(DateOnly ErsterTag, DateOnly LetzterTag)
{
    /// <summary>Alle Tage vom ersten bis zum letzten, aufsteigend.</summary>
    public IEnumerable<DateOnly> Alle()
    {
        for (var tag = ErsterTag; tag <= LetzterTag; tag = tag.AddDays(1))
        {
            yield return tag;
        }
    }

    /// <summary>Gehört der Tag zur Freizeit?</summary>
    public bool Enthaelt(DateOnly tag) => tag >= ErsterTag && tag <= LetzterTag;
}
