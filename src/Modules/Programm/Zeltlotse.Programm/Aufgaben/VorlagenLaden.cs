using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Programm.Daten;
using ApiVorlage = Zeltlotse.Api.Contracts.Programm.AufgabenVorlage;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// <c>GET /api/organisationen/{orgId}/aufgaben-vorlagen</c> (Org-Admin, FR-40): alle Aufgaben-Vorlagen der Organisation nach Phase und Titel. Den
/// Mandanten-Kontext hat der Host aus der Route gesetzt.
/// </summary>
internal static class VorlagenLadenEndpunkt
{
    public static async Task<Ok<IReadOnlyList<ApiVorlage>>> Laden(ProgrammLeseDbContext db, CancellationToken abbruch)
    {
        var vorlagen = await db.AufgabenVorlagen.ToListAsync(abbruch);
        return TypedResults.Ok<IReadOnlyList<ApiVorlage>>([.. AufgabenLesen.Sortiert(vorlagen).Select(AufgabenLesen.AlsApi)]);
    }
}
