using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Programm.Teilnehmer;

/// <summary>
/// <c>GET /api/teilnehmer/programm</c> (QR-Gast, Story 6-4, FR-64, NS-10): die Wochenübersicht der Freizeit aus der Gast-Sitzung, je Programmpunkt nur
/// Tag, Uhrzeit und Titel, sortiert wie der Wochenraster. Keine Kennung, keine Version, keine Übernahme, kein Material. Liest nur: Fehlende Mahlzeiten legt
/// erst der Wochenraster des Teams an. Den Mandanten hat der Host aus der Gast-Sitzung gesetzt.
/// </summary>
internal static class TeilnehmerWocheLadenEndpunkt
{
    public static async Task<Results<Ok<TeilnehmerWoche>, NotFound>> Laden(IQrGast gast, ProgrammLeseDbContext db, IFreizeitTageLesen tageLesen, CancellationToken abbruch)
    {
        if (gast.FreizeitId is not { } freizeitId || !(await tageLesen.TageAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var tage))
        {
            return TypedResults.NotFound();
        }

        var punkte = await db.Programmpunkte.Where(p => p.FreizeitId == freizeitId).ToListAsync(abbruch);
        return TypedResults.Ok(new TeilnehmerWoche(
            tage.ErsterTag,
            tage.LetzterTag,
            [.. ProgrammLesen.Sortiert(punkte).Select(p => new TeilnehmerPunkt(p.Tag, ProgrammpunktFelder.UhrzeitText(p.Beginn), p.Titel))]));
    }
}
