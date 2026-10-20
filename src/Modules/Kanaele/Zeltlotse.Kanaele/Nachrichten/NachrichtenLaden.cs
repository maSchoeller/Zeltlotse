using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Kanaele.Daten;

namespace Zeltlotse.Kanaele.Nachrichten;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kanaele/{kanal}/nachrichten</c> (Leitung und Team, Story 6-1, FR-61): höchstens die neuesten 200 Nachrichten des
/// Kanals, älteste zuerst (nach Zeitpunkt, bei Gleichstand in der Reihenfolge des Speicherns); der andere Kanal bleibt getrennt. Dazu die Zahl der Personen, die „Alle“ erreicht (über Freizeiten, Pfeil Kanäle → Freizeiten).
/// Auch nach dem Abschluss lesbar. Unbekannter Kanal <c>404</c>.
/// </summary>
internal static class NachrichtenLadenEndpunkt
{
    public static async Task<IResult> Laden(
        Guid freizeitId, string kanal, KanaeleLeseDbContext db, IFreizeitPersonenzahlLesen personenzahl, CancellationToken abbruch)
    {
        if (KanalFelder.AusRoute(kanal) is not { } gewaehlt)
        {
            return TypedResults.NotFound();
        }

        var neueste = await db.Nachrichten
            .Where(n => n.FreizeitId == freizeitId && n.Kanal == gewaehlt)
            .OrderByDescending(n => n.GesendetAm)
            .ThenByDescending(n => n.Laufnummer)
            .Take(KanalFelder.HoechstzahlNachrichten)
            .ToListAsync(abbruch);
        neueste.Reverse();
        return TypedResults.Ok(new KanalAnsicht(gewaehlt, [.. neueste.Select(AlsApi)], await personenzahl.AnzahlAsync(freizeitId, abbruch)));
    }

    /// <summary>Eine Nachricht, wie die API sie liefert.</summary>
    public static KanalNachricht AlsApi(Nachricht nachricht) => new(nachricht.Id, nachricht.AutorPersonId, nachricht.Text, nachricht.GesendetAm);
}
