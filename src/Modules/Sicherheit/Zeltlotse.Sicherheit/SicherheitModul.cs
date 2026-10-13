using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Sicherheit.Daten;
using Zeltlotse.Sicherheit.Sicherheitskette;

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

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 2-8: Sicherheitskette unter
    /// <c>/api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c>, lesen für Leitung und Team, einrichten (Autosave, AD-19) nur Leitung.
    /// </summary>
    public static IEndpointRouteBuilder MapSicherheitEndpoints(this IEndpointRouteBuilder api)
    {
        var sicherheit = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/sicherheit");
        sicherheit.MapGet("/sicherheitskette", SicherheitsketteLadenEndpunkt.Laden).NurFreizeitRolle(SicherheitRechte.Lesen);
        sicherheit.MapPatch("/sicherheitskette", SicherheitsketteFeldAendernEndpunkt.Aendern).NurFreizeitRolle(SicherheitRechte.Einrichten);
        return api;
    }
}
