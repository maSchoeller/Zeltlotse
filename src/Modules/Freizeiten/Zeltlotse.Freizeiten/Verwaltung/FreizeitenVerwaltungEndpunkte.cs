using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Leitung;
using Zeltlotse.Freizeiten.Rechte;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Freizeiten.Verwaltung;

/// <summary>
/// <c>GET /api/organisationen/{orgId}/freizeiten</c> (Org-Admin, Org-Überblick): die Freizeiten der Organisation nur mit
/// Name, Zeitraum und Phase (AD-12), nach erstem Tag absteigend, dazu die erlaubten Aktionen (AD-29). Den Mandanten-Kontext
/// hat die Rechteprüfung des Hosts aus der Route gesetzt.
/// </summary>
internal static class FreizeitenListeEndpunkt
{
    public static async Task<Ok<FreizeitenListe>> Laden(
        Guid orgId, FreizeitenLeseDbContext db, IKontextRollen kontextRollen, TimeProvider uhr, CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();
        var freizeiten = await db.Freizeiten
            .OrderByDescending(f => f.ErsterTag)
            .ThenBy(f => f.Name)
            .ThenBy(f => f.Id)
            .Select(f => new { f.Id, f.Name, f.ErsterTag, f.LetzterTag, f.AbgeschlossenAm })
            .ToListAsync(abbruch);
        var rollen = await kontextRollen.OrgRollenAsync(orgId, abbruch);

        return TypedResults.Ok(new FreizeitenListe(
            [
                .. freizeiten.Select(f => new FreizeitZeile(
                    f.Id, f.Name, f.ErsterTag, f.LetzterTag, FreizeitRechte.Api(FreizeitZeiten.Phase(f.ErsterTag, f.LetzterTag, f.AbgeschlossenAm, jetzt)))),
            ],
            FreizeitRechte.ErlaubtInOrganisation(rollen)));
    }
}

/// <summary><c>POST /api/organisationen/{orgId}/freizeiten</c> (Org-Admin): <c>201 {id}</c>, <c>400</c> je Feld.</summary>
internal static class FreizeitAnlegenEndpunkt
{
    public static async Task<Created<FreizeitAngelegt>> Anlegen(
        Guid orgId, FreizeitAnlegen anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var id = await sender.Send(new FreizeitAnlegenCommand(orgId, anfrage.Name, anfrage.ErsterTag, anfrage.LetzterTag, personId), abbruch);
        return TypedResults.Created($"/api/organisationen/{orgId}/freizeiten/{id}", new FreizeitAngelegt(id));
    }
}

/// <summary>
/// <c>GET /api/organisationen/{orgId}/freizeiten/{freizeitId}</c> (Org-Admin): Name, Zeitraum, Phase, die Leitung nur mit
/// Namen (über Personen, keine Mail-Adressen) und die offenen, nicht abgelaufenen Leitungs-Einladungen. Eine Freizeit einer
/// anderen Organisation gibt es unter dem Mandantenfilter nicht: <c>404</c>.
/// </summary>
internal static class FreizeitVerwaltenEndpunkt
{
    public static async Task<Results<Ok<FreizeitVerwaltung>, NotFound>> Laden(
        Guid freizeitId, FreizeitenLeseDbContext db, IPersonenNamen namen, TimeProvider uhr, CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();
        var freizeit = await db.Freizeiten.SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        if (freizeit is null)
        {
            return TypedResults.NotFound();
        }

        var leitung = await LeitungNamen.Async(db, namen, freizeitId, abbruch);
        var einladungen = await db.Einladungen
            .Where(e => e.FreizeitId == freizeitId && e.Status == LeitungsEinladungStatus.Offen && e.GueltigBis > jetzt)
            .OrderBy(e => e.Mail)
            .Select(e => new OffeneLeitungsEinladung(e.Id, e.Mail, e.GueltigBis))
            .ToListAsync(abbruch);

        return TypedResults.Ok(new FreizeitVerwaltung(
            freizeit.Id,
            freizeit.Name,
            freizeit.ErsterTag,
            freizeit.LetzterTag,
            FreizeitRechte.Api(freizeit.Phase(jetzt)),
            [.. leitung.Select(l => new LeitungsPerson(l.PersonId, l.Name?.Vorname, l.Name?.Nachname))],
            einladungen));
    }
}

/// <summary>
/// <c>POST /api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen</c> (Org-Admin): <c>202 {id}</c> und die
/// Einladungs-Mail; <c>400</c> je Feld; eine Freizeit einer anderen Organisation → <c>404</c>.
/// </summary>
internal static class LeitungEinladenEndpunkt
{
    public static async Task<Results<Accepted<LeitungEingeladen>, NotFound>> Einladen(
        Guid orgId,
        Guid freizeitId,
        LeitungEinladen anfrage,
        IAngemeldet angemeldet,
        FreizeitenLeseDbContext db,
        ISender sender,
        IMailVersand mail,
        IConfiguration konfiguration,
        CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        if (!await db.Freizeiten.AnyAsync(f => f.Id == freizeitId, abbruch))
        {
            return TypedResults.NotFound();
        }

        var neu = await sender.Send(new LeitungEinladenCommand(orgId, freizeitId, anfrage.Mail, personId), abbruch);
        await LeitungsMail.VerschickenAsync(mail, konfiguration, neu, abbruch);
        return TypedResults.Accepted((string?)null, new LeitungEingeladen(neu.EinladungId));
    }
}

/// <summary>Die Leitung einer Freizeit mit Namen (über Personen), sortiert nach Nach- und Vorname; fehlende Namen zuletzt.</summary>
internal static class LeitungNamen
{
    public static async Task<IReadOnlyList<(Guid PersonId, PersonenName? Name)>> Async(
        FreizeitenLeseDbContext db, IPersonenNamen namen, Guid freizeitId, CancellationToken abbruch)
    {
        var personen = await db.Rollen
            .Where(r => r.FreizeitId == freizeitId && r.Rolle == SharedKernel.Rechte.FreizeitRolle.Leitung)
            .Select(r => r.PersonId)
            .ToListAsync(abbruch);
        var gefunden = await namen.NamenAsync(personen, abbruch);
        return
        [
            .. personen
                .Select(p => (PersonId: p, Name: gefunden.GetValueOrDefault(p)))
                .OrderBy(p => string.IsNullOrWhiteSpace(p.Name?.Nachname) && string.IsNullOrWhiteSpace(p.Name?.Vorname))
                .ThenBy(p => p.Name?.Nachname ?? "", StringComparer.CurrentCulture)
                .ThenBy(p => p.Name?.Vorname ?? "", StringComparer.CurrentCulture)
                .ThenBy(p => p.PersonId),
        ];
    }
}
