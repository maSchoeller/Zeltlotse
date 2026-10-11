using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Stufe 3 (AD-27): öffnet die Transaktion auf dem DbContext aus <see cref="IModulCommand{TDbContext}"/>.
/// Handler öffnen nie selbst eine Transaktion. Wirft der Handler, wird nichts gespeichert.
/// </summary>
public sealed class TransaktionsStufe<TMessage, TResponse>(IServiceProvider dienste)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IModulCommandBasis
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var db = ModulCommandInfo<TMessage>.DbContext(dienste);
        if (db.Database.CurrentTransaction is not null)
        {
            return await next(message, cancellationToken);
        }

        await using var transaktion = await db.Database.BeginTransactionAsync(cancellationToken);
        var antwort = await next(message, cancellationToken);
        await transaktion.CommitAsync(cancellationToken);
        return antwort;
    }
}
