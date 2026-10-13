using System.Globalization;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using ApiProgrammpunkt = Zeltlotse.Api.Contracts.Programm.Programmpunkt;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/programm</c> (Leitung und Team, FR-34): der Wochenraster mit allen Programmpunkten. Fehlen Mahlzeiten
/// (weniger als drei je Tag), legt der interne Command <see cref="MahlzeitenSicherstellen"/> sie vorher an; das gilt in jeder Phase. Den
/// Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class WochenrasterLadenEndpunkt
{
    public static async Task<Results<Ok<Wochenraster>, NotFound>> Laden(
        Guid freizeitId, ProgrammLeseDbContext db, IFreizeitTageLesen tageLesen, ISender sender, IAngemeldet angemeldet, CancellationToken abbruch)
    {
        if (!(await tageLesen.TageAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var tage))
        {
            return TypedResults.NotFound();
        }

        var tageAnzahl = tage.LetzterTag.DayNumber - tage.ErsterTag.DayNumber + 1;
        var mahlzeiten = await db.Programmpunkte.CountAsync(p => p.FreizeitId == freizeitId && p.Mahlzeit != null, abbruch);
        if (mahlzeiten < Programmpunkt.Mahlzeiten.Count * tageAnzahl)
        {
            await sender.Send(new MahlzeitenSicherstellen(freizeitId), abbruch);
        }

        var punkte = await db.Programmpunkte.Where(p => p.FreizeitId == freizeitId).ToListAsync(abbruch);
        return TypedResults.Ok(new Wochenraster(tage.ErsterTag, tage.LetzterTag, [.. ProgrammLesen.Sortiert(punkte).Select(p => ProgrammLesen.AlsApi(p, angemeldet.PersonId))]));
    }
}

/// <summary>Gemeinsame Lesart der Programmpunkte für den Wochenraster und die Antworten der schreibenden Endpunkte.</summary>
internal static class ProgrammLesen
{
    private static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    /// <summary>Nach Tag, Beginn (ohne Uhrzeit zuletzt), Titel und Kennung.</summary>
    public static IEnumerable<Programmpunkt> Sortiert(IEnumerable<Programmpunkt> punkte) =>
        punkte
            .OrderBy(p => p.Tag)
            .ThenBy(p => p.Beginn is null)
            .ThenBy(p => p.Beginn)
            .ThenBy(p => p.Titel, Vergleich)
            .ThenBy(p => p.Id);

    /// <param name="ich">Die angemeldete Person; „Meins“ heißt: sie hat den Punkt übernommen.</param>
    public static ApiProgrammpunkt AlsApi(Programmpunkt punkt, Guid? ich) => new(
        punkt.Id,
        punkt.Version,
        punkt.Tag,
        ProgrammpunktFelder.UhrzeitText(punkt.Beginn),
        punkt.Titel,
        punkt.Mahlzeit,
        punkt.Uebernommen,
        ich is { } person && punkt.Uebernommen == person);
}

/// <summary>Fehlerantworten <c>409</c> der Programm-Endpunkte mit Problemcode (AD-27).</summary>
internal static class ProgrammProbleme
{
    public static ProblemHttpResult Problem(string code, string titel) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: titel,
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = code });

    public static ProblemHttpResult Konflikt(string titel) => Problem(Problemcodes.Konflikt, titel);

    public static ProblemHttpResult Abgeschlossen() => Problem(Problemcodes.FreizeitAbgeschlossen, "Diese Freizeit ist abgeschlossen und nur lesbar.");
}
