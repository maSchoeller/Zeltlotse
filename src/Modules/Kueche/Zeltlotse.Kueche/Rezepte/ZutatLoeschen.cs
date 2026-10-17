using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>Story 4-1: Entfernt eine Zutat aus einem Rezept (Leitung und Team, Modul Verpflegung). Eine abgeschlossene Freizeit ist nur lesbar (AD-17).</summary>
internal sealed record ZutatLoeschenCommand(Guid FreizeitId, Guid RezeptId, Guid ZutatId)
    : ICommand<KuecheLoeschen>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

internal sealed class ZutatLoeschenHandler(KuecheDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<ZutatLoeschenCommand, KuecheLoeschen>
{
    public async ValueTask<KuecheLoeschen> Handle(ZutatLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return KuecheLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return KuecheLoeschen.Abgeschlossen;
        }

        // Zeilensperre: Ein gleichzeitiges Löschen des Rezepts wartet bzw. lässt hier nichts mehr übrig (dann 404).
        var zutat = await db.ZutatSperrenAsync(command.FreizeitId, command.RezeptId, command.ZutatId, cancellationToken);
        if (zutat is null)
        {
            return KuecheLoeschen.NichtGefunden;
        }

        db.Zutaten.Remove(zutat);
        return KuecheLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/kueche/rezepte/{rezeptId}/zutaten/{zutatId}</c> (Leitung und Team, Modul Verpflegung): <c>204</c>;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannt, aus einem anderen Rezept oder einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class ZutatLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid rezeptId, Guid zutatId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new ZutatLoeschenCommand(freizeitId, rezeptId, zutatId), abbruch) switch
        {
            KuecheLoeschen.Geloescht => TypedResults.NoContent(),
            KuecheLoeschen.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
