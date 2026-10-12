using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Ui.Api;

/// <summary>Ergebnis von <see cref="FreizeitenApiClient.AnlegenAsync"/>: die Kennung oder die Fehler an den Feldern.</summary>
public sealed record FreizeitAnlegenErgebnis(Guid? Id, string? NameFehler, string? ErsterTagFehler, string? LetzterTagFehler);

/// <summary>Ergebnis von <see cref="FreizeitenApiClient.LeitungEinladenAsync"/>: die Kennung der Einladung oder der Fehler an der Mail-Adresse.</summary>
public sealed record LeitungEinladenErgebnis(Guid? Id, string? MailFehler);

/// <summary>
/// Typisierter Client des Moduls Freizeiten (AD-29, handgeschrieben): Freizeit-Kontext, Freizeiten einer Organisation
/// (Liste, Anlegen, Verwalten, Leitung einladen), Leitung übernehmen, Startseite der Freizeit, Warnhinweise zu Nachweisen und die
/// eigenen Freizeiten.
/// <c>403</c> und <c>404</c> beim Laden heißen „nicht verfügbar“ (<c>null</c>); andere Fehler enden in einer
/// <see cref="HttpRequestException"/>.
/// </summary>
public sealed class FreizeitenApiClient(HttpClient http)
{
    /// <summary><c>GET /api/freizeiten/{freizeitId}/kontext</c>: der Kontext oder <c>null</c> („nicht verfügbar“).</summary>
    public Task<FreizeitKontext?> KontextLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitKontext>($"/api/freizeiten/{freizeitId}/kontext", abbruch);

    /// <summary><c>GET /api/organisationen/{orgId}/freizeiten</c>: Freizeiten der Organisation mit erlaubten Aktionen oder <c>null</c>.</summary>
    public Task<FreizeitenListe?> ListeLadenAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitenListe>($"/api/organisationen/{orgId}/freizeiten", abbruch);

    /// <summary><c>POST /api/organisationen/{orgId}/freizeiten</c>: <c>201</c> mit Kennung; <c>400</c> mit den Fehlern an den Feldern.</summary>
    public async Task<FreizeitAnlegenErgebnis> AnlegenAsync(Guid orgId, string? name, DateOnly? ersterTag, DateOnly? letzterTag, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen(name, ersterTag, letzterTag), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            var nameFehler = await ApiFehler.FeldFehlerAsync(antwort, "name", abbruch);
            var ersterFehler = await ApiFehler.FeldFehlerAsync(antwort, "ersterTag", abbruch);
            var letzterFehler = await ApiFehler.FeldFehlerAsync(antwort, "letzterTag", abbruch);
            if (nameFehler is not null || ersterFehler is not null || letzterFehler is not null)
            {
                return new FreizeitAnlegenErgebnis(null, nameFehler, ersterFehler, letzterFehler);
            }
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<FreizeitAngelegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anlegen der Freizeit.");
        return new FreizeitAnlegenErgebnis(inhalt.Id, null, null, null);
    }

    /// <summary><c>GET /api/organisationen/{orgId}/freizeiten/{freizeitId}</c>: Verwaltung der Freizeit oder <c>null</c>.</summary>
    public Task<FreizeitVerwaltung?> VerwaltungLadenAsync(Guid orgId, Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitVerwaltung>($"/api/organisationen/{orgId}/freizeiten/{freizeitId}", abbruch);

    /// <summary><c>POST …/freizeiten/{freizeitId}/leitung/einladungen</c>: <c>202</c> mit Kennung; <c>400</c> mit dem Fehler an <c>mail</c>.</summary>
    public async Task<LeitungEinladenErgebnis> LeitungEinladenAsync(Guid orgId, Guid freizeitId, string? mail, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(
            $"/api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen", new LeitungEinladen(mail), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, "mail", abbruch) is { } mailFehler)
        {
            return new LeitungEinladenErgebnis(null, mailFehler);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<LeitungEingeladen>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Einladen der Leitung.");
        return new LeitungEinladenErgebnis(inhalt.Id, null);
    }

    /// <summary>
    /// <c>POST /api/freizeiten/leitung/annehmen</c>: die Freizeit bei <c>200</c>, <c>null</c> bei <c>404</c> (nicht mehr gültig
    /// oder für eine andere Mail-Adresse). Das Token geht nur im Körper.
    /// </summary>
    public async Task<Guid?> LeitungAnnehmenAsync(string token, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(token), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<LeitungAngenommen>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Annehmen der Leitung.");
        return inhalt.FreizeitId;
    }

    /// <summary><c>GET /api/freizeiten/{freizeitId}</c>: Startseite der Freizeit oder <c>null</c>.</summary>
    public Task<FreizeitAntwort?> LadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitAntwort>($"/api/freizeiten/{freizeitId}", abbruch);

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/nachweis-warnungen</c> (Leitung, FR-11): Personen des Teams mit Warnhinweisen zu
    /// Pflicht-Nachweisen oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<NachweisWarnungen?> NachweisWarnungenLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<NachweisWarnungen>($"/api/freizeiten/{freizeitId}/nachweis-warnungen", abbruch);

    /// <summary><c>GET /api/ich/freizeiten</c>: die eigenen Freizeiten mit den eigenen Rollen.</summary>
    public async Task<List<MeineFreizeit>> MeineLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync("/api/ich/freizeiten", abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<MeineFreizeit>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der eigenen Freizeiten.");
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
