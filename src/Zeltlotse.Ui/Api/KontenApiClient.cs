using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Konten;

namespace Zeltlotse.Ui.Api;

/// <summary>Ergebnis von <see cref="KontenApiClient.LoginLinkAnfordernAsync"/>: die Kennung der Anfrage oder der Fehler am Feld „Mail-Adresse“.</summary>
public sealed record LoginLinkErgebnis(Guid? AnfrageId, string? MailFehler);

/// <summary>Typisierter Client des Moduls Konten (AD-29, handgeschrieben): Anmelden, Abmelden, eigenes Konto.</summary>
public sealed class KontenApiClient(HttpClient http)
{
    /// <summary><c>POST /api/konten/login-link</c>: <c>202</c> mit Kennung; <c>400</c> mit dem Fehler am Feld <c>mail</c>.</summary>
    public async Task<LoginLinkErgebnis> LoginLinkAnfordernAsync(string? mail, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/konten/login-link", new LoginLinkAnfordern(mail), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, "mail", abbruch) is { } fehler)
        {
            return new LoginLinkErgebnis(null, fehler);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<LoginLinkAntwort>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anfordern des Login-Links.");
        return new LoginLinkErgebnis(inhalt.AnfrageId, null);
    }

    /// <summary><c>POST /api/konten/login-link/einloesen</c>: <c>true</c> bei <c>204</c> (Sitzung gesetzt), <c>false</c> bei <c>404</c>.</summary>
    public Task<bool> LoginLinkEinloesenAsync(string token, CancellationToken abbruch = default) =>
        AnmeldenAsync("/api/konten/login-link/einloesen", new LoginLinkEinloesen(token), HttpStatusCode.NotFound, abbruch);

    /// <summary><c>POST /api/konten/login-code/einloesen</c>: <c>true</c> bei <c>204</c>, <c>false</c> bei <c>404</c> (falsch oder abgelaufen).</summary>
    public Task<bool> LoginCodeEinloesenAsync(Guid anfrageId, string? code, CancellationToken abbruch = default) =>
        AnmeldenAsync("/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, code?.Trim()), HttpStatusCode.NotFound, abbruch);

    /// <summary><c>POST /api/konten/anmelden</c>: <c>true</c> bei <c>204</c>, <c>false</c> bei <c>401</c>.</summary>
    public Task<bool> MitPasswortAnmeldenAsync(string? mail, string? passwort, CancellationToken abbruch = default) =>
        AnmeldenAsync("/api/konten/anmelden", new PasswortAnmeldung(mail, passwort), HttpStatusCode.Unauthorized, abbruch);

    /// <summary><c>POST /api/konten/abmelden</c>.</summary>
    public async Task AbmeldenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync("/api/konten/abmelden", content: null, abbruch);
        if (antwort.StatusCode != HttpStatusCode.Unauthorized)
        {
            antwort.EnsureSuccessStatusCode();
        }
    }

    /// <summary><c>GET /api/ich/konto</c>.</summary>
    public async Task<KontoAntwort> KontoLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync("/api/ich/konto", abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<KontoAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Kontos.");
    }

    /// <summary><c>PUT /api/ich/konto/passwort</c>: <c>null</c> bei Erfolg, sonst der Fehler am Feld <c>passwort</c>.</summary>
    public async Task<string?> PasswortFestlegenAsync(string? passwort, CancellationToken abbruch = default)
    {
        using var antwort = await http.PutAsJsonAsync("/api/ich/konto/passwort", new PasswortFestlegen(passwort), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, "passwort", abbruch) is { } fehler)
        {
            return fehler;
        }

        antwort.EnsureSuccessStatusCode();
        return null;
    }

    private async Task<bool> AnmeldenAsync<T>(string pfad, T koerper, HttpStatusCode abgelehnt, CancellationToken abbruch)
    {
        using var antwort = await http.PostAsJsonAsync(pfad, koerper, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == abgelehnt)
        {
            return false;
        }

        antwort.EnsureSuccessStatusCode();
        return true;
    }
}
