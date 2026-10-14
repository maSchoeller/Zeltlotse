using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Inventar.Artikel;
using Zeltlotse.Inventar.Daten;

namespace Zeltlotse.Inventar.Reservierungen;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/inventar</c> (Leitung und Team, FR-37): erster und letzter Tag der Freizeit und alle Artikel der
/// Organisation nach Bezeichnung, je Artikel nur die Reservierungen dieser Freizeit (mit Konflikthinweisen aus allen Freizeiten). Auch nach dem
/// Abschluss lesbar. Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class FreizeitInventarLadenEndpunkt
{
    public static async Task<IResult> Laden(
        Guid freizeitId, InventarLeseDbContext db, IFreizeitTageLesen tage, IFreizeitNamenLesen namen, CancellationToken abbruch)
    {
        if (!(await tage.TageAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var freizeit))
        {
            return TypedResults.NotFound();
        }

        var artikel = await db.Artikel.ToListAsync(abbruch);
        var konflikte = await Konflikte.LadenAsync(db, namen, null, abbruch);
        return TypedResults.Ok(new FreizeitInventar(freizeit.ErsterTag, freizeit.LetzterTag, ArtikelLesen.Sortiert(artikel, a => konflikte.Von(a.Id, freizeitId))));
    }
}
