using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.SharedKernel.Datenschutz;
using ApiBeitraege = Zeltlotse.Api.Contracts.Finanzen.Beitraege;

namespace Zeltlotse.Finanzen.Beitraege;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/finanzen/beitraege</c> (Leitung und Team, Modul Finanzen, Story 5-3, FR-60): je bestätigter teilnehmender Person der
/// Freizeit (über Freizeiten, Pfeil Finanzen → Freizeiten) eine Zeile mit dem Haken „bezahlt“, wer ihn gesetzt hat und wann, dazu „x von y bezahlt“. Vermerke
/// von Personen, die nicht mehr bestätigt teilnehmen, bleiben gespeichert, stehen aber nicht darin. Auch nach dem Abschluss lesbar.
/// </summary>
internal static class BeitraegeLadenEndpunkt
{
    public static async Task<Ok<ApiBeitraege>> Laden(
        Guid freizeitId, FinanzenLeseDbContext db, IFreizeitTeilnehmendeLesen teilnehmende, CancellationToken abbruch)
    {
        var personen = await teilnehmende.TeilnehmendeAsync(freizeitId, abbruch);
        var vermerke = await db.BeitragVermerke.Where(v => v.FreizeitId == freizeitId && v.PersonId != GeloeschtePerson.Id).ToDictionaryAsync(v => v.PersonId, abbruch);
        List<BeitragZeile> zeilen =
        [
            .. personen.Select(p => vermerke.TryGetValue(p, out var vermerk) ? BeitragSchritte.AlsApi(vermerk) : BeitragSchritte.Offen(p)),
        ];
        return TypedResults.Ok(new ApiBeitraege(zeilen, zeilen.Count(z => z.Bezahlt), zeilen.Count));
    }
}

/// <summary>Story 5-3: Gemeinsames von Liste und Übergängen.</summary>
internal static class BeitragSchritte
{
    /// <summary>Eine Zeile mit Haken, wie die API sie liefert.</summary>
    public static BeitragZeile AlsApi(BeitragVermerk vermerk) => new(vermerk.PersonId, true, vermerk.AbgehaktVon, vermerk.AbgehaktAm);

    /// <summary>Eine Zeile ohne Haken.</summary>
    public static BeitragZeile Offen(Guid personId) => new(personId, false, null, null);
}
