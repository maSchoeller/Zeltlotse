using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 6-2 (FR-62, AD-13): Die Verschiebung eines Programmpunkts ist mitgeteilt („Ja, informieren“) oder der Hinweis ausgeblendet. Der Client
/// schickt die Uhrzeit mit, die er mitgeteilt hat; nur wenn sie noch die aktuelle ist, verschwindet „Geändert, noch nicht mitgeteilt“. Versendet
/// wird hier nichts: Die Nachricht geht vorher über den Kanal „Alle“ (Kanäle, bestätigt). Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ProgrammpunktMitgeteiltCommand(Guid FreizeitId, Guid PunktId, string? Beginn)
    : ICommand<ProgrammpunktMitteilung>, IModulCommand<ProgrammDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis von „mitgeteilt“.</summary>
internal abstract record ProgrammpunktMitteilung
{
    private ProgrammpunktMitteilung()
    {
    }

    public sealed record Erledigt : ProgrammpunktMitteilung;

    /// <summary>Der Punkt wurde inzwischen auf eine andere Uhrzeit verschoben.</summary>
    public sealed record Konflikt : ProgrammpunktMitteilung;

    public sealed record Abgeschlossen : ProgrammpunktMitteilung;

    public sealed record NichtGefunden : ProgrammpunktMitteilung;
}

internal sealed class ProgrammpunktMitgeteiltValidator : AbstractValidator<ProgrammpunktMitgeteiltCommand>
{
    public ProgrammpunktMitgeteiltValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!ProgrammpunktFelder.UhrzeitLesen(c.Beginn, out _))
            {
                kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Beginn, ProgrammpunktTexte.KeineUhrzeit));
            }
        });
}

internal sealed class ProgrammpunktMitgeteiltHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<ProgrammpunktMitgeteiltCommand, ProgrammpunktMitteilung>
{
    public async ValueTask<ProgrammpunktMitteilung> Handle(ProgrammpunktMitgeteiltCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new ProgrammpunktMitteilung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new ProgrammpunktMitteilung.Abgeschlossen();
        }

        // Zeilensperre (FOR UPDATE): Eine gleichzeitige Verschiebung läuft vorher oder nachher, nie dazwischen.
        var punkt = await db.ProgrammpunktSperrenAsync(command.FreizeitId, command.PunktId, cancellationToken);
        if (punkt is null)
        {
            return new ProgrammpunktMitteilung.NichtGefunden();
        }

        ProgrammpunktFelder.UhrzeitLesen(command.Beginn, out var beginn);
        return punkt.Mitgeteilt(beginn) ? new ProgrammpunktMitteilung.Erledigt() : new ProgrammpunktMitteilung.Konflikt();
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/punkte/{punktId}/mitgeteilt</c> (Leitung und Team, Story 6-2): Körper
/// <see cref="ProgrammpunktMitgeteilt"/>. <c>204</c>, auch ohne offenen Hinweis; ungültige Uhrzeit <c>400</c> (Feld <c>beginn</c>); inzwischen
/// verschoben <c>409 konflikt</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannter Punkt oder Punkt einer anderen
/// Freizeit <c>404</c>.
/// </summary>
internal static class ProgrammpunktMitgeteiltEndpunkt
{
    public static async Task<IResult> Mitgeteilt(Guid freizeitId, Guid punktId, ProgrammpunktMitgeteilt koerper, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new ProgrammpunktMitgeteiltCommand(freizeitId, punktId, koerper.Beginn), abbruch) switch
        {
            ProgrammpunktMitteilung.Erledigt => TypedResults.NoContent(),
            ProgrammpunktMitteilung.Konflikt => ProgrammProbleme.Konflikt("Der Programmpunkt wurde inzwischen erneut verschoben."),
            ProgrammpunktMitteilung.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
