using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Datenschutz;
using Zeltlotse.Programm.Materialien;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Programm;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class ProgrammModul
{
    internal const string Name = "Programm";

    internal const string Schema = "programm";

    public static IServiceCollection AddProgrammModul(this IServiceCollection services)
    {
        services.AddModul<ProgrammDbContext, ProgrammLeseDbContext>(Name, Schema);
        // Story 3-1/3-2: Die Übernahme eines Programmpunkts und das Mitbringen von Material sind Arbeitsdaten der Freizeit (AD-16).
        services.EmpfaengtEreignis<PersonGeloescht, ProgrammPersonLoeschen>();
        services.AddScoped<IDatenauskunft, ProgrammAuskunft>();
        services.AddScoped<ILoeschlauf, ProgrammLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 3-1: Wochenraster und Programmpunkte unter
    /// <c>/api/freizeiten/{freizeitId}/programm</c>, alles für Leitung und Team: lesen, anlegen, bearbeiten (Autosave, AD-19), löschen und
    /// übernehmen oder abgeben (nur für sich selbst). Story 3-2: Material an den Programmpunkten unter <c>…/programm/material</c>, ebenfalls für
    /// Leitung und Team: Liste lesen, am Punkt anlegen, bearbeiten (Autosave), löschen, Status setzen und mitbringen oder nicht (nur für sich
    /// selbst).
    /// </summary>
    public static IEndpointRouteBuilder MapProgrammEndpoints(this IEndpointRouteBuilder api)
    {
        var programm = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/programm");
        programm.MapGet("", WochenrasterLadenEndpunkt.Laden).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/punkte", ProgrammpunktAnlegenEndpunkt.Anlegen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPatch("/punkte/{punktId:guid}", ProgrammpunktFeldAendernEndpunkt.Aendern).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapDelete("/punkte/{punktId:guid}", ProgrammpunktLoeschenEndpunkt.Loeschen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/punkte/{punktId:guid}/uebernehmen", ProgrammpunktUebergangEndpunkt.Uebernehmen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/punkte/{punktId:guid}/abgeben", ProgrammpunktUebergangEndpunkt.Abgeben).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapGet("/material", MaterialListeLadenEndpunkt.Laden).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/punkte/{punktId:guid}/material", MaterialAnlegenEndpunkt.Anlegen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPatch("/material/{materialId:guid}", MaterialFeldAendernEndpunkt.Aendern).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapDelete("/material/{materialId:guid}", MaterialLoeschenEndpunkt.Loeschen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/material/{materialId:guid}/status", MaterialStatusSetzenEndpunkt.Setzen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/material/{materialId:guid}/mitbringen", MaterialMitbringenEndpunkt.Mitbringen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/material/{materialId:guid}/nicht-mitbringen", MaterialMitbringenEndpunkt.NichtMitbringen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        return api;
    }
}
