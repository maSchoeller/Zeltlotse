using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Leitung;
using Zeltlotse.Freizeiten.Rechte;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Freizeiten.Startseite;

/// <summary>
/// <c>POST /api/freizeiten/leitung/annehmen</c> (angemeldet, zugriffsbegrenzt): <c>200 {freizeitId}</c>, sonst <c>404</c>
/// ohne Hinweis auf den Grund. Die Mail-Adresse des Kontos kommt aus der Sitzung (<see cref="IAngemeldeteMail"/>), nie vom
/// Client. Erfolg steht als <c>Rollenvergabe</c> im Sicherheitsprotokoll.
/// </summary>
internal static class LeitungAnnehmenEndpunkt
{
    public static async Task<Results<Ok<LeitungAngenommen>, NotFound>> Annehmen(
        LeitungAnnehmen anfrage, IAngemeldet angemeldet, IAngemeldeteMail angemeldeteMail, ISender sender, ISicherheitsprotokoll protokoll, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var mail = await angemeldeteMail.MailAsync(abbruch);
        var angenommen = await sender.Send(new LeitungAnnehmenCommand(anfrage.Token, mail, personId), abbruch);
        if (angenommen is null)
        {
            return TypedResults.NotFound();
        }

        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Rollenvergabe, personId, angenommen.OrganisationId, angenommen.EinladungId, abbruch);
        return TypedResults.Ok(new LeitungAngenommen(angenommen.FreizeitId));
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}</c> (Leitung, Team): Startseite der Freizeit mit Zeitraum, Phase, letztem Tag des
/// Freigabefensters und der Leitung (Namen; Story 3-1: dazu die Kennung). Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class FreizeitLadenEndpunkt
{
    public static async Task<Results<Ok<FreizeitAntwort>, NotFound>> Laden(
        Guid freizeitId, FreizeitenLeseDbContext db, IPersonenNamen namen, TimeProvider uhr, CancellationToken abbruch)
    {
        var freizeit = await db.Freizeiten.SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        if (freizeit is null)
        {
            return TypedResults.NotFound();
        }

        var leitung = await LeitungNamen.Async(db, namen, freizeitId, abbruch);
        return TypedResults.Ok(new FreizeitAntwort(
            freizeit.Id,
            freizeit.Name,
            freizeit.ErsterTag,
            freizeit.LetzterTag,
            FreizeitRechte.Api(freizeit.Phase(uhr.GetUtcNow())),
            FreizeitZeiten.FreigabeBis(freizeit.LetzterTag, freizeit.FreigabeIntervallTage),
            [.. leitung.Select(l => new LeitungsName(l.Name?.Vorname, l.Name?.Nachname, l.PersonId))]));
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kontext</c> (Leitung, Team, AD-29): Rollen, Phase, Schalter und erlaubte Aktionen aus
/// derselben Auflösung wie die Rechteprüfung des Hosts. Schalter (3-7) gibt es noch nicht (leer); erlaubt sind die Aktionen aus
/// <see cref="FreizeitRechte.Erlaubt"/>.
/// </summary>
internal static class FreizeitKontextEndpunkt
{
    public static async Task<Results<Ok<FreizeitKontext>, NotFound>> Laden(
        Guid freizeitId, FreizeitenLeseDbContext db, IKontextRollen kontextRollen, TimeProvider uhr, CancellationToken abbruch)
    {
        var freizeit = await db.Freizeiten.SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        if (freizeit is null)
        {
            return TypedResults.NotFound();
        }

        var eigene = (await kontextRollen.FreizeitRollenAsync(freizeitId, abbruch)).Order().ToList();
        return TypedResults.Ok(new FreizeitKontext(
            freizeit.Id, freizeit.Name, FreizeitRechte.Api(freizeit.Phase(uhr.GetUtcNow())), [.. eigene.Select(FreizeitRechte.Api)], [], FreizeitRechte.Erlaubt(eigene)));
    }
}

/// <summary>
/// <c>GET /api/ich/freizeiten</c> (AD-26): die eigenen Freizeiten mit den eigenen Rollen über alle Organisationen hinweg,
/// nach erstem Tag absteigend (die neueste zuerst, wie die Liste der Organisation); nur Übersichtsfelder, keine Inhalte einer Freizeit.
/// </summary>
internal static class MeineFreizeitenEndpunkt
{
    public static async Task<Ok<List<MeineFreizeit>>> Laden(IAngemeldet angemeldet, FreizeitenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var jetzt = uhr.GetUtcNow();
        var rollen = await db.Rollen.IgnoreQueryFilters()
            .Where(r => r.PersonId == personId)
            .Select(r => new { r.FreizeitId, r.Rolle })
            .ToListAsync(abbruch);
        var ids = rollen.Select(r => r.FreizeitId).Distinct().ToList();
        var freizeiten = await db.Freizeiten.IgnoreQueryFilters()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.ErsterTag, f.LetzterTag, f.AbgeschlossenAm })
            .ToListAsync(abbruch);

        return TypedResults.Ok(freizeiten
            .OrderByDescending(f => f.ErsterTag)
            .ThenBy(f => f.Name, StringComparer.CurrentCulture)
            .ThenBy(f => f.Id)
            .Select(f => new MeineFreizeit(
                f.Id,
                f.Name,
                f.ErsterTag,
                f.LetzterTag,
                FreizeitRechte.Api(FreizeitZeiten.Phase(f.ErsterTag, f.LetzterTag, f.AbgeschlossenAm, jetzt)),
                [.. rollen.Where(r => r.FreizeitId == f.Id).Select(r => r.Rolle).Order().Select(FreizeitRechte.Api)]))
            .ToList());
    }
}
