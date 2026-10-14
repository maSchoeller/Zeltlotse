using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Inventar.Daten;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>
/// <c>GET /api/organisationen/{orgId}/inventar/anschaffungswuensche</c> (Org-Admin und Org-Überblick, FR-38): die Einträge der Schadens- und
/// Verlustlisten aller Freizeiten der Organisation in Phase gelaufen, je Freizeit gruppiert. Anschaffungswünsche sind abgeleitet: Sobald die
/// Leitung eine Freizeit abschließt, stehen ihre Einträge hier (keine eigene Zeile, kein Ereignis). Gruppen nach ihrem neuesten Eintrag, Einträge
/// neueste zuerst. Den Mandanten-Kontext hat der Host aus der Route gesetzt.
/// </summary>
internal static class AnschaffungswuenscheLadenEndpunkt
{
    public static async Task<Ok<List<AnschaffungswunschFreizeit>>> Laden(
        InventarLeseDbContext db, IFreizeitZeitpunkte zeitpunkte, IFreizeitNamenLesen namen, CancellationToken abbruch)
    {
        var freizeiten = await db.Schaeden.Select(s => s.FreizeitId).Distinct().ToListAsync(abbruch);
        var gelaufen = await Anschaffungswuensche.GelaufenAsync(zeitpunkte, freizeiten, abbruch);
        if (gelaufen.Count == 0)
        {
            return TypedResults.Ok(new List<AnschaffungswunschFreizeit>());
        }

        var eintraege = await db.Schaeden.Where(s => gelaufen.Contains(s.FreizeitId)).ToListAsync(abbruch);
        var name = await namen.NamenAsync(gelaufen, abbruch);
        return TypedResults.Ok(eintraege
            .GroupBy(s => s.FreizeitId)
            .OrderByDescending(g => g.Max(s => s.ErfasstAm))
            .ThenByDescending(g => g.Key)
            .Select(g => new AnschaffungswunschFreizeit(g.Key, name.GetValueOrDefault(g.Key) ?? "", SchadenLesen.Sortiert(g)))
            .ToList());
    }
}

/// <summary>Welche Freizeiten abgeschlossen sind: Nur ihre Einträge sind Anschaffungswünsche.</summary>
internal static class Anschaffungswuensche
{
    public static async Task<List<Guid>> GelaufenAsync(IFreizeitZeitpunkte zeitpunkte, IReadOnlyCollection<Guid> freizeiten, CancellationToken abbruch) =>
        freizeiten.Count == 0
            ? []
            : [.. (await zeitpunkte.ZeitpunkteAsync(freizeiten, abbruch)).Where(z => z.Value.Phase == FreizeitPhase.Gelaufen).Select(z => z.Key)];
}
