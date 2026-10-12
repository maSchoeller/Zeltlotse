using Zeltlotse.Api.Betrieb;
using Zeltlotse.Api.Datenschutz;
using Zeltlotse.Finanzen;
using Zeltlotse.Freizeiten;
using Zeltlotse.Inventar;
using Zeltlotse.Kanaele;
using Zeltlotse.Konten;
using Zeltlotse.Kueche;
using Zeltlotse.Organisationen;
using Zeltlotse.Personen;
using Zeltlotse.Programm;
using Zeltlotse.Sicherheit;

namespace Zeltlotse.Api;

/// <summary>Bindet die zehn Module aus AD-1 ein.</summary>
internal static class ZeltlotseModule
{
    public static IServiceCollection AddZeltlotseModule(this IServiceCollection services) =>
        services
            .AddKontenModul()
            .AddPersonenModul()
            .AddOrganisationenModul()
            .AddFreizeitenModul()
            .AddSicherheitModul()
            .AddProgrammModul()
            .AddInventarModul()
            .AddKuecheModul()
            .AddFinanzenModul()
            .AddKanaeleModul();

    /// <summary>
    /// Bindet die Endpunkte der Module und die Betriebs-Endpunkte des Hosts unter <c>/api</c> ein; liefert die Gruppe.
    /// Jeder Endpunkt unter <c>/api</c> verlangt eine Sitzung (AD-12), außer den ausdrücklich anonymen
    /// (<c>AllowAnonymous</c>: Betriebs-Endpunkte und die vier Anmelde-Endpunkte von Konten).
    /// </summary>
    public static RouteGroupBuilder MapZeltlotseEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.RequireAuthorization();
        api.MapBetriebEndpoints();
        api.MapKontenEndpoints();
        api.MapPersonenEndpoints();
        api.MapOrganisationenEndpoints();
        api.MapFreizeitenEndpoints();
        api.MapSicherheitEndpoints();
        api.MapProgrammEndpoints();
        api.MapInventarEndpoints();
        api.MapKuecheEndpoints();
        api.MapFinanzenEndpoints();
        api.MapKanaeleEndpoints();
        api.MapDatenschutzEndpoints();
        return api;
    }
}
