using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>
/// Story 3-4: Trägt einen Schaden, einen Verlust oder eine Entnahme auf der Schadens- und Verlustliste der Freizeit ein (Leitung und Team, FR-38).
/// Ohne Phasenprüfung: Nach dem Abschluss sind die Einträge Anschaffungswünsche, die beschreibbar bleiben (AD-17).
/// </summary>
internal sealed record SchadenEintragenCommand(Guid FreizeitId, SchadenArt? Art, string? Bezeichnung)
    : ICommand<SchadenVerlust>, IModulCommand<InventarDbContext>, IBenutzerCommand;

internal sealed class SchadenEintragenValidator : AbstractValidator<SchadenEintragenCommand>
{
    public SchadenEintragenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.Art is not { } art || !Enum.IsDefined(art))
            {
                kontext.AddFailure(new ValidationFailure(SchadenFelder.Art, c.Art is null ? "Art fehlt." : "Unbekannte Art."));
            }

            if (SchadenTexte.BezeichnungFehler(c.Bezeichnung) is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(SchadenFelder.Bezeichnung, fehler));
            }
        });
}

internal sealed class SchadenEintragenHandler(InventarDbContext db, IMandantenKontext mandant, TimeProvider uhr)
    : ICommandHandler<SchadenEintragenCommand, SchadenVerlust>
{
    public ValueTask<SchadenVerlust> Handle(SchadenEintragenCommand command, CancellationToken cancellationToken)
    {
        var organisationId = mandant.OrganisationId ?? throw new InvalidOperationException("Ohne Mandant erreicht niemand diesen Command.");
        var jetzt = uhr.GetUtcNow();
        var eintrag = SchadenVerlust.Eintragen(Guid.CreateVersion7(jetzt), organisationId, command.FreizeitId, command.Art!.Value, command.Bezeichnung!, jetzt);
        db.Schaeden.Add(eintrag);
        return ValueTask.FromResult(eintrag);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/inventar/schaeden</c> (Leitung und Team, FR-38): Körper <see cref="SchadenNeu"/>. <c>201</c> mit dem Eintrag
/// (mit Version; <c>Location</c> ist die Liste); Art fehlt oder unbekannt, Bezeichnung leer oder zu lang <c>400</c> am Feld. Auch nach dem Abschluss.
/// </summary>
internal static class SchadenEintragenEndpunkt
{
    public static async Task<IResult> Eintragen(Guid freizeitId, SchadenNeu koerper, ISender sender, CancellationToken abbruch)
    {
        var eintrag = await sender.Send(new SchadenEintragenCommand(freizeitId, koerper.Art, koerper.Bezeichnung), abbruch);
        return TypedResults.Created($"/api/freizeiten/{freizeitId}/inventar/schaeden", SchadenLesen.AlsApi(eintrag));
    }
}
