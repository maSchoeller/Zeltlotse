namespace Zeltlotse.Freizeiten.Contracts.TeilnehmerZugang;

/// <summary>
/// Story 6-4 (AD-11, AD-12, AD-20): Der QR-Link des Teilnehmer-Zugangs aus Sicht anderer Teile. Freizeiten besitzt den QR-Link. <see cref="PruefenAsync"/>
/// sucht ohne Mandantenfilter, denn dort ist die Gast-Sitzung die Quelle des Mandanten-Kontexts (AD-6); nur die Rechteprüfung des Hosts ruft sie auf.
/// <see cref="StempelAsync"/> liest unter dem Mandantenfilter (Pfeil Kanäle → Freizeiten: nur Gast-Abos mit dem aktuellen Stempel bekommen Pushes).
/// </summary>
public interface IQrZugangLesen
{
    /// <summary>
    /// Die Organisation der Freizeit, wenn der QR-Link der Freizeit mit diesem Stempel noch besteht, der Teilnehmer-Zugang eingeschaltet ist und das
    /// Freizeitende noch nicht erreicht ist; sonst <c>null</c>.
    /// </summary>
    Task<Guid?> PruefenAsync(Guid freizeitId, Guid stempel, CancellationToken abbruch = default);

    /// <summary>Der aktuelle Stempel des QR-Links der Freizeit (er wechselt mit jedem Erneuern); <c>null</c> ohne Link oder für eine fremde Freizeit.</summary>
    Task<Guid?> StempelAsync(Guid freizeitId, CancellationToken abbruch = default);
}
