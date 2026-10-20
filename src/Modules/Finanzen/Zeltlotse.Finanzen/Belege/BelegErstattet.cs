using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// Story 5-2 (FR-58, AD-17): Setzt oder löscht den Erstattungsvermerk eines Belegs (Leitung und Team, Modul Finanzen) nach der Konfliktregel aus AD-19.
/// Die einzige Ausnahme vom Schreibschutz in Finanzen: Auch nach dem Abschluss der Freizeit lässt sich vermerken, dass ein Beleg erstattet ist. Der
/// Handler prüft darum keine Phase.
/// </summary>
[ErlaubtNachAbschluss]
internal sealed record BelegErstattetCommand(Guid FreizeitId, Guid BelegId, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<BelegAenderung>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

internal sealed class BelegErstattetValidator : AbstractValidator<BelegErstattetCommand>
{
    public BelegErstattetValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (Beleg.JaNeinLesen(c.NeuerWert) is null)
            {
                kontext.AddFailure(new ValidationFailure(BelegFelder.Erstattet, "Nur true oder false."));
            }
        });
}

internal sealed class BelegErstattetHandler(FinanzenDbContext db) : ICommandHandler<BelegErstattetCommand, BelegAenderung>
{
    public async ValueTask<BelegAenderung> Handle(BelegErstattetCommand command, CancellationToken cancellationToken)
    {
        var beleg = await db.BelegSperrenAsync(command.FreizeitId, command.BelegId, cancellationToken);
        if (beleg is null)
        {
            return new BelegAenderung.NichtGefunden();
        }

        return beleg.ErstattetAendern(command.AlterWert, command.NeuerWert) ? new BelegAenderung.Gespeichert(beleg) : new BelegAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/finanzen/belege/{belegId}/erstattet</c> (Leitung und Team, Modul Finanzen, AD-19, auch nach dem Abschluss):
/// Körper <c>{ "version": n, "alterWert": "false", "erstattet": "true" }</c>. <c>200 { "version", "erstattet" }</c>; anderer Wert als <c>"true"</c> oder
/// <c>"false"</c> <c>400</c>; veralteter alter Wert <c>409 konflikt</c>; Beleg unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class BelegErstattetEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid belegId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, _, neuerWert) = FeldPatch.Lesen<BelegErstattetFeld>(koerper, BelegFelder.JsonName);
        var ergebnis = await sender.Send(new BelegErstattetCommand(freizeitId, belegId, version, alterWert, neuerWert), abbruch);
        return BelegSchritte.Antwort(ergebnis, beleg => TypedResults.Ok(new Dictionary<string, object?>
        {
            ["version"] = beleg.Version,
            [BelegFelder.Erstattet] = beleg.ErstattetText,
        }));
    }
}
