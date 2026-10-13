using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 2-9: Ergebnis von <see cref="SicherheitApiClient.GelesenAsync"/>.</summary>
public enum SchutzkonzeptGelesenErgebnis
{
    /// <summary>Bestätigt (auch, wenn es schon bestätigt war).</summary>
    Erfolg,

    /// <summary><c>409 konflikt</c>: Das Schutzkonzept wurde inzwischen ausgetauscht oder entfernt.</summary>
    Konflikt,

    /// <summary>Jede andere Antwort, z. B. eine abgeschlossene Freizeit oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 2-10: Ergebnis von <see cref="SicherheitApiClient.VorfallAnlegenAsync"/>.</summary>
public enum VorfallAnlegenErgebnis
{
    /// <summary>Gespeichert (auch, wenn dieselbe Kennung schon gespeichert war).</summary>
    Erfolg,

    /// <summary><c>409 freizeit-abgeschlossen</c>: Die Freizeit ist nur noch lesbar.</summary>
    Abgeschlossen,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler, ein Konflikt oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>
/// Typisierter Client des Moduls Sicherheit (AD-29, handgeschrieben). Story 2-8: die Sicherheitskette einer Freizeit. Story 2-9: das
/// Schutzkonzept einer Freizeit. Story 2-10: das Vorfall-Protokoll einer Freizeit und das Archiv der Vorfall-Notizen einer Organisation.
/// </summary>
public sealed class SicherheitApiClient(HttpClient http)
{
    /// <summary>Pfad der Sicherheitskette einer Freizeit, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string SicherheitskettePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/sicherheit/sicherheitskette";

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c> (Leitung und Team, FR-29): die Kette mit Version oder <c>null</c>
    /// bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<Sicherheitskette?> SicherheitsketteLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(SicherheitskettePfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Sicherheitskette>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Sicherheitskette.");
    }

    /// <summary>Story 2-9: Pfad des Schutzkonzepts einer Freizeit, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string SchutzkonzeptPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/sicherheit/schutzkonzept";

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept</c> (Leitung und Team, FR-30): das Schutzkonzept mit dem Team oder
    /// <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<Schutzkonzept?> SchutzkonzeptLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(SchutzkonzeptPfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Schutzkonzept>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Schutzkonzepts.");
    }

    /// <summary>
    /// <c>POST …/schutzkonzept/gelesen {dokumentId}</c>: bestätigt, ausgetauscht (<c>409 konflikt</c>) oder ein anderer Fehler (auch
    /// <c>409 freizeit-abgeschlossen</c>, <c>403</c>/<c>404</c> und <c>5xx</c>).
    /// </summary>
    public async Task<SchutzkonzeptGelesenErgebnis> GelesenAsync(Guid freizeitId, Guid dokumentId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{SchutzkonzeptPfad(freizeitId)}/gelesen", new SchutzkonzeptGelesen(dokumentId), ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            return SchutzkonzeptGelesenErgebnis.Erfolg;
        }

        return antwort.StatusCode == HttpStatusCode.Conflict && await CodeAsync(antwort, abbruch) == Problemcodes.Konflikt
            ? SchutzkonzeptGelesenErgebnis.Konflikt
            : SchutzkonzeptGelesenErgebnis.Fehler;
    }

    /// <summary>Story 2-10: Pfad des Vorfall-Protokolls einer Freizeit.</summary>
    public static string VorfaellePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/sicherheit/vorfaelle";

    /// <summary>
    /// Story 2-10: <c>GET /api/freizeiten/{freizeitId}/sicherheit/vorfaelle[?personId=]</c> (Leitung und Team, FR-31): die Einträge, neueste
    /// zuerst, oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<List<VorfallEintrag>?> VorfaelleLadenAsync(Guid freizeitId, Guid? personId = null, CancellationToken abbruch = default)
    {
        var pfad = personId is { } person ? $"{VorfaellePfad(freizeitId)}?personId={person}" : VorfaellePfad(freizeitId);
        using var antwort = await http.GetAsync(pfad, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<VorfallEintrag>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Vorfall-Protokolls.");
    }

    /// <summary>
    /// Story 2-10: <c>POST /api/freizeiten/{freizeitId}/sicherheit/vorfaelle</c>: gespeichert, abgeschlossene Freizeit
    /// (<c>409 freizeit-abgeschlossen</c>) oder ein anderer Fehler.
    /// </summary>
    public async Task<VorfallAnlegenErgebnis> VorfallAnlegenAsync(Guid freizeitId, VorfallEintragNeu eintrag, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(VorfaellePfad(freizeitId), eintrag, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            return VorfallAnlegenErgebnis.Erfolg;
        }

        return antwort.StatusCode == HttpStatusCode.Conflict && await CodeAsync(antwort, abbruch) == Problemcodes.FreizeitAbgeschlossen
            ? VorfallAnlegenErgebnis.Abgeschlossen
            : VorfallAnlegenErgebnis.Fehler;
    }

    /// <summary>Story 2-10: Pfad des Archivs der Vorfall-Notizen einer Organisation.</summary>
    public static string VorfallArchivPfad(Guid orgId) => $"/api/organisationen/{orgId}/sicherheit/vorfall-archiv";

    /// <summary>
    /// Story 2-10: <c>GET /api/organisationen/{orgId}/sicherheit/vorfall-archiv</c> (Org-Admin, FR-31): je abgeschlossener Freizeit die
    /// archivierten Einträge oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<List<VorfallArchivFreizeit>?> VorfallArchivLadenAsync(Guid orgId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(VorfallArchivPfad(orgId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<VorfallArchivFreizeit>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Archivs der Vorfall-Notizen.");
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
