using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Programm.Datenschutz;

/// <summary>
/// Story 3-1: Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16): Die Übernahme eines Programmpunkts und (Story 3-2) das
/// Mitbringen von Material sind Arbeitsdaten der Freizeit; in allen Organisationen wird die Kennung der Person zum Platzhalter
/// <see cref="GeloeschtePerson.Id"/>. Kein Benutzer-Command.
/// </summary>
internal sealed record ProgrammPersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<ProgrammDbContext>, IEreignisEmpfaenger<ProgrammPersonLoeschen, PersonGeloescht>
{
    public static ProgrammPersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);
}

internal sealed class ProgrammPersonLoeschenHandler(ProgrammDbContext db) : ICommandHandler<ProgrammPersonLoeschen>
{
    public async ValueTask<Unit> Handle(ProgrammPersonLoeschen command, CancellationToken cancellationToken)
    {
        await db.Programmpunkte.IgnoreQueryFilters().Where(p => p.PersonId == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PersonId, GeloeschtePerson.Id), cancellationToken);
        await db.Material.IgnoreQueryFilters().Where(m => m.PersonId == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.PersonId, GeloeschtePerson.Id), cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Datenauskunft von Programm (AD-16, Story 3-1): Abschnitt „Programm“ mit je übernommenem Programmpunkt der Zeile „Programmpunkt übernommen“
/// (Tag, Uhrzeit und Titel) und (Story 3-2) je mitgebrachtem Material der Zeile „Bringt Material mit“ (Tag und Titel des Programmpunkts,
/// Bezeichnung). Ohne Mandantenfilter, nur die Zeilen der Person.
/// </summary>
internal sealed class ProgrammAuskunft(ProgrammLeseDbContext db) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var punkte = await db.Programmpunkte.IgnoreQueryFilters()
            .Where(p => p.PersonId == personId)
            .OrderBy(p => p.Tag)
            .ThenBy(p => p.Beginn)
            .ThenBy(p => p.Id)
            .Select(p => new { p.Tag, p.Beginn, p.Titel })
            .ToListAsync(abbruch);
        var material = await (
                from m in db.Material.IgnoreQueryFilters()
                where m.PersonId == personId
                join p in db.Programmpunkte.IgnoreQueryFilters() on m.ProgrammpunktId equals p.Id
                orderby p.Tag, p.Beginn, p.Id, m.Bezeichnung, m.Id
                select new { p.Tag, p.Titel, m.Bezeichnung })
            .ToListAsync(abbruch);
        return
        [
            new AuskunftAbschnitt(
                "Programm",
                [
                    .. punkte.Select(p => new AuskunftZeile(
                        "Programmpunkt übernommen",
                        p.Beginn is { } beginn
                            ? $"{AuskunftFormat.Datum(p.Tag)} {ProgrammpunktFelder.UhrzeitText(beginn)}: {p.Titel}"
                            : $"{AuskunftFormat.Datum(p.Tag)}: {p.Titel}")),
                    .. material.Select(m => new AuskunftZeile("Bringt Material mit", $"{AuskunftFormat.Datum(m.Tag)} {m.Titel}: {m.Bezeichnung}")),
                ]),
        ];
    }
}

/// <summary>
/// Täglicher Lösch-Lauf von Programm (AD-16): die Programmpunkte und (Story 3-2) das Material der Freizeiten, deren Daten nach der Frist
/// abgelaufen sind.
/// </summary>
internal sealed class ProgrammLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new ProgrammpunkteLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Material und Programmpunkte der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind, endgültig löschen.</summary>
internal sealed record ProgrammpunkteLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<ProgrammDbContext>;

internal sealed class ProgrammpunkteLoeschenHandler(ProgrammDbContext db) : ICommandHandler<ProgrammpunkteLoeschen>
{
    public async ValueTask<Unit> Handle(ProgrammpunkteLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.Material.IgnoreQueryFilters().Where(m => freizeiten.Contains(m.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Programmpunkte.IgnoreQueryFilters().Where(p => freizeiten.Contains(p.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
