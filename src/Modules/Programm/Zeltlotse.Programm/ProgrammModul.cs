using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Inventar.Contracts.Events;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.Programm.Contracts.Mahlzeiten;
using Zeltlotse.Programm.Aufgaben;
using Zeltlotse.Programm.Bibliothek;
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
        // Story 3-1/3-2/3-5: Die Übernahme eines Programmpunkts oder einer Aufgabe und das Mitbringen von Material sind Arbeitsdaten der
        // Freizeit (AD-16).
        services.EmpfaengtEreignis<PersonGeloescht, ProgrammPersonLoeschen>();
        // Story 3-3: Eine Reservierung im Inventar setzt den Status des verknüpften Materials (Tabelle Ereignis-Fluss).
        services.EmpfaengtEreignis<ReservierungGeaendert, ProgrammReservierungUebernehmen>();
        services.AddScoped<IDatenauskunft, ProgrammAuskunft>();
        services.AddScoped<ILoeschlauf, ProgrammLoeschlauf>();
        // Story 4-2: die Mahlzeiten für den Essensplan der Küche (Pfeil Küche → Programm).
        services.AddScoped<IFreizeitMahlzeitenLesen, FreizeitMahlzeitenLesen>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 3-1: Wochenraster und Programmpunkte unter
    /// <c>/api/freizeiten/{freizeitId}/programm</c>, alles für Leitung und Team: lesen, anlegen, bearbeiten (Autosave, AD-19), löschen und
    /// übernehmen oder abgeben (nur für sich selbst). Story 3-2: Material an den Programmpunkten unter <c>…/programm/material</c>, ebenfalls für
    /// Leitung und Team: Liste lesen, am Punkt anlegen, bearbeiten (Autosave), löschen, Status setzen und mitbringen oder nicht (nur für sich
    /// selbst). Story 3-5: Aufgaben der Freizeit unter <c>…/programm/aufgaben</c> für Leitung und Team (lesen mit einmaliger Übernahme der Vorlagen,
    /// anlegen, Autosave, löschen, übernehmen und abgeben nur für sich selbst, erledigt und wieder offen, als Vorlage speichern) und die
    /// Aufgaben-Vorlagen der Organisation unter <c>/api/organisationen/{orgId}/aufgaben-vorlagen</c> nur für den Org-Admin. Story 3-6: die
    /// Programmbibliothek: Programmpunkt ablegen, Bibliothek lesen und Einträge übernehmen unter <c>…/programm</c> für Leitung und Team; die
    /// Bibliothek der Organisation unter <c>/api/organisationen/{orgId}/programmbibliothek</c> (lesen, Einträge entfernen) nur für den Org-Admin.
    /// Story 6-2: „mitgeteilt“ an einem verschobenen Programmpunkt für Leitung und Team.
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
        programm.MapPost("/punkte/{punktId:guid}/mitgeteilt", ProgrammpunktMitgeteiltEndpunkt.Mitgeteilt).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapGet("/material", MaterialListeLadenEndpunkt.Laden).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/punkte/{punktId:guid}/material", MaterialAnlegenEndpunkt.Anlegen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPatch("/material/{materialId:guid}", MaterialFeldAendernEndpunkt.Aendern).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapDelete("/material/{materialId:guid}", MaterialLoeschenEndpunkt.Loeschen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/material/{materialId:guid}/status", MaterialStatusSetzenEndpunkt.Setzen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/material/{materialId:guid}/mitbringen", MaterialMitbringenEndpunkt.Mitbringen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/material/{materialId:guid}/nicht-mitbringen", MaterialMitbringenEndpunkt.NichtMitbringen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);

        programm.MapGet("/aufgaben", AufgabenLadenEndpunkt.Laden).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/aufgaben", AufgabeAnlegenEndpunkt.Anlegen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPatch("/aufgaben/{aufgabeId:guid}", AufgabeFeldAendernEndpunkt.Aendern).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapDelete("/aufgaben/{aufgabeId:guid}", AufgabeLoeschenEndpunkt.Loeschen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/aufgaben/{aufgabeId:guid}/uebernehmen", AufgabeUebergangEndpunkt.Uebernehmen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/aufgaben/{aufgabeId:guid}/abgeben", AufgabeUebergangEndpunkt.Abgeben).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/aufgaben/{aufgabeId:guid}/erledigt", AufgabeUebergangEndpunkt.Erledigt).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/aufgaben/{aufgabeId:guid}/offen", AufgabeUebergangEndpunkt.Offen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/aufgaben/{aufgabeId:guid}/vorlage", AufgabeAlsVorlageEndpunkt.Speichern).NurFreizeitRolle(ProgrammRechte.Mitarbeit);

        programm.MapPost("/punkte/{punktId:guid}/bibliothek", ProgrammpunktAblegenEndpunkt.Ablegen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapGet("/bibliothek", BibliothekLadenEndpunkt.Laden).NurFreizeitRolle(ProgrammRechte.Mitarbeit);
        programm.MapPost("/bibliothek/{eintragId:guid}/uebernehmen", BibliothekUebernehmenEndpunkt.Uebernehmen).NurFreizeitRolle(ProgrammRechte.Mitarbeit);

        var vorlagen = api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/aufgaben-vorlagen");
        vorlagen.MapGet("", VorlagenLadenEndpunkt.Laden).NurOrgRolle(ProgrammRechte.Vorlagen);
        vorlagen.MapPost("", VorlageAnlegenEndpunkt.Anlegen).NurOrgRolle(ProgrammRechte.Vorlagen);
        vorlagen.MapPatch("/{vorlageId:guid}", VorlageFeldAendernEndpunkt.Aendern).NurOrgRolle(ProgrammRechte.Vorlagen);
        vorlagen.MapDelete("/{vorlageId:guid}", VorlageLoeschenEndpunkt.Loeschen).NurOrgRolle(ProgrammRechte.Vorlagen);

        var bibliothek = api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/programmbibliothek");
        bibliothek.MapGet("", OrgBibliothekLadenEndpunkt.Laden).NurOrgRolle(ProgrammRechte.Bibliothek);
        bibliothek.MapDelete("/{eintragId:guid}", EintragLoeschenEndpunkt.Loeschen).NurOrgRolle(ProgrammRechte.Bibliothek);
        return api;
    }
}
