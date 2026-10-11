using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Passwort;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Anmeldung;

namespace Zeltlotse.Konten.Ich;

/// <summary>
/// <c>GET /api/ich/konto</c>: Mail-Adresse, ob ein Passwort festgelegt ist, und die erlaubten Aktionen (AD-29), nur für das
/// eigene Konto (AD-26). <c>organisationen-anlegen</c> steht genau dann darin, wenn das Konto Global Admin ist – dasselbe
/// Kennzeichen prüft der Host für <c>.NurGlobalAdmin()</c>.
/// </summary>
internal static class KontoLadenEndpunkt
{
    public static async Task<Results<Ok<KontoAntwort>, NotFound>> Laden(IAngemeldet angemeldet, KontenLeseDbContext db, CancellationToken abbruch)
    {
        var kontoId = angemeldet.KontoId;
        var konto = await db.Konten
            .Where(k => k.Id == kontoId)
            .Select(k => new { k.Mail, HatPasswort = k.PasswortHash != null, k.GlobalAdmin })
            .SingleOrDefaultAsync(abbruch);
        if (konto is null)
        {
            return TypedResults.NotFound();
        }

        IReadOnlyList<string> erlaubt = konto.GlobalAdmin ? [Aktionsschluessel.OrganisationenAnlegen] : [];
        return TypedResults.Ok(new KontoAntwort(konto.Mail, konto.HatPasswort, erlaubt));
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
