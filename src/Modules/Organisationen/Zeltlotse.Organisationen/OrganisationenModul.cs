using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Organisationen.Daten;

namespace Zeltlotse.Organisationen;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class OrganisationenModul
{
    internal const string Name = "Organisationen";

    internal const string Schema = "organisationen";

    public static IServiceCollection AddOrganisationenModul(this IServiceCollection services)
    {
        services.AddModul<OrganisationenDbContext, OrganisationenLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapOrganisationenEndpoints(this IEndpointRouteBuilder api) => api;
}
