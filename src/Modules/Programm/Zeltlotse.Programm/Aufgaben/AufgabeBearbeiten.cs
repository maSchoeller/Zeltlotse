using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5: Ändert genau ein Feld einer Aufgabe (Titel oder Phase, AD-19). Konfliktregel: Weicht der aktuelle Wert desselben Felds vom
/// mitgeschickten alten Wert ab, ist das Ergebnis <see cref="AufgabeAenderung.Konflikt"/>. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record AufgabeFeldAendernCommand(Guid FreizeitId, Guid AufgabeId, AufgabeFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<AufgabeAenderung>, IModulCommand<ProgrammDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> die verfolgte Aufgabe (nach dem Speichern mit neuer Version).</summary>
internal abstract record AufgabeAenderung
{
    private AufgabeAenderung()
    {
    }

    public sealed record Gespeichert(Aufgabe Aufgabe) : AufgabeAenderung;

    public sealed record Konflikt : AufgabeAenderung;

    public sealed record Abgeschlossen : AufgabeAenderung;

    public sealed record NichtGefunden : AufgabeAenderung;
}

internal sealed class AufgabeFeldAendernValidator : AbstractValidator<AufgabeFeldAendernCommand>
{
    public AufgabeFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            switch (c.Feld)
            {
                case AufgabeFeld.Titel when AufgabenTexte.TitelFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(AufgabenFelder.Titel, fehler));
                    break;
                case AufgabeFeld.Phase when !AufgabenFelder.PhaseLesen(c.NeuerWert, out _):
                    kontext.AddFailure(new ValidationFailure(AufgabenFelder.Phase, AufgabenTexte.UnbekanntePhase));
                    break;
                case AufgabeFeld.Titel or AufgabeFeld.Phase:
                    break;
                default:
                    kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                    break;
            }
        });
}

internal sealed class AufgabeFeldAendernHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<AufgabeFeldAendernCommand, AufgabeAenderung>
{
    public async ValueTask<AufgabeAenderung> Handle(AufgabeFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new AufgabeAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new AufgabeAenderung.Abgeschlossen();
        }

        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var aufgabe = await db.AufgabeSperrenAsync(command.FreizeitId, command.AufgabeId, cancellationToken);
        if (aufgabe is null)
        {
            return new AufgabeAenderung.NichtGefunden();
        }

        return aufgabe.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new AufgabeAenderung.Gespeichert(aufgabe)
            : new AufgabeAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/programm/aufgaben/{aufgabeId}</c> (Leitung und Team, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "titel"|"phase": neu }</c> mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>; ungültiger Wert, kein, mehr als ein oder ein
/// unbekanntes Feld <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>;
/// Aufgabe einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class AufgabeFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid aufgabeId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<AufgabeFeld>(koerper, AufgabenFelder.JsonName);
        return await sender.Send(new AufgabeFeldAendernCommand(freizeitId, aufgabeId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            AufgabeAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Aufgabe.Version,
                [AufgabenFelder.JsonName(feld)] = gespeichert.Aufgabe.Wert(feld),
            }),
            AufgabeAenderung.Konflikt => ProgrammProbleme.Konflikt("Der Wert wurde inzwischen geändert."),
            AufgabeAenderung.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
