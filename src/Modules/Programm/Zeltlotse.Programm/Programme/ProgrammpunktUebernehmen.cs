using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Programme;

/// <summary>Story 3-1: „Ich übernehme“ (FR-35) oder „Abgeben“; die Person ist immer die angemeldete, die Leitung teilt nichts zu.</summary>
internal enum Uebergang
{
    Uebernehmen,
    Abgeben,
}

/// <summary>
/// Story 3-1: Statuswechsel der Übernahme eines Programmpunkts (AD-28): übernehmen oder abgeben, nur für sich selbst. Wiederholt ist beides
/// unschädlich; hat eine andere Person den Punkt, ist das ein Konflikt. Die Zeilensperre lässt zwei gleichzeitige Übernahmen nacheinander laufen:
/// genau eine gewinnt. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ProgrammpunktUebergangCommand(Guid FreizeitId, Guid PunktId, Guid PersonId, Uebergang Uebergang)
    : ICommand<ProgrammpunktUebergang>, IModulCommand<ProgrammDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(ProgrammpunktUebergangCommand)}({FreizeitId}, {PunktId}, {Uebergang})";
}

/// <summary>Ergebnis eines Übergangs; bei <see cref="Erfolg"/> der verfolgte Punkt (nach dem Speichern mit neuer Version).</summary>
internal abstract record ProgrammpunktUebergang
{
    private ProgrammpunktUebergang()
    {
    }

    public sealed record Erfolg(Programmpunkt Punkt) : ProgrammpunktUebergang;

    /// <summary>Eine andere Person hat den Punkt übernommen.</summary>
    public sealed record Konflikt : ProgrammpunktUebergang;

    public sealed record Abgeschlossen : ProgrammpunktUebergang;

    public sealed record NichtGefunden : ProgrammpunktUebergang;
}

internal sealed class ProgrammpunktUebergangHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<ProgrammpunktUebergangCommand, ProgrammpunktUebergang>
{
    public async ValueTask<ProgrammpunktUebergang> Handle(ProgrammpunktUebergangCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new ProgrammpunktUebergang.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new ProgrammpunktUebergang.Abgeschlossen();
        }

        var punkt = await db.ProgrammpunktSperrenAsync(command.FreizeitId, command.PunktId, cancellationToken);
        if (punkt is null)
        {
            return new ProgrammpunktUebergang.NichtGefunden();
        }

        var erlaubt = command.Uebergang == Uebergang.Uebernehmen ? punkt.Uebernehmen(command.PersonId) : punkt.Abgeben(command.PersonId);
        return erlaubt ? new ProgrammpunktUebergang.Erfolg(punkt) : new ProgrammpunktUebergang.Konflikt();
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/punkte/{punktId}/uebernehmen</c> und <c>…/abgeben</c> (Leitung und Team, FR-35): <c>200</c>
/// mit dem Programmpunkt, auch wiederholt; von einer anderen Person übernommen <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; unbekannter Punkt oder Punkt einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class ProgrammpunktUebergangEndpunkt
{
    public static Task<IResult> Uebernehmen(Guid freizeitId, Guid punktId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, punktId, Uebergang.Uebernehmen, angemeldet, sender, abbruch);

    public static Task<IResult> Abgeben(Guid freizeitId, Guid punktId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, punktId, Uebergang.Abgeben, angemeldet, sender, abbruch);

    private static async Task<IResult> AusfuehrenAsync(
        Guid freizeitId, Guid punktId, Uebergang uebergang, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var ich = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        return await sender.Send(new ProgrammpunktUebergangCommand(freizeitId, punktId, ich, uebergang), abbruch) switch
        {
            ProgrammpunktUebergang.Erfolg erfolg => TypedResults.Ok(ProgrammLesen.AlsApi(erfolg.Punkt, ich)),
            ProgrammpunktUebergang.Konflikt => ProgrammProbleme.Konflikt("Jemand anderes hat diesen Programmpunkt übernommen."),
            ProgrammpunktUebergang.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
