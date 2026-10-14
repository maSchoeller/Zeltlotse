using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Freizeiten.Abschluss;

/// <summary>
/// Story 3-8: Die Leitung schließt die Freizeit ab (AD-17, AD-28, CAP-27): nur in der Nachbereitung. Danach ist sie gelaufen, das Ereignis
/// <c>FreizeitAbgeschlossen</c> geht über die Outbox (Sicherheit archiviert die Vorfall-Notizen), und die Pipeline lehnt jeden weiteren
/// Benutzer-Command der Freizeit ab; ein zweiter Abschluss endet deshalb schon dort mit <c>409 freizeit-abgeschlossen</c>. Offene Aufgaben
/// halten den Abschluss nicht auf; die Warnung steht nur in der Oberfläche.
/// </summary>
internal sealed record FreizeitAbschliessenCommand(Guid FreizeitId)
    : ICommand<FreizeitAbschluss>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand;

internal enum FreizeitAbschluss
{
    Abgeschlossen,
    NichtGefunden,

    /// <summary>In Vorbereitung oder läuft noch: <c>400</c> an <c>phase</c>.</summary>
    ZuFrueh,

    /// <summary>Schon gelaufen (zweite Absicherung neben der Pipeline): <c>409 freizeit-abgeschlossen</c>.</summary>
    SchonAbgeschlossen,
}

internal sealed class FreizeitAbschliessenHandler(FreizeitenDbContext db, TimeProvider uhr) : ICommandHandler<FreizeitAbschliessenCommand, FreizeitAbschluss>
{
    public async ValueTask<FreizeitAbschluss> Handle(FreizeitAbschliessenCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE) unter dem Mandantenfilter: Zwei gleichzeitige Abschlüsse laufen nacheinander, nur einer legt das Ereignis ab.
        var freizeit = (await db.Freizeiten
                .FromSql($"SELECT *, xmin FROM freizeiten.freizeit WHERE \"Id\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (freizeit is null)
        {
            return FreizeitAbschluss.NichtGefunden;
        }

        var jetzt = uhr.GetUtcNow();
        if (freizeit.Abschliessen(jetzt))
        {
            return FreizeitAbschluss.Abgeschlossen;
        }

        return freizeit.Phase(jetzt) == FreizeitPhase.Gelaufen ? FreizeitAbschluss.SchonAbgeschlossen : FreizeitAbschluss.ZuFrueh;
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/abschliessen</c> (nur Leitung, Story 3-8): <c>204</c>; vor dem Freizeitende <c>400</c> an
/// <c>phase</c>; schon abgeschlossen <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.
/// </summary>
internal static class FreizeitAbschliessenEndpunkt
{
    public const string PhaseFeld = "phase";

    public const string ZuFrueh = "Abschließen geht erst nach dem letzten Freizeittag.";

    public static async Task<IResult> Abschliessen(Guid freizeitId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new FreizeitAbschliessenCommand(freizeitId), abbruch) switch
        {
            FreizeitAbschluss.Abgeschlossen => TypedResults.NoContent(),
            FreizeitAbschluss.ZuFrueh => throw new ValidierungsAusnahme(new Dictionary<string, string[]> { [PhaseFeld] = [ZuFrueh] }),
            FreizeitAbschluss.SchonAbgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
