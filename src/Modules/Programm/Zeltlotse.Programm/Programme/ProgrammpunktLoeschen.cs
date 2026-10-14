using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 3-1: Löscht einen Programmpunkt (Leitung und Team). Feste Mahlzeiten lassen sich nicht löschen (FR-34). Eine abgeschlossene Freizeit ist
/// nur lesbar (AD-17).
/// </summary>
internal sealed record ProgrammpunktLoeschenCommand(Guid FreizeitId, Guid PunktId)
    : ICommand<ProgrammpunktLoeschen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis von <see cref="ProgrammpunktLoeschenCommand"/>.</summary>
internal enum ProgrammpunktLoeschen
{
    Geloescht,

    /// <summary>Eine Mahlzeit bleibt.</summary>
    Mahlzeit,

    Abgeschlossen,

    NichtGefunden,
}

internal sealed class ProgrammpunktLoeschenHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<ProgrammpunktLoeschenCommand, ProgrammpunktLoeschen>
{
    public async ValueTask<ProgrammpunktLoeschen> Handle(ProgrammpunktLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return ProgrammpunktLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return ProgrammpunktLoeschen.Abgeschlossen;
        }

        var punkt = await db.ProgrammpunktSperrenAsync(command.FreizeitId, command.PunktId, cancellationToken);
        if (punkt is null)
        {
            return ProgrammpunktLoeschen.NichtGefunden;
        }

        if (!punkt.Loeschbar)
        {
            return ProgrammpunktLoeschen.Mahlzeit;
        }

        db.Programmpunkte.Remove(punkt);
        return ProgrammpunktLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/programm/punkte/{punktId}</c> (Leitung und Team): <c>204</c>; Mahlzeit <c>409 konflikt</c>;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannter Punkt oder Punkt einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class ProgrammpunktLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid punktId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new ProgrammpunktLoeschenCommand(freizeitId, punktId), abbruch) switch
        {
            ProgrammpunktLoeschen.Geloescht => TypedResults.NoContent(),
            ProgrammpunktLoeschen.Mahlzeit => ProgrammProbleme.Konflikt("Mahlzeiten lassen sich nicht löschen."),
            ProgrammpunktLoeschen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
