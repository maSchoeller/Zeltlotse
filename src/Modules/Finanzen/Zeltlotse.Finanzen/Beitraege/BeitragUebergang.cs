using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Beitraege;

/// <summary>Story 5-3: Statuswechsel des Beitrags einer teilnehmenden Person (AD-19, AD-28: POST statt PATCH).</summary>
internal enum BeitragUebergang
{
    /// <summary>Haken setzen: legt den Vermerk an, falls noch keiner da ist.</summary>
    Bezahlt,

    /// <summary>Haken entfernen: löscht den Vermerk endgültig, falls einer da ist.</summary>
    Offen,
}

/// <summary>
/// Story 5-3 (FR-60): Setzt oder entfernt den Haken „Beitrag bezahlt“ einer bestätigten teilnehmenden Person (Leitung und Team, Modul Finanzen). Beides ist
/// wiederholt unschädlich: Ein zweiter Haken lässt den ersten Vermerk mit „abgehakt von“ und Zeitpunkt unverändert, zwei gleichzeitige Haken ergeben genau
/// einen Vermerk. <see cref="AbgehaktVon"/> ist die angemeldete Person. Kein <c>[ErlaubtNachAbschluss]</c>: AD-17 nennt den Vermerk nicht, eine abgeschlossene
/// Freizeit ist darum nur lesbar.
/// </summary>
internal sealed record BeitragUebergangCommand(Guid FreizeitId, Guid PersonId, Guid AbgehaktVon, BeitragUebergang Uebergang)
    : ICommand<BeitragErgebnis>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;

    /// <summary>Keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(BeitragUebergangCommand)}({FreizeitId}, {Uebergang})";
}

/// <summary>Story 5-3: Ergebnis eines Übergangs.</summary>
internal abstract record BeitragErgebnis
{
    private BeitragErgebnis()
    {
    }

    /// <summary>Der Stand danach.</summary>
    public sealed record Erfolg(BeitragZeile Zeile) : BeitragErgebnis;

    /// <summary>Die Freizeit ist abgeschlossen und nur lesbar: <c>409 freizeit-abgeschlossen</c> (AD-17).</summary>
    public sealed record Abgeschlossen : BeitragErgebnis;

    /// <summary>Freizeit unbekannt oder die Person ist keine bestätigte Teilnehmende: <c>404</c>.</summary>
    public sealed record NichtGefunden : BeitragErgebnis;
}

internal sealed class BeitragUebergangHandler(
    FinanzenDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitTeilnehmendeLesen teilnehmende, TimeProvider uhr)
    : ICommandHandler<BeitragUebergangCommand, BeitragErgebnis>
{
    public async ValueTask<BeitragErgebnis> Handle(BeitragUebergangCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new BeitragErgebnis.NichtGefunden();
        }

        // Die zweite Prüfung des Schreibschutzes (AD-17; die Pipeline prüft zuerst).
        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new BeitragErgebnis.Abgeschlossen();
        }

        if (!(await teilnehmende.TeilnehmendeAsync(command.FreizeitId, cancellationToken)).Contains(command.PersonId))
        {
            return new BeitragErgebnis.NichtGefunden();
        }

        switch (command.Uebergang)
        {
            case BeitragUebergang.Bezahlt:
                var vermerk = await db.BeitragVermerkSichernAsync(
                    Kennung.Neu(uhr), organisationId, command.FreizeitId, command.PersonId, command.AbgehaktVon, uhr.GetUtcNow(), cancellationToken);
                return new BeitragErgebnis.Erfolg(BeitragSchritte.AlsApi(vermerk));
            case BeitragUebergang.Offen:
                await db.BeitragVermerke.Where(v => v.FreizeitId == command.FreizeitId && v.PersonId == command.PersonId).ExecuteDeleteAsync(cancellationToken);
                return new BeitragErgebnis.Erfolg(BeitragSchritte.Offen(command.PersonId));
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Uebergang, null);
        }
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/finanzen/beitraege/{personId}/bezahlt</c> und <c>…/offen</c> (Leitung und Team, Modul Finanzen, FR-60): <c>200</c>
/// mit der <see cref="BeitragZeile"/>, auch wiederholt; die Person ist keine bestätigte Teilnehmende der Freizeit <c>404</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class BeitragUebergangEndpunkt
{
    public static Task<IResult> Bezahlt(Guid freizeitId, Guid personId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, personId, BeitragUebergang.Bezahlt, angemeldet, sender, abbruch);

    public static Task<IResult> Offen(Guid freizeitId, Guid personId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, personId, BeitragUebergang.Offen, angemeldet, sender, abbruch);

    private static async Task<IResult> AusfuehrenAsync(
        Guid freizeitId, Guid personId, BeitragUebergang uebergang, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var ich = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        return await sender.Send(new BeitragUebergangCommand(freizeitId, personId, ich, uebergang), abbruch) switch
        {
            BeitragErgebnis.Erfolg erfolg => TypedResults.Ok(erfolg.Zeile),
            BeitragErgebnis.Abgeschlossen => FinanzenProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
