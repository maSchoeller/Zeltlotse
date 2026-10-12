using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Legt Einsichten an (FR-10) und hält „höchstens eine offene je (Person, Organisation)“ ein: Gleichzeitige Aufrufe für dieselbe
/// Person und Organisation laufen nacheinander (Advisory-Lock), dahinter steht der partielle eindeutige Index. Bekommt nur die
/// Tabelle, nicht den schreibenden DbContext (AD-27). Der Mandantenfilter gilt; der Aufrufer hat den Kontext der Organisation
/// gesetzt.
/// </summary>
internal static class EinsichtSicherstellen
{
    /// <summary>
    /// Von Hand: Gibt es schon eine offene Einsicht (auch mit abgelaufener Freigabe), kommt sie zurück; sonst entsteht eine neue,
    /// sofort gebunden an die anfragende Person (AD-9).
    /// </summary>
    /// <returns>Die offene Einsicht und ob sie eben entstanden ist.</returns>
    public static async Task<(NachweisEinsicht Einsicht, bool Neu)> VonHandAsync(
        DbContext db,
        DbSet<NachweisEinsicht> einsichten,
        Guid organisationId,
        Guid personId,
        Guid? freizeitId,
        Guid angefragtVon,
        TimeProvider uhr,
        CancellationToken abbruch)
    {
        await NachweisRegeln.EinsichtSperrenAsync(db, personId, organisationId, abbruch);
        var offen = await einsichten
            .Where(e => e.OrganisationId == organisationId && e.PersonId == personId && e.Status != EinsichtStatus.Abgeschlossen)
            .FirstOrDefaultAsync(abbruch);
        if (offen is not null)
        {
            return (offen, false);
        }

        var neu = NachweisEinsicht.Anfragen(Kennung.Neu(uhr), organisationId, personId, freizeitId, angefragtVon, angefragtVon, uhr.GetUtcNow());
        einsichten.Add(neu);
        return (neu, true);
    }

    /// <summary>
    /// Automatisch nur bei der ersten Mitarbeit in der Organisation (NS-3): Gibt es für (Person, Organisation) noch gar keine
    /// Einsicht, egal in welchem Status, entsteht eine ohne anfragende und ohne prüfende Person. Später fragt die Organisation
    /// von Hand an.
    /// </summary>
    /// <returns>Die neue Einsicht oder <c>null</c>, wenn es schon eine gab.</returns>
    public static async Task<NachweisEinsicht?> AutomatischAsync(
        DbContext db,
        DbSet<NachweisEinsicht> einsichten,
        Guid organisationId,
        Guid personId,
        Guid freizeitId,
        TimeProvider uhr,
        CancellationToken abbruch)
    {
        await NachweisRegeln.EinsichtSperrenAsync(db, personId, organisationId, abbruch);
        if (await einsichten.AnyAsync(e => e.OrganisationId == organisationId && e.PersonId == personId, abbruch))
        {
            return null;
        }

        var neu = NachweisEinsicht.Anfragen(Kennung.Neu(uhr), organisationId, personId, freizeitId, null, null, uhr.GetUtcNow());
        einsichten.Add(neu);
        return neu;
    }
}

/// <summary>
/// Stellt die automatische Einsicht einer Person mit Freizeit-Rolle sicher (ohne anfragende und ohne prüfende Person), nur wenn es
/// in der Organisation noch keine Einsicht gibt. Der Empfänger von <c>FreizeitRolleGeaendert</c> nutzt denselben Schritt; die
/// Demo-Daten rufen den Command ausdrücklich auf. Ergebnis: die neue Einsicht, sonst die jüngste vorhandene. Kein
/// Benutzer-Command.
/// </summary>
internal sealed record AutomatischeEinsichtSicherstellen(Guid OrganisationId, Guid FreizeitId, Guid PersonId)
    : ICommand<Guid>, IModulCommand<PersonenDbContext>;

