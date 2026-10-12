using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Rollen;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Organisationen.Datenschutz;

/// <summary>
/// Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16): löscht in allen Organisationen die Org-Rollen, die
/// Mitgliedschaften (personeneigen, Story 2-3) und
/// die eigenen Nachweis-Bestätigungen der Person (ohne die Person belegen sie nichts) und ersetzt „geprüft von“ und „eingeladen
/// von“ in Daten anderer durch den Platzhalter. Kein Benutzer-Command.
/// </summary>
internal sealed record OrgPersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<OrganisationenDbContext>, IEreignisEmpfaenger<OrgPersonLoeschen, PersonGeloescht>
{
    public static OrgPersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);
}

internal sealed class OrgPersonLoeschenHandler(OrganisationenDbContext db) : ICommandHandler<OrgPersonLoeschen>
{
    public async ValueTask<Unit> Handle(OrgPersonLoeschen command, CancellationToken cancellationToken)
    {
        var personId = command.PersonId;
        await db.Rollen.IgnoreQueryFilters().Where(r => r.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Bestaetigungen.IgnoreQueryFilters().Where(b => b.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Mitgliedschaften.IgnoreQueryFilters().Where(m => m.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Bestaetigungen.IgnoreQueryFilters().Where(b => b.GeprueftVon == personId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.GeprueftVon, GeloeschtePerson.Id), cancellationToken);
        await db.Einladungen.IgnoreQueryFilters().Where(e => e.EingeladenVon == personId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EingeladenVon, GeloeschtePerson.Id), cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Datenauskunft von Organisationen (AD-16): die Rollen der Person, die Nachweis-Bestätigungen über sie und ihre Mitgliedschaften, je mit dem Namen der
/// Organisation. Ohne Mandantenfilter, nur die Zeilen der Person (AD-6).
/// </summary>
internal sealed class OrganisationenAuskunft(OrganisationenLeseDbContext db) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var rollen = await db.Rollen.IgnoreQueryFilters().Where(r => r.PersonId == personId).ToListAsync(abbruch);
        var bestaetigungen = await db.Bestaetigungen.IgnoreQueryFilters().Where(b => b.PersonId == personId).ToListAsync(abbruch);
        var mitgliedschaften = await db.Mitgliedschaften.IgnoreQueryFilters().Where(m => m.PersonId == personId).ToListAsync(abbruch);
        var orgIds = rollen.Select(r => r.OrganisationId)
            .Concat(bestaetigungen.Select(b => b.OrganisationId))
            .Concat(mitgliedschaften.Select(m => m.OrganisationId))
            .Distinct()
            .ToList();
        var namen = await db.Organisationen.Where(o => orgIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, abbruch);
        string Name(Guid id) => namen.GetValueOrDefault(id) ?? "";

        return
        [
            new AuskunftAbschnitt(
                "Rollen in Organisationen",
                [
                    .. rollen.OrderBy(r => Name(r.OrganisationId), StringComparer.CurrentCulture).ThenBy(r => r.Rolle)
                        .Select(r => new AuskunftZeile(Name(r.OrganisationId), $"{OrgRechte.Text(r.Rolle)} seit {AuskunftFormat.Datum(r.VergebenAm)}")),
                ]),
            new AuskunftAbschnitt(
                "Nachweis-Bestätigungen",
                [
                    .. bestaetigungen.OrderBy(b => b.Art).ThenBy(b => Name(b.OrganisationId), StringComparer.CurrentCulture)
                        .Select(b => new AuskunftZeile(
                            $"{Art(b.Art)} ({Name(b.OrganisationId)})",
                            $"eingesehen am {AuskunftFormat.Datum(b.EingesehenAm)}, gültig bis {AuskunftFormat.Datum(b.GueltigBis)}")),
                ]),
            new AuskunftAbschnitt(
                "Mitgliedschaften",
                [
                    .. mitgliedschaften.OrderBy(m => Name(m.OrganisationId), StringComparer.CurrentCulture)
                        .Select(m => new AuskunftZeile(Name(m.OrganisationId), $"Mitarbeitende von {Name(m.OrganisationId)} seit {AuskunftFormat.Datum(m.SeitAm)}")),
                ]),
        ];
    }

    private static string Art(NachweisArt art) => art switch
    {
        NachweisArt.Fuehrungszeugnis => "Führungszeugnis",
        NachweisArt.Juleica => "JuLeiCa",
        NachweisArt.ErsteHilfe => "Erste Hilfe",
        NachweisArt.Praeventionsschulung => "Präventionsschulung",
        _ => art.ToString(),
    };
}

/// <summary>Täglicher Lösch-Lauf von Organisationen (AD-16): schickt nur <see cref="OrgFristenLoeschen"/>.</summary>
internal sealed class OrganisationenLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch) =>
        await sender.Send(new OrgFristenLoeschen(auftrag.Jetzt, auftrag.NachweisBestaetigungFristen), abbruch);
}

/// <summary>
/// Fristen von Organisationen (AD-16, NFR-5): Org-Einladungen, deren Ablauf mehr als <see cref="TechnischeFristen.Einladungen"/>
/// zurückliegt, und Nachweis-Bestätigungen mit „gültig bis“ + Frist der Organisation ≤ heute (Europe/Berlin). Über alle
/// Organisationen hinweg; kein Benutzer-Command.
/// </summary>
internal sealed record OrgFristenLoeschen(DateTimeOffset Jetzt, IReadOnlyList<NachweisBestaetigungFrist> Fristen)
    : ICommand, IModulCommand<OrganisationenDbContext>;

internal sealed class OrgFristenLoeschenHandler(OrganisationenDbContext db) : ICommandHandler<OrgFristenLoeschen>
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public async ValueTask<Unit> Handle(OrgFristenLoeschen command, CancellationToken cancellationToken)
    {
        var grenze = command.Jetzt - TechnischeFristen.Einladungen;
        await db.Einladungen.IgnoreQueryFilters().Where(e => e.GueltigBis < grenze).ExecuteDeleteAsync(cancellationToken);

        var heute = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(command.Jetzt, Berlin).DateTime);
        foreach (var frist in command.Fristen)
        {
            var abgelaufen = (await db.Bestaetigungen.IgnoreQueryFilters()
                    .Where(b => b.OrganisationId == frist.OrganisationId && b.GueltigBis <= heute)
                    .ToListAsync(cancellationToken))
                .Where(b => b.GueltigBis.AddMonths(frist.Monate) <= heute)
                .ToList();
            db.Bestaetigungen.RemoveRange(abgelaufen);
        }

        return Unit.Value;
    }
}
