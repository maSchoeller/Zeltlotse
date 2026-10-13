using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Materialien;

/// <summary>Story 3-2: Löscht ein Material (Leitung und Team). Eine abgeschlossene Freizeit ist nur lesbar (AD-17).</summary>
internal sealed record MaterialLoeschenCommand(Guid FreizeitId, Guid MaterialId)
    : ICommand<MaterialLoeschen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="MaterialLoeschenCommand"/>.</summary>
internal enum MaterialLoeschen
{
    Geloescht,
    Abgeschlossen,
    NichtGefunden,
}

internal sealed class MaterialLoeschenHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<MaterialLoeschenCommand, MaterialLoeschen>
{
    public async ValueTask<MaterialLoeschen> Handle(MaterialLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return MaterialLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return MaterialLoeschen.Abgeschlossen;
        }

        var material = await db.MaterialSperrenAsync(command.FreizeitId, command.MaterialId, cancellationToken);
        if (material is null)
        {
            return MaterialLoeschen.NichtGefunden;
        }

        db.Material.Remove(material);
        return MaterialLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/programm/material/{materialId}</c> (Leitung und Team): <c>204</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; unbekanntes Material oder Material einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class MaterialLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid materialId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new MaterialLoeschenCommand(freizeitId, materialId), abbruch) switch
        {
            MaterialLoeschen.Geloescht => TypedResults.NoContent(),
            MaterialLoeschen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
