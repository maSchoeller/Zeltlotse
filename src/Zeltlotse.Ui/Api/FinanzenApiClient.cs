using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 5-1: Ergebnis einer schreibenden Aktion in den Finanzen.</summary>
public enum FinanzenAktionErgebnis
{
    Erfolg,

    /// <summary><c>409 freizeit-abgeschlossen</c>: Die Freizeit ist nur noch lesbar.</summary>
    Abgeschlossen,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 5-1: Ergebnis von „Einnahme hinzufügen“; bei Erfolg der Posten, sonst die Fehler je Feld.</summary>
public sealed record EinnahmeAktion(FinanzenAktionErgebnis Ergebnis, Einnahmeposten? Posten, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>
/// Typisierter Client des Moduls Finanzen (AD-29, handgeschrieben). Story 5-1: die Kalkulation einer Freizeit (nur mit dem Modul Finanzen; sonst wie nicht
/// verfügbar), Einnahmen anlegen und entfernen. Den Autosave von Planzahlen, Kategorien und Einnahmen übernimmt der Autosave-Baustein mit
/// <see cref="KalkulationPfad"/>, <see cref="KategoriePfad"/> und <see cref="EinnahmePfad"/>. Der Beitragsrechner ruft den Server nicht.
/// </summary>
public sealed class FinanzenApiClient(HttpClient http)
{
    /// <summary>Pfad der Kalkulation, auch für den Autosave der Planzahlen (<c>PATCH</c>, AD-19).</summary>
    public static string KalkulationPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/finanzen/kalkulation";

    /// <summary>Pfad einer Kostenkategorie für den Autosave (<c>PATCH</c>).</summary>
    public static string KategoriePfad(Guid freizeitId, Kostenkategorie kategorie) => $"{KalkulationPfad(freizeitId)}/kategorien/{kategorie}";

    /// <summary>Pfad der Einnahmen.</summary>
    public static string EinnahmenPfad(Guid freizeitId) => $"{KalkulationPfad(freizeitId)}/einnahmen";

    /// <summary>Pfad eines Einnahmepostens, auch für den Autosave (<c>PATCH</c>).</summary>
    public static string EinnahmePfad(Guid freizeitId, Guid einnahmeId) => $"{EinnahmenPfad(freizeitId)}/{einnahmeId}";

    /// <summary><c>GET …/finanzen/kalkulation</c>: die Kalkulation oder <c>null</c> bei <c>403</c>/<c>404</c> (auch ohne Modul Finanzen).</summary>
    public async Task<Kalkulation?> KalkulationLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(KalkulationPfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Kalkulation>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort beim Laden: die Kalkulation.");
    }

    /// <summary><c>POST …/einnahmen</c>: angelegt mit dem Posten, Feldfehler (<c>400</c>, je JSON-Feldname), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<EinnahmeAktion> EinnahmeAnlegenAsync(Guid freizeitId, EinnahmepostenNeu einnahme, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(EinnahmenPfad(freizeitId), einnahme, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var angelegt = await antwort.Content.ReadFromJsonAsync<Einnahmeposten>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zur Einnahme.");
            return new EinnahmeAktion(FinanzenAktionErgebnis.Erfolg, angelegt, new Dictionary<string, string>());
        }

        return new EinnahmeAktion(await FehlerAsync(antwort, abbruch), null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
    }

    /// <summary><c>DELETE …/einnahmen/{einnahmeId}</c>: entfernt, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<FinanzenAktionErgebnis> EinnahmeLoeschenAsync(Guid freizeitId, Guid einnahmeId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(EinnahmePfad(freizeitId, einnahmeId), abbruch);
        return antwort.IsSuccessStatusCode ? FinanzenAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Ordnet eine Fehlerantwort zu: <c>409 freizeit-abgeschlossen</c> ist „abgeschlossen“, alles andere ein Fehler.</summary>
    private static async Task<FinanzenAktionErgebnis> FehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode != HttpStatusCode.Conflict)
        {
            return FinanzenAktionErgebnis.Fehler;
        }

        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var code)
                   && code.ValueKind == JsonValueKind.String
                   && code.GetString() == Problemcodes.FreizeitAbgeschlossen
                ? FinanzenAktionErgebnis.Abgeschlossen
                : FinanzenAktionErgebnis.Fehler;
        }
        catch (JsonException)
        {
            return FinanzenAktionErgebnis.Fehler;
        }
    }
}
