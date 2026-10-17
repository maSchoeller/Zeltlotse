using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// Story 4-1: Legt ein Rezept der Freizeit an (Leitung und Team, Modul Verpflegung, FR-46). Name und Basis-Personenzahl sind Pflicht, die
/// Zubereitung folgt per Autosave. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record RezeptAnlegenCommand(Guid FreizeitId, string? Name, string? BasisPersonen)
    : ICommand<RezeptAnlegen>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="RezeptAnlegenCommand"/>; bei <see cref="Angelegt"/> das verfolgte Rezept (nach dem Speichern mit Version).</summary>
internal abstract record RezeptAnlegen
{
    private RezeptAnlegen()
    {
    }

    public sealed record Angelegt(Rezept Rezept) : RezeptAnlegen;

    public sealed record Abgeschlossen : RezeptAnlegen;

    public sealed record NichtGefunden : RezeptAnlegen;
}

internal sealed class RezeptAnlegenValidator : AbstractValidator<RezeptAnlegenCommand>
{
    public RezeptAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (KuecheWerte.PflichtFehler(c.Name, RezeptFelder.NameMaximaleLaenge, "Name fehlt.") is { } nameFehler)
            {
                kontext.AddFailure(new ValidationFailure(RezeptFelder.Name, nameFehler));
            }

            if (KuecheWerte.BasisFehler(c.BasisPersonen) is { } basisFehler)
            {
                kontext.AddFailure(new ValidationFailure(RezeptFelder.BasisPersonen, basisFehler));
            }
        });
}

internal sealed class RezeptAnlegenHandler(KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<RezeptAnlegenCommand, RezeptAnlegen>
{
    public async ValueTask<RezeptAnlegen> Handle(RezeptAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new RezeptAnlegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new RezeptAnlegen.Abgeschlossen();
        }

        var rezept = Rezept.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, command.Name!, KuecheWerte.BasisLesen(command.BasisPersonen)!.Value);
        db.Rezepte.Add(rezept);
        return new RezeptAnlegen.Angelegt(rezept);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kueche/rezepte</c> (Leitung und Team, Modul Verpflegung): Körper <see cref="RezeptNeu"/>. <c>201</c> mit dem
/// Rezept (ohne Zutaten, mit Version und Personenzahl; <c>Location</c> ist das Rezept); Name fehlt oder zu lang, Basis fehlt oder ungültig
/// <c>400</c> am Feld; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class RezeptAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid freizeitId, RezeptNeu koerper, ISender sender, IFreizeitPersonenzahlLesen personenzahl, CancellationToken abbruch) =>
        await sender.Send(new RezeptAnlegenCommand(freizeitId, koerper.Name, koerper.BasisPersonen), abbruch) switch
        {
            RezeptAnlegen.Angelegt angelegt => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/kueche/rezepte/{angelegt.Rezept.Id}",
                KuecheLesen.AlsApi(angelegt.Rezept, [], await personenzahl.AnzahlAsync(freizeitId, abbruch))),
            RezeptAnlegen.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
