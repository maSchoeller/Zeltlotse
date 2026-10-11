using FluentValidation;
using Mediator;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Stufe 1 (AD-27): Formprüfung mit FluentValidation. Bei Fehlern läuft der Handler nicht;
/// die Ausnahme trägt die Fehler je JSON-Feldname.
/// </summary>
public sealed class ValidierungsStufe<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validatoren)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var fehler = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validatoren)
        {
            var ergebnis = await validator.ValidateAsync(message, cancellationToken);
            fehler.AddRange(ergebnis.Errors);
        }

        if (fehler.Count > 0)
        {
            throw ValidierungsAusnahme.Aus(fehler);
        }

        return await next(message, cancellationToken);
    }

    /// <summary>„Adresse.Strasse“ → „adresse.strasse“, „Posten[0].Name“ → „posten[0].name“.</summary>
    internal static string JsonName(string eigenschaft) => ValidierungsAusnahme.JsonName(eigenschaft);
}
