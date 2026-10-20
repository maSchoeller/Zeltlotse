using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// Story 5-2: Löscht einen Beleg samt seiner Datei im Dateispeicher (Leitung und Team, Modul Finanzen; Inline-Bestätigung in der Oberfläche). Eine
/// abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record BelegLoeschenCommand(Guid FreizeitId, Guid BelegId)
    : ICommand<BelegAenderung>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

internal sealed class BelegLoeschenHandler(FinanzenDbContext db, IFreizeitZeitpunkte zeitpunkte, IDateiSpeicher speicher)
    : ICommandHandler<BelegLoeschenCommand, BelegAenderung>
{
    public async ValueTask<BelegAenderung> Handle(BelegLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (await BelegSchritte.FreizeitPruefenAsync(zeitpunkte, command.FreizeitId, cancellationToken) is { } abbruch)
        {
            return abbruch;
        }

        var beleg = await db.BelegSperrenAsync(command.FreizeitId, command.BelegId, cancellationToken);
        if (beleg is null)
        {
            return new BelegAenderung.NichtGefunden();
        }

        db.Belege.Remove(beleg);
        if (beleg.DateiArt is not null)
        {
            await speicher.LoeschenAsync(Beleg.DateiPraefix, beleg.Id, cancellationToken);
        }

        return new BelegAenderung.Geloescht();
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/finanzen/belege/{belegId}</c> (Leitung und Team, Modul Finanzen): <c>204</c>, auch die Datei ist weg;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class BelegLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid belegId, ISender sender, CancellationToken abbruch) =>
        BelegSchritte.Antwort(await sender.Send(new BelegLoeschenCommand(freizeitId, belegId), abbruch), _ => TypedResults.NoContent());
}
