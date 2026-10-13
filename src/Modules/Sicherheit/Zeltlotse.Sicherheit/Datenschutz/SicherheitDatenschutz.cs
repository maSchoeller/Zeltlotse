using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.Sicherheit.Daten;

namespace Zeltlotse.Sicherheit.Datenschutz;

/// <summary>
/// Story 2-9: Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16): löscht in allen Organisationen die
/// Schutzkonzept-Bestätigungen der Person. Sie beschreiben die Person selbst (wie Team-Notizen über sie), darum kein Platzhalter. Kein
/// Benutzer-Command.
/// </summary>
internal sealed record SicherheitPersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<SicherheitDbContext>, IEreignisEmpfaenger<SicherheitPersonLoeschen, PersonGeloescht>
{
    public static SicherheitPersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);
}

internal sealed class SicherheitPersonLoeschenHandler(SicherheitDbContext db) : ICommandHandler<SicherheitPersonLoeschen>
{
    public async ValueTask<Unit> Handle(SicherheitPersonLoeschen command, CancellationToken cancellationToken)
    {
        await db.SchutzkonzeptBestaetigungen.IgnoreQueryFilters().Where(b => b.PersonId == command.PersonId).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Datenauskunft von Sicherheit (AD-16, Story 2-9): je Schutzkonzept-Bestätigung der Person die Zeile „Als gelesen bestätigt“ mit dem
/// Zeitpunkt; ohne Mandantenfilter, nur diese Zeilen.
/// </summary>
internal sealed class SicherheitAuskunft(SicherheitLeseDbContext db) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var bestaetigungen = await db.SchutzkonzeptBestaetigungen.IgnoreQueryFilters()
            .Where(b => b.PersonId == personId)
            .OrderBy(b => b.GelesenAm)
            .ThenBy(b => b.Id)
            .Select(b => b.GelesenAm)
            .ToListAsync(abbruch);
        return [new AuskunftAbschnitt("Schutzkonzept", [.. bestaetigungen.Select(am => new AuskunftZeile("Als gelesen bestätigt", AuskunftFormat.Zeitpunkt(am)))])];
    }
}

/// <summary>Täglicher Lösch-Lauf von Sicherheit (AD-16): Bestätigungen der Freizeiten, deren Daten nach der Frist abgelaufen sind.</summary>
internal sealed class SicherheitLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new SchutzkonzeptBestaetigungenLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Schutzkonzept-Bestätigungen der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind, endgültig löschen.</summary>
internal sealed record SchutzkonzeptBestaetigungenLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<SicherheitDbContext>;

internal sealed class SchutzkonzeptBestaetigungenLoeschenHandler(SicherheitDbContext db) : ICommandHandler<SchutzkonzeptBestaetigungenLoeschen>
{
    public async ValueTask<Unit> Handle(SchutzkonzeptBestaetigungenLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.SchutzkonzeptBestaetigungen.IgnoreQueryFilters().Where(b => freizeiten.Contains(b.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
