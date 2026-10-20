using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Kanaele.Daten;

namespace Zeltlotse.Kanaele.Notizen;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kanaele/notizen/{art}</c> (Leitung und Team, Story 6-3, FR-63): alle offenen Punkte der Liste, älteste zuerst (bei
/// Gleichstand in der Reihenfolge des Speicherns), und höchstens die 50 zuletzt abgehakten, zuletzt abgehakte zuerst. Auch nach dem Abschluss lesbar.
/// Unbekannte Art <c>404</c>.
/// </summary>
internal static class NotizenLadenEndpunkt
{
    public static async Task<IResult> Laden(Guid freizeitId, string art, KanaeleLeseDbContext db, CancellationToken abbruch)
    {
        if (KanalFelder.NotizArtAusRoute(art) is not { } gewaehlt)
        {
            return TypedResults.NotFound();
        }

        var liste = db.Notizen.Where(n => n.FreizeitId == freizeitId && n.Art == gewaehlt);
        var offen = await liste
            .Where(n => n.ErledigtAm == null)
            .OrderBy(n => n.AngelegtAm)
            .ThenBy(n => n.Laufnummer)
            .ToListAsync(abbruch);
        var abgehakt = await liste
            .Where(n => n.ErledigtAm != null)
            .OrderByDescending(n => n.ErledigtAm)
            .ThenByDescending(n => n.Laufnummer)
            .Take(KanalFelder.HoechstzahlAbgehakt)
            .ToListAsync(abbruch);
        return TypedResults.Ok(new NotizListe(gewaehlt, [.. offen.Select(AlsApi)], [.. abgehakt.Select(AlsApi)]));
    }

    /// <summary>Ein Punkt, wie die API ihn liefert.</summary>
    public static KanalNotiz AlsApi(Notiz notiz) => new(notiz.Id, notiz.Art, notiz.Text, notiz.AngelegtAm, notiz.ErledigtAm);
}
