using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>
/// Story 3-4: Ein Anschaffungswunsch ist erledigt (Org-Admin, FR-38): Der Eintrag verschwindet, auch von der Liste der Freizeit. Nur Einträge
/// abgeschlossener Freizeiten sind Anschaffungswünsche; alle anderen gehören noch der Freizeit.
/// </summary>
internal sealed record AnschaffungswunschErledigtCommand(Guid EintragId) : ICommand<bool>, IModulCommand<InventarDbContext>, IBenutzerCommand;

internal sealed class AnschaffungswunschErledigtHandler(InventarDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<AnschaffungswunschErledigtCommand, bool>
{
    public async ValueTask<bool> Handle(AnschaffungswunschErledigtCommand command, CancellationToken cancellationToken)
    {
        if (await db.SchadenSperrenAsync(command.EintragId, cancellationToken) is not { } eintrag
            || (await Anschaffungswuensche.GelaufenAsync(zeitpunkte, [eintrag.FreizeitId], cancellationToken)).Count == 0)
        {
            return false;
        }

        db.Schaeden.Remove(eintrag);
        return true;
    }
}

/// <summary>
/// <c>DELETE /api/organisationen/{orgId}/inventar/anschaffungswuensche/{eintragId}</c> (Org-Admin): „erledigt“, <c>204</c>; unbekannt, aus einer
/// anderen Organisation oder aus einer Freizeit, die noch nicht abgeschlossen ist, <c>404</c>.
/// </summary>
internal static class AnschaffungswunschErledigtEndpunkt
{
    public static async Task<IResult> Erledigt(Guid eintragId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new AnschaffungswunschErledigtCommand(eintragId), abbruch) ? TypedResults.NoContent() : TypedResults.NotFound();
}
