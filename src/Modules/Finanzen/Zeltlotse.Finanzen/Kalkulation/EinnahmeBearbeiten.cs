using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1: Ändert genau ein Feld eines Einnahmepostens (Bezeichnung oder Betrag, AD-19). Konfliktregel wie bei den Planzahlen; eine abgeschlossene
/// Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record EinnahmeFeldAendernCommand(Guid FreizeitId, Guid EinnahmeId, EinnahmepostenFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<FinanzenAenderung<Einnahmeposten>>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

internal sealed class EinnahmeFeldAendernValidator : AbstractValidator<EinnahmeFeldAendernCommand>
{
    public EinnahmeFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var fehler = c.Feld switch
            {
                EinnahmepostenFeld.Bezeichnung => FinanzenWerte.PflichtFehler(c.NeuerWert, EinnahmepostenFelder.BezeichnungMaximaleLaenge, "Bezeichnung fehlt."),
                EinnahmepostenFeld.Betrag => FinanzenWerte.BetragFehler(c.NeuerWert),
                _ => null,
            };
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
            else if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(EinnahmepostenFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class EinnahmeFeldAendernHandler(FinanzenDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<EinnahmeFeldAendernCommand, FinanzenAenderung<Einnahmeposten>>
{
    public async ValueTask<FinanzenAenderung<Einnahmeposten>> Handle(EinnahmeFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new FinanzenAenderung<Einnahmeposten>.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new FinanzenAenderung<Einnahmeposten>.Abgeschlossen();
        }

        var posten = await db.EinnahmeSperrenAsync(command.FreizeitId, command.EinnahmeId, cancellationToken);
        if (posten is null)
        {
            return new FinanzenAenderung<Einnahmeposten>.NichtGefunden();
        }

        return posten.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new FinanzenAenderung<Einnahmeposten>.Gespeichert(posten)
            : new FinanzenAenderung<Einnahmeposten>.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/finanzen/kalkulation/einnahmen/{einnahmeId}</c> (Leitung und Team, Modul Finanzen, AD-19): Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"betrag": neu }</c>. <c>200 { "version", "&lt;feld&gt;" }</c>; ungültiger Wert oder Körper
/// <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; Posten unbekannt oder aus
/// einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class EinnahmeFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid einnahmeId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<EinnahmepostenFeld>(koerper, EinnahmepostenFelder.JsonName);
        return await sender.Send(new EinnahmeFeldAendernCommand(freizeitId, einnahmeId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            FinanzenAenderung<Einnahmeposten>.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Zeile.Version,
                [EinnahmepostenFelder.JsonName(feld)] = gespeichert.Zeile.Wert(feld),
            }),
            FinanzenAenderung<Einnahmeposten>.Konflikt => FinanzenProbleme.Konflikt(),
            FinanzenAenderung<Einnahmeposten>.Abgeschlossen => FinanzenProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
