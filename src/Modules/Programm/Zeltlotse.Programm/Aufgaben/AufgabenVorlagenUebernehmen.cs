using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5, FR-40: Übernimmt einmalig alle Aufgaben-Vorlagen der Organisation als Aufgaben der Freizeit (Titel, Phase, Verknüpfung). Läuft
/// beim Laden der Aufgaben, solange es für die Freizeit keine Zeile in <c>aufgaben_vorlagen_uebernahme</c> gibt. Die Zeile entsteht per
/// <c>INSERT … ON CONFLICT DO NOTHING</c>; nur wenn sie neu ist, wird kopiert: auch zwei gleichzeitige Aufrufe kopieren genau einmal, und später
/// gelöschte Aufgaben kommen nicht wieder. Kein Benutzer-Command; nach dem Abschluss (Phase „gelaufen“) übernimmt er nichts.
/// </summary>
internal sealed record AufgabenVorlagenUebernehmen(Guid FreizeitId) : ICommand<int>, IModulCommand<ProgrammDbContext>;

internal sealed class AufgabenVorlagenUebernehmenHandler(ProgrammDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<AufgabenVorlagenUebernehmen, int>
{
    /// <summary>Liefert die Zahl der übernommenen Vorlagen.</summary>
    public async ValueTask<int> Handle(AufgabenVorlagenUebernehmen command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit)
            || freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return 0;
        }

        var jetzt = uhr.GetUtcNow();
        var neu = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO programm.aufgaben_vorlagen_uebernahme ("FreizeitId", "OrganisationId", "UebernommenAm")
             VALUES ({command.FreizeitId}, {organisationId}, {jetzt})
             ON CONFLICT ("FreizeitId") DO NOTHING
             """,
            cancellationToken);
        if (neu == 0)
        {
            return 0;
        }

        var vorlagen = await db.AufgabenVorlagen.ToListAsync(cancellationToken);
        foreach (var vorlage in vorlagen)
        {
            db.Aufgaben.Add(Aufgabe.AusVorlage(Guid.CreateVersion7(jetzt), command.FreizeitId, vorlage));
        }

        return vorlagen.Count;
    }
}
