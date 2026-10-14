using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Inventar.Datenschutz;

/// <summary>
/// Story 3-3: Täglicher Lösch-Lauf von Inventar (AD-16): die Reservierungen der Freizeiten, deren Daten nach der Frist abgelaufen sind. Sonst
/// blieben sie ohne Namen der Freizeit stehen, und ihr Artikel ließe sich nie mehr löschen. Die Artikel gehören der Organisation und bleiben.
/// </summary>
internal sealed class InventarLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new ReservierungenLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Reservierungen der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind, endgültig löschen (ohne Ereignisse).</summary>
internal sealed record ReservierungenLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<InventarDbContext>;

internal sealed class ReservierungenLoeschenHandler(InventarDbContext db) : ICommandHandler<ReservierungenLoeschen>
{
    public async ValueTask<Unit> Handle(ReservierungenLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.Reservierungen.IgnoreQueryFilters().Where(r => freizeiten.Contains(r.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
