using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Materialien;

/// <summary>
/// Story 3-2: Legt ein Material an einem Programmpunkt an (Leitung und Team, FR-36), mit Status „gebraucht“. Nur die Bezeichnung ist nötig, die
/// Menge ist freiwillig. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record MaterialAnlegenCommand(Guid FreizeitId, Guid PunktId, string? Bezeichnung, string? Menge)
    : ICommand<MaterialAnlegen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis von <see cref="MaterialAnlegenCommand"/>; bei <see cref="Angelegt"/> das verfolgte Material (nach dem Speichern mit Version) und sein Punkt.</summary>
internal abstract record MaterialAnlegen
{
    private MaterialAnlegen()
    {
    }

    public sealed record Angelegt(Material Material, Programmpunkt Punkt) : MaterialAnlegen;

    public sealed record Abgeschlossen : MaterialAnlegen;

    public sealed record NichtGefunden : MaterialAnlegen;
}

internal sealed class MaterialAnlegenValidator : AbstractValidator<MaterialAnlegenCommand>
{
    public MaterialAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (MaterialTexte.BezeichnungFehler(c.Bezeichnung) is { } bezeichnungFehler)
            {
                kontext.AddFailure(new ValidationFailure(MaterialFelder.Bezeichnung, bezeichnungFehler));
            }

            if (MaterialTexte.MengeFehler(c.Menge) is { } mengeFehler)
            {
                kontext.AddFailure(new ValidationFailure(MaterialFelder.Menge, mengeFehler));
            }
        });
}

internal sealed class MaterialAnlegenHandler(ProgrammDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<MaterialAnlegenCommand, MaterialAnlegen>
{
    public async ValueTask<MaterialAnlegen> Handle(MaterialAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new MaterialAnlegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new MaterialAnlegen.Abgeschlossen();
        }

        // Zeilensperre am Programmpunkt: ein gleichzeitiges Löschen des Punkts wartet, bis das Material steht (und nimmt es dann mit).
        var punkt = await db.ProgrammpunktSperrenAsync(command.FreizeitId, command.PunktId, cancellationToken);
        if (punkt is null)
        {
            return new MaterialAnlegen.NichtGefunden();
        }

        var material = Material.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, punkt.Id, command.Bezeichnung!, command.Menge);
        db.Material.Add(material);
        return new MaterialAnlegen.Angelegt(material, punkt);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/punkte/{punktId}/material</c> (Leitung und Team, FR-36): Körper <see cref="MaterialNeu"/>.
/// <c>201</c> mit dem Material (Status „gebraucht“, mit Version; <c>Location</c> ist die Materialliste); Bezeichnung leer oder zu lang, Menge zu
/// lang <c>400</c> am Feld; Punkt unbekannt oder aus einer anderen Freizeit <c>404</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class MaterialAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid freizeitId, Guid punktId, MaterialNeu koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new MaterialAnlegenCommand(freizeitId, punktId, koerper.Bezeichnung, koerper.Menge), abbruch) switch
        {
            MaterialAnlegen.Angelegt angelegt => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/programm/material", MaterialLesen.AlsApi(angelegt.Material, angelegt.Punkt, angemeldet.PersonId)),
            MaterialAnlegen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
