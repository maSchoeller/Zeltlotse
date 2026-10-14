using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Story 3-8, Schreibschutz (AD-17, AD-27): Ein Benutzer-Command (<see cref="IBenutzerCommand"/>) mit Freizeit-Bezug
/// (<see cref="IFreizeitCommand"/>) läuft nicht, wenn seine Freizeit gelaufen ist; dann <see cref="FreizeitAbgeschlossenAusnahme"/>, und der
/// Handler läuft nicht. Ausgenommen sind Commands mit <see cref="ErlaubtNachAbschlussAttribute"/>. Alle anderen Nachrichten, insbesondere
/// Ereignis-Empfänger, laufen ohne Abfrage durch; der Abschluss-Stand wird nur für geprüfte Nachrichten aufgelöst.
/// </summary>
public sealed class SchreibschutzStufe<TMessage, TResponse>(IServiceProvider dienste)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private static readonly bool Erlaubt = typeof(TMessage).IsDefined(typeof(ErlaubtNachAbschlussAttribute), inherit: false);

    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (!Erlaubt
            && message is IBenutzerCommand and IFreizeitCommand { FreizeitId: var freizeitId }
            && freizeitId != Guid.Empty
            && await dienste.GetRequiredService<IAbschlussStand>().IstAbgeschlossenAsync(freizeitId, cancellationToken))
        {
            throw new FreizeitAbgeschlossenAusnahme(freizeitId);
        }

        return await next(message, cancellationToken);
    }
}
