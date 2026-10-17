using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Kueche.Datenschutz;

/// <summary>
/// Story 4-1: Täglicher Lösch-Lauf der Küche (AD-16): die Rezepte samt Zutaten der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen
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
        await db.Zutaten.IgnoreQueryFilters().Where(z => freizeiten.Contains(z.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        await db.Rezepte.IgnoreQueryFilters().Where(r => freizeiten.Contains(r.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