internal sealed class AutomatischeEinsichtSicherstellenHandler(PersonenDbContext db, TimeProvider uhr)
    : ICommandHandler<AutomatischeEinsichtSicherstellen, Guid>
{
    public async ValueTask<Guid> Handle(AutomatischeEinsichtSicherstellen command, CancellationToken cancellationToken)
    {
        if (await EinsichtSicherstellen.AutomatischAsync(db, db.Einsichten, command.OrganisationId, command.PersonId, command.FreizeitId, uhr, cancellationToken) is { } neu)
        {
            return neu.Id;
        }

        return await db.Einsichten
            .Where(e => e.OrganisationId == command.OrganisationId && e.PersonId == command.PersonId)
            .OrderBy(e => e.Status == EinsichtStatus.Abgeschlossen)
            .ThenByDescending(e => e.AngefragtAm)
            .Select(e => e.Id)
            .FirstAsync(cancellationToken);
    }
}

/// <summary>
/// Einsicht von Hand anfragen (FR-10): durch die Leitung einer Freizeit (<see cref="FreizeitId"/> gesetzt) nur für Personen mit
/// Rolle in dieser Freizeit, durch den Org-Admin (<see cref="FreizeitId"/> <c>null</c>) für Personen mit Rolle in einer Freizeit
/// der Organisation; nie für sich selbst. Rollen kommen aus der eigenen Liste von Personen (AD-8). Idempotent: Eine offene
/// Einsicht kommt zurück.
/// </summary>
internal sealed record EinsichtAnfragenCommand(Guid OrganisationId, Guid? FreizeitId, Guid AnfragendePersonId, Guid? PersonId)
    : ICommand<EinsichtAnfrageErgebnis>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Anfrage von Hand.</summary>
internal abstract record EinsichtAnfrageErgebnis
{
    private EinsichtAnfrageErgebnis()
    {
    }

    /// <summary>Neu angelegt: <c>201</c>.</summary>
    public sealed record Neu(Guid EinsichtId) : EinsichtAnfrageErgebnis;

    /// <summary>Schon offen: <c>200</c>.</summary>
    public sealed record Vorhanden(Guid EinsichtId) : EinsichtAnfrageErgebnis;

    /// <summary>Keine Person mit Rolle hier, oder die anfragende Person selbst: <c>404</c>.</summary>
    public sealed record NichtGefunden : EinsichtAnfrageErgebnis;
}

internal sealed class EinsichtAnfragenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<EinsichtAnfragenCommand, EinsichtAnfrageErgebnis>
{
    public async ValueTask<EinsichtAnfrageErgebnis> Handle(EinsichtAnfragenCommand command, CancellationToken cancellationToken)
    {
        if (command.PersonId is not { } personId || personId == command.AnfragendePersonId
            || !await AnfragbarAsync(db.FreizeitRollen, command.FreizeitId, personId, cancellationToken))
        {
            return new EinsichtAnfrageErgebnis.NichtGefunden();
        }

        var (einsicht, neu) = await EinsichtSicherstellen.VonHandAsync(
            db, db.Einsichten, command.OrganisationId, personId, command.FreizeitId, command.AnfragendePersonId, uhr, cancellationToken);
        return neu ? new EinsichtAnfrageErgebnis.Neu(einsicht.Id) : new EinsichtAnfrageErgebnis.Vorhanden(einsicht.Id);
    }

    /// <summary>Eine Rolle in der Freizeit bzw. in einer Freizeit der Organisation (Mandantenfilter).</summary>
    internal static Task<bool> AnfragbarAsync(IQueryable<FreizeitRolle> rollen, Guid? freizeitId, Guid personId, CancellationToken abbruch) =>
        freizeitId is { } freizeit
            ? rollen.AnyAsync(r => r.FreizeitId == freizeit && r.PersonId == personId, abbruch)
            : rollen.AnyAsync(r => r.PersonId == personId, abbruch);
}
