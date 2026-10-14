using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>Story 3-5: Löscht eine Aufgabe (Leitung und Team). Eine abgeschlossene Freizeit ist nur lesbar (AD-17).</summary>
internal sealed record AufgabeLoeschenCommand(Guid FreizeitId, Guid AufgabeId)
    : ICommand<AufgabeLoeschen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="AufgabeLoeschenCommand"/>.</summary>
internal enum AufgabeLoeschen
{
    Geloescht,
    Abgeschlossen,
    NichtGefunden,
}

internal sealed class AufgabeLoeschenHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<AufgabeLoeschenCommand, AufgabeLoeschen>
{
    public async ValueTask<AufgabeLoeschen> Handle(AufgabeLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return AufgabeLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return AufgabeLoeschen.Abgeschlossen;
        }

        var aufgabe = await db.AufgabeSperrenAsync(command.FreizeitId, command.AufgabeId, cancellationToken);
        if (aufgabe is null)
        {
            return AufgabeLoeschen.NichtGefunden;
        }

        db.Aufgaben.Remove(aufgabe);
        return AufgabeLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/programm/aufgaben/{aufgabeId}</c> (Leitung und Team): <c>204</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; unbekannte Aufgabe oder Aufgabe einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class AufgabeLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid aufgabeId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new AufgabeLoeschenCommand(freizeitId, aufgabeId), abbruch) switch
        {
            AufgabeLoeschen.Geloescht => TypedResults.NoContent(),
            AufgabeLoeschen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
