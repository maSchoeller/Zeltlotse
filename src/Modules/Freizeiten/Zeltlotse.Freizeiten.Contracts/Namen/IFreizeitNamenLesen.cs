namespace Zeltlotse.Freizeiten.Contracts.Namen;

/// <summary>
/// Story 2-10: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Sicherheit → Freizeiten): die Namen von Freizeiten, z. B. für das Archiv
/// der Vorfall-Notizen. Läuft unter dem Mandantenfilter: Freizeiten anderer Organisationen fehlen im Ergebnis.
/// </summary>
public interface IFreizeitNamenLesen
{
    /// <summary>Je vorhandener Freizeit ihr Name; unbekannte Kennungen fehlen.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamenAsync(IReadOnlyCollection<Guid> freizeitIds, CancellationToken abbruch = default);
}
