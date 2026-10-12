using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Domaene;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Organisationen.Mitglieder;

/// <summary>
/// Empfänger von <see cref="AnmeldungBestaetigt"/> (Tabelle Ereignis-Fluss, FR-7): Bestätigte Mitarbeitende werden Mitarbeitende der
/// Organisation aus dem Umschlag. Teilnehmende und gelöschte Personen ignoriert er; eine vorhandene Mitgliedschaft bleibt
/// (idempotent). Kein Benutzer-Command.
/// </summary>
internal sealed record MitgliedschaftAnlegen(Umschlag Umschlag, Guid PersonId, AnmeldungArt Art)
    : ICommand, IModulCommand<OrganisationenDbContext>, IEreignisEmpfaenger<MitgliedschaftAnlegen, AnmeldungBestaetigt>
{
    public static MitgliedschaftAnlegen Aus(Umschlag umschlag, AnmeldungBestaetigt ereignis) => new(umschlag, ereignis.PersonId, ereignis.Art);
}

internal sealed class MitgliedschaftAnlegenHandler(OrganisationenDbContext db, TimeProvider uhr) : ICommandHandler<MitgliedschaftAnlegen>
{
    public async ValueTask<Unit> Handle(MitgliedschaftAnlegen command, CancellationToken cancellationToken)
    {
        if (command.Art != AnmeldungArt.Mitarbeitende || command.PersonId == GeloeschtePerson.Id)
        {
            return Unit.Value;
        }

        var organisationId = command.Umschlag.OrganisationId
                             ?? throw new InvalidOperationException("Eine Anmeldung gehört immer zu einer Organisation (Umschlag ohne Mandant).");
        // Gleichzeitige Bestätigungen derselben Person in zwei Freizeiten der Organisation legen nur eine Zeile an.
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({$"mitgliedschaft:{organisationId}:{command.PersonId}"}, 0))", cancellationToken);
        if (!await db.Mitgliedschaften.AnyAsync(m => m.OrganisationId == organisationId && m.PersonId == command.PersonId, cancellationToken))
        {
            db.Mitgliedschaften.Add(Mitgliedschaft.Anlegen(organisationId, command.PersonId, uhr.GetUtcNow()));
        }

        return Unit.Value;
    }
}
