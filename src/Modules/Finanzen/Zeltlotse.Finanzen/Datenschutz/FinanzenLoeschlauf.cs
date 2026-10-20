using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Finanzen.Belege;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Finanzen.Datenschutz;

/// <summary>
/// Story 5-1: Täglicher Lösch-Lauf der Finanzen (AD-16): Planzahlen, Kategorien und Einnahmeposten der Kalkulation der Freizeiten, deren Daten nach der
/// Frist der Organisation abgelaufen sind. Story 5-2: dazu die Belege (aufbewahrungspflichtig) samt ihren Dateien, zuerst die Dateien, dann die Zeilen. Eine
/// eigene Belegfrist gibt es nicht; es gilt die Frist der Freizeit-Daten. Story 5-3: dazu die Vermerke „Beitrag bezahlt“ (aufbewahrungspflichtig). Datenauskunft und Empfänger von <c>PersonGeloescht</c> stehen in
/// <see cref="FinanzenAuskunft"/> und <see cref="FinanzenPersonLoeschen"/>.
/// </summary>
internal sealed class FinanzenLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new KalkulationenLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>
/// Kalkulation, Belege (mit Dateien) und Vermerke „Beitrag bezahlt“ der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind, endgültig löschen. Kein
/// Benutzer-Command.
/// </summary>
internal sealed record KalkulationenLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<FinanzenDbContext>;

internal sealed class KalkulationenLoeschenHandler(FinanzenDbContext db, IDateiSpeicher speicher) : ICommandHandler<KalkulationenLoeschen>
{
    public async ValueTask<Unit> Handle(KalkulationenLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        var mitDatei = await db.Belege.IgnoreQueryFilters()
            .Where(b => freizeiten.Contains(b.FreizeitId) && b.DateiArt != null)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
        foreach (var beleg in mitDatei)
        {
            await speicher.LoeschenAsync(Beleg.DateiPraefix, beleg, cancellationToken);
        }

        await db.Belege.IgnoreQueryFilters().Where(b => freizeiten.Contains(b.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.BeitragVermerke.IgnoreQueryFilters().Where(v => freizeiten.Contains(v.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Einnahmeposten.IgnoreQueryFilters().Where(e => freizeiten.Contains(e.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.KategoriePosten.IgnoreQueryFilters().Where(k => freizeiten.Contains(k.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Kalkulationen.IgnoreQueryFilters().Where(k => freizeiten.Contains(k.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
