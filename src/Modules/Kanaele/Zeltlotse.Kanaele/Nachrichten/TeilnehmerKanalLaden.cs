using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kanaele.Nachrichten;

/// <summary>
/// <c>GET /api/teilnehmer/kanal</c> (QR-Gast, Story 6-4, FR-61, FR-64): höchstens die neuesten 200 Nachrichten im Kanal „Alle“ der Freizeit aus der
/// Gast-Sitzung, älteste zuerst, ohne Autor. Den Kanal „Team“ gibt es für Gäste nicht. Den Mandanten hat der Host aus der Gast-Sitzung gesetzt.
/// </summary>
internal static class TeilnehmerKanalLadenEndpunkt
{
    public static async Task<Results<Ok<TeilnehmerKanal>, NotFound>> Laden(IQrGast gast, KanaeleLeseDbContext db, CancellationToken abbruch)
    {
        if (gast.FreizeitId is not { } freizeitId)
        {
            return TypedResults.NotFound();
        }

        var neueste = await db.Nachrichten
            .Where(n => n.FreizeitId == freizeitId && n.Kanal == Kanal.Alle)
            .OrderByDescending(n => n.GesendetAm)
            .ThenByDescending(n => n.Laufnummer)
            .Take(KanalFelder.HoechstzahlNachrichten)
            .Select(n => new TeilnehmerNachricht(n.Id, n.Text, n.GesendetAm))
            .ToListAsync(abbruch);
        neueste.Reverse();
        return TypedResults.Ok(new TeilnehmerKanal(neueste));
    }
}
