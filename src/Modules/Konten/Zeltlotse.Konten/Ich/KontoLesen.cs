using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Konten.Daten;

namespace Zeltlotse.Konten.Ich;

/// <summary><see cref="IKontoLesen"/> über den Lese-Kontext (AD-27).</summary>
internal sealed class KontoLesen(KontenLeseDbContext db) : IKontoLesen
{
    public Task<bool> IstGlobalAdminAsync(Guid kontoId, CancellationToken abbruch = default) =>
        db.Konten.AnyAsync(k => k.Id == kontoId && k.GlobalAdmin, abbruch);

    public Task<string?> MailAsync(Guid kontoId, CancellationToken abbruch = default) =>
        db.Konten.Where(k => k.Id == kontoId).Select(k => (string?)k.Mail).SingleOrDefaultAsync(abbruch);
}
