using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Anmeldungen;
using Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;
using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Datenschutz;
using Zeltlotse.Freizeiten.Fragen;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Freizeiten.Nachweise;
using Zeltlotse.Freizeiten.Rechte;
using Zeltlotse.Freizeiten.Startseite;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mail;
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
        services.AddScoped<IFreizeitEndenLesen, FreizeitEndenLesen>();
        services.AddScoped<IBestaetigteTeilnehmende, BestaetigteTeilnehmendeZaehlen>();
        services.VerschicktMail<AnmeldungEntschiedenMail, AnmeldungEntschiedenMailAufbau>();
        services.EmpfaengtEreignis<PersonGeloescht, FreizeitPersonLoeschen>();
        services.AddScoped<IDatenauskunft, FreizeitenAuskunft>();
        services.AddScoped<ILoeschlauf, FreizeitenLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein (FR-6, FR-45). Rechte prüft der Host zentral über die
    /// Merkmale (AD-12): Organisations-Routen (Liste, Anlegen, Verwalten, Einladen) tragen <c>.NurOrgRolle(…)</c> mit
    /// <c>{orgId:guid}</c>, Freizeit-Routen <c>.NurFreizeitRolle(…)</c> mit <c>{freizeitId:guid}</c>; nie beide. Die
    /// Warnhinweise zu Pflicht-Nachweisen (FR-11) sieht die Leitung mit denselben Rollen wie „Nachweise prüfen“. Die Einstellungen
    /// der Freizeitseite und die Anmeldelinks pflegt nur die Leitung (Story 2-1); die beiden Seiten eines Anmeldelinks sind anonym.
    /// Story 2-2: Gast-Anmeldung über den Anmeldelink und der Gast-Link sind anonym und zugriffsbegrenzt; die Anmeldung mit Konto und
    /// die eigenen Anmeldungen liegen unter <c>/api/ich/anmeldungen</c> (AD-26).
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

        // Anmeldelinks (AD-11): anonym, zugriffsbegrenzt; der Mandant kommt aus dem Token, nie aus der Route (AD-6).
        var oeffentlich = freizeiten.MapGroup("/oeffentlich/{token}").AllowAnonymous().Zugriffsbegrenzt();
        oeffentlich.MapGet("", FreizeitseiteOeffentlichEndpunkt.Laden);
        oeffentlich.MapGet("/vertrauensseite", VertrauensseiteEndpunkt.Laden);
        // Story 2-2: Gast-Anmeldung über den Anmeldelink (Antiforgery über die Dienst-Kennung der öffentlichen Seiten, AD-25).
        oeffentlich.MapPost("/anmeldungen", GastAnmeldenEndpunkt.Absenden);

        // Gast-Link (AD-11): anonym, zugriffsbegrenzt; nur die Anmeldungen der Gast-Familie.
        freizeiten.MapGet("/gast/{gastToken}", GastLinkEndpunkt.Laden).AllowAnonymous().Zugriffsbegrenzt();

        var freizeit = freizeiten.MapGroup($"/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}");
        freizeit.MapGet("", FreizeitLadenEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        freizeit.MapGet("/kontext", FreizeitKontextEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        freizeit.MapGet("/nachweis-warnungen", NachweisWarnungenEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.NachweisePruefen);
        freizeit.MapGet("/freizeitseite", FreizeitseiteLadenEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        freizeit.MapPatch("/freizeitseite", FreizeitseiteFeldAendernEndpunkt.Aendern).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        freizeit.MapPost("/anmeldelinks/{art}/erneuern", AnmeldelinkErneuernEndpunkt.Erneuern).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        // Story 2-4: Fragen-Auswahl des Freizeitpasses je Zielgruppe (FR-22), nur Leitung.
        freizeit.MapGet("/fragen", FragenAuswahlLadenEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        freizeit.MapPatch("/fragen", FragenAuswahlAendernEndpunkt.Aendern).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        // Story 2-3: Anmeldungen der Leitung (Liste, Bestätigen, Absagen); Statuswechsel als POST je Übergang (AD-19, AD-28).
        freizeit.MapGet("/anmeldungen", AnmeldungenListeEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Anmeldungen);
        freizeit.MapPost("/anmeldungen/{anmeldungId:guid}/bestaetigen", AnmeldungEntscheidenEndpunkt.Bestaetigen).NurFreizeitRolle(FreizeitRechte.Anmeldungen);
        freizeit.MapPost("/anmeldungen/{anmeldungId:guid}/absagen", AnmeldungEntscheidenEndpunkt.Absagen).NurFreizeitRolle(FreizeitRechte.Anmeldungen);

        api.MapGet("/ich/freizeiten", MeineFreizeitenEndpunkt.Laden).Organisationsuebergreifend();
        var anmeldungen = api.MapGroup("/ich/anmeldungen").Organisationsuebergreifend();
        anmeldungen.MapGet("", MeineAnmeldungenEndpunkt.Laden);
        anmeldungen.MapPost("", AnmeldenMitKontoEndpunkt.Absenden).Zugriffsbegrenzt();
        return api;
    }
}
