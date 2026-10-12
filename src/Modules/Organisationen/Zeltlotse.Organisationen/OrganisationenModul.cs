using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Organisationen.Contracts.Loeschfristen;
using Zeltlotse.Organisationen.Contracts.Nachweise;
using Zeltlotse.Organisationen.Contracts.Rollen;
using Zeltlotse.Organisationen.Contracts.Vertrauensseite;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Mitglieder;
using Zeltlotse.Organisationen.Datenschutz;
using Zeltlotse.Organisationen.Nachweise;
using Zeltlotse.Organisationen.Rollen;
using Zeltlotse.Organisationen.Verwaltung;
using Zeltlotse.Organisationen.Vertrauensseite;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Organisationen;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class OrganisationenModul
{
    internal const string Name = "Organisationen";

    internal const string Schema = "organisationen";

    public static IServiceCollection AddOrganisationenModul(this IServiceCollection services)
    {
        services.AddModul<OrganisationenDbContext, OrganisationenLeseDbContext>(Name, Schema);
        services.AddScoped<IOrgRollenLesen, OrgRollenLesen>();
        services.AddScoped<INachweisStandLesen, NachweisStandLesen>();
        services.EmpfaengtEreignis<NachweisEingesehen, NachweisBestaetigungSpeichern>();
        services.EmpfaengtEreignis<PersonGeloescht, OrgPersonLoeschen>();
        services.EmpfaengtEreignis<AnmeldungBestaetigt, MitgliedschaftAnlegen>();
        services.AddScoped<ILoeschfristenLesen, LoeschfristenLesen>();
        services.AddScoped<IVertrauensAngabenLesen, VertrauensAngabenLesen>();
        services.AddScoped<IDatenauskunft, OrganisationenAuskunft>();
        services.AddScoped<ILoeschlauf, OrganisationenLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein (FR-5). Rechte prüft der Host zentral über die
    /// Merkmale (AD-12); jede Route mit <c>{orgId}</c> trägt <c>.NurOrgRolle(…)</c> und die Einschränkung <c>:guid</c>,
    /// damit <c>/api/organisationen/einladungen/annehmen</c> nicht kollidiert.
    /// </summary>
    public static IEndpointRouteBuilder MapOrganisationenEndpoints(this IEndpointRouteBuilder api)
    {
        var organisationen = api.MapGroup("/organisationen");
        organisationen.MapGet("", OrganisationenListeEndpunkt.Laden).NurGlobalAdmin();
        organisationen.MapPost("", OrganisationAnlegenEndpunkt.Anlegen).NurGlobalAdmin();
        organisationen.MapPost("/einladungen/annehmen", EinladungAnnehmenEndpunkt.Annehmen).Zugriffsbegrenzt();

        var organisation = organisationen.MapGroup($"/{{{Rechtemerkmale.OrgRouteWert}:guid}}");
        organisation.MapGet("", OrganisationLadenEndpunkt.Laden).NurOrgRolle(OrgRechte.Lesen);
        organisation.MapGet("/rollen", RollenLadenEndpunkt.Laden).NurOrgRolle(OrgRechte.RollenVergeben);
        organisation.MapPost("/einladungen", EinladungSchickenEndpunkt.Schicken).NurOrgRolle(OrgRechte.RollenVergeben);
        organisation.MapGet("/nachweis-richtlinie", RichtlinieLadenEndpunkt.Laden).NurOrgRolle(OrgRechte.RichtlinieVerwalten);
        organisation.MapPatch("/nachweis-richtlinie/{art}", RichtlinieFeldAendernEndpunkt.Aendern).NurOrgRolle(OrgRechte.RichtlinieVerwalten);
        organisation.MapGet("/loeschfristen", LoeschfristenLadenEndpunkt.Laden).NurOrgRolle(OrgRechte.LoeschfristenVerwalten);
        organisation.MapPatch("/loeschfristen", LoeschfristFeldAendernEndpunkt.Aendern).NurOrgRolle(OrgRechte.LoeschfristenVerwalten);

        api.MapGet("/ich/organisationen", MeineOrganisationenEndpunkt.Laden).Organisationsuebergreifend();
        api.MapGet("/ich/organisationen/nachweis-bestaetigungen", MeineBestaetigungenEndpunkt.Laden).Organisationsuebergreifend();
        api.MapGet("/ich/organisationen/namen", OrganisationsNamenEndpunkt.Laden).Organisationsuebergreifend();
        return api;
    }
}
