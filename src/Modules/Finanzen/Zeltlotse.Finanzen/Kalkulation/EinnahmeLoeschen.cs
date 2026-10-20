using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>Story 5-1: Ergebnis eines Löschens in den Finanzen.</summary>
internal enum FinanzenLoeschen
{
    Geloescht,
    Abgeschlossen,
    NichtGefunden,
}

/// <summary>Story 5-1: Entfernt einen Einnahmeposten (Leitung und Team, Modul Finanzen). Eine abgeschlossene Freizeit ist nur lesbar (AD-17).</summary>
internal sealed record EinnahmeLoeschenCommand(Guid FreizeitId, Guid EinnahmeId)
    : ICommand<FinanzenLoeschen>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

internal sealed class EinnahmeLoeschenHandler(FinanzenDbContext db, IFreizeitZeitpunkte zeitpunkte) : ICommandHandler<EinnahmeLoeschenCommand, FinanzenLoeschen>
{
    public async ValueTask<FinanzenLoeschen> Handle(EinnahmeLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return FinanzenLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return FinanzenLoeschen.Abgeschlossen;
        }

        var posten = await db.EinnahmeSperrenAsync(command.FreizeitId, command.EinnahmeId, cancellationToken);
        if (posten is null)
        {
            return FinanzenLoeschen.NichtGefunden;
        }

        db.Einnahmeposten.Remove(posten);
        return FinanzenLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/finanzen/kalkulation/einnahmen/{einnahmeId}</c> (Leitung und Team, Modul Finanzen): <c>204</c>; abgeschlossene
/// Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class EinnahmeLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid einnahmeId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinnahmeLoeschenCommand(freizeitId, einnahmeId), abbruch) switch
        {
            FinanzenLoeschen.Geloescht => TypedResults.NoContent(),
            FinanzenLoeschen.Abgeschlossen => FinanzenProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
