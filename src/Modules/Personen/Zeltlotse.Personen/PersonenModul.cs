using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Datenschutz;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Freizeitpass;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.Personen.Gaeste;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mail;
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
        services.AddScoped<IPersonenSteckbriefe, PersonenSteckbriefe>();
        services.AddScoped<IGastZugaenge, GastZugaengeLesen>();
        services.AddScoped<IVertretung, VertretungLesen>();
        services.EmpfaengtEreignis<FreizeitRolleGeaendert, FreizeitRolleUebernehmen>();
        services.EmpfaengtEreignis<AnmeldungEingegangen, FreigabeAnlegen>();
        services.EmpfaengtEreignis<AnmeldungBestaetigt, FreigabeTeamSetzen>();
        services.EmpfaengtEreignis<AnmeldungAbgesagt, FreigabeBeenden>();
        services.AddScoped<IAnmeldeKontakt, AnmeldeKontaktLesen>();
        services.VerschicktMail<NachweisErinnerungMail, NachweisErinnerungMailAufbau>();
        services.AddScoped<IDatenauskunft, PersonenAuskunft>();
        services.AddScoped<ILoeschlauf, PersonenLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein: das eigene Profil unter <c>/api/ich/profil</c> und die
    /// eigene Familie unter <c>/api/ich/familie</c> (AD-26, organisationsübergreifend), das Annehmen von Familien-Einladung und
    /// Profil-Übergabe unter <c>/api/personen</c> (angemeldet, zugriffsbegrenzt, AD-11). Das Auslösen der Übergabe verlangt
    /// eine erneute Anmeldung (AD-25). Die eigenen Nachweise liegen unter <c>/api/ich/nachweise</c> (AD-26); geprüft wird unter
    /// <c>/api/freizeiten/{freizeitId}/personen/nachweise</c> (Leitung) und <c>/api/organisationen/{orgId}/personen/nachweise</c>
    /// (Org-Admin), Mandant und Rolle aus der Route (AD-6, AD-12). Erinnern (FR-11) gibt es nur im Freizeit-Kontext unter
    /// <c>…/personen/nachweise/erinnerungen</c>. Das Zugriffsprotokoll liegt unter <c>/api/ich/zugriffsprotokoll</c>; Konto löschen
    /// (<c>POST /api/ich/profil/loeschen</c>) verlangt eine erneute Anmeldung (AD-25).
    /// </summary>
    public static IEndpointRouteBuilder MapPersonenEndpoints(this IEndpointRouteBuilder api)
    {
        var profil = api.MapGroup("/ich/profil").Organisationsuebergreifend();
        profil.MapGet("", ProfilLadenEndpunkt.Laden);
        profil.MapPost("", ProfilSicherstellenEndpunkt.Sicherstellen);
        profil.MapPatch("", ProfilFeldAendernEndpunkt.Aendern);
        profil.MapPost("/loeschen", PersonLoeschenEndpunkt.Loeschen).ErneuteAnmeldung();

        api.MapGet("/ich/zugriffsprotokoll", ZugriffsprotokollEndpunkt.Laden).Organisationsuebergreifend();

        // Story 2-4: Freizeitpass der Person selbst oder eines Kindes der Familie (AD-26, FR-23).
        var freizeitpass = api.MapGroup("/ich/freizeitpass/{personId:guid}").Organisationsuebergreifend();
        freizeitpass.MapGet("", FreizeitpassLadenEndpunkt.Laden);
        freizeitpass.MapPatch("/antworten/{kennung}", FreizeitpassAntwortAendernEndpunkt.Aendern);
        freizeitpass.MapPost("/antworten/{kennung}/bestaetigen", FreizeitpassAntwortBestaetigenEndpunkt.Bestaetigen);

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

        // Story 2-2: Gast-Anmeldung (anonym, zugriffsbegrenzt; Antiforgery über die Dienst-Kennung der öffentlichen Seiten, AD-25)
        // und „Konto aus diesen Daten erstellen“ (angemeldet, zugriffsbegrenzt, AD-11, AD-26).
        var gaeste = personen.MapGroup("/oeffentlich/gaeste").AllowAnonymous().Zugriffsbegrenzt();
        gaeste.MapPost("", GastAnlegenEndpunkt.Anlegen);
        gaeste.MapPost("/{gastToken}/kinder", GastKindAnlegenEndpunkt.Anlegen);
        // Story 2-4: Freizeitpass eines Kindes der Gast-Familie über den Gast-Link (klassisches Formular der öffentlichen Seiten).
        gaeste.MapGet("/{gastToken}/freizeitpass/{personId:guid}", GastFreizeitpassLadenEndpunkt.Laden);
        gaeste.MapPost("/{gastToken}/freizeitpass/{personId:guid}", GastFreizeitpassSpeichernEndpunkt.Speichern);
        api.MapPost("/ich/gastprofil/uebernehmen", GastprofilUebernehmenEndpunkt.Uebernehmen).Organisationsuebergreifend().Zugriffsbegrenzt();
        return api;
    }
}
