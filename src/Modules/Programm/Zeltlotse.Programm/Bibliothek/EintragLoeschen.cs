using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Bibliothek;

/// <summary>
/// Story 3-6: Entfernt einen Eintrag aus der Programmbibliothek (Org-Admin). Programmpunkte, die als dieser Eintrag abgelegt wurden, bleiben; ihre
/// Verknüpfung wird leer (Fremdschlüssel <c>ON DELETE SET NULL</c>), danach lassen sie sich erneut ablegen.
/// </summary>
internal sealed record EintragLoeschenCommand(Guid EintragId) : ICommand<bool>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

internal sealed class EintragLoeschenHandler(ProgrammDbContext db) : ICommandHandler<EintragLoeschenCommand, bool>
{
    /// <summary><c>true</c>, wenn entfernt; <c>false</c>, wenn es den Eintrag in der Organisation nicht gibt.</summary>
    public async ValueTask<bool> Handle(EintragLoeschenCommand command, CancellationToken cancellationToken)
    {
        var eintrag = await db.EintragSperrenAsync(command.EintragId, cancellationToken);
        if (eintrag is null)
        {
            return false;
        }

        db.Bibliothek.Remove(eintrag);
        return true;
    }
}

/// <summary>
/// <c>DELETE /api/organisationen/{orgId}/programmbibliothek/{eintragId}</c> (Org-Admin): <c>204</c>; unbekannt oder aus einer anderen Organisation
/// <c>404</c>.
/// </summary>
internal static class EintragLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid eintragId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EintragLoeschenCommand(eintragId), abbruch) ? TypedResults.NoContent() : TypedResults.NotFound();
}
