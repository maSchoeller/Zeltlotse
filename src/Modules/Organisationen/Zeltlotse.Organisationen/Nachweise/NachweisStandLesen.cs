using Microsoft.EntityFrameworkCore;
using Zeltlotse.Organisationen.Contracts.Nachweise;
using Zeltlotse.Organisationen.Daten;
using StandArt = Zeltlotse.Organisationen.Contracts.Nachweise.NachweisArt;

namespace Zeltlotse.Organisationen.Nachweise;

/// <summary>
/// <see cref="INachweisStandLesen"/> über den Lese-Kontext unter dem Mandantenfilter (AD-6, AD-27): Pflicht-Arten aus den
/// gespeicherten Richtlinien-Zeilen in Enum-Reihenfolge, dazu „gültig bis“ der Bestätigungen der angefragten Personen.
/// </summary>
internal sealed class NachweisStandLesen(OrganisationenLeseDbContext db) : INachweisStandLesen
{
    public async Task<NachweisStand> StandAsync(IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default)
    {
        var pflicht = await db.Richtlinien.Where(r => r.Pflicht).OrderBy(r => r.Art).Select(r => r.Art).ToListAsync(abbruch);
        if (personIds.Count == 0)
        {
            return new NachweisStand([.. pflicht.Select(a => (StandArt)(int)a)], []);
        }

        var ids = personIds.Distinct().ToList();
        var bestaetigungen = await db.Bestaetigungen
            .Where(b => ids.Contains(b.PersonId))
            .OrderBy(b => b.PersonId)
            .ThenBy(b => b.Art)
            .Select(b => new { b.PersonId, b.Art, b.GueltigBis })
            .ToListAsync(abbruch);
        return new NachweisStand(
            [.. pflicht.Select(a => (StandArt)(int)a)],
            [.. bestaetigungen.Select(b => new NachweisGueltigkeit(b.PersonId, (StandArt)(int)b.Art, b.GueltigBis))]);
    }
}
