using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 3-1: Legt einen Programmpunkt an einem Tag der Freizeit an (Leitung und Team, FR-34). Nur der Titel ist nötig (NS-5), dazu der Tag; die
/// Uhrzeit ist freiwillig. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ProgrammpunktAnlegenCommand(Guid FreizeitId, string? Titel, DateOnly? Tag, string? Beginn)
    : ICommand<ProgrammpunktAnlegen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="ProgrammpunktAnlegenCommand"/>; bei <see cref="Angelegt"/> der verfolgte Punkt (nach dem Speichern mit Version).</summary>
internal abstract record ProgrammpunktAnlegen
{
    private ProgrammpunktAnlegen()
    {
    }

    public sealed record Angelegt(Programmpunkt Punkt) : ProgrammpunktAnlegen;

    /// <summary>Der Tag gehört nicht zur Freizeit.</summary>
    public sealed record TagAusserhalb : ProgrammpunktAnlegen;

    public sealed record Abgeschlossen : ProgrammpunktAnlegen;

    public sealed record NichtGefunden : ProgrammpunktAnlegen;
}

internal sealed class ProgrammpunktAnlegenValidator : AbstractValidator<ProgrammpunktAnlegenCommand>
{
    public ProgrammpunktAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (ProgrammpunktTexte.TitelFehler(c.Titel) is { } titelFehler)
            {
                kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Titel, titelFehler));
            }

            if (c.Tag is null)
            {
                kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Tag, "Tag fehlt."));
            }

            if (!ProgrammpunktFelder.UhrzeitLesen(c.Beginn, out _))
            {
                kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Beginn, ProgrammpunktTexte.KeineUhrzeit));
            }
        });
}

internal sealed class ProgrammpunktAnlegenHandler(ProgrammDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitTageLesen tageLesen, TimeProvider uhr)
    : ICommandHandler<ProgrammpunktAnlegenCommand, ProgrammpunktAnlegen>
{
    public async ValueTask<ProgrammpunktAnlegen> Handle(ProgrammpunktAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit)
            || !(await tageLesen.TageAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var tage))
        {
            return new ProgrammpunktAnlegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new ProgrammpunktAnlegen.Abgeschlossen();
        }

        if (!tage.Enthaelt(command.Tag!.Value))
        {
            return new ProgrammpunktAnlegen.TagAusserhalb();
        }

        ProgrammpunktFelder.UhrzeitLesen(command.Beginn, out var beginn);
        var punkt = Programmpunkt.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, command.Tag.Value, beginn, command.Titel!);
        db.Programmpunkte.Add(punkt);
        return new ProgrammpunktAnlegen.Angelegt(punkt);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/punkte</c> (Leitung und Team, FR-34): Körper <see cref="ProgrammpunktNeu"/>. <c>201</c> mit dem
/// Programmpunkt (mit Version, <c>Location</c> ist der Wochenraster); Titel leer oder zu lang, Tag fehlt oder außerhalb der Freizeit, Beginn nicht <c>HH:mm</c> <c>400</c> am Feld;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class ProgrammpunktAnlegenEndpunkt
{
    public const string TagAusserhalb = "Dieser Tag gehört nicht zur Freizeit.";

    public static async Task<IResult> Anlegen(Guid freizeitId, ProgrammpunktNeu koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new ProgrammpunktAnlegenCommand(freizeitId, koerper.Titel, koerper.Tag, koerper.Beginn), abbruch) switch
        {
            ProgrammpunktAnlegen.Angelegt angelegt => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/programm", ProgrammLesen.AlsApi(angelegt.Punkt, angemeldet.PersonId)),
            ProgrammpunktAnlegen.TagAusserhalb => throw new ValidierungsAusnahme(new Dictionary<string, string[]> { [ProgrammpunktFelder.Tag] = [TagAusserhalb] }),
            ProgrammpunktAnlegen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}

/// <summary>Texte der Formprüfung, gleich für Anlegen und Bearbeiten.</summary>
internal static class ProgrammpunktTexte
{
    public const string KeineUhrzeit = "Uhrzeit im Format HH:mm, z. B. 14:30.";

    /// <summary>Der Fehler zum Titel oder <c>null</c>: Pflicht, getrimmt höchstens <see cref="ProgrammpunktFelder.TitelMaximaleLaenge"/> Zeichen.</summary>
    public static string? TitelFehler(string? titel)
    {
        var text = titel?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return "Titel fehlt.";
        }

        return text.Length > ProgrammpunktFelder.TitelMaximaleLaenge ? $"Höchstens {ProgrammpunktFelder.TitelMaximaleLaenge} Zeichen." : null;
    }
}
