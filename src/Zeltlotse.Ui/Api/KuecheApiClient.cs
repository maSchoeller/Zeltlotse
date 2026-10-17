using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Kueche;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 4-1: Ergebnis einer schreibenden Aktion in der Küche.</summary>
public enum KuecheAktionErgebnis
{
    Erfolg,

    /// <summary><c>409 freizeit-abgeschlossen</c>: Die Freizeit ist nur noch lesbar.</summary>
    Abgeschlossen,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 4-1: Ergebnis von „Rezept anlegen“; bei Erfolg das Rezept, sonst die Fehler je Feld.</summary>
public sealed record RezeptAktion(KuecheAktionErgebnis Ergebnis, Rezept? Rezept, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>Story 4-1: Ergebnis von „Zutat hinzufügen“; bei Erfolg die Zutat, sonst die Fehler je Feld.</summary>
public sealed record ZutatAktion(KuecheAktionErgebnis Ergebnis, Zutat? Zutat, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>
/// Typisierter Client des Moduls Küche (AD-29, handgeschrieben). Story 4-1: die Rezepte einer Freizeit mit hochgerechneten Mengen (nur mit dem Modul
/// Verpflegung; sonst wie nicht verfügbar). Den Autosave der Felder von Rezept und Zutat übernimmt der Autosave-Baustein mit
/// <see cref="RezeptPfad"/> und <see cref="ZutatPfad"/>.
/// </summary>
public sealed class KuecheApiClient(HttpClient http)
{
    /// <summary>Pfad der Rezepte einer Freizeit.</summary>
    public static string RezeptePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/kueche/rezepte";

    /// <summary>Pfad eines Rezepts, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string RezeptPfad(Guid freizeitId, Guid rezeptId) => $"{RezeptePfad(freizeitId)}/{rezeptId}";

    /// <summary>Pfad einer Zutat, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string ZutatPfad(Guid freizeitId, Guid rezeptId, Guid zutatId) => $"{RezeptPfad(freizeitId, rezeptId)}/zutaten/{zutatId}";

    /// <summary><c>GET …/kueche/rezepte</c>: die Rezepte und die Personenzahl oder <c>null</c> bei <c>403</c>/<c>404</c> (auch ohne Verpflegung).</summary>
    public Task<Rezeptliste?> RezepteLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<Rezeptliste>(RezeptePfad(freizeitId), "die Rezepte", abbruch);

    /// <summary><c>GET …/kueche/rezepte/{rezeptId}</c>: das Rezept mit hochgerechneten Mengen oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<Rezept?> RezeptLadenAsync(Guid freizeitId, Guid rezeptId, CancellationToken abbruch = default) =>
        LadenAsync<Rezept>(RezeptPfad(freizeitId, rezeptId), "das Rezept", abbruch);

    /// <summary><c>POST …/kueche/rezepte</c>: angelegt mit dem Rezept, Feldfehler (<c>400</c>, je JSON-Feldname), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<RezeptAktion> RezeptAnlegenAsync(Guid freizeitId, RezeptNeu rezept, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(RezeptePfad(freizeitId), rezept, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var angelegt = await antwort.Content.ReadFromJsonAsync<Rezept>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zum Rezept.");
            return new RezeptAktion(KuecheAktionErgebnis.Erfolg, angelegt, new Dictionary<string, string>());
        }

        return new RezeptAktion(await FehlerAsync(antwort, abbruch), null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
    }

    /// <summary><c>POST …/rezepte/{rezeptId}/zutaten</c>: angelegt mit der Zutat, Feldfehler (<c>400</c>), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<ZutatAktion> ZutatAnlegenAsync(Guid freizeitId, Guid rezeptId, ZutatNeu zutat, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{RezeptPfad(freizeitId, rezeptId)}/zutaten", zutat, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var angelegt = await antwort.Content.ReadFromJsonAsync<Zutat>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zur Zutat.");
            return new ZutatAktion(KuecheAktionErgebnis.Erfolg, angelegt, new Dictionary<string, string>());
        }

        return new ZutatAktion(await FehlerAsync(antwort, abbruch), null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
    }

    /// <summary><c>DELETE …/kueche/rezepte/{rezeptId}</c>: gelöscht (samt Zutaten), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KuecheAktionErgebnis> RezeptLoeschenAsync(Guid freizeitId, Guid rezeptId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(RezeptPfad(freizeitId, rezeptId), abbruch);
        return antwort.IsSuccessStatusCode ? KuecheAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary><c>DELETE …/zutaten/{zutatId}</c>: entfernt, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KuecheAktionErgebnis> ZutatLoeschenAsync(Guid freizeitId, Guid rezeptId, Guid zutatId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(ZutatPfad(freizeitId, rezeptId, zutatId), abbruch);
        return antwort.IsSuccessStatusCode ? KuecheAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    private async Task<T?> LadenAsync<T>(string pfad, string was, CancellationToken abbruch)
        where T : class
    {
        using var antwort = await http.GetAsync(pfad, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<T>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException($"Leere Antwort beim Laden: {was}.");
    }

    /// <summary>Ordnet eine Fehlerantwort zu: <c>409 freizeit-abgeschlossen</c>, alles andere ist ein Fehler.</summary>
    private static async Task<KuecheAktionErgebnis> FehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode != HttpStatusCode.Conflict)
        {
            return KuecheAktionErgebnis.Fehler;
        }

        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var code)
                   && code.ValueKind == JsonValueKind.String
                   && code.GetString() == Problemcodes.FreizeitAbgeschlossen
                ? KuecheAktionErgebnis.Abgeschlossen
                : KuecheAktionErgebnis.Fehler;
        }
        catch (JsonException)
        {
            return KuecheAktionErgebnis.Fehler;
        }
    }
}
