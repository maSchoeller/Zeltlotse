using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Nachweise;
using Zeltlotse.Freizeiten.Rechte;
using Zeltlotse.Freizeiten.Startseite;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Freizeiten;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class FreizeitenModul
{
    internal const string Name = "Freizeiten";

    internal const string Schema = "freizeiten";

    public static IServiceCollection AddFreizeitenModul(this IServiceCollection services)
    {
        services.AddModul<FreizeitenDbContext, FreizeitenLeseDbContext>(Name, Schema);
        services.AddScoped<IFreizeitRollenLesen, FreizeitRollenLesen>();
        services.AddScoped<IFreizeitZeitpunkte, FreizeitZeitpunkteLesen>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein (FR-6, FR-45). Rechte prüft der Host zentral über die
    /// Merkmale (AD-12): Organisations-Routen (Liste, Anlegen, Verwalten, Einladen) tragen <c>.NurOrgRolle(…)</c> mit
    /// <c>{orgId:guid}</c>, Freizeit-Routen <c>.NurFreizeitRolle(…)</c> mit <c>{freizeitId:guid}</c>; nie beide. Die
    /// Warnhinweise zu Pflicht-Nachweisen (FR-11) sieht die Leitung mit denselben Rollen wie „Nachweise prüfen“.
    /// </summary>
    public static IEndpointRouteBuilder MapFreizeitenEndpoints(this IEndpointRouteBuilder api)
    {
        var organisation = api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/freizeiten");
        organisation.MapGet("", FreizeitenListeEndpunkt.Laden).NurOrgRolle(FreizeitRechte.ListeSehen);
        organisation.MapPost("", FreizeitAnlegenEndpunkt.Anlegen).NurOrgRolle(FreizeitRechte.Anlegen);
        organisation.MapGet($"/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}", FreizeitVerwaltenEndpunkt.Laden).NurOrgRolle(FreizeitRechte.LeitungEinsetzen);
        organisation.MapPost($"/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/leitung/einladungen", LeitungEinladenEndpunkt.Einladen)
            .NurOrgRolle(FreizeitRechte.LeitungEinsetzen);

        var freizeiten = api.MapGroup("/freizeiten");
        freizeiten.MapPost("/leitung/annehmen", LeitungAnnehmenEndpunkt.Annehmen).Zugriffsbegrenzt();

        var freizeit = freizeiten.MapGroup($"/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}");
        freizeit.MapGet("", FreizeitLadenEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        freizeit.MapGet("/kontext", FreizeitKontextEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        freizeit.MapGet("/nachweis-warnungen", NachweisWarnungenEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.NachweisePruefen);

        api.MapGet("/ich/freizeiten", MeineFreizeitenEndpunkt.Laden).Organisationsuebergreifend();
        return api;
    }
}
