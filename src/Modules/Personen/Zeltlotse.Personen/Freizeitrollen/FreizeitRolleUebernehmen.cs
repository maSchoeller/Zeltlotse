using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Personen.Freizeitrollen;

/// <summary>
/// Eine Freizeit-Rolle in der eigenen Liste von Personen (AD-8): Personen prüft Leitung und Team später gegen diese Liste,
/// nie gegen eine Angabe des Aufrufers. Gepflegt wird sie ausschließlich über <see cref="FreizeitRolleGeaendert"/>.
/// Eindeutig je (Freizeit, Person).
/// </summary>
internal sealed class FreizeitRolle : IOrganisationsEigen
{
    private FreizeitRolle()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid PersonId { get; private init; }

    public FreizeitRolleArt Rolle { get; private set; }

    public static FreizeitRolle Anlegen(Guid organisationId, Guid freizeitId, Guid personId, FreizeitRolleArt rolle) => new()
    {
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        PersonId = personId,
        Rolle = rolle,
    };

    public void RolleSetzen(FreizeitRolleArt rolle) => Rolle = rolle;
}

/// <summary>
/// Empfänger von <see cref="FreizeitRolleGeaendert"/> (Tabelle Ereignis-Fluss): löscht die Zeile bei <see cref="FreizeitRolleArt.Entzogen"/>;
/// sonst legt er die Zeile an oder setzt die Rolle und legt
/// bei der ersten Mitarbeit in der Organisation eine Einsicht in die Nachweise an (FR-10, NS-3, ohne neue Zeile im Ereignis-Fluss). Der
/// Mandant kommt aus dem Umschlag (AD-4, AD-6); kein Benutzer-Command.
/// </summary>
internal sealed record FreizeitRolleUebernehmen(Umschlag Umschlag, Guid FreizeitId, Guid PersonId, FreizeitRolleArt Rolle)
    : ICommand, IModulCommand<PersonenDbContext>, IEreignisEmpfaenger<FreizeitRolleUebernehmen, FreizeitRolleGeaendert>
{
    public static FreizeitRolleUebernehmen Aus(Umschlag umschlag, FreizeitRolleGeaendert ereignis) =>
        new(umschlag, ereignis.FreizeitId, ereignis.PersonId, ereignis.Rolle);
}

internal sealed class FreizeitRolleUebernehmenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<FreizeitRolleUebernehmen>
{
    public async ValueTask<Unit> Handle(FreizeitRolleUebernehmen command, CancellationToken cancellationToken)
    {
        var organisationId = command.Umschlag.OrganisationId
                             ?? throw new InvalidOperationException("Eine Freizeit-Rolle gehört immer zu einer Organisation (Umschlag ohne Mandant).");
        var vorhanden = await db.FreizeitRollen
            .SingleOrDefaultAsync(r => r.FreizeitId == command.FreizeitId && r.PersonId == command.PersonId, cancellationToken);

        // Story 2-3: Nach einer Absage hat die Person keine Rolle mehr; die Zeile verschwindet, Einsichten bleiben, wie sie sind.
        if (command.Rolle == FreizeitRolleArt.Entzogen)
        {
            if (vorhanden is not null)
            {
                db.FreizeitRollen.Remove(vorhanden);
            }

            return Unit.Value;
        }

        if (vorhanden is null)
        {
            db.FreizeitRollen.Add(FreizeitRolle.Anlegen(organisationId, command.FreizeitId, command.PersonId, command.Rolle));
        }
        else
        {
            vorhanden.RolleSetzen(command.Rolle);
        }

        // FR-10, NS-3: Die erste Mitarbeit in der Organisation fragt die Einsicht in die Nachweise automatisch an (ohne
        // prüfende Person); gibt es dort schon eine Einsicht, fragt die Organisation später von Hand an.
        await EinsichtSicherstellen.AutomatischAsync(db, db.Einsichten, organisationId, command.PersonId, command.FreizeitId, uhr, cancellationToken);
        return Unit.Value;
    }
}
