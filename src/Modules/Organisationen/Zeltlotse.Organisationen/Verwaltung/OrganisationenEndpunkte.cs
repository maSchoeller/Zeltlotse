using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Einladungen;
using Zeltlotse.Organisationen.Rollen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Organisationen.Verwaltung;

/// <summary><c>GET /api/organisationen</c> (Global Admin): alle Organisationen, nach Name sortiert, ohne Personendaten.</summary>
internal static class OrganisationenListeEndpunkt
{
    public static async Task<Ok<List<OrganisationZeile>>> Laden(OrganisationenLeseDbContext db, CancellationToken abbruch)
    {
        var liste = await db.Organisationen
            .OrderBy(o => o.Name)
            .ThenBy(o => o.Id)
            .Select(o => new OrganisationZeile(o.Id, o.Name, o.EingerichtetAm != null))
            .ToListAsync(abbruch);
        return TypedResults.Ok(liste);
    }
}

/// <summary>
/// <c>POST /api/organisationen</c> (Global Admin): legt die Organisation an und lädt den ersten Org-Admin ein; die
/// Einladungs-Mail geht direkt nach dem erfolgreichen Command raus (AD-13). <c>201 {id}</c>, <c>400</c> je Feld.
/// </summary>
internal static class OrganisationAnlegenEndpunkt
{
    public static async Task<Created<OrganisationAngelegt>> Anlegen(
        OrganisationAnlegen anfrage, IAngemeldet angemeldet, ISender sender, IMailVersand mail, IConfiguration konfiguration, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var neu = await sender.Send(new OrganisationAnlegenCommand(anfrage.Name, anfrage.Mail, personId), abbruch);
        await EinladungVerschicken.Async(mail, konfiguration, neu, abbruch);
        return TypedResults.Created($"/api/organisationen/{neu.OrganisationId}", new OrganisationAngelegt(neu.OrganisationId));
    }
}

/// <summary>
/// <c>GET /api/organisationen/{orgId}</c> (Org-Admin, Org-Überblick): Name, eigene Rollen und erlaubte Aktionen (AD-29).
/// Die Rechteprüfung des Hosts hat den Mandanten-Kontext schon gesetzt.
/// </summary>
internal static class OrganisationLadenEndpunkt
{
    public static async Task<Results<Ok<OrganisationAntwort>, NotFound>> Laden(
        Guid orgId, OrganisationenLeseDbContext db, IKontextRollen kontextRollen, CancellationToken abbruch)
    {
        var name = await db.Organisationen.Where(o => o.Id == orgId).Select(o => o.Name).SingleOrDefaultAsync(abbruch);
        if (name is null)
        {
            return TypedResults.NotFound();
        }

        var rollen = (await kontextRollen.OrgRollenAsync(orgId, abbruch)).Order().ToList();
        return TypedResults.Ok(new OrganisationAntwort(orgId, name, [.. rollen.Select(OrgRechte.Api)], OrgRechte.Erlaubt(rollen)));
    }
}

/// <summary>
/// <c>GET /api/organisationen/{orgId}/rollen</c> (Org-Admin): Personen mit Rolle (nur Namen über Personen, keine
/// Mail-Adressen) und die offenen, nicht abgelaufenen Einladungen.
/// </summary>
internal static class RollenLadenEndpunkt
{
    public static async Task<Ok<RollenAntwort>> Laden(
        OrganisationenLeseDbContext db, Personen.Contracts.Namen.IPersonenNamen namen, TimeProvider uhr, CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();
        var rollen = await db.Rollen.Select(r => new { r.PersonId, r.Rolle }).ToListAsync(abbruch);
        var personen = await namen.NamenAsync([.. rollen.Select(r => r.PersonId).Distinct()], abbruch);
        var einladungen = await db.Einladungen
            .Where(e => e.Status == Domaene.OrgEinladungStatus.Offen && e.GueltigBis > jetzt)
            .OrderBy(e => e.Mail)
            .ThenBy(e => e.Rolle)
            .Select(e => new { e.Id, e.Mail, e.Rolle, e.GueltigBis })
            .ToListAsync(abbruch);

        return TypedResults.Ok(new RollenAntwort(
            [
                .. rollen
                    .Select(r => (r.PersonId, r.Rolle, Name: personen.GetValueOrDefault(r.PersonId)))
                    .OrderBy(r => string.IsNullOrWhiteSpace(r.Name?.Nachname) && string.IsNullOrWhiteSpace(r.Name?.Vorname))
                    .ThenBy(r => r.Name?.Nachname ?? "", StringComparer.CurrentCulture)
                    .ThenBy(r => r.Name?.Vorname ?? "", StringComparer.CurrentCulture)
                    .ThenBy(r => r.PersonId)
                    .ThenBy(r => r.Rolle)
                    .Select(r => new RollenPerson(r.PersonId, r.Name?.Vorname, r.Name?.Nachname, OrgRechte.Api(r.Rolle))),
            ],
            [.. einladungen.Select(e => new OffeneEinladung(e.Id, e.Mail, OrgRechte.Api(e.Rolle), e.GueltigBis))]));
    }
}

