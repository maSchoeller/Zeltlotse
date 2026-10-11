using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Schluessel;

/// <summary>Liest umhüllte Personen-Schlüssel für die zentrale Verschlüsselung im SharedKernel (AD-14), nur lesend.</summary>
internal sealed class PersonenSchluesselQuelle(PersonenLeseDbContext db) : IPersonenSchluesselQuelle
{
    public Task<byte[]?> UmhuelltenSchluesselLesenAsync(Guid personId, CancellationToken abbruch = default) =>
        db.PersonenSchluessel
            .Where(s => s.PersonId == personId)
            .Select(s => s.Umhuellt)
            .SingleOrDefaultAsync(abbruch);
}
