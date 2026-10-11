using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Sicherheit.Daten;

namespace Zeltlotse.Sicherheit;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class SicherheitModul
{
    internal const string Name = "Sicherheit";

    internal const string Schema = "sicherheit";

    public static IServiceCollection AddSicherheitModul(this IServiceCollection services)
    {
        services.AddModul<SicherheitDbContext, SicherheitLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapSicherheitEndpoints(this IEndpointRouteBuilder api) => api;
}
