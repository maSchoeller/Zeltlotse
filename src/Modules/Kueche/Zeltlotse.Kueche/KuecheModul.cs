using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Datenschutz;
using Zeltlotse.Kueche.Einkauf;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Rechte;
using FreizeitRolle = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Kueche;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class KuecheModul
{
    internal const string Name = "Kueche";

    internal const string Schema = "kueche";

    /// <summary>Story 4-1: Küche ist eine Team-Aufgabe ohne eigene Rolle: Leitung und Team dürfen alles.</summary>
    internal static readonly FreizeitRolle[] Mitarbeit = [FreizeitRolle.Leitung, FreizeitRolle.Team];

    public static IServiceCollection AddKuecheModul(this IServiceCollection services)
    {
        services.AddModul<KuecheDbContext, KuecheLeseDbContext>(Name, Schema);
        // Story 4-1: Rezepte sind Arbeitsdaten der Freizeit und werden nach der Frist gelöscht (AD-16).
        services.AddScoped<ILoeschlauf, KuecheLoeschlauf>();
        // Story 4-2: gemeinsame Abfrage von Essensplan, Einkaufsliste und Tausch-Vorschau (AD-27).
        services.AddScoped<EinkaufAbfrage>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 4-1: Rezepte der Freizeit unter
    /// <c>/api/freizeiten/{freizeitId}/kueche/rezepte</c> für Leitung und Team, nur mit eingeschaltetem Modul Verpflegung (sonst <c>404</c>, AD-20):
    /// Liste und Rezept lesen (mit hochgerechneten Mengen), Rezept anlegen, bearbeiten (Autosave, AD-19) und löschen, Zutaten hinzufügen, bearbeiten
    /// und entfernen. Story 4-2: unter <c>…/kueche</c> der Essensplan (lesen, Gerichte vormerken, entfernen, Tausch-Vorschau und Tausch) und die
    /// Einkaufsliste (lesen, Hand-Mengen, Hand-Posten anlegen, bearbeiten und entfernen), ebenso für Leitung und Team mit dem Modul Verpflegung. Story 4-3:
    /// Posten freigeben und abhaken (an den PATCH-Routen), die Tages-Freigabe und die Seite der Einkaufenden (<c>…/kueche/einkauf</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapKuecheEndpoints(this IEndpointRouteBuilder api)
    {
        var rezepte = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/kueche/rezepte");
        rezepte.MapGet("", RezepteLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        rezepte.MapPost("", RezeptAnlegenEndpunkt.Anlegen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        rezepte.MapGet("/{rezeptId:guid}", RezeptLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        rezepte.MapPatch("/{rezeptId:guid}", RezeptFeldAendernEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        rezepte.MapDelete("/{rezeptId:guid}", RezeptLoeschenEndpunkt.Loeschen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        rezepte.MapPost("/{rezeptId:guid}/zutaten", ZutatAnlegenEndpunkt.Anlegen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        rezepte.MapPatch("/{rezeptId:guid}/zutaten/{zutatId:guid}", ZutatFeldAendernEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        rezepte.MapDelete("/{rezeptId:guid}/zutaten/{zutatId:guid}", ZutatLoeschenEndpunkt.Loeschen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);

        var kueche = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/kueche");
        kueche.MapGet("/essensplan", EssensplanLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapPost("/gerichte", GerichtVormerkenEndpunkt.Vormerken).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapDelete("/gerichte/{gerichtId:guid}", GerichtEntfernenEndpunkt.Entfernen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapGet("/gerichte/{gerichtId:guid}/tausch", TauschvorschauEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapPost("/gerichte/{gerichtId:guid}/tauschen", GerichtTauschenEndpunkt.Tauschen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapGet("/einkaufsliste", EinkaufslisteLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapPatch("/einkaufsliste/gerichte/{gerichtId:guid}/zutaten/{zutatId:guid}", HandMengeAendernEndpunkt.Aendern)
            .NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapPost("/einkaufsliste/posten", EinkaufspostenAnlegenEndpunkt.Anlegen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapPatch("/einkaufsliste/posten/{postenId:guid}", EinkaufspostenFeldAendernEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapDelete("/einkaufsliste/posten/{postenId:guid}", EinkaufspostenLoeschenEndpunkt.Loeschen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        // Story 4-3: Tages-Freigabe und die Seite der Einkaufenden.
        kueche.MapPost("/einkaufsliste/freigabe", EinkaufFreigebenEndpunkt.Freigeben).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        kueche.MapGet("/einkauf", EinkaufLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Verpflegung);
        return api;
    }
}
