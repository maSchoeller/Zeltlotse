using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Profil;

namespace Zeltlotse.Personen.Gaeste;

/// <summary>
/// Die Familie eines Gast-Zugangs und wie lange er gilt (AD-7, AD-11): die Kinder der Familie des Elternteils und das späteste
/// „gültig bis“ der Freigaben aller Personen der Familie. Freigaben sind organisationseigen; hier zählen sie über alle
/// Organisationen hinweg, aber nur für die Personen dieser Familie (personenbezogener Lesepfad, AD-26). Bekommt nur Tabellen,
/// nie den schreibenden DbContext (AD-27).
/// </summary>
internal static class GastFamilie
{
    public static async Task<(Guid? FamilieId, IReadOnlyList<Guid> Kinder, DateTimeOffset GueltigBis)> LesenAsync(
        IQueryable<FamilienElternteil> elternteile, IQueryable<Person> personen, IQueryable<Freigabe> freigaben, GastZugang zugang, CancellationToken abbruch)
    {
        var familieId = await elternteile.Where(e => e.PersonId == zugang.ElternPersonId).Select(e => (Guid?)e.FamilieId).SingleOrDefaultAsync(abbruch);
        List<Guid> kinder = familieId is { } familie
            ? await personen.Where(p => p.FamilieId == familie).OrderBy(p => p.AngelegtAm).ThenBy(p => p.Id).Select(p => p.Id).ToListAsync(abbruch)
            : [];
        List<Guid> alle = [zugang.ElternPersonId, .. kinder];
        var bis = await freigaben.IgnoreQueryFilters().Where(f => alle.Contains(f.PersonId)).Select(f => f.GueltigBis).ToListAsync(abbruch);
        return (familieId, kinder, zugang.GueltigBis(bis));
    }
}
