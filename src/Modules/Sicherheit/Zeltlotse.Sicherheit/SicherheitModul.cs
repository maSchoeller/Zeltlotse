using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Sicherheit.Daten;
using Zeltlotse.Sicherheit.Datenschutz;
using Zeltlotse.Sicherheit.Schutzkonzept;
using Zeltlotse.Sicherheit.Sicherheitskette;

namespace Zeltlotse.Sicherheit;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class SicherheitModul
{
    internal const string Name = "Sicherheit";

    internal const string Schema = "sicherheit";

    public static IServiceCollection AddSicherheitModul(this IServiceCollection services)
    {
        services.AddModul<SicherheitDbContext, SicherheitLeseDbContext>(Name, Schema);
        // Story 2-9: Schutzkonzept-Bestätigungen sind Arbeitsdaten der Freizeit (AD-16).
        services.EmpfaengtEreignis<PersonGeloescht, SicherheitPersonLoeschen>();
        services.AddScoped<IDatenauskunft, SicherheitAuskunft>();
        services.AddScoped<ILoeschlauf, SicherheitLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 2-8: Sicherheitskette unter
    /// <c>/api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c>, lesen für Leitung und Team, einrichten (Autosave, AD-19) nur Leitung.
    /// Story 2-9: Schutzkonzept unter <c>…/sicherheit/schutzkonzept</c>, lesen und bestätigen (<c>…/gelesen</c>) für Leitung und Team, das
    /// Dokument wählen (Autosave) nur Leitung.
    /// </summary>
    public static IEndpointRouteBuilder MapSicherheitEndpoints(this IEndpointRouteBuilder api)
    {
        var sicherheit = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/sicherheit");
        sicherheit.MapGet("/sicherheitskette", SicherheitsketteLadenEndpunkt.Laden).NurFreizeitRolle(SicherheitRechte.Lesen);
        sicherheit.MapPatch("/sicherheitskette", SicherheitsketteFeldAendernEndpunkt.Aendern).NurFreizeitRolle(SicherheitRechte.Einrichten);
        // Story 2-9: Schutzkonzept lesen und bestätigen (Leitung und Team), das Dokument wählen nur die Leitung.
        sicherheit.MapGet("/schutzkonzept", SchutzkonzeptLadenEndpunkt.Laden).NurFreizeitRolle(SicherheitRechte.Lesen);
        sicherheit.MapPatch("/schutzkonzept", SchutzkonzeptFeldAendernEndpunkt.Aendern).NurFreizeitRolle(SicherheitRechte.Einrichten);
        sicherheit.MapPost("/schutzkonzept/gelesen", SchutzkonzeptGelesenEndpunkt.Bestaetigen).NurFreizeitRolle(SicherheitRechte.Lesen);
        return api;
    }
}
