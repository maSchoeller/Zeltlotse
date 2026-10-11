using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Organisationen;

namespace Zeltlotse.Ui.Api;

/// <summary>Ergebnis von <see cref="OrganisationenApiClient.AnlegenAsync"/>: die Kennung oder die Fehler an den Feldern.</summary>
public sealed record AnlegenErgebnis(Guid? Id, string? NameFehler, string? MailFehler);

/// <summary>Ergebnis von <see cref="OrganisationenApiClient.EinladenAsync"/>: die Kennung der Einladung oder die Fehler an den Feldern.</summary>
public sealed record EinladenErgebnis(Guid? Id, string? MailFehler, string? RolleFehler);

/// <summary>
/// Typisierter Client des Moduls Organisationen (AD-29, handgeschrieben): Organisationen (Global Admin), eine Organisation
/// mit Rollen und Einladungen (Org-Admin), Einladung annehmen und die eigenen Organisationen.
/// <c>403</c> und <c>404</c> beim Laden heißen „nicht verfügbar“ (<c>null</c>); andere Fehler enden in einer
/// <see cref="HttpRequestException"/>.
/// </summary>
public sealed class OrganisationenApiClient(HttpClient http)
{
    /// <summary><c>GET /api/organisationen</c>: alle Organisationen oder <c>null</c> ohne Global Admin.</summary>
    public Task<List<OrganisationZeile>?> ListeLadenAsync(CancellationToken abbruch = default) =>
        LadenAsync<List<OrganisationZeile>>("/api/organisationen", abbruch);

    /// <summary><c>POST /api/organisationen</c>: <c>201</c> mit Kennung; <c>400</c> mit den Fehlern an <c>name</c> und <c>mail</c>.</summary>
    public async Task<AnlegenErgebnis> AnlegenAsync(string? name, string? mail, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/organisationen", new OrganisationAnlegen(name, mail), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            var nameFehler = await ApiFehler.FeldFehlerAsync(antwort, "name", abbruch);
            var mailFehler = await ApiFehler.FeldFehlerAsync(antwort, "mail", abbruch);
            if (nameFehler is not null || mailFehler is not null)
            {
                return new AnlegenErgebnis(null, nameFehler, mailFehler);
            }
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<OrganisationAngelegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anlegen der Organisation.");
        return new AnlegenErgebnis(inhalt.Id, null, null);
    }

    /// <summary><c>GET /api/organisationen/{orgId}</c>: Name, eigene Rollen und erlaubte Aktionen oder <c>null</c>.</summary>
    public Task<OrganisationAntwort?> LadenAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<OrganisationAntwort>($"/api/organisationen/{orgId}", abbruch);

    /// <summary><c>GET /api/organisationen/{orgId}/rollen</c>: Personen mit Rolle und offene Einladungen oder <c>null</c>.</summary>
    public Task<RollenAntwort?> RollenLadenAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<RollenAntwort>($"/api/organisationen/{orgId}/rollen", abbruch);

    /// <summary><c>POST /api/organisationen/{orgId}/einladungen</c>: <c>202</c> mit Kennung; <c>400</c> mit den Fehlern an <c>mail</c> und <c>rolle</c>.</summary>
    public async Task<EinladenErgebnis> EinladenAsync(Guid orgId, string? mail, OrgRolle? rolle, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"/api/organisationen/{orgId}/einladungen", new EinladungSchicken(mail, rolle), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            var mailFehler = await ApiFehler.FeldFehlerAsync(antwort, "mail", abbruch);
            var rolleFehler = await ApiFehler.FeldFehlerAsync(antwort, "rolle", abbruch);
            if (mailFehler is not null || rolleFehler is not null)
            {
                return new EinladenErgebnis(null, mailFehler, rolleFehler);
            }
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<EinladungVerschickt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Einladen.");
        return new EinladenErgebnis(inhalt.Id, null, null);
    }

    /// <summary>
    /// <c>POST /api/organisationen/einladungen/annehmen</c>: die Organisation bei <c>200</c>, <c>null</c> bei <c>404</c>
    /// (nicht mehr gültig oder für eine andere Mail-Adresse). Das Token geht nur im Körper.
    /// </summary>
    public async Task<Guid?> AnnehmenAsync(string token, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(token), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<EinladungAngenommen>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Annehmen der Einladung.");
        return inhalt.OrganisationId;
    }

    /// <summary><c>GET /api/ich/organisationen</c>: die eigenen Organisationen mit den eigenen Rollen.</summary>
    public async Task<List<MeineOrganisation>> MeineLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync("/api/ich/organisationen", abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<MeineOrganisation>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der eigenen Organisationen.");
    }

    private async Task<T?> LadenAsync<T>(string pfad, CancellationToken abbruch)
        where T : class
    {
        using var antwort = await http.GetAsync(pfad, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<T>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException($"Leere Antwort von {pfad}.");
    }
}
