using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Personen.Daten;

namespace Zeltlotse.Personen;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class PersonenModul
{
    internal const string Name = "Personen";

    internal const string Schema = "personen";

    public static IServiceCollection AddPersonenModul(this IServiceCollection services)
    {
        services.AddModul<PersonenDbContext, PersonenLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapPersonenEndpoints(this IEndpointRouteBuilder api) => api;
}
