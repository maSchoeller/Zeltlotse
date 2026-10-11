using Mediator;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Stufe 4 (AD-28): nach dem Handler die Ereignisse aller geänderten Aggregate einsammeln und
/// zusammen mit der Änderung in einem <c>SaveChanges</c> in die Outbox schreiben.
/// </summary>
public sealed class OutboxStufe<TMessage, TResponse>(IServiceProvider dienste, TimeProvider uhr, EreignisKatalog katalog)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IModulCommandBasis
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var db = ModulCommandInfo<TMessage>.DbContext(dienste);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Die Outbox-Stufe läuft ohne Transaktion; die Transaktions-Stufe muss vor ihr stehen (AD-27).");
        }

        var antwort = await next(message, cancellationToken);
        await db.OutboxSchreibenUndSpeichernAsync(uhr, katalog, cancellationToken);
        return antwort;
    }
}
