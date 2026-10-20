using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Finanzen.Belege;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Datenschutz;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Rechte;
using FreizeitRolle = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Finanzen;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class FinanzenModul
{
    internal const string Name = "Finanzen";

    internal const string Schema = "finanzen";

    /// <summary>
    /// Story 5-1: Die Kalkulation steht nicht auf der Leitungsliste (rollen-und-rechte.md): Leitung und Team dürfen alles. Story 5-2: ebenso Belege, Soll-Ist
    /// und Erstattungen.
    /// </summary>
    internal static readonly FreizeitRolle[] Mitarbeit = [FreizeitRolle.Leitung, FreizeitRolle.Team];

    public static IServiceCollection AddFinanzenModul(this IServiceCollection services)
    {
        services.AddModul<FinanzenDbContext, FinanzenLeseDbContext>(Name, Schema);
        // Story 5-1: Die Kalkulation ist Arbeitsdatum der Freizeit und wird nach der Frist gelöscht (AD-16). Story 5-2: Belege sind aufbewahrungspflichtig:
        // Bei PersonGeloescht wird „bezahlt von“ zum Platzhalter, nach der Frist löscht der Lösch-Lauf Belege und Dateien; die Auskunft nennt die Belege.
        services.EmpfaengtEreignis<PersonGeloescht, FinanzenPersonLoeschen>();
        services.AddScoped<IDatenauskunft, FinanzenAuskunft>();
        services.AddScoped<ILoeschlauf, FinanzenLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 5-1: die Kalkulation der Freizeit unter
    /// <c>/api/freizeiten/{freizeitId}/finanzen/kalkulation</c> für Leitung und Team, nur mit eingeschaltetem Modul Finanzen (sonst <c>404</c>, AD-20):
    /// lesen (mit Ergebnis), Planzahlen und Kategorien per Autosave ändern (AD-19), Einnahmeposten anlegen, ändern und entfernen. Story 5-2: Belege unter
    /// <c>…/finanzen/belege</c> (lesen, anlegen, Autosave, löschen, Erstattungsvermerk auch nach dem Abschluss, Datei hochladen, ausliefern und entfernen) und
    /// der Soll-Ist-Vergleich unter <c>…/finanzen/soll-ist</c>, ebenfalls für Leitung und Team und nur mit dem Modul Finanzen.
    /// </summary>
    public static IEndpointRouteBuilder MapFinanzenEndpoints(this IEndpointRouteBuilder api)
    {
        var kalkulation = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/finanzen/kalkulation");
        kalkulation.MapGet("", KalkulationLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        kalkulation.MapPatch("", KalkulationFeldAendernEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        kalkulation.MapPatch("/kategorien/{kategorie}", KategorieFeldAendernEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        kalkulation.MapPost("/einnahmen", EinnahmeAnlegenEndpunkt.Anlegen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        kalkulation.MapPatch("/einnahmen/{einnahmeId:guid}", EinnahmeFeldAendernEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        kalkulation.MapDelete("/einnahmen/{einnahmeId:guid}", EinnahmeLoeschenEndpunkt.Loeschen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);

        var finanzen = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/finanzen");
        var belege = finanzen.MapGroup("/belege");
        belege.MapGet("", BelegeLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        belege.MapPost("", BelegAnlegenEndpunkt.Anlegen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        belege.MapPatch("/{belegId:guid}", BelegFeldAendernEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        belege.MapDelete("/{belegId:guid}", BelegLoeschenEndpunkt.Loeschen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        belege.MapPatch("/{belegId:guid}/erstattet", BelegErstattetEndpunkt.Aendern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        belege.MapPost("/{belegId:guid}/datei", BelegDateiEndpunkte.Hochladen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        belege.MapGet("/{belegId:guid}/datei", BelegDateiEndpunkte.Ausliefern).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        belege.MapDelete("/{belegId:guid}/datei", BelegDateiEndpunkte.Entfernen).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        finanzen.MapGet("/soll-ist", SollIstLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(FunktionsSchalter.Finanzen);
        return api;
    }
}
