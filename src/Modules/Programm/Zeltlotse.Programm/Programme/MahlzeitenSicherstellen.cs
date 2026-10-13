using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 3-1, FR-34: Legt die fehlenden festen Mahlzeiten (Frühstück, Mittag- und Abendessen) an jedem Tag der Freizeit an. Läuft beim Laden des
/// Wochenrasters, wenn weniger als drei Mahlzeiten je Tag da sind. Kein Benutzer-Command: Er ändert keine Planung, gilt also auch nach dem
/// Abschluss (AD-17). Idempotent über den eindeutigen Teilindex (Freizeit, Tag, Mahlzeit); auch zwei gleichzeitige Aufrufe legen nichts doppelt an.
/// </summary>
internal sealed record MahlzeitenSicherstellen(Guid FreizeitId) : ICommand<int>, IModulCommand<ProgrammDbContext>;

internal sealed class MahlzeitenSicherstellenHandler(ProgrammDbContext db, IMandantenKontext mandant, IFreizeitTageLesen tageLesen, TimeProvider uhr)
    : ICommandHandler<MahlzeitenSicherstellen, int>
{
    /// <summary>Liefert die Zahl der neu angelegten Mahlzeiten.</summary>
    public async ValueTask<int> Handle(MahlzeitenSicherstellen command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await tageLesen.TageAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var tage))
        {
            return 0;
        }

        var jetzt = uhr.GetUtcNow();
        var punkte = tage.Alle()
            .SelectMany(tag => Programmpunkt.Mahlzeiten.Select(m => Programmpunkt.MahlzeitAnlegen(Guid.CreateVersion7(jetzt), organisationId, command.FreizeitId, tag, m.Mahlzeit)))
            .ToList();
        Guid[] ids = [.. punkte.Select(p => p.Id)];
        DateOnly[] tageListe = [.. punkte.Select(p => p.Tag)];
        TimeOnly[] beginne = [.. punkte.Select(p => p.Beginn!.Value)];
        string[] titel = [.. punkte.Select(p => p.Titel)];
        short[] mahlzeiten = [.. punkte.Select(p => (short)p.Mahlzeit!.Value)];
        return await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO programm.programmpunkt ("Id", "OrganisationId", "FreizeitId", "Tag", "Beginn", "Titel", "Mahlzeit")
             SELECT neu.id, {organisationId}, {command.FreizeitId}, neu.tag, neu.beginn, neu.titel, neu.mahlzeit
             FROM unnest({ids}, {tageListe}, {beginne}, {titel}, {mahlzeiten}) AS neu(id, tag, beginn, titel, mahlzeit)
             ON CONFLICT ("FreizeitId", "Tag", "Mahlzeit") WHERE "Mahlzeit" IS NOT NULL DO NOTHING
             """,
            cancellationToken);
    }
}
