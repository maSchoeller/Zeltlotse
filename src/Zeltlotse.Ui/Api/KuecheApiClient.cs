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

    /// <summary>Story 4-2: <c>409 konflikt</c>, z. B. ein Tausch mit veralteter Version.</summary>
    Konflikt,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 4-1: Ergebnis von „Rezept anlegen“; bei Erfolg das Rezept, sonst die Fehler je Feld.</summary>
public sealed record RezeptAktion(KuecheAktionErgebnis Ergebnis, Rezept? Rezept, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>Story 4-1: Ergebnis von „Zutat hinzufügen“; bei Erfolg die Zutat, sonst die Fehler je Feld.</summary>
public sealed record ZutatAktion(KuecheAktionErgebnis Ergebnis, Zutat? Zutat, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>Story 4-2: Ergebnis von „Gericht vormerken“; bei Erfolg das Gericht, sonst die Fehler je Feld.</summary>
public sealed record GerichtAktion(KuecheAktionErgebnis Ergebnis, GerichtStand? Gericht, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>Story 4-2: Ergebnis von „Posten ergänzen“; bei Erfolg der Posten, sonst die Fehler je Feld.</summary>
public sealed record PostenAktion(KuecheAktionErgebnis Ergebnis, EinkaufslistenPosten? Posten, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>
/// Typisierter Client des Moduls Küche (AD-29, handgeschrieben). Story 4-1: die Rezepte einer Freizeit mit hochgerechneten Mengen (nur mit dem Modul
/// Verpflegung; sonst wie nicht verfügbar). Den Autosave der Felder von Rezept und Zutat übernimmt der Autosave-Baustein mit
/// <see cref="RezeptPfad"/> und <see cref="ZutatPfad"/>. Story 4-2: Essensplan mit Vormerken, Entfernen, Tausch-Vorschau und Tausch, die Einkaufsliste
/// mit Hand-Posten; den Autosave von Hand-Mengen und Hand-Posten übernimmt der Autosave-Baustein mit <see cref="HandMengePfad"/> und
/// <see cref="PostenPfad"/>.
/// </summary>
public sealed class KuecheApiClient(HttpClient http)
{
    /// <summary>Pfad der Rezepte einer Freizeit.</summary>
    public static string RezeptePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/kueche/rezepte";

    /// <summary>Pfad eines Rezepts, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string RezeptPfad(Guid freizeitId, Guid rezeptId) => $"{RezeptePfad(freizeitId)}/{rezeptId}";

    /// <summary>Pfad einer Zutat, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string ZutatPfad(Guid freizeitId, Guid rezeptId, Guid zutatId) => $"{RezeptPfad(freizeitId, rezeptId)}/zutaten/{zutatId}";

    /// <summary>Story 4-2: Pfad des Essensplans.</summary>
    public static string EssensplanPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/kueche/essensplan";

    /// <summary>Story 4-2: Pfad eines Gerichts.</summary>
    public static string GerichtPfad(Guid freizeitId, Guid gerichtId) => $"/api/freizeiten/{freizeitId}/kueche/gerichte/{gerichtId}";

    /// <summary>Story 4-2: Pfad der Einkaufsliste.</summary>
    public static string EinkaufslistePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/kueche/einkaufsliste";

    /// <summary>Story 4-2: Pfad einer Hand-Menge für den Autosave-Baustein (<c>PATCH</c>, Feld <c>menge</c>).</summary>
    public static string HandMengePfad(Guid freizeitId, Guid gerichtId, Guid zutatId) => $"{EinkaufslistePfad(freizeitId)}/gerichte/{gerichtId}/zutaten/{zutatId}";

    /// <summary>Story 4-2: Pfad eines Hand-Postens, auch für den Autosave-Baustein.</summary>
    public static string PostenPfad(Guid freizeitId, Guid postenId) => $"{EinkaufslistePfad(freizeitId)}/posten/{postenId}";

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

    /// <summary>Story 4-2: <c>GET …/kueche/essensplan</c> oder <c>null</c> bei <c>403</c>/<c>404</c> (auch ohne Verpflegung).</summary>
    public Task<Essensplan?> EssensplanLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<Essensplan>(EssensplanPfad(freizeitId), "den Essensplan", abbruch);

    /// <summary>Story 4-2: <c>GET …/kueche/einkaufsliste</c> oder <c>null</c> bei <c>403</c>/<c>404</c> (auch ohne Verpflegung).</summary>
    public Task<Einkaufsliste?> EinkaufslisteLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<Einkaufsliste>(EinkaufslistePfad(freizeitId), "die Einkaufsliste", abbruch);

    /// <summary>Story 4-2: <c>GET …/gerichte/{gerichtId}/tausch?rezeptId=</c>: die Vorschau oder <c>null</c> (Gericht oder Rezept nicht mehr da).</summary>
    public async Task<Tauschvorschau?> TauschvorschauLadenAsync(Guid freizeitId, Guid gerichtId, Guid rezeptId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync($"{GerichtPfad(freizeitId, gerichtId)}/tausch?rezeptId={rezeptId}", abbruch);
        if (antwort.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Tauschvorschau>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zur Tausch-Vorschau.");
    }

    /// <summary>Story 4-2: <c>POST …/kueche/gerichte</c>: vorgemerkt, Feldfehler (<c>400</c>), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<GerichtAktion> GerichtVormerkenAsync(Guid freizeitId, GerichtNeu gericht, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"/api/freizeiten/{freizeitId}/kueche/gerichte", gericht, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var angelegt = await antwort.Content.ReadFromJsonAsync<GerichtStand>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zum Gericht.");
            return new GerichtAktion(KuecheAktionErgebnis.Erfolg, angelegt, new Dictionary<string, string>());
        }

        return new GerichtAktion(await FehlerAsync(antwort, abbruch), null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
    }

    /// <summary>Story 4-2: <c>DELETE …/kueche/gerichte/{gerichtId}</c>: entfernt, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KuecheAktionErgebnis> GerichtEntfernenAsync(Guid freizeitId, Guid gerichtId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(GerichtPfad(freizeitId, gerichtId), abbruch);
        return antwort.IsSuccessStatusCode ? KuecheAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Story 4-2: <c>POST …/gerichte/{gerichtId}/tauschen</c>: getauscht, Konflikt (Version veraltet), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KuecheAktionErgebnis> GerichtTauschenAsync(Guid freizeitId, Guid gerichtId, GerichtTauschen tausch, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{GerichtPfad(freizeitId, gerichtId)}/tauschen", tausch, ApiJson.Optionen, abbruch);
        return antwort.IsSuccessStatusCode ? KuecheAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch, mitKonflikt: true);
    }

    /// <summary>Story 4-2: <c>POST …/einkaufsliste/posten</c>: ergänzt, Feldfehler (<c>400</c>), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<PostenAktion> PostenAnlegenAsync(Guid freizeitId, EinkaufspostenNeu posten, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{EinkaufslistePfad(freizeitId)}/posten", posten, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var angelegt = await antwort.Content.ReadFromJsonAsync<EinkaufslistenPosten>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zum Posten.");
            return new PostenAktion(KuecheAktionErgebnis.Erfolg, angelegt, new Dictionary<string, string>());
        }

        return new PostenAktion(await FehlerAsync(antwort, abbruch), null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
    }

    /// <summary>Story 4-2: <c>DELETE …/einkaufsliste/posten/{postenId}</c>: entfernt, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KuecheAktionErgebnis> PostenLoeschenAsync(Guid freizeitId, Guid postenId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(PostenPfad(freizeitId, postenId), abbruch);
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

    /// <summary>
    /// Ordnet eine Fehlerantwort zu: <c>409 freizeit-abgeschlossen</c>, mit <paramref name="mitKonflikt"/> auch <c>409 konflikt</c> (Story 4-2, Tausch),
    /// alles andere ist ein Fehler.
    /// </summary>
    private static async Task<KuecheAktionErgebnis> FehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch, bool mitKonflikt = false)
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
                ? code.GetString() switch
                {
                    Problemcodes.FreizeitAbgeschlossen => KuecheAktionErgebnis.Abgeschlossen,
                    Problemcodes.Konflikt when mitKonflikt => KuecheAktionErgebnis.Konflikt,
                    _ => KuecheAktionErgebnis.Fehler,
                }
                : KuecheAktionErgebnis.Fehler;
        }
        catch (JsonException)
        {
            return KuecheAktionErgebnis.Fehler;
        }
    }
}
