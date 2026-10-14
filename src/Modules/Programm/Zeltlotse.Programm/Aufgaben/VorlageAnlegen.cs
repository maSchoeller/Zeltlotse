using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5: Legt eine Aufgaben-Vorlage in der Organisation des Mandanten an (Org-Admin, FR-40). Nur der Titel ist nötig; ohne Phase gilt
/// Vorbereitung. Hängt an keiner Freizeit.
/// </summary>
internal sealed record VorlageAnlegenCommand(string? Titel, AufgabenPhase? Phase)
    : ICommand<AufgabenVorlage>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

internal sealed class VorlageAnlegenValidator : AbstractValidator<VorlageAnlegenCommand>
{
    public VorlageAnlegenValidator() =>
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

internal sealed class VorlageAnlegenHandler(ProgrammDbContext db, IMandantenKontext mandant, TimeProvider uhr)
    : ICommandHandler<VorlageAnlegenCommand, AufgabenVorlage>
{
    public ValueTask<AufgabenVorlage> Handle(VorlageAnlegenCommand command, CancellationToken cancellationToken)
    {
        var organisationId = mandant.OrganisationId ?? throw new InvalidOperationException("Ohne Mandanten-Kontext keine Vorlage (AD-6).");
        var vorlage = AufgabenVorlage.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.Titel!, command.Phase ?? AufgabenPhase.Vorbereitung);
        db.AufgabenVorlagen.Add(vorlage);
        return ValueTask.FromResult(vorlage);
    }
}

/// <summary>
/// <c>POST /api/organisationen/{orgId}/aufgaben-vorlagen</c> (Org-Admin, FR-40): Körper <see cref="AufgabenVorlageNeu"/>. <c>201</c> mit der
/// Vorlage (mit Version; <c>Location</c> ist die Liste); Titel leer oder zu lang, Phase unbekannt <c>400</c> am Feld.
/// </summary>
internal static class VorlageAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid orgId, AufgabenVorlageNeu koerper, ISender sender, CancellationToken abbruch)
    {
        var vorlage = await sender.Send(new VorlageAnlegenCommand(koerper.Titel, koerper.Phase), abbruch);
        return TypedResults.Created($"/api/organisationen/{orgId}/aufgaben-vorlagen", AufgabenLesen.AlsApi(vorlage));
    }
}
