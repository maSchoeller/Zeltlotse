using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Gaeste;

/// <summary><see cref="IGastZugaenge"/> über den Lese-Kontext: prüft das Gast-Token, liest nur und schreibt nichts (auch kein Protokoll).</summary>
internal sealed class GastZugaengeLesen(PersonenLeseDbContext db, TimeProvider uhr) : IGastZugaenge
{
    public async Task<GastZugangInfo?> PruefenAsync(string? token, CancellationToken abbruch = default)
    {
        if (!Zugangslink.KannTokenSein(token))
        {
            return null;
        }

        var hash = Zugangslink.TokenHash(GastZugang.Zweck, token!);
        var zugang = await db.GastZugaenge.SingleOrDefaultAsync(z => z.TokenHash == hash, abbruch);
        if (zugang is null)
        {
            return null;
        }

        var familie = await GastFamilie.LesenAsync(db.Elternteile, db.Personen, db.Freigaben, zugang, abbruch);
        return zugang.IstGueltig(uhr.GetUtcNow(), [familie.GueltigBis])
            ? new GastZugangInfo(zugang.ElternPersonId, zugang.Mail, familie.Kinder, familie.GueltigBis)
            : null;
    }
}

/// <summary>
/// <see cref="IVertretung"/> über den Lese-Kontext: die Person selbst und die Kinder der Familie, in der sie Elternteil ist; anmelden
/// darf sie sich selbst und Kinder unter 18 (Volljährigkeit nur aus <see cref="Volljaehrigkeit"/>).
/// </summary>
internal sealed class VertretungLesen(PersonenLeseDbContext db, TimeProvider uhr) : IVertretung
{
    public async Task<bool> DarfAnmeldenAsync(Guid angemeldetePersonId, Guid personId, CancellationToken abbruch = default)
    {
        if (angemeldetePersonId == personId)
        {
            return true;
        }

        var familieId = await FamilieAsync(angemeldetePersonId, abbruch);
        if (familieId is null)
        {
            return false;
        }

        var geburtsdatum = await db.Personen
            .Where(p => p.Id == personId && p.FamilieId == familieId)
            .Select(p => p.Geburtsdatum)
            .SingleOrDefaultAsync(abbruch);
        return geburtsdatum is { } tag && !Volljaehrigkeit.Ist(tag, Volljaehrigkeit.Heute(uhr));
    }

    public async Task<IReadOnlyList<Guid>> PersonenAsync(Guid angemeldetePersonId, CancellationToken abbruch = default)
    {
        var familieId = await FamilieAsync(angemeldetePersonId, abbruch);
        List<Guid> kinder = familieId is null
            ? []
            : await db.Personen.Where(p => p.FamilieId == familieId).OrderBy(p => p.Geburtsdatum).ThenBy(p => p.Id).Select(p => p.Id).ToListAsync(abbruch);
        return [angemeldetePersonId, .. kinder];
    }

    private Task<Guid?> FamilieAsync(Guid personId, CancellationToken abbruch) =>
        db.Elternteile.Where(e => e.PersonId == personId).Select(e => (Guid?)e.FamilieId).SingleOrDefaultAsync(abbruch);
}
