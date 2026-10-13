using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Team;
using Zeltlotse.Freizeiten.Daten;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Rechte;

/// <summary>Story 2-9: <see cref="IFreizeitTeamLesen"/> über den Lese-Kontext unter dem Mandantenfilter, je Person ein Eintrag.</summary>
internal sealed class FreizeitTeamLesen(FreizeitenLeseDbContext db) : IFreizeitTeamLesen
{
    public async Task<IReadOnlyList<FreizeitTeamMitglied>> TeamAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        var rollen = await db.Rollen
            .Where(r => r.FreizeitId == freizeitId && (r.Rolle == RolleArt.Leitung || r.Rolle == RolleArt.Team))
            .Select(r => new { r.PersonId, r.Rolle })
            .ToListAsync(abbruch);
        return [.. rollen.GroupBy(r => r.PersonId).Select(g => new FreizeitTeamMitglied(g.Key, g.Any(r => r.Rolle == RolleArt.Leitung)))];
    }
}
