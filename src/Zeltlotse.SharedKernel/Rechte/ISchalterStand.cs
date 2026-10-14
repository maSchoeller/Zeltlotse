namespace Zeltlotse.SharedKernel.Rechte;

/// <summary>
/// Stand der Funktions-Schalter einer Freizeit für die laufende Anfrage (AD-17, AD-20). Der API-Host setzt die Schnittstelle aus der
/// lesenden Schnittstelle von Freizeiten um und liest je Freizeit höchstens einmal pro Anfrage. Eine unbekannte oder fremde Freizeit
/// hat keinen Schalter an.
/// </summary>
public interface ISchalterStand
{
    Task<bool> IstAnAsync(Guid freizeitId, FunktionsSchalter schalter, CancellationToken abbruch = default);
}
