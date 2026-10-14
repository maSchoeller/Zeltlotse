using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Bibliothek;

/// <summary>
/// Story 3-6, CAP-24: „In Bibliothek ablegen“ (Leitung und Team): legt einen Eintrag der Programmbibliothek in der Organisation der Freizeit an
/// (Titel, Material, Aufgaben am Punkt) und verknüpft den Punkt. Ist der Punkt schon mit einem (noch vorhandenen) Eintrag verknüpft, entsteht
/// nichts Neues; die Zeilensperre am Punkt lässt einen Doppeltipp nacheinander laufen. Mahlzeiten lassen sich nicht ablegen. Eine
/// abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ProgrammpunktAblegenCommand(Guid FreizeitId, Guid PunktId)
    : ICommand<ProgrammpunktAblegen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="ProgrammpunktAblegenCommand"/>; bei <see cref="Abgelegt"/> der verknüpfte Punkt.</summary>
internal abstract record ProgrammpunktAblegen
{
    private ProgrammpunktAblegen()
    {
    }

    public sealed record Abgelegt(Programmpunkt Punkt) : ProgrammpunktAblegen;

    /// <summary>Feste Mahlzeiten gehören zu jeder Freizeit und kommen nicht in die Bibliothek.</summary>
    public sealed record Mahlzeit : ProgrammpunktAblegen;

    public sealed record Abgeschlossen : ProgrammpunktAblegen;

    public sealed record NichtGefunden : ProgrammpunktAblegen;
}

internal sealed class ProgrammpunktAblegenHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<ProgrammpunktAblegenCommand, ProgrammpunktAblegen>
{
    public async ValueTask<ProgrammpunktAblegen> Handle(ProgrammpunktAblegenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new ProgrammpunktAblegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new ProgrammpunktAblegen.Abgeschlossen();
        }

        var punkt = await db.ProgrammpunktSperrenAsync(command.FreizeitId, command.PunktId, cancellationToken);
        if (punkt is null)
        {
            return new ProgrammpunktAblegen.NichtGefunden();
        }

        if (punkt.Mahlzeit is not null)
        {
            return new ProgrammpunktAblegen.Mahlzeit();
        }

        // Die Verknüpfung zeigt immer auf einen vorhandenen Eintrag: Wird er entfernt, leert der Fremdschlüssel sie (ON DELETE SET NULL).
        if (punkt.BibliotheksEintragId is null)
        {
            var material = await db.Material.Where(m => m.ProgrammpunktId == punkt.Id).ToListAsync(cancellationToken);
            var aufgaben = await db.Aufgaben.Where(a => a.ProgrammpunktId == punkt.Id).ToListAsync(cancellationToken);
            var jetzt = uhr.GetUtcNow();
            var eintrag = BibliotheksEintrag.Ablegen(Guid.CreateVersion7(jetzt), punkt.OrganisationId, punkt, material, aufgaben, jetzt);
            db.Bibliothek.Add(eintrag);
            punkt.BibliothekMerken(eintrag.Id);
        }

        return new ProgrammpunktAblegen.Abgelegt(punkt);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/punkte/{punktId}/bibliothek</c> (Leitung und Team, FR-41): <c>200</c> mit dem Programmpunkt
/// (<c>inBibliothek = true</c>), auch wiederholt ohne zweiten Eintrag; Mahlzeit <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; unbekannter Punkt oder Punkt einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class ProgrammpunktAblegenEndpunkt
{
    public static async Task<IResult> Ablegen(Guid freizeitId, Guid punktId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new ProgrammpunktAblegenCommand(freizeitId, punktId), abbruch) switch
        {
            ProgrammpunktAblegen.Abgelegt abgelegt => TypedResults.Ok(ProgrammLesen.AlsApi(abgelegt.Punkt, angemeldet.PersonId)),
            ProgrammpunktAblegen.Mahlzeit => ProgrammProbleme.Konflikt("Mahlzeiten lassen sich nicht in der Programmbibliothek ablegen."),
            ProgrammpunktAblegen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
