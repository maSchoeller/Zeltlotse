using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Datenschutz;

/// <summary>Täglicher Lösch-Lauf von Personen (AD-16): schickt nur <see cref="PersonenFristenLoeschen"/>.</summary>
internal sealed class PersonenLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch) =>
        await sender.Send(new PersonenFristenLoeschen(auftrag.Jetzt, auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
}

/// <summary>
/// Fristen von Personen (AD-16, NFR-5): Familien-Einladungen und Profil-Übergaben, deren Ablauf mehr als
/// <see cref="TechnischeFristen.Einladungen"/> zurückliegt, und die Nachweis-Erinnerungen der Freizeiten, deren Daten nach der Frist
/// der Organisation abgelaufen sind (Arbeitsdaten der Freizeit). Über alle Organisationen hinweg; kein Benutzer-Command.
/// </summary>
internal sealed record PersonenFristenLoeschen(DateTimeOffset Jetzt, IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<PersonenDbContext>;

internal sealed class PersonenFristenLoeschenHandler(PersonenDbContext db) : ICommandHandler<PersonenFristenLoeschen>
{
    public async ValueTask<Unit> Handle(PersonenFristenLoeschen command, CancellationToken cancellationToken)
    {
        var grenze = command.Jetzt - TechnischeFristen.Einladungen;
        await db.FamilienEinladungen.Where(e => e.GueltigBis < grenze).ExecuteDeleteAsync(cancellationToken);
        await db.Uebergaben.Where(u => u.GueltigBis < grenze).ExecuteDeleteAsync(cancellationToken);
        if (command.FreizeitIds.Count > 0)
        {
            var freizeiten = command.FreizeitIds.ToList();
            await db.Erinnerungen.IgnoreQueryFilters().Where(e => freizeiten.Contains(e.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
