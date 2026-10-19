namespace Zeltlotse.Freizeiten.Contracts.Fragen;

/// <summary>
/// Story 4-2: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Küche → Freizeiten): ob eine Freizeit eine Frage des Freizeitpasses abfragt (AD-10),
/// z. B. die Allergie-Frage für den Abgleich der Küche. Läuft unter dem Mandantenfilter: Freizeiten anderer Organisationen fragen nichts ab.
/// </summary>
public interface IFreizeitFragenLesen
{
    /// <summary>Die Freizeit fragt die Kennung für Teilnehmende oder für Mitarbeitende ab.</summary>
    Task<bool> FragtAbAsync(Guid freizeitId, string kennung, CancellationToken abbruch = default);
}
