using System.Globalization;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Finanzen.Datenschutz;

/// <summary>
/// Story 5-2: Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16): Belege sind aufbewahrungspflichtig für die Organisation. In allen
/// Organisationen wird „bezahlt von“ zum Platzhalter <see cref="GeloeschtePerson.Id"/>; Betrag, Kategorie, Bezeichnung, Vermerk und Datei bleiben. Story 5-3:
/// ebenso die Vermerke „Beitrag bezahlt“ (aufbewahrungspflichtig): die teilnehmende Person und wer abgehakt hat werden zum Platzhalter, der Vermerk bleibt.
/// Kein Benutzer-Command.
/// </summary>
internal sealed record FinanzenPersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<FinanzenDbContext>, IEreignisEmpfaenger<FinanzenPersonLoeschen, PersonGeloescht>
{
    public static FinanzenPersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);

    /// <summary>Keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(FinanzenPersonLoeschen)}({Umschlag.EreignisId})";
}

internal sealed class FinanzenPersonLoeschenHandler(FinanzenDbContext db) : ICommandHandler<FinanzenPersonLoeschen>
{
    public async ValueTask<Unit> Handle(FinanzenPersonLoeschen command, CancellationToken cancellationToken)
    {
        await db.Belege.IgnoreQueryFilters().Where(b => b.PersonId == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.PersonId, GeloeschtePerson.Id), cancellationToken);
        await db.BeitragVermerke.IgnoreQueryFilters().Where(v => v.PersonId == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.PersonId, GeloeschtePerson.Id), cancellationToken);
        await db.BeitragVermerke.IgnoreQueryFilters().Where(v => v.AbgehaktVon == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.AbgehaktVon, GeloeschtePerson.Id), cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Story 5-2: Datenauskunft der Finanzen (AD-16): Abschnitt „Belege“ mit je Beleg, den die Person bezahlt hat, Kategorie, Betrag, Bezeichnung, erfasst am und
/// erstattet. Story 5-3: Abschnitt „Beiträge“ mit dem eigenen Beitrag („Beitrag bezahlt“, vermerkt am …) und den eigenen Haken für andere, ohne deren Namen
/// oder Kennungen. Ohne Mandantenfilter, nur die Zeilen der Person. Die Kalkulation enthält keine Personendaten.
/// </summary>
internal sealed class FinanzenAuskunft(FinanzenLeseDbContext db) : IDatenauskunft
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var belege = await db.Belege.IgnoreQueryFilters()
            .Where(b => b.PersonId == personId)
            .OrderBy(b => b.ErfasstAm)
            .ThenBy(b => b.Id)
            .Select(b => new { b.Kategorie, b.Betrag, b.Bezeichnung, b.ErfasstAm, b.Erstattet })
            .ToListAsync(abbruch);
        return
        [
            new AuskunftAbschnitt(
                "Belege",
                [
                    .. belege.Select(b => new AuskunftZeile(
                        $"Beleg vom {AuskunftFormat.Datum(b.ErfasstAm)}",
                        $"{Kostenkategorien.Text(b.Kategorie)}, {b.Betrag.ToString("N2", Deutsch)} €"
                        + (b.Bezeichnung is null ? "" : $", {b.Bezeichnung}")
                        + $", erstattet: {AuskunftFormat.JaNein(b.Erstattet)}")),
                ]),
            new AuskunftAbschnitt("Beiträge", await BeitraegeAsync(personId, abbruch)),
        ];
    }

    /// <summary>Story 5-3: zuerst der eigene Beitrag, dann die Haken für andere, je nach Zeitpunkt.</summary>
    private async Task<List<AuskunftZeile>> BeitraegeAsync(Guid personId, CancellationToken abbruch)
    {
        var eigene = await db.BeitragVermerke.IgnoreQueryFilters()
            .Where(v => v.PersonId == personId)
            .OrderBy(v => v.AbgehaktAm)
            .ThenBy(v => v.Id)
            .Select(v => v.AbgehaktAm)
            .ToListAsync(abbruch);
        var gesetzt = await db.BeitragVermerke.IgnoreQueryFilters()
            .Where(v => v.AbgehaktVon == personId)
            .OrderBy(v => v.AbgehaktAm)
            .ThenBy(v => v.Id)
            .Select(v => v.AbgehaktAm)
            .ToListAsync(abbruch);
        return
        [
            .. eigene.Select(am => new AuskunftZeile("Beitrag bezahlt", $"vermerkt am {AuskunftFormat.Datum(am)}")),
            .. gesetzt.Select(am => new AuskunftZeile("Beitrag einer teilnehmenden Person als bezahlt abgehakt", $"am {AuskunftFormat.Datum(am)}")),
        ];
    }
}
