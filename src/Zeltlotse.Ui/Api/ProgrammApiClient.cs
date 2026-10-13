using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 3-1: Ergebnis einer schreibenden Aktion im Programm.</summary>
public enum ProgrammAktionErgebnis
{
    Erfolg,

    /// <summary><c>409 konflikt</c>: z. B. eine andere Person hat den Punkt übernommen, oder eine Mahlzeit lässt sich nicht löschen.</summary>
    Konflikt,

    /// <summary><c>409 freizeit-abgeschlossen</c>: Die Freizeit ist nur noch lesbar.</summary>
    Abgeschlossen,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 3-1: Ergebnis von Anlegen, Übernehmen und Abgeben; bei Erfolg der Programmpunkt, beim Anlegen sonst die Fehler je Feld.</summary>
public sealed record ProgrammAktion(ProgrammAktionErgebnis Ergebnis, Programmpunkt? Punkt, IReadOnlyDictionary<string, string> Feldfehler)
{
    public static ProgrammAktion Ohne(ProgrammAktionErgebnis ergebnis) => new(ergebnis, null, new Dictionary<string, string>());
}

/// <summary>
/// Typisierter Client des Moduls Programm (AD-29, handgeschrieben). Story 3-1: der Wochenraster einer Freizeit und ihre Programmpunkte; den
/// Autosave von Titel und Uhrzeit übernimmt der Autosave-Baustein mit <see cref="PunktPfad"/>.
/// </summary>
public sealed class ProgrammApiClient(HttpClient http)
{
    /// <summary>Pfad des Wochenrasters einer Freizeit.</summary>
    public static string ProgrammPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/programm";

    /// <summary>Pfad eines Programmpunkts, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string PunktPfad(Guid freizeitId, Guid punktId) => $"{ProgrammPfad(freizeitId)}/punkte/{punktId}";

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/programm</c> (Leitung und Team, FR-34): der Wochenraster mit allen Programmpunkten oder <c>null</c> bei
    /// <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<Wochenraster?> LadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(ProgrammPfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Wochenraster>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Wochenrasters.");
    }

    /// <summary>
    /// <c>POST …/programm/punkte</c>: angelegt mit dem Programmpunkt, Feldfehler (<c>400</c>, Fehler je JSON-Feldname), abgeschlossene Freizeit
    /// oder ein anderer Fehler.
    /// </summary>
    public async Task<ProgrammAktion> AnlegenAsync(Guid freizeitId, ProgrammpunktNeu punkt, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{ProgrammPfad(freizeitId)}/punkte", punkt, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new ProgrammAktion(ProgrammAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        return await AktionAsync(antwort, abbruch);
    }

    /// <summary><c>POST …/punkte/{punktId}/uebernehmen</c> („Ich übernehme“, FR-35): mit dem Programmpunkt danach.</summary>
    public async Task<ProgrammAktion> UebernehmenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{PunktPfad(freizeitId, punktId)}/uebernehmen", null, abbruch);
        return await AktionAsync(antwort, abbruch);
    }

    /// <summary><c>POST …/punkte/{punktId}/abgeben</c>: mit dem Programmpunkt danach.</summary>
    public async Task<ProgrammAktion> AbgebenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{PunktPfad(freizeitId, punktId)}/abgeben", null, abbruch);
        return await AktionAsync(antwort, abbruch);
    }

    /// <summary><c>DELETE …/punkte/{punktId}</c>: gelöscht, Mahlzeit (<c>409 konflikt</c>), abgeschlossene Freizeit oder ein anderer Fehler.</summary>
    public async Task<ProgrammAktionErgebnis> LoeschenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(PunktPfad(freizeitId, punktId), abbruch);
        return antwort.IsSuccessStatusCode ? ProgrammAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    private static async Task<ProgrammAktion> AktionAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (!antwort.IsSuccessStatusCode)
        {
            return ProgrammAktion.Ohne(await FehlerAsync(antwort, abbruch));
        }

        var punkt = await antwort.Content.ReadFromJsonAsync<Programmpunkt>(ApiJson.Optionen, abbruch)
                    ?? throw new HttpRequestException("Leere Antwort zum Programmpunkt.");
        return new ProgrammAktion(ProgrammAktionErgebnis.Erfolg, punkt, new Dictionary<string, string>());
    }

    /// <summary>Ordnet eine Fehlerantwort zu: <c>409</c> nach Problemcode, alles andere ist ein Fehler.</summary>
    private static async Task<ProgrammAktionErgebnis> FehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode != HttpStatusCode.Conflict)
        {
            return ProgrammAktionErgebnis.Fehler;
        }

        return await CodeAsync(antwort, abbruch) switch
        {
            Problemcodes.Konflikt => ProgrammAktionErgebnis.Konflikt,
            Problemcodes.FreizeitAbgeschlossen => ProgrammAktionErgebnis.Abgeschlossen,
            _ => ProgrammAktionErgebnis.Fehler,
        };
    }

    /// <summary>Der Problemcode einer ProblemDetails-Antwort oder <c>null</c>.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
