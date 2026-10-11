using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Contracts.Sitzungen;
using Zeltlotse.Konten.Daten;

namespace Zeltlotse.Konten.Sitzungen;

/// <summary>Sitzungsprüfung über den Lese-Kontext (AD-12, AD-27).</summary>
internal sealed class KontoPruefung(KontenLeseDbContext db) : IKontoPruefung
{
    public Task<bool> SitzungGueltigAsync(Guid kontoId, Guid personId, Guid sitzungsStempel, CancellationToken abbruch = default) =>
        db.Konten.AnyAsync(k => k.Id == kontoId && k.PersonId == personId && k.SitzungsStempel == sitzungsStempel, abbruch);
}

/// <summary>Was eine neue Sitzung braucht; nur innerhalb des Moduls.</summary>
internal sealed record SitzungsDaten(Guid KontoId, Guid PersonId, Guid SitzungsStempel);
