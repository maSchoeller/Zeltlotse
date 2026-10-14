using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>Story 3-5: Statuswechsel einer Aufgabe: übernehmen und abgeben nur für sich selbst, abhaken und wieder öffnen für jede Person.</summary>
internal enum AufgabeUebergang
{
    Uebernehmen,
    Abgeben,
    Erledigen,
    Oeffnen,
}

/// <summary>
/// Story 3-5: Statuswechsel einer Aufgabe (AD-28). Übernehmen und Abgeben wie am Programmpunkt: nur für sich selbst, wiederholt unschädlich, hat
/// eine andere Person die Aufgabe, ist das ein Konflikt; die Zeilensperre lässt zwei gleichzeitige Übernahmen nacheinander laufen. Erledigt und
/// Wieder offen darf jede Person der Freizeit, auch wiederholt; die Übernahme bleibt. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record AufgabeUebergangCommand(Guid FreizeitId, Guid AufgabeId, Guid PersonId, AufgabeUebergang Uebergang)
    : ICommand<AufgabeUebergangErgebnis>, IModulCommand<ProgrammDbContext>, IBenutzerCommand
{
    /// <summary>Keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(AufgabeUebergangCommand)}({FreizeitId}, {AufgabeId}, {Uebergang})";
}

/// <summary>Ergebnis eines Übergangs; bei <see cref="Erfolg"/> die verfolgte Aufgabe (nach dem Speichern mit neuer Version).</summary>
internal abstract record AufgabeUebergangErgebnis
{
    private AufgabeUebergangErgebnis()
    {
    }

    public sealed record Erfolg(Aufgabe Aufgabe) : AufgabeUebergangErgebnis;

    /// <summary>Eine andere Person hat die Aufgabe übernommen.</summary>
    public sealed record Konflikt : AufgabeUebergangErgebnis;

    public sealed record Abgeschlossen : AufgabeUebergangErgebnis;

    public sealed record NichtGefunden : AufgabeUebergangErgebnis;
}

internal sealed class AufgabeUebergangHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<AufgabeUebergangCommand, AufgabeUebergangErgebnis>
{
    public async ValueTask<AufgabeUebergangErgebnis> Handle(AufgabeUebergangCommand command, CancellationToken cancellationToken)
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

        var erlaubt = true;
        switch (command.Uebergang)
        {
            case AufgabeUebergang.Uebernehmen:
                erlaubt = aufgabe.Uebernehmen(command.PersonId);
                break;
            case AufgabeUebergang.Abgeben:
                erlaubt = aufgabe.Abgeben(command.PersonId);
                break;
            case AufgabeUebergang.Erledigen:
                aufgabe.Erledigen();
                break;
            case AufgabeUebergang.Oeffnen:
                aufgabe.Oeffnen();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Uebergang, null);
        }

        return erlaubt ? new AufgabeUebergangErgebnis.Erfolg(aufgabe) : new AufgabeUebergangErgebnis.Konflikt();
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/aufgaben/{aufgabeId}/uebernehmen</c>, <c>…/abgeben</c>, <c>…/erledigt</c> und <c>…/offen</c>
/// (Leitung und Team, FR-39): <c>200</c> mit der Aufgabe, auch wiederholt; übernehmen oder abgeben, während eine andere Person die Aufgabe hat,
/// <c>409 konflikt</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannte Aufgabe oder Aufgabe einer anderen Freizeit
/// <c>404</c>.
/// </summary>
internal static class AufgabeUebergangEndpunkt
{
    public static Task<IResult> Uebernehmen(Guid freizeitId, Guid aufgabeId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, aufgabeId, AufgabeUebergang.Uebernehmen, angemeldet, sender, abbruch);

    public static Task<IResult> Abgeben(Guid freizeitId, Guid aufgabeId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, aufgabeId, AufgabeUebergang.Abgeben, angemeldet, sender, abbruch);

    public static Task<IResult> Erledigt(Guid freizeitId, Guid aufgabeId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, aufgabeId, AufgabeUebergang.Erledigen, angemeldet, sender, abbruch);

    public static Task<IResult> Offen(Guid freizeitId, Guid aufgabeId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, aufgabeId, AufgabeUebergang.Oeffnen, angemeldet, sender, abbruch);

    private static async Task<IResult> AusfuehrenAsync(
        Guid freizeitId, Guid aufgabeId, AufgabeUebergang uebergang, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var ich = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        return await sender.Send(new AufgabeUebergangCommand(freizeitId, aufgabeId, ich, uebergang), abbruch) switch
        {
            AufgabeUebergangErgebnis.Erfolg erfolg => TypedResults.Ok(AufgabenLesen.AlsApi(erfolg.Aufgabe, ich)),
            AufgabeUebergangErgebnis.Konflikt => ProgrammProbleme.Konflikt("Jemand anderes hat diese Aufgabe übernommen."),
            AufgabeUebergangErgebnis.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
