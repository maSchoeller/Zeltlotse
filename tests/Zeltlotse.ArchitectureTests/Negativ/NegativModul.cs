using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Zeltlotse.ArchitectureTests.Negativ;

// Bewusst im Wurzel-Namensraum der Assembly, damit die Klasse als Modul-Registrierung gilt.
namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// Negativ-Fixture für AD-27: Die Registrierung selbst darf den schreibenden DbContext kennen,
/// ein Endpunkt-Lambda in ihr aber nicht.
/// </summary>
internal static class NegativModul
{
    public static IEndpointRouteBuilder MapNegativEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/negativ", (NegativDbContext db) => db.Database.CanConnect());
        return api;
    }
}
