using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Daten;

namespace Zeltlotse.Personen.Gaeste;

/// <summary>
/// <see cref="IAnmeldeKontakt"/> über den Lese-Kontext und Konten (Pfeil Personen → Konten): eigenes Konto, sonst der zuerst
/// eingetragene Elternteil der Familie mit Konto (zuerst eingetragen = zuerst angelegtes Profil), sonst die Mail des ältesten
/// Gast-Zugangs der Eltern. Familien und Gast-Zugänge sind nicht organisationseigen; es gibt keinen Mandantenfilter zu umgehen.
/// </summary>
internal sealed class AnmeldeKontaktLesen(PersonenLeseDbContext db, IKontoLesen konten) : IAnmeldeKontakt
{
    public async Task<string?> MailAsync(Guid personId, CancellationToken abbruch = default)
    {
        var person = await db.Personen.Where(p => p.Id == personId).Select(p => new { p.Id, p.FamilieId }).SingleOrDefaultAsync(abbruch);
        if (person is null)
        {
            return null;
        }

        if (await konten.MailDerPersonAsync(personId, abbruch) is { } eigene)
        {
            return eigene;
        }

        if (person.FamilieId is not { } familie)
        {
            return null;
        }

        var eltern = await db.Elternteile
            .Where(e => e.FamilieId == familie)
            .Join(db.Personen, e => e.PersonId, p => p.Id, (e, p) => new { p.Id, p.AngelegtAm })
            .OrderBy(p => p.AngelegtAm)
            .ThenBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(abbruch);
        foreach (var elternteil in eltern)
        {
            if (await konten.MailDerPersonAsync(elternteil, abbruch) is { } mail)
            {
                return mail;
            }
        }

        return await db.GastZugaenge
            .Where(z => eltern.Contains(z.ElternPersonId))
            .OrderBy(z => z.AngelegtAm)
            .Select(z => z.Mail)
            .FirstOrDefaultAsync(abbruch);
    }
}
