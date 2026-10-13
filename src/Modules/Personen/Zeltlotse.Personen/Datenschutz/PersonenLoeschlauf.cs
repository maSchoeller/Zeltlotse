using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Datenschutz;

/// <summary>
/// Täglicher Lösch-Lauf von Personen (AD-16): schickt <see cref="PersonenFristenLoeschen"/> und <see cref="GastProfileLoeschen"/>
/// (abgelaufene, nie übernommene Gast-Links, AD-7).
/// </summary>
internal sealed class PersonenLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        await sender.Send(new PersonenFristenLoeschen(auftrag.Jetzt, auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        await sender.Send(new GastProfileLoeschen(auftrag.Jetzt), abbruch);
    }
}

/// <summary>
/// Fristen von Personen (AD-16, NFR-5): Familien-Einladungen und Profil-Übergaben, deren Ablauf mehr als
/// <see cref="TechnischeFristen.Einladungen"/> zurückliegt, und die Nachweis- und Freizeitpass-Erinnerungen der Freizeiten, deren Daten nach der Frist
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
            await db.FreizeitpassErinnerungen.IgnoreQueryFilters().Where(e => freizeiten.Contains(e.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
