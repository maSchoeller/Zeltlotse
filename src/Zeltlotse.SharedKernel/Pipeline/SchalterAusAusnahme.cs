using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Ein Benutzer-Command gehört zu einem Funktions-Schalter, der für die Freizeit aus ist (AD-20). Der API-Host antwortet darauf wie
/// ein abgeschalteter Endpunkt mit <c>404</c>, ohne Details.
/// </summary>
public sealed class SchalterAusAusnahme(Guid freizeitId, FunktionsSchalter schalter)
    : Exception("Der Funktions-Schalter ist für diese Freizeit aus.")
{
    public Guid FreizeitId { get; } = freizeitId;

    public FunktionsSchalter Schalter { get; } = schalter;
}
