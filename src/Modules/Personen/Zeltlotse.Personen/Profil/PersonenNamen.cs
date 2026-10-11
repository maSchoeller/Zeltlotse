using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.Personen.Daten;

namespace Zeltlotse.Personen.Profil;

/// <summary><see cref="IPersonenNamen"/> über den Lese-Kontext (AD-27), eine Abfrage für die ganze Menge (AD-8).</summary>
internal sealed class PersonenNamen(PersonenLeseDbContext db) : IPersonenNamen
{
    public async Task<IReadOnlyDictionary<Guid, PersonenName>> NamenAsync(IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default)
    {
        if (personIds.Count == 0)
        {
            return new Dictionary<Guid, PersonenName>();
        }

        var ids = personIds.Distinct().ToList();
        return await db.Personen
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new PersonenName(p.Vorname, p.Nachname), abbruch);
    }
}
