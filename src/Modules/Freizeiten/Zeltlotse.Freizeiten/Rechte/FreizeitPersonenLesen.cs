using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel.Datenschutz;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Rechte;

/// <summary>
/// Story 2-10: <see cref="IFreizeitPersonenLesen"/> über den Lese-Kontext unter dem Mandantenfilter: eine Anmeldung, die nicht abgesagt ist,
/// oder eine Freizeit-Rolle Leitung oder Team.
/// </summary>
internal sealed class FreizeitPersonenLesen(FreizeitenLeseDbContext db) : IFreizeitPersonenLesen
{
    public async Task<bool> GehoertDazuAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default)
    {
        if (personId == GeloeschtePerson.Id)
        {
            return false;
        }

        return await db.Anmeldungen.AnyAsync(a => a.FreizeitId == freizeitId && a.PersonId == personId && a.Status != AnmeldeStatus.Abgesagt, abbruch)
               || await db.Rollen.AnyAsync(r => r.FreizeitId == freizeitId && r.PersonId == personId && (r.Rolle == RolleArt.Leitung || r.Rolle == RolleArt.Team), abbruch);
    }
}

/// <summary>
/// Story 4-1: <see cref="IFreizeitPersonenzahlLesen"/> über den Lese-Kontext unter dem Mandantenfilter: bestätigte Teilnehmende vereint mit Leitung und
/// Team, jede Person einmal; der Platzhalter einer gelöschten Person zählt je bestätigter Anmeldung einzeln.
/// </summary>
internal sealed class FreizeitPersonenzahlLesen(FreizeitenLeseDbContext db) : IFreizeitPersonenzahlLesen
{
    public async Task<int> AnzahlAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        var teilnehmende = await db.Anmeldungen
            .Where(a => a.FreizeitId == freizeitId && a.Art == AnmeldeArt.Teilnehmende && a.Status == AnmeldeStatus.Bestaetigt)
            .Select(a => a.PersonId)
            .ToListAsync(abbruch);
        var team = await db.Rollen
            .Where(r => r.FreizeitId == freizeitId && (r.Rolle == RolleArt.Leitung || r.Rolle == RolleArt.Team))
            .Select(r => r.PersonId)
            .ToListAsync(abbruch);
        var personen = teilnehmende.Concat(team).Where(p => p != GeloeschtePerson.Id).Distinct().Count();
        return personen + teilnehmende.Count(p => p == GeloeschtePerson.Id);
    }
}

/// <summary>Story 2-10: <see cref="IFreizeitNamenLesen"/> über den Lese-Kontext unter dem Mandantenfilter.</summary>
internal sealed class FreizeitNamenLesen(FreizeitenLeseDbContext db) : IFreizeitNamenLesen
{
    public async Task<IReadOnlyDictionary<Guid, string>> NamenAsync(IReadOnlyCollection<Guid> freizeitIds, CancellationToken abbruch = default)
    {
        if (freizeitIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = freizeitIds.ToList();
        return await db.Freizeiten.Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, abbruch);
    }
}
