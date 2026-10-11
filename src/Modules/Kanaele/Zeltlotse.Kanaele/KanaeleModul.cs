using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Kanaele.Daten;

namespace Zeltlotse.Kanaele;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class KanaeleModul
{
    internal const string Name = "Kanaele";

    internal const string Schema = "kanaele";

    public static IServiceCollection AddKanaeleModul(this IServiceCollection services)
    {
        services.AddModul<KanaeleDbContext, KanaeleLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapKanaeleEndpoints(this IEndpointRouteBuilder api) => api;
}
