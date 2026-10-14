using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5: Löscht eine Aufgaben-Vorlage (Org-Admin). Aufgaben, die aus ihr stammen oder als Vorlage gespeichert wurden, bleiben; ihre
/// Verknüpfung wird leer (Fremdschlüssel <c>ON DELETE SET NULL</c>), danach lässt sich die Aufgabe erneut als Vorlage speichern.
/// </summary>
internal sealed record VorlageLoeschenCommand(Guid VorlageId) : ICommand<bool>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

internal sealed class VorlageLoeschenHandler(ProgrammDbContext db) : ICommandHandler<VorlageLoeschenCommand, bool>
{
    /// <summary><c>true</c>, wenn gelöscht; <c>false</c>, wenn es die Vorlage in der Organisation nicht gibt.</summary>
    public async ValueTask<bool> Handle(VorlageLoeschenCommand command, CancellationToken cancellationToken)
    {
        var vorlage = await db.VorlageSperrenAsync(command.VorlageId, cancellationToken);
        if (vorlage is null)
        {
            return false;
        }

        db.AufgabenVorlagen.Remove(vorlage);
        return true;
    }
}

/// <summary>
/// <c>DELETE /api/organisationen/{orgId}/aufgaben-vorlagen/{vorlageId}</c> (Org-Admin): <c>204</c>; unbekannt oder aus einer anderen
/// Organisation <c>404</c>.
/// </summary>
internal static class VorlageLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid vorlageId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new VorlageLoeschenCommand(vorlageId), abbruch) ? TypedResults.NoContent() : TypedResults.NotFound();
}
