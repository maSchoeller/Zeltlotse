using Microsoft.AspNetCore.Builder;

namespace Zeltlotse.SharedKernel.Rechte;

/// <summary>
/// Endpunkt-Merkmale der zentralen Rechteprüfung im API-Host (AD-6, AD-12). Der Host prüft sie nach Routing und
/// Authentifizierung, vor der Modellbindung: Global Admin fehlt → <c>403</c>; keine Rolle in der Organisation aus dem
/// Routenwert <see cref="OrgRouteWert"/> → <c>404</c>; Rolle vorhanden, aber nicht die verlangte → <c>403</c>.
/// </summary>
public static class Rechtemerkmale
{
    /// <summary>Name des Routenwerts mit der Organisation; Routen schreiben ihn als <c>{orgId:guid}</c>.</summary>
    public const string OrgRouteWert = "orgId";

    /// <summary>Nur für Global Admins.</summary>
    public static TBuilder NurGlobalAdmin<TBuilder>(this TBuilder endpunkt)
        where TBuilder : IEndpointConventionBuilder =>
        endpunkt.WithMetadata(NurGlobalAdminMetadaten.Instanz);

    /// <summary>Nur für Personen mit einer der Rollen in der Organisation aus der Route.</summary>
    public static TBuilder NurOrgRolle<TBuilder>(this TBuilder endpunkt, params OrgRolle[] rollen)
        where TBuilder : IEndpointConventionBuilder
    {
        if (rollen.Length == 0)
        {
            throw new ArgumentException("Mindestens eine Rolle.", nameof(rollen));
        }

        return endpunkt.WithMetadata(new NurOrgRolleMetadaten(rollen));
    }
}

/// <summary>Merkmal: Der Endpunkt verlangt einen Global Admin.</summary>
public sealed class NurGlobalAdminMetadaten
{
    public static NurGlobalAdminMetadaten Instanz { get; } = new();

    private NurGlobalAdminMetadaten()
    {
    }
}

/// <summary>Merkmal: Der Endpunkt verlangt eine der <see cref="Rollen"/> in der Organisation der Route.</summary>
public sealed class NurOrgRolleMetadaten(IReadOnlyList<OrgRolle> rollen)
{
    public IReadOnlyList<OrgRolle> Rollen { get; } = rollen;
}
