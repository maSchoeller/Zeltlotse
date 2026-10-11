using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Personen;

namespace Zeltlotse.Ui.Api;

/// <summary>Typisierter Client des Moduls Personen (AD-29, handgeschrieben); bisher das eigene Profil.</summary>
public sealed class PersonenApiClient(HttpClient http)
{
    /// <summary>Pfad des eigenen Profils, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public const string ProfilPfad = "/api/ich/profil";

    /// <summary><c>GET /api/ich/profil</c>: das Profil oder <c>null</c> (<c>404</c>), solange die Person noch nicht angelegt ist.</summary>
    public async Task<ProfilAntwort?> ProfilLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(ProfilPfad, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<ProfilAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Profils.");
    }

    /// <summary><c>POST /api/ich/profil</c>: legt die Person an, falls sie fehlt (idempotent), und liefert das Profil.</summary>
    public async Task<ProfilAntwort> ProfilSicherstellenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync(ProfilPfad, content: null, abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<ProfilAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Anlegen des Profils.");
    }

    /// <summary>Lädt das Profil und legt es an, falls es fehlt.</summary>
    public async Task<ProfilAntwort> ProfilLadenOderAnlegenAsync(CancellationToken abbruch = default) =>
        await ProfilLadenAsync(abbruch) ?? await ProfilSicherstellenAsync(abbruch);
}
