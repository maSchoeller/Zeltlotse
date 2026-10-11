using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Finanzen.Daten;

namespace Zeltlotse.Finanzen;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class FinanzenModul
{
    internal const string Name = "Finanzen";

    internal const string Schema = "finanzen";

    public static IServiceCollection AddFinanzenModul(this IServiceCollection services)
    {
        services.AddModul<FinanzenDbContext, FinanzenLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapFinanzenEndpoints(this IEndpointRouteBuilder api) => api;
}