/// <summary><c>POST /api/organisationen/{orgId}/einladungen</c> (Org-Admin): <c>202 {id}</c> und die Einladungs-Mail; <c>400</c> je Feld.</summary>
internal static class EinladungSchickenEndpunkt
{
    public static async Task<Accepted<EinladungVerschickt>> Schicken(
        Guid orgId, EinladungSchicken anfrage, IAngemeldet angemeldet, ISender sender, IMailVersand mail, IConfiguration konfiguration, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var neu = await sender.Send(new EinladungAnlegenCommand(orgId, anfrage.Mail, OrgRechte.Server(anfrage.Rolle), personId), abbruch);
        await EinladungVerschicken.Async(mail, konfiguration, neu, abbruch);
        return TypedResults.Accepted((string?)null, new EinladungVerschickt(neu.EinladungId));
    }
}

/// <summary>
/// <c>POST /api/organisationen/einladungen/annehmen</c> (angemeldet, zugriffsbegrenzt): <c>200 {organisationId}</c>, sonst
/// <c>404</c> ohne Hinweis auf den Grund. Erfolg steht als <c>Rollenvergabe</c> im Sicherheitsprotokoll.
/// </summary>
internal static class EinladungAnnehmenEndpunkt
{
    public static async Task<Results<Ok<EinladungAngenommen>, NotFound>> Annehmen(
        EinladungAnnehmen anfrage, IAngemeldet angemeldet, ISender sender, SharedKernel.Sicherheitsprotokoll.ISicherheitsprotokoll protokoll, CancellationToken abbruch)
    {
        var kontoId = angemeldet.KontoId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        // Gleichzeitige Annahmen laufen nacheinander (Zeilensperre im Handler); nur die erste gelingt.
        var angenommen = await sender.Send(new EinladungAnnehmenCommand(anfrage.Token, kontoId, personId), abbruch);
        if (angenommen is null)
        {
            return TypedResults.NotFound();
        }

        await protokoll.SchreibenAsync(
            SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt.Rollenvergabe, personId, angenommen.OrganisationId, angenommen.EinladungId, abbruch);
        return TypedResults.Ok(new EinladungAngenommen(angenommen.OrganisationId));
    }
}

/// <summary>
/// <c>GET /api/ich/organisationen</c> (AD-26): die eigenen Organisationen mit den eigenen Rollen, über alle Organisationen
/// hinweg – der einzige Lesepfad neben der Token-Suche ohne Mandantenfilter.
/// </summary>
internal static class MeineOrganisationenEndpunkt
{
    public static async Task<Ok<List<MeineOrganisation>>> Laden(IAngemeldet angemeldet, OrganisationenLeseDbContext db, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var rollen = await db.Rollen.IgnoreQueryFilters()
            .Where(r => r.PersonId == personId)
            .Select(r => new { r.OrganisationId, r.Rolle })
            .ToListAsync(abbruch);
        var ids = rollen.Select(r => r.OrganisationId).Distinct().ToList();
        var organisationen = await db.Organisationen.Where(o => ids.Contains(o.Id)).Select(o => new { o.Id, o.Name }).ToListAsync(abbruch);

        return TypedResults.Ok(organisationen
            .OrderBy(o => o.Name, StringComparer.CurrentCulture)
            .ThenBy(o => o.Id)
            .Select(o => new MeineOrganisation(
                o.Id,
                o.Name,
                [.. rollen.Where(r => r.OrganisationId == o.Id).Select(r => r.Rolle).Order().Select(OrgRechte.Api)]))
            .ToList());
    }
}

/// <summary>Schickt die Einladungs-Mail; Mail-Adresse und Token erscheinen nie in Logs (AD-22).</summary>
internal static class EinladungVerschicken
{
    public static Task Async(IMailVersand mail, IConfiguration konfiguration, NeueEinladung neu, CancellationToken abbruch)
    {
        var adresse = OeffentlicheAdresse.Lesen(konfiguration);
        return mail.SendenAsync(neu.Mail, EinladungsMail.Betreff, EinladungsMail.Text(adresse, neu.OrganisationName, neu.Rolle, neu.Token), abbruch);
    }
}
