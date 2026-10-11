namespace Zeltlotse.Api;

/// <summary>
/// Zusätzliche Endpunkte nur für Tests (z. B. ein Endpunkt, der absichtlich wirft). Der Host bildet sie ausschließlich
/// in der Umgebung <c>Testing</c> ab; im Betrieb ist keiner registriert und keiner würde abgebildet.
/// </summary>
public interface IZusatzEndpunkte
{
    /// <summary>Endpunkte unter <c>/api</c>.</summary>
    void Abbilden(IEndpointRouteBuilder api);

    /// <summary>Endpunkte außerhalb von <c>/api</c> (z. B. ein Formular mit dem Antiforgery-Token von Blazor).</summary>
    void AbbildenAusserhalbApi(IEndpointRouteBuilder app)
    {
    }
}
