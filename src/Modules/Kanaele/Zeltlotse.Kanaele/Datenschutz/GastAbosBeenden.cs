using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Kanaele.Datenschutz;

/// <summary>
/// Story 6-4: Empfänger von <see cref="FreizeitAbgeschlossen"/> (Tabelle Ereignis-Fluss: „Kanäle → Gast-Abos beenden“, AD-21). Nach dem Abschluss kann niemand
/// mehr senden; deshalb löscht er alle Gast-Abos und auch die Push-Abos von Leitung und Team dieser Freizeit (Datensparsamkeit). Andere Freizeiten bleiben
/// unberührt. Mandant aus dem Umschlag; kein Benutzer-Command. Eine wiederholte Zustellung ändert nichts.
/// </summary>
internal sealed record GastAbosBeenden(Umschlag Umschlag, Guid FreizeitId)
    : ICommand, IModulCommand<KanaeleDbContext>, IEreignisEmpfaenger<GastAbosBeenden, FreizeitAbgeschlossen>
{
    public static GastAbosBeenden Aus(Umschlag umschlag, FreizeitAbgeschlossen ereignis) => new(umschlag, ereignis.FreizeitId);
}

internal sealed class GastAbosBeendenHandler(KanaeleDbContext db) : ICommandHandler<GastAbosBeenden>
{
    public async ValueTask<Unit> Handle(GastAbosBeenden command, CancellationToken cancellationToken)
    {
        await db.GastPushAbos.Where(a => a.FreizeitId == command.FreizeitId).ExecuteDeleteAsync(cancellationToken);
        await db.PushAbos.Where(a => a.FreizeitId == command.FreizeitId).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
