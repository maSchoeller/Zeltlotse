using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5: Legt eine Aufgabe der Freizeit an (Leitung und Team, FR-39), offen und unbesetzt. Nur der Titel ist nötig; ohne Phase gilt
/// Vorbereitung. Optional hängt sie an einem Programmpunkt derselben Freizeit. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record AufgabeAnlegenCommand(Guid FreizeitId, string? Titel, AufgabenPhase? Phase, Guid? ProgrammpunktId)
    : ICommand<AufgabeAnlegen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis von <see cref="AufgabeAnlegenCommand"/>; bei <see cref="Angelegt"/> die verfolgte Aufgabe (nach dem Speichern mit Version).</summary>
internal abstract record AufgabeAnlegen
{
    private AufgabeAnlegen()
    {
    }

    public sealed record Angelegt(Aufgabe Aufgabe) : AufgabeAnlegen;

    public sealed record Abgeschlossen : AufgabeAnlegen;

    /// <summary>Freizeit oder Programmpunkt (in dieser Freizeit) unbekannt.</summary>
    public sealed record NichtGefunden : AufgabeAnlegen;
}

internal sealed class AufgabeAnlegenValidator : AbstractValidator<AufgabeAnlegenCommand>
{
    public AufgabeAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (AufgabenTexte.TitelFehler(c.Titel) is { } titelFehler)
            {
                kontext.AddFailure(new ValidationFailure(AufgabenFelder.Titel, titelFehler));
            }

            if (AufgabenTexte.PhaseUnbekannt(c.Phase))
            {
                kontext.AddFailure(new ValidationFailure(AufgabenFelder.Phase, AufgabenTexte.UnbekanntePhase));
            }
        });
}

internal sealed class AufgabeAnlegenHandler(ProgrammDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<AufgabeAnlegenCommand, AufgabeAnlegen>
{
    public async ValueTask<AufgabeAnlegen> Handle(AufgabeAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new AufgabeAnlegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new AufgabeAnlegen.Abgeschlossen();
        }

        // Zeilensperre am Programmpunkt: ein gleichzeitiges Löschen des Punkts wartet, bis die Aufgabe steht (und nimmt sie dann mit).
        if (command.ProgrammpunktId is { } punktId && await db.ProgrammpunktSperrenAsync(command.FreizeitId, punktId, cancellationToken) is null)
        {
            return new AufgabeAnlegen.NichtGefunden();
        }

        var aufgabe = Aufgabe.Anlegen(
            Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, command.Titel!, command.Phase ?? AufgabenPhase.Vorbereitung, command.ProgrammpunktId);
        db.Aufgaben.Add(aufgabe);
        return new AufgabeAnlegen.Angelegt(aufgabe);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/aufgaben</c> (Leitung und Team, FR-39): Körper <see cref="AufgabeNeu"/>. <c>201</c> mit der
/// Aufgabe (offen, unbesetzt, mit Version; <c>Location</c> ist die Aufgabenliste); Titel leer oder zu lang, Phase unbekannt <c>400</c> am Feld;
/// Programmpunkt unbekannt oder aus einer anderen Freizeit <c>404</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class AufgabeAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid freizeitId, AufgabeNeu koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new AufgabeAnlegenCommand(freizeitId, koerper.Titel, koerper.Phase, koerper.ProgrammpunktId), abbruch) switch
        {
            AufgabeAnlegen.Angelegt angelegt => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/programm/aufgaben", AufgabenLesen.AlsApi(angelegt.Aufgabe, angemeldet.PersonId)),
            AufgabeAnlegen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
