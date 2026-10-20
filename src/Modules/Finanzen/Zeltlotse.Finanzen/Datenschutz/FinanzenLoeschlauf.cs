using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Finanzen.Datenschutz;

/// <summary>
/// Story 5-1: Täglicher Lösch-Lauf der Finanzen (AD-16): Planzahlen, Kategorien und Einnahmeposten der Kalkulation der Freizeiten, deren Daten nach der
/// Frist der Organisation abgelaufen sind. Die Kalkulation enthält keine Personendaten; Datenauskunft und Empfänger von <c>PersonGeloescht</c> braucht sie
/// nicht.
/// </summary>
internal sealed class FinanzenLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new KalkulationenLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Die Kalkulation der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind, endgültig löschen. Kein Benutzer-Command.</summary>
internal sealed record KalkulationenLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<FinanzenDbContext>;

internal sealed class KalkulationenLoeschenHandler(FinanzenDbContext db) : ICommandHandler<KalkulationenLoeschen>
{
    public async ValueTask<Unit> Handle(KalkulationenLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.Einnahmeposten.IgnoreQueryFilters().Where(e => freizeiten.Contains(e.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.KategoriePosten.IgnoreQueryFilters().Where(k => freizeiten.Contains(k.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Kalkulationen.IgnoreQueryFilters().Where(k => freizeiten.Contains(k.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
