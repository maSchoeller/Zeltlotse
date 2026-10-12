using Microsoft.EntityFrameworkCore;
using Zeltlotse.Organisationen.Contracts.Vertrauensseite;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;
using StandArt = Zeltlotse.Organisationen.Contracts.Nachweise.NachweisArt;

namespace Zeltlotse.Organisationen.Vertrauensseite;

/// <summary>
/// <see cref="IVertrauensAngabenLesen"/> über den Lese-Kontext (AD-27): Die Organisation selbst hat keinen Mandantenfilter, darum
/// wird sie ausdrücklich über den Mandanten-Kontext gesucht; Löschfristen und Richtlinien laufen unter dem Filter. Fehlt die Zeile
/// der Löschfristen, gelten die Vorgabewerte (wie beim Lösch-Dienst).
/// </summary>
internal sealed class VertrauensAngabenLesen(OrganisationenLeseDbContext db, IMandantenKontext mandant) : IVertrauensAngabenLesen
{
    public async Task<VertrauensAngaben?> LesenAsync(CancellationToken abbruch = default)
    {
        if (mandant.OrganisationId is not { } organisationId)
        {
            return null;
        }

        var name = await db.Organisationen.Where(o => o.Id == organisationId).Select(o => o.Name).SingleOrDefaultAsync(abbruch);
        if (name is null)
        {
            return null;
        }

        var fristen = await db.Loeschfristen
            .Select(f => new { f.FreizeitDatenMonate, f.NachweisBestaetigungMonate })
            .SingleOrDefaultAsync(abbruch);
        var pflicht = await db.Richtlinien.Where(r => r.Pflicht).OrderBy(r => r.Art).Select(r => r.Art).ToListAsync(abbruch);
        return new VertrauensAngaben(
            name,
            fristen?.FreizeitDatenMonate ?? Loeschfristen.VorgabeMonate,
            fristen?.NachweisBestaetigungMonate ?? Loeschfristen.VorgabeMonate,
            [.. pflicht.Select(a => (StandArt)(int)a)]);
    }
}
