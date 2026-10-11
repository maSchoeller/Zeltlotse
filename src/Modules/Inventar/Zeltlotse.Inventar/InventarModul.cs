using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Inventar.Daten;

namespace Zeltlotse.Inventar;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class InventarModul
{
    internal const string Name = "Inventar";

    internal const string Schema = "inventar";

    public static IServiceCollection AddInventarModul(this IServiceCollection services)
    {
        services.AddModul<InventarDbContext, InventarLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapInventarEndpoints(this IEndpointRouteBuilder api) => api;
}
