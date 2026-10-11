using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Programm.Daten;

namespace Zeltlotse.Programm;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class ProgrammModul
{
    internal const string Name = "Programm";

    internal const string Schema = "programm";

    public static IServiceCollection AddProgrammModul(this IServiceCollection services)
    {
        services.AddModul<ProgrammDbContext, ProgrammLeseDbContext>(Name, Schema);
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; sie entstehen mit den Fach-Stories.</summary>
    public static IEndpointRouteBuilder MapProgrammEndpoints(this IEndpointRouteBuilder api) => api;
}
