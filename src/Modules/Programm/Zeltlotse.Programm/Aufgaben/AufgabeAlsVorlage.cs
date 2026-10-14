using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5, CAP-23: „Als Vorlage speichern“ (Leitung und Team): legt eine Aufgaben-Vorlage mit Titel und Phase der Aufgabe in der
/// Organisation der Freizeit an und verknüpft sie. Ist die Aufgabe schon mit einer (noch vorhandenen) Vorlage verknüpft, entsteht nichts Neues;
/// die Zeilensperre an der Aufgabe lässt einen Doppeltipp nacheinander laufen. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record AufgabeAlsVorlageCommand(Guid FreizeitId, Guid AufgabeId)
    : ICommand<AufgabeUebergangErgebnis>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

internal sealed class AufgabeAlsVorlageHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<AufgabeAlsVorlageCommand, AufgabeUebergangErgebnis>
{
    public async ValueTask<AufgabeUebergangErgebnis> Handle(AufgabeAlsVorlageCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new AufgabeUebergangErgebnis.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new AufgabeUebergangErgebnis.Abgeschlossen();
        }

        var aufgabe = await db.AufgabeSperrenAsync(command.FreizeitId, command.AufgabeId, cancellationToken);
        if (aufgabe is null)
        {
            return new AufgabeUebergangErgebnis.NichtGefunden();
        }

        // Die Verknüpfung zeigt immer auf eine vorhandene Vorlage: Wird sie gelöscht, leert der Fremdschlüssel sie (ON DELETE SET NULL).
        if (aufgabe.VorlageId is null)
        {
            var vorlage = AufgabenVorlage.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), aufgabe.OrganisationId, aufgabe.Titel, aufgabe.Phase);
            db.AufgabenVorlagen.Add(vorlage);
            aufgabe.VorlageMerken(vorlage.Id);
        }

        return new AufgabeUebergangErgebnis.Erfolg(aufgabe);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/aufgaben/{aufgabeId}/vorlage</c> (Leitung und Team, FR-40): <c>200</c> mit der Aufgabe
/// (<c>vorlage = true</c>), auch wiederholt ohne zweite Vorlage; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannte Aufgabe
/// oder Aufgabe einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class AufgabeAlsVorlageEndpunkt
{
    public static async Task<IResult> Speichern(Guid freizeitId, Guid aufgabeId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new AufgabeAlsVorlageCommand(freizeitId, aufgabeId), abbruch) switch
        {
            AufgabeUebergangErgebnis.Erfolg erfolg => TypedResults.Ok(AufgabenLesen.AlsApi(erfolg.Aufgabe, angemeldet.PersonId)),
            AufgabeUebergangErgebnis.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
