using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Personen.Freigaben;

/// <summary>Stufe einer <see cref="Freigabe"/> (AD-8); gespeichert als Zahl, nur anhängen. Die Stufe Team kommt mit Story 2-3.</summary>
internal enum FreigabeStufe : short
{
    Leitung = 1,
}

/// <summary>
/// Freigabe einer Person für eine Freizeit (AD-8): entsteht mit dem Eingang der Anmeldung in der Stufe Leitung und gilt bis zum Ende
/// des Freigabefensters. Organisationseigen (Mandantenfilter), gehört aber der Person (personeneigen, AD-16). Je Anmeldung höchstens
/// eine. Fragen-Kennungen und Sichtregeln folgen mit den Stories 2-3 bis 2-5.
/// </summary>
internal sealed class Freigabe : IOrganisationsEigen
{
    private Freigabe()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid PersonId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid AnmeldungId { get; private init; }

    public FreigabeStufe Stufe { get; private init; }

    public DateTimeOffset GueltigBis { get; private init; }

    public DateTimeOffset AngelegtAm { get; private init; }

    public static Freigabe Leitung(
        Guid id, Guid organisationId, Guid personId, Guid freizeitId, Guid anmeldungId, DateTimeOffset gueltigBis, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        PersonId = personId,
        FreizeitId = freizeitId,
        AnmeldungId = anmeldungId,
        Stufe = FreigabeStufe.Leitung,
        GueltigBis = gueltigBis,
        AngelegtAm = jetzt,
    };
}

/// <summary>
/// Empfänger von <see cref="AnmeldungEingegangen"/> (Tabelle Ereignis-Fluss, AD-4, AD-8): legt die Freigabe der Stufe Leitung an,
/// gültig bis zum Ende des Freigabefensters der Freizeit. Idempotent je Anmeldung; ist die Person inzwischen gelöscht, entsteht
/// nichts. Der Mandant kommt aus dem Umschlag (AD-6); kein Benutzer-Command.
/// </summary>
internal sealed record FreigabeAnlegen(Umschlag Umschlag, Guid AnmeldungId, Guid FreizeitId, Guid PersonId, DateTimeOffset GueltigBis)
    : ICommand, IModulCommand<PersonenDbContext>, IEreignisEmpfaenger<FreigabeAnlegen, AnmeldungEingegangen>
{
    public static FreigabeAnlegen Aus(Umschlag umschlag, AnmeldungEingegangen ereignis) =>
        new(umschlag, ereignis.AnmeldungId, ereignis.FreizeitId, ereignis.PersonId, ereignis.FreigabeGueltigBis);
}

internal sealed class FreigabeAnlegenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<FreigabeAnlegen>
{
    public async ValueTask<Unit> Handle(FreigabeAnlegen command, CancellationToken cancellationToken)
    {
        var organisationId = command.Umschlag.OrganisationId
                             ?? throw new InvalidOperationException("Eine Anmeldung gehört immer zu einer Organisation (Umschlag ohne Mandant).");
        if (await db.Freigaben.AnyAsync(f => f.AnmeldungId == command.AnmeldungId, cancellationToken)
            || !await db.Personen.AnyAsync(p => p.Id == command.PersonId, cancellationToken))
        {
            return Unit.Value;
        }

        db.Freigaben.Add(Freigabe.Leitung(
            Kennung.Neu(uhr), organisationId, command.PersonId, command.FreizeitId, command.AnmeldungId, command.GueltigBis, uhr.GetUtcNow()));
        return Unit.Value;
    }
}
