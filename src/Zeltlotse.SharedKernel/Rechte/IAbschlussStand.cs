namespace Zeltlotse.SharedKernel.Rechte;

/// <summary>
/// Story 3-8: ob eine Freizeit gelaufen ist (AD-17), für die laufende Anfrage. Der API-Host setzt die Schnittstelle aus den Zeitpunkten
/// von Freizeiten um und liest je Freizeit höchstens einmal pro Anfrage. Eine unbekannte oder fremde Freizeit gilt als nicht gelaufen.
/// </summary>
public interface IAbschlussStand
{
    Task<bool> IstAbgeschlossenAsync(Guid freizeitId, CancellationToken abbruch = default);
}
