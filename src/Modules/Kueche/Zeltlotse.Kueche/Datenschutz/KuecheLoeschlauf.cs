using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Kueche.Datenschutz;

/// <summary>
/// Story 4-1: Täglicher Lösch-Lauf der Küche (AD-16): die Rezepte samt Zutaten (Story 4-2: dazu Gerichte, Hand-Mengen und Hand-Posten; Story 4-3: der Stand der Posten; Story 4-4: der Restetag der Mahlzeiten) der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen
/// sind. Rezepte sind Arbeitsdaten der Freizeit ohne Personenbezug; Datenauskunft und Empfänger von <c>PersonGeloescht</c> braucht die Küche dafür
/// nicht.
/// </summary>
internal sealed class KuecheLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new RezepteLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Zutaten und Rezepte der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind, endgültig löschen. Kein Benutzer-Command.</summary>
internal sealed record RezepteLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<KuecheDbContext>;

internal sealed class RezepteLoeschenHandler(KuecheDbContext db) : ICommandHandler<RezepteLoeschen>
{
    public async ValueTask<Unit> Handle(RezepteLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        // Story 4-2: Hand-Mengen, Gerichte und Hand-Posten zuerst (sie hängen an Zutaten und Rezepten); Story 4-3: der Stand der Posten davor.
        await db.PostenStaende.IgnoreQueryFilters().Where(s => freizeiten.Contains(s.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        // Story 4-4: der Restetag der Mahlzeiten.
        await db.MahlzeitStaende.IgnoreQueryFilters().Where(s => freizeiten.Contains(s.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.MengenVonHand.IgnoreQueryFilters().Where(h => freizeiten.Contains(h.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Gerichte.IgnoreQueryFilters().Where(g => freizeiten.Contains(g.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Einkaufsposten.IgnoreQueryFilters().Where(p => freizeiten.Contains(p.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Zutaten.IgnoreQueryFilters().Where(z => freizeiten.Contains(z.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Rezepte.IgnoreQueryFilters().Where(r => freizeiten.Contains(r.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
