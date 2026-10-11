using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Freizeiten.Daten;

namespace Zeltlotse.Freizeiten;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class FreizeitenModul
{
    internal const string Name = "Freizeiten";

    internal const string Schema = "freizeiten";

    public static IServiceCollection AddFreizeitenModul(this IServiceCollection services)
    {
        services.AddModul<FreizeitenDbContext, FreizeitenLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapFreizeitenEndpoints(this IEndpointRouteBuilder api) => api;
}
