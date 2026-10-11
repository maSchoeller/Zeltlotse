using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Passwort;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Anmeldung;

namespace Zeltlotse.Konten.Ich;

/// <summary><c>GET /api/ich/konto</c>: Mail-Adresse und ob ein Passwort festgelegt ist, nur für das eigene Konto (AD-26).</summary>
internal static class KontoLadenEndpunkt
{
    public static async Task<Results<Ok<KontoAntwort>, NotFound>> Laden(IAngemeldet angemeldet, KontenLeseDbContext db, CancellationToken abbruch)
    {
        var kontoId = angemeldet.KontoId;
        var konto = await db.Konten
            .Where(k => k.Id == kontoId)
            .Select(k => new KontoAntwort(k.Mail, k.PasswortHash != null))
            .SingleOrDefaultAsync(abbruch);
        return konto is null ? TypedResults.NotFound() : TypedResults.Ok(konto);
    }
}

/// <summary>
/// <c>PUT /api/ich/konto/passwort</c>: Passwort festlegen oder ändern (10–200 Zeichen). Andere Sitzungen enden; die eigene
/// wird mit dem neuen Sitzungsstempel neu ausgestellt.
/// </summary>
internal static class PasswortFestlegenEndpunkt
{
    public static async Task<NoContent> Festlegen(
        PasswortFestlegen anfrage, IAngemeldet angemeldet, ISender sender, HttpContext kontext, CancellationToken abbruch)
    {
        var kontoId = angemeldet.KontoId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var sitzung = await sender.Send(new PasswortFestlegenCommand(kontoId, anfrage.Passwort), abbruch);
        await SitzungAusstellen.AnmeldenAsync(kontext, sitzung);
        return TypedResults.NoContent();
    }
}
