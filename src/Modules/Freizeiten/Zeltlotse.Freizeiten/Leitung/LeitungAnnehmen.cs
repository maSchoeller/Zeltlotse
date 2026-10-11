using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.Leitung;

/// <summary>
/// Nimmt eine Leitungs-Einladung an (AD-11, FR-6): Token gültig, offen, nicht abgelaufen und die Mail-Adresse des
/// angemeldeten Kontos (<paramref name="KontoMail"/>, vom Endpunkt aus der Sitzung) gleich der Einladungs-Mail. Dann wird die
/// Person Leitung der Freizeit der Einladung (<see cref="Freizeit.LeitungEinsetzen"/>). <c>null</c> in jedem anderen Fall;
/// die Einladung bleibt dann unverändert, und <c>LinkEinloesungFehlgeschlagen</c> steht im Sicherheitsprotokoll.
/// </summary>
/// <remarks>
/// Die Suche per Token-Hash umgeht den Mandantenfilter: Hier ist das Token die Quelle des Mandanten-Kontexts (AD-6). Danach
/// setzt der Handler den Kontext auf die Organisation der Einladung. Einladung und Freizeit liegen unter Zeilensperre.
/// </remarks>
internal sealed record LeitungAnnehmenCommand(string? Token, string? KontoMail, Guid PersonId)
    : ICommand<AngenommeneLeitung?>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand
{
    /// <summary>Nie Token oder Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(LeitungAnnehmenCommand)}({PersonId})";
}

internal sealed record AngenommeneLeitung(Guid EinladungId, Guid FreizeitId, Guid OrganisationId);

internal sealed class LeitungAnnehmenHandler(FreizeitenDbContext db, MandantenKontext mandant, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<LeitungAnnehmenCommand, AngenommeneLeitung?>
{
    public async ValueTask<AngenommeneLeitung?> Handle(LeitungAnnehmenCommand command, CancellationToken cancellationToken)
    {
        LeitungsEinladung? einladung = null;
        if (Zugangslink.KannTokenSein(command.Token))
        {
            var hash = Zugangslink.TokenHash(LeitungsEinladung.Zweck, command.Token!);
            einladung = await LeitungSperre.EinladungAsync(db.Einladungen, hash, cancellationToken);
        }

        var jetzt = uhr.GetUtcNow();
        if (einladung is null || !einladung.IstAnnehmbar(jetzt) || !einladung.GiltFuer(command.KontoMail))
        {
            await protokoll.SchreibenAsync(
                SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, command.PersonId, einladung?.OrganisationId, einladung?.Id, cancellationToken);
            return null;
        }

        mandant.Setzen(einladung.OrganisationId);
        einladung.Annehmen(jetzt);

        var freizeit = await LeitungSperre.FreizeitAsync(db.Freizeiten, einladung.FreizeitId, einladung.OrganisationId, cancellationToken);
        await db.Rollen.Where(r => r.FreizeitId == freizeit.Id).LoadAsync(cancellationToken);
        freizeit.LeitungEinsetzen(command.PersonId, jetzt);

        return new AngenommeneLeitung(einladung.Id, freizeit.Id, freizeit.OrganisationId);
    }
}

/// <summary>
/// Lädt Einladung und Freizeit mit Zeilensperre (<c>FOR UPDATE</c>) in der Transaktion der Pipeline: Gleichzeitige Annahmen
/// laufen nacheinander. Die Einladung ohne Mandantenfilter (Kontext-Quelle Token, AD-6), die Freizeit nur in der
/// Organisation der Einladung. Bekommt nur die Tabellen des Handlers, nicht den schreibenden DbContext (AD-27).
/// </summary>
internal static class LeitungSperre
{
    public static async Task<LeitungsEinladung?> EinladungAsync(DbSet<LeitungsEinladung> einladungen, byte[] tokenHash, CancellationToken abbruch) =>
        (await einladungen
            .FromSql($"SELECT *, xmin FROM freizeiten.leitungs_einladung WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(abbruch))
        .SingleOrDefault();

    public static async Task<Freizeit> FreizeitAsync(DbSet<Freizeit> freizeiten, Guid freizeitId, Guid organisationId, CancellationToken abbruch) =>
        (await freizeiten
            .FromSql($"SELECT * FROM freizeiten.freizeit WHERE \"Id\" = {freizeitId} AND \"OrganisationId\" = {organisationId} FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(abbruch))
        .Single();
}
