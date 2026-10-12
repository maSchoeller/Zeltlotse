using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.SharedKernel.Anmeldung;

namespace Zeltlotse.Personen.Zugriffsprotokoll;

/// <summary>
/// <c>GET /api/ich/zugriffsprotokoll</c> (AD-8, AD-26, FR-13): die Einträge über die Person selbst und über die Kinder ihrer Familie,
/// neueste zuerst, mit den Namen von betroffener und lesender Person. Fremde Einträge sind nie enthalten. Der Name der lesenden
/// Person ist <c>null</c>, wenn sie gelöscht ist (Platzhalter).
/// </summary>
internal static class ZugriffsprotokollEndpunkt
{
    public static async Task<Ok<List<ZugriffsprotokollEintrag>>> Laden(IAngemeldet angemeldet, PersonenLeseDbContext db, CancellationToken abbruch)
    {
        var ich = FamilieAntworten.PersonId(angemeldet);
        var familieId = await db.Elternteile.Where(e => e.PersonId == ich).Select(e => (Guid?)e.FamilieId).SingleOrDefaultAsync(abbruch);
        var betroffene = familieId is { } familie
            ? await db.Personen.Where(p => p.FamilieId == familie).Select(p => p.Id).ToListAsync(abbruch)
            : [];
        betroffene.Add(ich);

        var eintraege = await db.EinsichtProtokoll
            .Where(e => betroffene.Contains(e.BetroffenePersonId))
            .OrderByDescending(e => e.Am)
            .ThenByDescending(e => e.Id)
            .ToListAsync(abbruch);
        var personIds = eintraege.SelectMany(e => new[] { e.BetroffenePersonId, e.LesendePersonId }).Distinct().ToList();
        var namen = await db.Personen
            .Where(p => personIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => NameAus(p.Vorname, p.Nachname), abbruch);

        return TypedResults.Ok(eintraege.Select(e => new ZugriffsprotokollEintrag(
                e.Am,
                e.BetroffenePersonId,
                namen.GetValueOrDefault(e.BetroffenePersonId),
                e.LesendePersonId,
                namen.GetValueOrDefault(e.LesendePersonId),
                e.OrganisationId,
                e.FreizeitId,
                (ZugriffZweck)(int)e.Zweck))
            .ToList());
    }

    /// <summary>„Vorname Nachname“; ohne beide ein leerer Text (die Person gibt es, nur ohne Namen).</summary>
    private static string NameAus(string? vorname, string? nachname) =>
        string.Join(' ', new[] { vorname, nachname }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
}
