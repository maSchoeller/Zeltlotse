using Mediator;
using Zeltlotse.Inventar.Contracts.Events;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Programm.Materialien;

/// <summary>
/// Story 3-3: Empfänger von <see cref="ReservierungGeaendert"/> (Tabelle Ereignis-Fluss: Inventar → Programm, Materialstatus aktualisieren). Sucht
/// das Material derselben Freizeit (Mandant aus dem Umschlag); fehlt es (gelöscht, andere Freizeit, unbekannte Kennung), geschieht nichts.
/// Reserviert → <see cref="Material.ReservierungZuordnen"/> (Status „vorhanden“), aufgehoben → <see cref="Material.ReservierungLoesen"/> (nur
/// bei passender Kennung, Status „gebraucht“). Kein Benutzer-Command: läuft auch nach dem Abschluss.
/// </summary>
internal sealed record ProgrammReservierungUebernehmen(Umschlag Umschlag, ReservierungGeaendert Ereignis)
    : ICommand, IModulCommand<ProgrammDbContext>, IEreignisEmpfaenger<ProgrammReservierungUebernehmen, ReservierungGeaendert>
{
    public static ProgrammReservierungUebernehmen Aus(Umschlag umschlag, ReservierungGeaendert ereignis) => new(umschlag, ereignis);
}

internal sealed class ProgrammReservierungUebernehmenHandler(ProgrammDbContext db) : ICommandHandler<ProgrammReservierungUebernehmen>
{
    public async ValueTask<Unit> Handle(ProgrammReservierungUebernehmen command, CancellationToken cancellationToken)
    {
        var ereignis = command.Ereignis;
        if (ereignis.MaterialId is not { } materialId
            || await db.MaterialSperrenAsync(ereignis.FreizeitId, materialId, cancellationToken) is not { } material)
        {
            return Unit.Value;
        }

        switch (ereignis.Art)
        {
            case ReservierungArt.Reserviert:
                material.ReservierungZuordnen(ereignis.ReservierungId);
                break;
            case ReservierungArt.Aufgehoben:
                material.ReservierungLoesen(ereignis.ReservierungId);
                break;
        }

        return Unit.Value;
    }
}
