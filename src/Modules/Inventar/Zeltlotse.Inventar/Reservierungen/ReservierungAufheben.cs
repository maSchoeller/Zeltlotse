using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Inventar.Reservierungen;

/// <summary>
/// Story 3-3: Hebt eine Reservierung der Freizeit auf (Leitung und Team). Hängt ein Material daran, geht das Ereignis „aufgehoben“ in die Outbox,
/// bevor die Zeile verschwindet. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ReservierungAufhebenCommand(Guid FreizeitId, Guid ReservierungId)
    : ICommand<ReservierungAufheben>, IModulCommand<InventarDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="ReservierungAufhebenCommand"/>.</summary>
internal enum ReservierungAufheben
{
    Aufgehoben,
    Abgeschlossen,
    NichtGefunden,
}

internal sealed class ReservierungAufhebenHandler(InventarDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<ReservierungAufhebenCommand, ReservierungAufheben>
{
    public async ValueTask<ReservierungAufheben> Handle(ReservierungAufhebenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return ReservierungAufheben.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return ReservierungAufheben.Abgeschlossen;
        }

        // Zeilensperre: Ein gleichzeitiges zweites Aufheben wartet und findet danach nichts mehr (404 statt Fehler).
        var reservierung = await db.ReservierungSperrenAsync(command.FreizeitId, command.ReservierungId, cancellationToken);
        if (reservierung is null)
        {
            return ReservierungAufheben.NichtGefunden;
        }

        reservierung.Aufheben();
        db.Reservierungen.Remove(reservierung);
        return ReservierungAufheben.Aufgehoben;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/inventar/reservierungen/{reservierungId}</c> (Leitung und Team): <c>204</c>; unbekannt oder aus einer
/// anderen Freizeit <c>404</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class ReservierungAufhebenEndpunkt
{
    public static async Task<IResult> Aufheben(Guid freizeitId, Guid reservierungId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new ReservierungAufhebenCommand(freizeitId, reservierungId), abbruch) switch
        {
            ReservierungAufheben.Aufgehoben => TypedResults.NoContent(),
            ReservierungAufheben.Abgeschlossen => InventarProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
