using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Api.Betrieb;

/// <summary>
/// Schreibt jede Zeile mit einem eigenen, frischen DbContext und damit in eigener Verbindung und Transaktion:
/// Ein Rollback beim Aufrufer nimmt den Eintrag nicht mit, und der Eintrag hängt nicht an dessen Transaktion.
/// </summary>
internal sealed class BetriebSicherheitsprotokoll(IDbContextFactory<BetriebDbContext> kontexte, TimeProvider uhr) : ISicherheitsprotokoll
{
    public async Task SchreibenAsync(
        SicherheitsEreignisArt art,
        Guid? personId = null,
        Guid? organisationId = null,
        Guid? bezugId = null,
        CancellationToken abbruch = default)
    {
        if (!Enum.IsDefined(art))
        {
            throw new ArgumentOutOfRangeException(nameof(art));
        }

        await using var db = await kontexte.CreateDbContextAsync(abbruch);
        db.Sicherheitsprotokoll.Add(new SicherheitsprotokollEintrag
        {
            Id = Kennung.Neu(uhr),
            Zeitpunkt = uhr.GetUtcNow(),
            Art = art,
            PersonId = personId,
            OrganisationId = organisationId,
            BezugId = bezugId,
        });
        await db.SaveChangesAsync(abbruch);
    }
}
