using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Finanzen.Daten;
using ApiBeleg = Zeltlotse.Api.Contracts.Finanzen.Beleg;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/finanzen/belege</c> (Leitung und Team, Modul Finanzen, Story 5-2, FR-58): alle Belege der Freizeit, neueste zuerst
/// (nach „erfasst am“, dann Kennung). Auch nach dem Abschluss lesbar. Belege einer anderen Freizeit oder Organisation erscheinen nie.
/// </summary>
internal static class BelegeLadenEndpunkt
{
    public static async Task<Ok<List<ApiBeleg>>> Laden(Guid freizeitId, FinanzenLeseDbContext db, CancellationToken abbruch)
    {
        var belege = await db.Belege.Where(b => b.FreizeitId == freizeitId).OrderByDescending(b => b.ErfasstAm).ThenByDescending(b => b.Id).ToListAsync(abbruch);
        return TypedResults.Ok(belege.Select(BelegSchritte.AlsApi).ToList());
    }
}
