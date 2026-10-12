using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Personen;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class PersonenModul
{
    internal const string Name = "Personen";

    internal const string Schema = "personen";

    public static IServiceCollection AddPersonenModul(this IServiceCollection services)
    {
        services.AddModul<PersonenDbContext, PersonenLeseDbContext>(Name, Schema);
        services.AddScoped<IPersonenSchluesselQuelle, PersonenSchluesselQuelle>();
        services.AddScoped<IPersonenNamen, PersonenNamen>();
        services.EmpfaengtEreignis<FreizeitRolleGeaendert, FreizeitRolleUebernehmen>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein: das eigene Profil unter <c>/api/ich/profil</c> und die
    /// eigene Familie unter <c>/api/ich/familie</c> (AD-26, organisationsübergreifend), das Annehmen von Familien-Einladung und
    /// Profil-Übergabe unter <c>/api/personen</c> (angemeldet, zugriffsbegrenzt, AD-11). Das Auslösen der Übergabe verlangt
    /// eine erneute Anmeldung (AD-25). Die eigenen Nachweise liegen unter <c>/api/ich/nachweise</c> (AD-26); geprüft wird unter
    /// <c>/api/freizeiten/{freizeitId}/personen/nachweise</c> (Leitung) und <c>/api/organisationen/{orgId}/personen/nachweise</c>
    /// (Org-Admin), Mandant und Rolle aus der Route (AD-6, AD-12).
    /// </summary>
    public static IEndpointRouteBuilder MapPersonenEndpoints(this IEndpointRouteBuilder api)
    {
        var profil = api.MapGroup("/ich/profil").Organisationsuebergreifend();
        profil.MapGet("", ProfilLadenEndpunkt.Laden);
        profil.MapPost("", ProfilSicherstellenEndpunkt.Sicherstellen);
        profil.MapPatch("", ProfilFeldAendernEndpunkt.Aendern);

        var familie = api.MapGroup("/ich/familie").Organisationsuebergreifend();
        familie.MapGet("", FamilieLadenEndpunkt.Laden);
        familie.MapPost("/kinder", KindAnlegenEndpunkt.Anlegen);
        familie.MapPatch("/kinder/{personId:guid}", KindFeldAendernEndpunkt.Aendern);
        familie.MapPost("/einladungen", ElternteilEinladenEndpunkt.Einladen);
        familie.MapPost("/kinder/{personId:guid}/uebergabe", ProfilUebergebenEndpunkt.Uebergeben).ErneuteAnmeldung();

        var nachweise = api.MapGroup("/ich/nachweise").Organisationsuebergreifend();
        nachweise.MapGet("", NachweiseLadenEndpunkt.Laden);
        nachweise.MapPost("", NachweisAnlegenEndpunkt.Anlegen);
        nachweise.MapPatch("/{nachweisId:guid}", NachweisFeldAendernEndpunkt.Aendern);
        nachweise.MapPost("/{nachweisId:guid}/datei", NachweisDateiEndpunkt.Hochladen);
        nachweise.MapPost("/einsichten/{einsichtId:guid}/freigeben", EinsichtFreigebenEndpunkt.Freigeben);

        FreizeitPruefEndpunkt.Abbilden(api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/personen/nachweise")
            .NurFreizeitRolle(NachweisRechte.FreizeitPruefen));
        OrgPruefEndpunkt.Abbilden(api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/personen/nachweise")
            .NurOrgRolle(NachweisRechte.OrgPruefen));

        var personen = api.MapGroup("/personen");
        personen.MapPost("/familie/einladungen/annehmen", FamilienEinladungAnnehmenEndpunkt.Annehmen).Zugriffsbegrenzt();
        personen.MapPost("/uebergabe/annehmen", ProfilUebernehmenEndpunkt.Uebernehmen).Zugriffsbegrenzt();
        return api;
    }
}
