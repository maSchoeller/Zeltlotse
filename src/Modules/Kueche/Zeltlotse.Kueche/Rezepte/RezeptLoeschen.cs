using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// Story 4-1: Löscht ein Rezept samt Zutaten (Leitung und Team, Modul Verpflegung; die Datenbank nimmt die Zutaten per <c>ON DELETE CASCADE</c> mit).
/// Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record RezeptLoeschenCommand(Guid FreizeitId, Guid RezeptId)
    : ICommand<KuecheLoeschen>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="RezeptLoeschenCommand"/> und <see cref="ZutatLoeschenCommand"/>.</summary>
internal enum KuecheLoeschen
{
    Geloescht,
    Abgeschlossen,
    NichtGefunden,
}

internal sealed class RezeptLoeschenHandler(KuecheDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<RezeptLoeschenCommand, KuecheLoeschen>
{
    public async ValueTask<KuecheLoeschen> Handle(RezeptLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return KuecheLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return KuecheLoeschen.Abgeschlossen;
        }

        // Zeilensperre: Ein gleichzeitiges Anlegen einer Zutat wartet und findet das Rezept danach nicht mehr (404 statt Fremdschlüssel-Fehler).
        var rezept = await db.RezeptSperrenAsync(command.FreizeitId, command.RezeptId, cancellationToken);
        if (rezept is null)
        {
            return KuecheLoeschen.NichtGefunden;
        }

        db.Rezepte.Remove(rezept);
        return KuecheLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/kueche/rezepte/{rezeptId}</c> (Leitung und Team, Modul Verpflegung): <c>204</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class RezeptLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid rezeptId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new RezeptLoeschenCommand(freizeitId, rezeptId), abbruch) switch
        {
            KuecheLoeschen.Geloescht => TypedResults.NoContent(),
            KuecheLoeschen.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
