using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Kanaele.Datenschutz;

/// <summary>
/// Story 6-1: Täglicher Lösch-Lauf der Kanäle (AD-16): Nachrichten, Push-Abos (Story 6-4: auch die der QR-Gäste) und (Story 6-3) Notizen der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen
/// sind. Datenauskunft und Empfänger von <c>PersonGeloescht</c> stehen in <see cref="KanaeleAuskunft"/> und <see cref="KanaelePersonLoeschen"/>.
/// </summary>
internal sealed class KanaeleLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new KanaeleLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Nachrichten, Push-Abos und Notizen der Freizeiten mit abgelaufener Frist endgültig löschen. Kein Benutzer-Command.</summary>
internal sealed record KanaeleLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<KanaeleDbContext>;

internal sealed class KanaeleLoeschenHandler(KanaeleDbContext db) : ICommandHandler<KanaeleLoeschen>
{
    public async ValueTask<Unit> Handle(KanaeleLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.Nachrichten.IgnoreQueryFilters().Where(n => freizeiten.Contains(n.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.PushAbos.IgnoreQueryFilters().Where(a => freizeiten.Contains(a.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.GastPushAbos.IgnoreQueryFilters().Where(a => freizeiten.Contains(a.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Notizen.IgnoreQueryFilters().Where(n => freizeiten.Contains(n.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
