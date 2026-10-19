using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Abschluss;
using Zeltlotse.Freizeiten.Anmeldungen;
using Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;
using Zeltlotse.Freizeiten.Contracts.Dokumente;
using Zeltlotse.Freizeiten.Contracts.Fragen;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.Freizeiten.Contracts.Schalter;
using Zeltlotse.Freizeiten.Contracts.Team;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Datenschutz;
using Zeltlotse.Freizeiten.Dokumente;
using Zeltlotse.Freizeiten.Fragen;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Freizeiten.Module;
using Zeltlotse.Freizeiten.Nachweise;
using Zeltlotse.Freizeiten.Personenliste;
using Zeltlotse.Freizeiten.TeamNotizen;
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
        // Story 3-7: die Funktions-Schalter für Rechteprüfung, Pipeline und Kontext (AD-20).
        services.AddScoped<IFreizeitSchalterLesen, FreizeitSchalterLesen>();
        // Story 2-9: Team und Dokumente für das Schutzkonzept (Pfeil Sicherheit → Freizeiten).
        services.AddScoped<IFreizeitTeamLesen, FreizeitTeamLesen>();
        services.AddScoped<IFreizeitDokumenteLesen, FreizeitDokumenteLesen>();
        // Story 2-10: Zugehörigkeit einer Person und Namen der Freizeiten für das Vorfall-Protokoll (Pfeil Sicherheit → Freizeiten).
        services.AddScoped<IFreizeitPersonenLesen, FreizeitPersonenLesen>();
        services.AddScoped<IFreizeitNamenLesen, FreizeitNamenLesen>();
        services.AddScoped<IFreizeitEndenLesen, FreizeitEndenLesen>();
        // Story 4-1: die Personenzahl für die Hochrechnung der Rezepte (Pfeil Küche → Freizeiten).
        services.AddScoped<IFreizeitPersonenzahlLesen, FreizeitPersonenzahlLesen>();
        // Story 4-2: ob eine Freizeit die Allergie-Frage abfragt (Pfeil Küche → Freizeiten).
        services.AddScoped<IFreizeitFragenLesen, FreizeitFragenLesen>();
        // Story 3-1: die Tage einer Freizeit für Wochenraster und Mahlzeiten (Pfeil Programm → Freizeiten).
        services.AddScoped<IFreizeitTageLesen, FreizeitTageLesen>();
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

        // Story 2-7: die Datei eines öffentlichen Dokuments der Freizeit des Anmeldelinks.
        oeffentlich.MapGet("/dokumente/{dokumentId:guid}", OeffentlichesDokumentEndpunkt.Datei);

        // Gast-Link (AD-11): anonym, zugriffsbegrenzt; nur die Anmeldungen der Gast-Familie und (Story 2-7) ihre Elterninfo.
        var gast = freizeiten.MapGroup("/gast/{gastToken}").AllowAnonymous().Zugriffsbegrenzt();
        gast.MapGet("", GastLinkEndpunkt.Laden);
        gast.MapGet("/anmeldungen/{anmeldungId:guid}/elterninfo", ElterninfoGastEndpunkt.Laden);
        gast.MapGet("/anmeldungen/{anmeldungId:guid}/elterninfo/{dokumentId:guid}", ElterninfoGastEndpunkt.Datei);

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
        // Story 3-7: Module der Freizeit schalten (FR-44, AD-20), nur Leitung.
        freizeit.MapGet("/module", ModuleLadenEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        freizeit.MapPatch("/module", ModulAendernEndpunkt.Aendern).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        // Story 3-8: Freizeit abschließen (AD-17, CAP-27), nur Leitung; Übergang als POST (AD-19, AD-28).
        freizeit.MapPost("/abschliessen", FreizeitAbschliessenEndpunkt.Abschliessen).NurFreizeitRolle(FreizeitRechte.Einstellungen);
        // Story 2-3: Anmeldungen der Leitung (Liste, Bestätigen, Absagen); Statuswechsel als POST je Übergang (AD-19, AD-28).
        freizeit.MapGet("/anmeldungen", AnmeldungenListeEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Anmeldungen);
        freizeit.MapPost("/anmeldungen/{anmeldungId:guid}/bestaetigen", AnmeldungEntscheidenEndpunkt.Bestaetigen).NurFreizeitRolle(FreizeitRechte.Anmeldungen);
        freizeit.MapPost("/anmeldungen/{anmeldungId:guid}/absagen", AnmeldungEntscheidenEndpunkt.Absagen).NurFreizeitRolle(FreizeitRechte.Anmeldungen);
        // Story 2-5: Personenliste und Team-Notizen für Leitung und Team (FR-24, FR-26); den Freizeitpass selbst liefert Personen.
        freizeit.MapGet("/personenliste", PersonenlisteEndpunkt.Laden).NurFreizeitRolle(FreizeitRechte.Freizeitpaesse);
        freizeit.MapGet("/anmeldungen/{anmeldungId:guid}/notizen", TeamNotizEndpunkte.Laden).NurFreizeitRolle(FreizeitRechte.Freizeitpaesse);
        freizeit.MapPost("/anmeldungen/{anmeldungId:guid}/notizen", TeamNotizEndpunkte.Anlegen).NurFreizeitRolle(FreizeitRechte.Freizeitpaesse);
        freizeit.MapDelete("/anmeldungen/{anmeldungId:guid}/notizen/{notizId:guid}", TeamNotizEndpunkte.Loeschen).NurFreizeitRolle(FreizeitRechte.Freizeitpaesse);
        // Story 2-7: Dokumente der Freizeit und Einbinden von Org-Dokumenten (FR-43); Dokumente sind keine Leitungsaufgabe.
        var dokumente = freizeit.MapGroup("/dokumente");
        dokumente.MapGet("", DokumentEndpunkte.Laden).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        dokumente.MapPost("", DokumentEndpunkte.Hochladen).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        dokumente.MapPatch("/{dokumentId:guid}", DokumentEndpunkte.Aendern).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        dokumente.MapDelete("/{dokumentId:guid}", DokumentEndpunkte.Entfernen).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        dokumente.MapGet("/{dokumentId:guid}/datei", DokumentEndpunkte.Datei).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        dokumente.MapGet("/org", DokumentEndpunkte.OrgAuswahl).NurFreizeitRolle(FreizeitRechte.Mitarbeit);
        dokumente.MapPost("/einbindungen", DokumentEndpunkte.Einbinden).NurFreizeitRolle(FreizeitRechte.Mitarbeit);

        api.MapGet("/ich/freizeiten", MeineFreizeitenEndpunkt.Laden).Organisationsuebergreifend();
        var anmeldungen = api.MapGroup("/ich/anmeldungen").Organisationsuebergreifend();
        anmeldungen.MapGet("", MeineAnmeldungenEndpunkt.Laden);
        anmeldungen.MapPost("", AnmeldenMitKontoEndpunkt.Absenden).Zugriffsbegrenzt();
        // Story 2-7: Elterninfo je Anmeldung (FR-68); den Mandanten setzt die geprüfte Anmeldung.
        anmeldungen.MapGet("/{anmeldungId:guid}/elterninfo", ElterninfoKontoEndpunkt.Laden);
        anmeldungen.MapGet("/{anmeldungId:guid}/elterninfo/{dokumentId:guid}", ElterninfoKontoEndpunkt.Datei);
        return api;
    }
}
