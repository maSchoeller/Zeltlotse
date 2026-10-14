using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Inventar.Artikel;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.Inventar.Datenschutz;
using Zeltlotse.Inventar.Reservierungen;
using Zeltlotse.Inventar.Schaeden;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Sicherheit.Contracts.Events;

namespace Zeltlotse.Inventar;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class InventarModul
{
    internal const string Name = "Inventar";

    internal const string Schema = "inventar";

    public static IServiceCollection AddInventarModul(this IServiceCollection services)
    {
        // Story 3-3: Inventar hat keinen Personenbezug (keine PersonId), darum weder Empfänger von PersonGeloescht noch Datenauskunft (AD-16).
        // Die Reservierungen und die Schadens- und Verlustlisten abgelaufener Freizeiten löscht der tägliche Lösch-Lauf.
        services.AddModul<InventarDbContext, InventarLeseDbContext>(Name, Schema);
        services.AddScoped<ILoeschlauf, InventarLoeschlauf>();
        // Story 3-4: Entnahmen aus dem Vorfall-Protokoll (Tabelle Ereignis-Fluss).
        services.EmpfaengtEreignis<ErsteHilfeEntnommen, ErsteHilfeEntnahmeEintragen>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 3-3: das Inventar der Organisation unter
    /// <c>/api/organisationen/{orgId}/inventar</c>, lesen für Org-Admin und Org-Überblick, pflegen (anlegen, Autosave, löschen) nur Org-Admin;
    /// das Inventar in der Freizeit unter <c>/api/freizeiten/{freizeitId}/inventar</c> für Leitung und Team: lesen, reservieren und aufheben.
    /// Story 3-4: die Schadens- und Verlustliste der Freizeit unter <c>…/inventar/schaeden</c> (Leitung und Team, in jeder Phase) und die
    /// Anschaffungswünsche der Organisation unter <c>…/inventar/anschaffungswuensche</c> (lesen Org-Admin und Org-Überblick, „erledigt“ nur Org-Admin).
    /// </summary>
    public static IEndpointRouteBuilder MapInventarEndpoints(this IEndpointRouteBuilder api)
    {
        var organisation = api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/inventar");
        organisation.MapGet("", OrgInventarLadenEndpunkt.Laden).NurOrgRolle(InventarRechte.OrgLesen);
        organisation.MapPost("", ArtikelAnlegenEndpunkt.Anlegen).NurOrgRolle(InventarRechte.OrgVerwalten);
        organisation.MapPatch("/{artikelId:guid}", ArtikelFeldAendernEndpunkt.Aendern).NurOrgRolle(InventarRechte.OrgVerwalten);
        organisation.MapDelete("/{artikelId:guid}", ArtikelLoeschenEndpunkt.Loeschen).NurOrgRolle(InventarRechte.OrgVerwalten);
        organisation.MapGet("/anschaffungswuensche", AnschaffungswuenscheLadenEndpunkt.Laden).NurOrgRolle(InventarRechte.OrgLesen);
        organisation.MapDelete("/anschaffungswuensche/{eintragId:guid}", AnschaffungswunschErledigtEndpunkt.Erledigt).NurOrgRolle(InventarRechte.OrgVerwalten);

        var freizeit = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/inventar");
        freizeit.MapGet("", FreizeitInventarLadenEndpunkt.Laden).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapPost("/{artikelId:guid}/reservierungen", ReservierenEndpunkt.Anlegen).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapDelete("/reservierungen/{reservierungId:guid}", ReservierungAufhebenEndpunkt.Aufheben).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapGet("/schaeden", SchaedenLadenEndpunkt.Laden).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapPost("/schaeden", SchadenEintragenEndpunkt.Eintragen).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapPatch("/schaeden/{eintragId:guid}", SchadenFeldAendernEndpunkt.Aendern).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapDelete("/schaeden/{eintragId:guid}", SchadenEntfernenEndpunkt.Entfernen).NurFreizeitRolle(InventarRechte.Mitarbeit);
        return api;
    }
}
