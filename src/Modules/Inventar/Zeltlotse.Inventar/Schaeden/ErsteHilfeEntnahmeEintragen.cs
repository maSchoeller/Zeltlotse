using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Contracts.Events;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>
/// Story 3-4: Empfänger von <see cref="ErsteHilfeEntnommen"/> (Tabelle Ereignis-Fluss: Sicherheit → Inventar, Eintrag auf der Schadens- und
/// Verlustliste, FR-32). Legt einen Eintrag „Erste-Hilfe-Kasten“ der Art Entnahme mit der Kennung des Vorfall-Eintrags an; den Text kennt Inventar
/// nicht. Gibt es zu dieser Kennung schon einen Eintrag, geschieht nichts. Mandant aus dem Umschlag; Ereignis-Empfänger ohne angemeldete Person.
/// Die Liste hat keine Phasensperre (AD-17, Anschaffungswünsche).
/// </summary>
internal sealed record ErsteHilfeEntnahmeEintragen(Umschlag Umschlag, ErsteHilfeEntnommen Ereignis)
    : ICommand, IModulCommand<InventarDbContext>, IEreignisEmpfaenger<ErsteHilfeEntnahmeEintragen, ErsteHilfeEntnommen>
{
    public static ErsteHilfeEntnahmeEintragen Aus(Umschlag umschlag, ErsteHilfeEntnommen ereignis) => new(umschlag, ereignis);
}

internal sealed class ErsteHilfeEntnahmeEintragenHandler(InventarDbContext db, IMandantenKontext mandant, TimeProvider uhr)
    : ICommandHandler<ErsteHilfeEntnahmeEintragen>
{
    public async ValueTask<Unit> Handle(ErsteHilfeEntnahmeEintragen command, CancellationToken cancellationToken)
    {
        var ereignis = command.Ereignis;
        if (mandant.OrganisationId is not { } organisationId
            || await db.Schaeden.AnyAsync(s => s.VorfallEintragId == ereignis.VorfallEintragId, cancellationToken))
        {
            return Unit.Value;
        }

        db.Schaeden.Add(SchadenVerlust.AusEntnahme(
            Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, ereignis.FreizeitId, ereignis.VorfallEintragId, ereignis.ErfasstAm));
        return Unit.Value;
    }
}
