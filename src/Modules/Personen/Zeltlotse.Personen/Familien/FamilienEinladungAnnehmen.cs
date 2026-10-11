using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Nimmt eine Familien-Einladung an (FR-3, AD-11): Token gültig, offen, nicht abgelaufen und die Mail-Adresse des angemeldeten
/// Kontos (<paramref name="KontoMail"/>, vom Endpunkt aus der Sitzung) gleich der Einladungs-Mail; sonst
/// <see cref="FamilienAnnahme.Ungueltig"/> ohne Änderung und <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll.
/// Ist die Person schon Elternteil derselben Familie, wird nur die Einladung angenommen. Elternteil einer anderen Familie oder
/// eine inzwischen volle Familie ergeben <see cref="FamilienAnnahme.Konflikt"/> ohne Änderung. Einladung und Familie liegen
/// unter Zeilensperre.
/// </summary>
internal sealed record FamilienEinladungAnnehmenCommand(string? Token, string? KontoMail, Guid PersonId)
    : ICommand<FamilienAnnahme>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie Token oder Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(FamilienEinladungAnnehmenCommand)}({PersonId})";
}

/// <summary>Ergebnis des Annehmens einer Familien-Einladung.</summary>
internal abstract record FamilienAnnahme
{
    private FamilienAnnahme()
    {
    }

    public sealed record Angenommen(Guid EinladungId, Guid FamilieId) : FamilienAnnahme;

    /// <summary>Nicht gültig, abgelaufen, schon benutzt, verworfen oder für eine andere Mail-Adresse: <c>404</c>.</summary>
    public sealed record Ungueltig : FamilienAnnahme;

    /// <summary>Elternteil einer anderen Familie oder Familie schon mit zwei Eltern: <c>409</c>.</summary>
    public sealed record Konflikt : FamilienAnnahme;
}

internal sealed class FamilienEinladungAnnehmenHandler(PersonenDbContext db, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<FamilienEinladungAnnehmenCommand, FamilienAnnahme>
{
    public async ValueTask<FamilienAnnahme> Handle(FamilienEinladungAnnehmenCommand command, CancellationToken cancellationToken)
    {
        // Dieselbe Sperre je Person wie „Kind anlegen“ und „Elternteil einladen“: Eine Person wird nicht gleichzeitig
        // Elternteil zweier Familien.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({command.PersonId.ToString()}, 0))", cancellationToken);
        FamilienEinladung? einladung = null;
        if (Zugangslink.KannTokenSein(command.Token))
        {
            einladung = await FamilienSperre.EinladungAsync(
                db.FamilienEinladungen, Zugangslink.TokenHash(FamilienEinladung.Zweck, command.Token!), cancellationToken);
        }

        var jetzt = uhr.GetUtcNow();
        if (einladung is null || !einladung.IstAnnehmbar(jetzt) || !einladung.GiltFuer(command.KontoMail))
        {
            await protokoll.SchreibenAsync(
                SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, command.PersonId, bezugId: einladung?.Id, abbruch: cancellationToken);
            return new FamilienAnnahme.Ungueltig();
        }

        await FamilienSperre.FamilieAsync(db.Familien, einladung.FamilieId, cancellationToken);
        var eigene = await db.Elternteile.SingleOrDefaultAsync(e => e.PersonId == command.PersonId, cancellationToken);
        if (eigene is not null && eigene.FamilieId != einladung.FamilieId)
        {
            return new FamilienAnnahme.Konflikt();
        }

        if (eigene is null)
        {
            if (await db.Elternteile.CountAsync(e => e.FamilieId == einladung.FamilieId, cancellationToken) >= Familie.HoechstensEltern)
            {
                return new FamilienAnnahme.Konflikt();
            }

            db.Elternteile.Add(FamilienElternteil.Anlegen(einladung.FamilieId, command.PersonId));
        }

        einladung.Annehmen(jetzt);
        return new FamilienAnnahme.Angenommen(einladung.Id, einladung.FamilieId);
    }
}
