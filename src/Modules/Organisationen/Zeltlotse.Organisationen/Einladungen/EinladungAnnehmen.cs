using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Domaene;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Einladungen;

/// <summary>
/// Nimmt eine Org-Einladung an (AD-11): Token gültig, offen, nicht abgelaufen und die Mail-Adresse des angemeldeten Kontos
/// gleich der Einladungs-Mail. Vergibt die Rolle (schon vorhanden → keine zweite Zeile), setzt den Status und bei
/// Org-Admin ggf. <see cref="Organisation.EingerichtetAm"/>. <c>null</c> in jedem anderen Fall; die Einladung bleibt dann
/// unverändert, und <c>LinkEinloesungFehlgeschlagen</c> steht im Sicherheitsprotokoll.
/// </summary>
/// <remarks>
/// Die Suche per Token-Hash ist die einzige Stelle, die den Mandantenfilter umgeht: Hier ist das Token die Quelle des
/// Mandanten-Kontexts (AD-6). Danach setzt der Handler den Kontext auf die Organisation der Einladung.
/// </remarks>
internal sealed record EinladungAnnehmenCommand(string? Token, Guid KontoId, Guid PersonId)
    : ICommand<AngenommeneEinladung?>, IModulCommand<OrganisationenDbContext>, IBenutzerCommand
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(EinladungAnnehmenCommand)}({PersonId})";
}

internal sealed record AngenommeneEinladung(Guid EinladungId, Guid OrganisationId, OrgRolleArt Rolle);

internal sealed class EinladungAnnehmenHandler(
    OrganisationenDbContext db, MandantenKontext mandant, IKontoLesen konten, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<EinladungAnnehmenCommand, AngenommeneEinladung?>
{
    public async ValueTask<AngenommeneEinladung?> Handle(EinladungAnnehmenCommand command, CancellationToken cancellationToken)
    {
        OrgEinladung? einladung = null;
        if (Zugangslink.KannTokenSein(command.Token))
        {
            var hash = Zugangslink.TokenHash(OrgEinladung.Zweck, command.Token!);
            einladung = await EinladungSperre.PerTokenHashAsync(db.Einladungen, hash, cancellationToken);
        }

        var jetzt = uhr.GetUtcNow();
        if (einladung is null
            || !einladung.IstAnnehmbar(jetzt)
            || !einladung.GiltFuer(await konten.MailAsync(command.KontoId, cancellationToken)))
        {
            await protokoll.SchreibenAsync(
                SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, command.PersonId, einladung?.OrganisationId, einladung?.Id, cancellationToken);
            return null;
        }

        mandant.Setzen(einladung.OrganisationId);
        einladung.Annehmen(jetzt);

        var vorhanden = await db.Rollen.AnyAsync(
            r => r.OrganisationId == einladung.OrganisationId && r.PersonId == command.PersonId && r.Rolle == einladung.Rolle,
            cancellationToken);
        if (!vorhanden)
        {
            db.Rollen.Add(OrgRolle.Vergeben(einladung.OrganisationId, command.PersonId, einladung.Rolle, jetzt));
        }

        if (einladung.Rolle == OrgRolleArt.OrgAdmin)
        {
            var organisation = await db.Organisationen.SingleAsync(o => o.Id == einladung.OrganisationId, cancellationToken);
            organisation.Einrichten(jetzt);
        }

        return new AngenommeneEinladung(einladung.Id, einladung.OrganisationId, einladung.Rolle);
    }
}

/// <summary>
/// Lädt eine Einladung per Token-Hash mit Zeilensperre (<c>FOR UPDATE</c>) in der Transaktion der Pipeline: Gleichzeitige
/// Annahmen derselben Einladung laufen nacheinander, nur eine gelingt. Ohne Mandantenfilter (Kontext-Quelle Token, AD-6).
/// Bekommt nur die Tabelle des Handlers, nicht den schreibenden DbContext (AD-27).
/// </summary>
internal static class EinladungSperre
{
    public static async Task<OrgEinladung?> PerTokenHashAsync(DbSet<OrgEinladung> einladungen, byte[] tokenHash, CancellationToken abbruch) =>
        (await einladungen
            .FromSql($"SELECT *, xmin FROM organisationen.org_einladung WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(abbruch))
        .SingleOrDefault();
}
