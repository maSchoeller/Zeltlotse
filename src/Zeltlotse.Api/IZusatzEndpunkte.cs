namespace Zeltlotse.Api;

/// <summary>
/// Zusätzliche Endpunkte nur für Tests (z. B. ein Endpunkt, der absichtlich wirft). Der Host bildet sie ausschließlich
/// in der Umgebung <c>Testing</c> unter <c>/api</c> ab; im Betrieb ist keiner registriert und keiner würde abgebildet.
/// </summary>
public interface IZusatzEndpunkte
{
    void Abbilden(IEndpointRouteBuilder api);
}
