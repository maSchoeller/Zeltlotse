using Microsoft.AspNetCore.Builder;

namespace Zeltlotse.SharedKernel.Web;

/// <summary>
/// Personenbezogener Lesepfad über Organisationen hinweg (AD-26): Endpunkte unter <c>/api/ich/</c> melden sich damit an.
/// Der Architektur- bzw. HTTP-Test kennt diese Endpunkte namentlich.
/// </summary>
public static class OrganisationsuebergreifendErweiterung
{
    public static TBuilder Organisationsuebergreifend<TBuilder>(this TBuilder endpunkt)
        where TBuilder : IEndpointConventionBuilder =>
        endpunkt.WithMetadata(OrganisationsuebergreifendMetadaten.Instanz);
}

/// <summary>Merkmal an einem Endpunkt, der über Organisationen hinweg für die eigene Person liest (AD-26).</summary>
public sealed class OrganisationsuebergreifendMetadaten
{
    public static OrganisationsuebergreifendMetadaten Instanz { get; } = new();

    private OrganisationsuebergreifendMetadaten()
    {
    }
}
