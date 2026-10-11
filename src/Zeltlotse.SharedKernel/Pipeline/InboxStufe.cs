using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Outbox;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Stufe 2 (AD-4, nur Ereignis-Empfänger): verwirft bereits verarbeitete und ältere Ereignisse.
/// Sonst merkt sie (Ereignis, Empfänger) und die Folgenummer vor; gespeichert wird das im selben
/// <c>SaveChanges</c> und in derselben Transaktion wie die Folgeaktion.
/// </summary>
public sealed class InboxStufe<TMessage, TResponse>(IServiceProvider dienste, TimeProvider uhr)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IEreignisEmpfaenger, IModulCommandBasis
{
    private static readonly string Empfaenger = typeof(TMessage).FullName!;

    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var db = ModulCommandInfo<TMessage>.DbContext(dienste);
        var umschlag = message.Umschlag;

        var bereitsVerarbeitet = await db.InboxEintraege
            .AnyAsync(e => e.EreignisId == umschlag.EreignisId && e.Empfaenger == Empfaenger, cancellationToken);
        if (bereitsVerarbeitet)
        {
            return default!;
        }

        var stand = await db.InboxStaende
            .SingleOrDefaultAsync(s => s.DatensatzId == umschlag.DatensatzId && s.Empfaenger == Empfaenger, cancellationToken);
        if (stand is not null && stand.Folgenummer >= umschlag.Folgenummer)
        {
            return default!;
        }

        db.InboxEintraege.Add(new InboxEintrag
        {
            EreignisId = umschlag.EreignisId,
            Empfaenger = Empfaenger,
            VerarbeitetAm = uhr.GetUtcNow(),
        });
        if (stand is null)
        {
            db.InboxStaende.Add(new InboxStand { DatensatzId = umschlag.DatensatzId, Empfaenger = Empfaenger, Folgenummer = umschlag.Folgenummer });
        }
        else
        {
            stand.Folgenummer = umschlag.Folgenummer;
        }

        return await next(message, cancellationToken);
    }
}
