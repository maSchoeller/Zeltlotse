using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Kueche.Daten;

namespace Zeltlotse.Kueche;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class KuecheModul
{
    internal const string Name = "Kueche";

    internal const string Schema = "kueche";

    public static IServiceCollection AddKuecheModul(this IServiceCollection services)
    {
        services.AddModul<KuecheDbContext, KuecheLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapKuecheEndpoints(this IEndpointRouteBuilder api) => api;
}
