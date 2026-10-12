using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Datenschutz;

/// <summary>
/// Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16): löscht in allen Organisationen die Freizeit-Rollen
/// der Person und ersetzt „angelegt von“, „eingeladen von“ und die Person ihrer Anmeldungen durch den Platzhalter. Ein
/// <c>FreizeitRolleGeaendert</c> gibt es dafür nicht: Personen hat seine Kopie der Rollen schon gelöscht. Kein Benutzer-Command.
/// </summary>
internal sealed record FreizeitPersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<FreizeitenDbContext>, IEreignisEmpfaenger<FreizeitPersonLoeschen, PersonGeloescht>
{
    public static FreizeitPersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);
}

internal sealed class FreizeitPersonLoeschenHandler(FreizeitenDbContext db) : ICommandHandler<FreizeitPersonLoeschen>
{
    public async ValueTask<Unit> Handle(FreizeitPersonLoeschen command, CancellationToken cancellationToken)
    {
        var personId = command.PersonId;
        await db.Rollen.IgnoreQueryFilters().Where(r => r.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Freizeiten.IgnoreQueryFilters().Where(f => f.AngelegtVon == personId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.AngelegtVon, GeloeschtePerson.Id), cancellationToken);
        await db.Einladungen.IgnoreQueryFilters().Where(e => e.EingeladenVon == personId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EingeladenVon, GeloeschtePerson.Id), cancellationToken);
        // Anmeldungen sind Arbeitsdaten der Freizeit (AD-16): Die Person wird zum Platzhalter, die Anmeldung bleibt bis zur Frist.
        await db.Anmeldungen.IgnoreQueryFilters().Where(a => a.PersonId == personId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PersonId, GeloeschtePerson.Id), cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Datenauskunft von Freizeiten (AD-16): die Mitarbeit der Person mit Name der Freizeit und Rolle und die Anmeldungen der Person und
/// der Kinder ihrer Familie (<see cref="IVertretung"/>); ohne Mandantenfilter, nur diese Zeilen.
/// </summary>
internal sealed class FreizeitenAuskunft(FreizeitenLeseDbContext db, IVertretung vertretung, IPersonenNamen namen) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var rollen = await db.Rollen.IgnoreQueryFilters().Where(r => r.PersonId == personId).ToListAsync(abbruch);
        var freizeitIds = rollen.Select(r => r.FreizeitId).Distinct().ToList();
        var freizeitNamen = await db.Freizeiten.IgnoreQueryFilters().Where(f => freizeitIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, abbruch);
        return
        [
            new AuskunftAbschnitt(
                "Mitarbeit in Freizeiten",
                [
                    .. rollen.OrderBy(r => freizeitNamen.GetValueOrDefault(r.FreizeitId) ?? "", StringComparer.CurrentCulture)
                        .Select(r => new AuskunftZeile(freizeitNamen.GetValueOrDefault(r.FreizeitId) ?? "", Rolle(r.Rolle))),
                ]),
            new AuskunftAbschnitt("Anmeldungen", await AnmeldungenAsync(personId, abbruch)),
        ];
    }

    private async Task<List<AuskunftZeile>> AnmeldungenAsync(Guid personId, CancellationToken abbruch)
    {
        List<Guid> ids = [.. await vertretung.PersonenAsync(personId, abbruch)];
        var anmeldungen = await db.Anmeldungen.IgnoreQueryFilters().Where(a => ids.Contains(a.PersonId)).OrderBy(a => a.EingegangenAm).ToListAsync(abbruch);
        var freizeitIds = anmeldungen.Select(a => a.FreizeitId).Distinct().ToList();
        var freizeitNamen = await db.Freizeiten.IgnoreQueryFilters().Where(f => freizeitIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, abbruch);
        var vornamen = await namen.NamenAsync([.. anmeldungen.Select(a => a.PersonId).Distinct()], abbruch);
        return
        [
            .. anmeldungen.Select(a => new AuskunftZeile(
                freizeitNamen.GetValueOrDefault(a.FreizeitId) ?? "",
                $"{AuskunftFormat.Name(vornamen.GetValueOrDefault(a.PersonId)?.Vorname, vornamen.GetValueOrDefault(a.PersonId)?.Nachname)}, "
                + $"{(a.Art == AnmeldeArt.Teilnehmende ? "Teilnehmende" : "Mitarbeitende")}, eingegangen am {AuskunftFormat.Datum(a.EingegangenAm)}, "
                + $"Teilnahmebedingungen bestätigt am {AuskunftFormat.Datum(a.TeilnahmebedingungenBestaetigtAm)}")),
        ];
    }

    private static string Rolle(RolleArt rolle) => rolle switch
    {
        RolleArt.Leitung => "Leitung",
        RolleArt.Team => "Team",
        _ => rolle.ToString(),
    };
}

/// <summary><see cref="IFreizeitEndenLesen"/> über den Lese-Kontext, ohne Mandantenfilter (nur für den Lösch-Dienst, AD-6).</summary>
internal sealed class FreizeitEndenLesen(FreizeitenLeseDbContext db) : IFreizeitEndenLesen
{
    public async Task<IReadOnlyList<FreizeitEnde>> AlleAsync(CancellationToken abbruch = default) =>
        [
            .. (await db.Freizeiten.IgnoreQueryFilters().Select(f => new { f.Id, f.OrganisationId, f.LetzterTag }).ToListAsync(abbruch))
                .Select(f => new FreizeitEnde(f.Id, f.OrganisationId, FreizeitZeiten.Freizeitende(f.LetzterTag))),
        ];
}

/// <summary>
/// Täglicher Lösch-Lauf von Freizeiten (AD-16): schickt <see cref="LeitungsEinladungenLoeschen"/> und
/// <see cref="AnmeldungenLoeschen"/>. Freizeit-Rollen löscht keine Frist.
/// </summary>
internal sealed class FreizeitenLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        await sender.Send(new LeitungsEinladungenLoeschen(auftrag.Jetzt), abbruch);
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new AnmeldungenLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Anmeldungen der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind (Arbeitsdaten, AD-16), endgültig löschen.</summary>
internal sealed record AnmeldungenLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<FreizeitenDbContext>;

internal sealed class AnmeldungenLoeschenHandler(FreizeitenDbContext db) : ICommandHandler<AnmeldungenLoeschen>
{
    public async ValueTask<Unit> Handle(AnmeldungenLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.Anmeldungen.IgnoreQueryFilters().Where(a => freizeiten.Contains(a.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Leitungs-Einladungen, deren Ablauf mehr als <see cref="TechnischeFristen.Einladungen"/> zurückliegt, endgültig löschen.</summary>
internal sealed record LeitungsEinladungenLoeschen(DateTimeOffset Jetzt) : ICommand, IModulCommand<FreizeitenDbContext>;

internal sealed class LeitungsEinladungenLoeschenHandler(FreizeitenDbContext db) : ICommandHandler<LeitungsEinladungenLoeschen>
{
    public async ValueTask<Unit> Handle(LeitungsEinladungenLoeschen command, CancellationToken cancellationToken)
    {
        var grenze = command.Jetzt - TechnischeFristen.Einladungen;
        await db.Einladungen.IgnoreQueryFilters().Where(e => e.GueltigBis < grenze).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
