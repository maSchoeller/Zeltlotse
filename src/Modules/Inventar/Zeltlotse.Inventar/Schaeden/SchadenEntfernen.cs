using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>Story 3-4: Entfernt einen Eintrag von der Schadens- und Verlustliste der Freizeit (Leitung und Team). Ohne Phasenprüfung (AD-17).</summary>
internal sealed record SchadenEntfernenCommand(Guid FreizeitId, Guid EintragId) : ICommand<bool>, IModulCommand<InventarDbContext>, IBenutzerCommand;

internal sealed class SchadenEntfernenHandler(InventarDbContext db) : ICommandHandler<SchadenEntfernenCommand, bool>
{
    public async ValueTask<bool> Handle(SchadenEntfernenCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre: Ein gleichzeitiges zweites Entfernen wartet und findet danach nichts mehr (404 statt Fehler).
        if (await db.SchadenSperrenAsync(command.FreizeitId, command.EintragId, cancellationToken) is not { } eintrag)
        {
            return false;
        }

        db.Schaeden.Remove(eintrag);
        return true;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/inventar/schaeden/{eintragId}</c> (Leitung und Team): <c>204</c>; unbekannt oder aus einer anderen
/// Freizeit <c>404</c>.
/// </summary>
internal static class SchadenEntfernenEndpunkt
{
    public static async Task<IResult> Entfernen(Guid freizeitId, Guid eintragId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new SchadenEntfernenCommand(freizeitId, eintragId), abbruch) ? TypedResults.NoContent() : TypedResults.NotFound();
}
