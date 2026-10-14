using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Funktions-Schalter (AD-20, AD-27): Ein Benutzer-Command (<see cref="IBenutzerCommand"/>), der mit <see cref="ISchalterCommand"/> zu
/// einem Schalter gehört, läuft nur, wenn der Schalter für seine Freizeit an ist; sonst <see cref="SchalterAusAusnahme"/>, und der Handler
/// läuft nicht. Alle anderen Nachrichten, insbesondere Ereignis-Empfänger, laufen ohne Abfrage durch; der Schalter-Stand wird nur für
/// geprüfte Nachrichten aufgelöst.
/// </summary>
public sealed class SchalterStufe<TMessage, TResponse>(IServiceProvider dienste)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (message is IBenutzerCommand and ISchalterCommand mitSchalter
            && !await dienste.GetRequiredService<ISchalterStand>().IstAnAsync(mitSchalter.FreizeitId, mitSchalter.Schalter, cancellationToken))
        {
            throw new SchalterAusAusnahme(mitSchalter.FreizeitId, mitSchalter.Schalter);
        }

        return await next(message, cancellationToken);
    }
}
