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
/// Ergebnis von <see cref="OrganisationenApiClient.OrgDokumentHochladenAsync"/> (Story 2-7): das Org-Dokument, der Fehler am Feld
/// <c>datei</c> oder nicht verfügbar (<c>403</c>/<c>404</c>).
/// </summary>
public sealed record OrgDokumentHochladenErgebnis(OrgDokument? Dokument, string? DateiFehler, bool NichtVerfuegbar);

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

    /// <summary><c>GET /api/organisationen/{orgId}/nachweis-richtlinie</c>: je Art Pflicht und Gültigkeit oder <c>null</c>.</summary>
    public Task<List<RichtlinieZeile>?> RichtlinieLadenAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<List<RichtlinieZeile>>($"/api/organisationen/{orgId}/nachweis-richtlinie", abbruch);

    /// <summary>Pfad einer Richtlinien-Zeile für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string RichtliniePfad(Guid orgId, NachweisArt art) =>
        $"/api/organisationen/{orgId}/nachweis-richtlinie/{RichtlinieFelder.JsonName(art)}";

    /// <summary><c>GET /api/organisationen/{orgId}/loeschfristen</c>: beide Fristen mit Version oder <c>null</c> (<c>403</c>/<c>404</c>).</summary>
    public Task<LoeschfristenAntwort?> LoeschfristenLadenAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<LoeschfristenAntwort>(LoeschfristenPfad(orgId), abbruch);

    /// <summary>Pfad der Löschfristen, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string LoeschfristenPfad(Guid orgId) => $"/api/organisationen/{orgId}/loeschfristen";

    /// <summary><c>GET /api/ich/organisationen/nachweis-bestaetigungen</c>: die eigenen Bestätigungen aller Organisationen.</summary>
    public async Task<List<NachweisBestaetigungZeile>> BestaetigungenLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync("/api/ich/organisationen/nachweis-bestaetigungen", abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<NachweisBestaetigungZeile>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Bestätigungen.");
    }

    /// <summary>
    /// <c>GET /api/ich/organisationen/namen?id=…</c>: die Namen der genannten Organisationen (höchstens
    /// <see cref="OrganisationsNamen.HoechstensKennungen"/> je Abfrage; mehr werden in Teilen abgefragt).
    /// </summary>
    public async Task<Dictionary<Guid, string>> NamenLadenAsync(IEnumerable<Guid> kennungen, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(kennungen);
        var namen = new Dictionary<Guid, string>();
        foreach (var teil in kennungen.Distinct().Chunk(OrganisationsNamen.HoechstensKennungen))
        {
            var abfrage = string.Join("&", teil.Select(id => $"{OrganisationsNamen.Parameter}={id}"));
            using var antwort = await http.GetAsync($"/api/ich/organisationen/namen?{abfrage}", abbruch);
            antwort.EnsureSuccessStatusCode();
            foreach (var zeile in await antwort.Content.ReadFromJsonAsync<List<OrganisationName>>(ApiJson.Optionen, abbruch) ?? [])
            {
                namen[zeile.OrganisationId] = zeile.Name;
            }
        }

        return namen;
    }

    // ---------- Story 2-7: Org-Dokumente ----------

    /// <summary>Pfad der Org-Dokumente einer Organisation.</summary>
    public static string OrgDokumentePfad(Guid orgId) => $"/api/organisationen/{orgId}/dokumente";

    /// <summary>Pfad der Datei eines Org-Dokuments, für einen Link; die API liefert sie als Anhang.</summary>
    public static string OrgDokumentDateiPfad(Guid orgId, Guid dokumentId) => $"{OrgDokumentePfad(orgId)}/{dokumentId}/datei";

    /// <summary><c>GET /api/organisationen/{orgId}/dokumente</c> (Org-Admin): die Org-Dokumente nach Titel oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<OrgDokumente?> OrgDokumenteLadenAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<OrgDokumente>(OrgDokumentePfad(orgId), abbruch);

    /// <summary>
    /// <c>POST /api/organisationen/{orgId}/dokumente</c> (Multipart <c>datei</c>): das Org-Dokument bei <c>201</c>, der Fehler am Feld bei
    /// <c>400</c> oder nicht verfügbar (<c>403</c>/<c>404</c>). Netzfehler enden in einer <see cref="HttpRequestException"/>.
    /// </summary>
    public async Task<OrgDokumentHochladenErgebnis> OrgDokumentHochladenAsync(Guid orgId, Stream inhalt, string dateiname, string? contentType, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        using var datei = new StreamContent(inhalt);
        datei.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        using var formular = new MultipartFormDataContent { { datei, "datei", string.IsNullOrWhiteSpace(dateiname) ? "datei" : dateiname } };
        using var antwort = await http.PostAsync(OrgDokumentePfad(orgId), formular, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, "datei", abbruch) is { } fehler)
        {
            return new OrgDokumentHochladenErgebnis(null, fehler, NichtVerfuegbar: false);
        }

        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return new OrgDokumentHochladenErgebnis(null, null, NichtVerfuegbar: true);
        }

        antwort.EnsureSuccessStatusCode();
        var dokument = await antwort.Content.ReadFromJsonAsync<OrgDokument>(ApiJson.Optionen, abbruch)
                       ?? throw new HttpRequestException("Leere Antwort beim Hochladen des Org-Dokuments.");
        return new OrgDokumentHochladenErgebnis(dokument, null, NichtVerfuegbar: false);
    }

    /// <summary><c>DELETE /api/organisationen/{orgId}/dokumente/{dokumentId}</c>: <c>true</c> bei <c>204</c>, <c>false</c> bei <c>403</c>/<c>404</c>.</summary>
    public async Task<bool> OrgDokumentEntfernenAsync(Guid orgId, Guid dokumentId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync($"{OrgDokumentePfad(orgId)}/{dokumentId}", abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return false;
        }

        antwort.EnsureSuccessStatusCode();
        return true;
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
