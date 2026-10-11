using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Konten.Daten;

namespace Zeltlotse.Konten;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class KontenModul
{
    internal const string Name = "Konten";

    internal const string Schema = "konten";

    public static IServiceCollection AddKontenModul(this IServiceCollection services)
    {
        services.AddModul<KontenDbContext, KontenLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapKontenEndpoints(this IEndpointRouteBuilder api) => api;
}
