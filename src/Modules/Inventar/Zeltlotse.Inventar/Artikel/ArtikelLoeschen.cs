using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Inventar.Artikel;

/// <summary>
/// Story 3-3: Löscht einen Artikel (Org-Admin). Hat er noch Reservierungen, bleibt er: sonst verschwänden die Reservierungen still und ein
/// verknüpftes Material bliebe „vorhanden“. Die Zeilensperre am Artikel lässt ein gleichzeitiges Reservieren vorher oder nachher laufen.
/// </summary>
internal sealed record ArtikelLoeschenCommand(Guid ArtikelId) : ICommand<ArtikelLoeschen>, IModulCommand<InventarDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="ArtikelLoeschenCommand"/>.</summary>
internal enum ArtikelLoeschen
{
    Geloescht,
    Reserviert,
    NichtGefunden,
}

internal sealed class ArtikelLoeschenHandler(InventarDbContext db) : ICommandHandler<ArtikelLoeschenCommand, ArtikelLoeschen>
{
    public async ValueTask<ArtikelLoeschen> Handle(ArtikelLoeschenCommand command, CancellationToken cancellationToken)
    {
        var artikel = await db.ArtikelSperrenAsync(command.ArtikelId, cancellationToken);
        if (artikel is null)
        {
            return ArtikelLoeschen.NichtGefunden;
        }

        if (await db.Reservierungen.AnyAsync(r => r.ArtikelId == artikel.Id, cancellationToken))
        {
            return ArtikelLoeschen.Reserviert;
        }

        db.Artikel.Remove(artikel);
        return ArtikelLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/organisationen/{orgId}/inventar/{artikelId}</c> (Org-Admin): <c>204</c>; mit Reservierungen <c>409 konflikt</c>; unbekannt
/// oder aus einer anderen Organisation <c>404</c>.
/// </summary>
internal static class ArtikelLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid artikelId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new ArtikelLoeschenCommand(artikelId), abbruch) switch
        {
            ArtikelLoeschen.Geloescht => TypedResults.NoContent(),
            ArtikelLoeschen.Reserviert => InventarProbleme.Konflikt("Noch reserviert."),
            _ => TypedResults.NotFound(),
        };
}
