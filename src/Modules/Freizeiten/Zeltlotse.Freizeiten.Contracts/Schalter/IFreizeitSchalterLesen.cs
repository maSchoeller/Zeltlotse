using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Freizeiten.Contracts.Schalter;

/// <summary>
/// Story 3-7: die eingeschalteten Funktions-Schalter einer Freizeit (AD-20). Freizeiten besitzt die Schalter; der API-Host liest sie für
/// Endpunkte und Pipeline (einmal pro Anfrage, AD-17), der Kontext-Endpunkt für die Oberfläche. Läuft unter dem Mandantenfilter: Eine
/// unbekannte oder fremde Freizeit hat keinen Schalter an.
/// </summary>
public interface IFreizeitSchalterLesen
{
    /// <summary>Die eingeschalteten Schalter in Enum-Reihenfolge; leer für eine unbekannte oder fremde Freizeit.</summary>
    Task<IReadOnlyCollection<FunktionsSchalter>> SchalterAsync(Guid freizeitId, CancellationToken abbruch = default);
}
