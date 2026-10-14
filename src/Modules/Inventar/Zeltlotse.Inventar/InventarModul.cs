using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Inventar.Artikel;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.Inventar.Datenschutz;
using Zeltlotse.Inventar.Reservierungen;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Inventar;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class InventarModul
{
    internal const string Name = "Inventar";

    internal const string Schema = "inventar";

    public static IServiceCollection AddInventarModul(this IServiceCollection services)
    {
        // Story 3-3: Inventar hat keinen Personenbezug (keine PersonId), darum weder Empfänger von PersonGeloescht noch Datenauskunft (AD-16).
        // Die Reservierungen abgelaufener Freizeiten löscht der tägliche Lösch-Lauf.
        services.AddModul<InventarDbContext, InventarLeseDbContext>(Name, Schema);
        services.AddScoped<ILoeschlauf, InventarLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 3-3: das Inventar der Organisation unter
    /// <c>/api/organisationen/{orgId}/inventar</c>, lesen für Org-Admin und Org-Überblick, pflegen (anlegen, Autosave, löschen) nur Org-Admin;
    /// das Inventar in der Freizeit unter <c>/api/freizeiten/{freizeitId}/inventar</c> für Leitung und Team: lesen, reservieren und aufheben.
    /// </summary>
    public static IEndpointRouteBuilder MapInventarEndpoints(this IEndpointRouteBuilder api)
    {
        var organisation = api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/inventar");
        organisation.MapGet("", OrgInventarLadenEndpunkt.Laden).NurOrgRolle(InventarRechte.OrgLesen);
        organisation.MapPost("", ArtikelAnlegenEndpunkt.Anlegen).NurOrgRolle(InventarRechte.OrgVerwalten);
        organisation.MapPatch("/{artikelId:guid}", ArtikelFeldAendernEndpunkt.Aendern).NurOrgRolle(InventarRechte.OrgVerwalten);
        organisation.MapDelete("/{artikelId:guid}", ArtikelLoeschenEndpunkt.Loeschen).NurOrgRolle(InventarRechte.OrgVerwalten);

        var freizeit = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/inventar");
        freizeit.MapGet("", FreizeitInventarLadenEndpunkt.Laden).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapPost("/{artikelId:guid}/reservierungen", ReservierenEndpunkt.Anlegen).NurFreizeitRolle(InventarRechte.Mitarbeit);
        freizeit.MapDelete("/reservierungen/{reservierungId:guid}", ReservierungAufhebenEndpunkt.Aufheben).NurFreizeitRolle(InventarRechte.Mitarbeit);
        return api;
    }
}
