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

/// <summary>Story 5-2: Ergebnis von „Beleg erfassen“; bei Erfolg der Beleg, sonst die Fehler je Feld.</summary>
public sealed record BelegAktion(FinanzenAktionErgebnis Ergebnis, Beleg? Beleg, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>
/// Story 5-2: Ergebnis des Uploads einer Datei an einen Beleg: der Beleg bei <c>200</c>, der Fehler am Feld bei <c>400</c>, abgeschlossen (<c>409</c>) oder
/// nicht verfügbar (<c>403</c>/<c>404</c>). Netzfehler enden in einer <see cref="HttpRequestException"/>; dann bleibt der Beleg, und die Seite bietet
/// „Erneut versuchen“ an.
/// </summary>
public sealed record BelegDateiErgebnis(Beleg? Beleg, string? DateiFehler, bool Abgeschlossen = false, bool NichtVerfuegbar = false);

/// <summary>Story 5-3: Ergebnis von „bezahlt“ oder „offen“; bei Erfolg der neue Stand der Zeile.</summary>
public sealed record BeitragAktion(FinanzenAktionErgebnis Ergebnis, BeitragZeile? Zeile);

/// <summary>
/// Typisierter Client des Moduls Finanzen (AD-29, handgeschrieben). Story 5-1: die Kalkulation einer Freizeit (nur mit dem Modul Finanzen; sonst wie nicht
/// verfügbar), Einnahmen anlegen und entfernen. Den Autosave von Planzahlen, Kategorien und Einnahmen übernimmt der Autosave-Baustein mit
/// <see cref="KalkulationPfad"/>, <see cref="KategoriePfad"/> und <see cref="EinnahmePfad"/>. Der Beitragsrechner ruft den Server nicht. Story 5-2: Belege
/// laden, anlegen und löschen, Datei hochladen und entfernen, Soll-Ist laden; Autosave über <see cref="BelegPfad"/> und <see cref="ErstattetPfad"/>, das Bild
/// über <see cref="DateiPfad"/>. Story 5-3: Beiträge laden, als bezahlt abhaken und wieder öffnen.
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

    /// <summary>Story 5-2: Pfad der Belege.</summary>
    public static string BelegePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/finanzen/belege";

    /// <summary>Story 5-2: Pfad eines Belegs, auch für den Autosave (<c>PATCH</c>, AD-19).</summary>
    public static string BelegPfad(Guid freizeitId, Guid belegId) => $"{BelegePfad(freizeitId)}/{belegId}";

    /// <summary>Story 5-2: Pfad des Erstattungsvermerks für den Autosave (<c>PATCH</c>, auch nach dem Abschluss).</summary>
    public static string ErstattetPfad(Guid freizeitId, Guid belegId) => $"{BelegPfad(freizeitId, belegId)}/erstattet";

    /// <summary>Story 5-2: Pfad der Datei eines Belegs (Bild in der Seite, PDF als Download).</summary>
    public static string DateiPfad(Guid freizeitId, Guid belegId) => $"{BelegPfad(freizeitId, belegId)}/datei";

    /// <summary>Story 5-2: Pfad des Soll-Ist-Vergleichs.</summary>
    public static string SollIstPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/finanzen/soll-ist";

    /// <summary>Story 5-3: Pfad der Beiträge.</summary>
    public static string BeitraegePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/finanzen/beitraege";

    /// <summary>Story 5-3: Pfad des Übergangs „bezahlt“ (<c>POST</c>, AD-28).</summary>
    public static string BeitragBezahltPfad(Guid freizeitId, Guid personId) => $"{BeitraegePfad(freizeitId)}/{personId}/bezahlt";

    /// <summary>Story 5-3: Pfad des Übergangs „offen“ (<c>POST</c>).</summary>
    public static string BeitragOffenPfad(Guid freizeitId, Guid personId) => $"{BeitraegePfad(freizeitId)}/{personId}/offen";

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

    /// <summary>Story 5-2: <c>GET …/finanzen/belege</c>: die Belege (neueste zuerst) oder <c>null</c> bei <c>403</c>/<c>404</c> (auch ohne Modul Finanzen).</summary>
    public Task<List<Beleg>?> BelegeLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<List<Beleg>>(BelegePfad(freizeitId), abbruch);

    /// <summary>Story 5-2: <c>GET …/finanzen/soll-ist</c>: der Vergleich oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<SollIst?> SollIstLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<SollIst>(SollIstPfad(freizeitId), abbruch);

    /// <summary>Story 5-2: <c>POST …/belege</c>: angelegt mit dem Beleg, Feldfehler (<c>400</c>, je JSON-Feldname), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<BelegAktion> BelegAnlegenAsync(Guid freizeitId, BelegNeu beleg, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(BelegePfad(freizeitId), beleg, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var angelegt = await antwort.Content.ReadFromJsonAsync<Beleg>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zum Beleg.");
            return new BelegAktion(FinanzenAktionErgebnis.Erfolg, angelegt, new Dictionary<string, string>());
        }

        return new BelegAktion(await FehlerAsync(antwort, abbruch), null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
    }

    /// <summary>Story 5-2: <c>DELETE …/belege/{belegId}</c>: gelöscht (mit Datei), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<FinanzenAktionErgebnis> BelegLoeschenAsync(Guid freizeitId, Guid belegId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(BelegPfad(freizeitId, belegId), abbruch);
        return antwort.IsSuccessStatusCode ? FinanzenAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Story 5-2: <c>POST …/belege/{belegId}/datei</c> (Multipart <c>datei</c>); ein neuer Upload ersetzt die alte Datei.</summary>
    public async Task<BelegDateiErgebnis> DateiHochladenAsync(
        Guid freizeitId, Guid belegId, Stream inhalt, string dateiname, string? contentType, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        using var datei = new StreamContent(inhalt);
        datei.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        using var formular = new MultipartFormDataContent { { datei, BelegFelder.Datei, string.IsNullOrWhiteSpace(dateiname) ? "beleg" : dateiname } };
        using var antwort = await http.PostAsync(DateiPfad(freizeitId, belegId), formular, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch) is { Count: > 0 } fehler)
        {
            return new BelegDateiErgebnis(null, fehler.GetValueOrDefault(BelegFelder.Datei) ?? fehler.Values.First());
        }

        switch (antwort.StatusCode)
        {
            case HttpStatusCode.Conflict:
                return new BelegDateiErgebnis(null, null, Abgeschlossen: true);
            case HttpStatusCode.Forbidden or HttpStatusCode.NotFound:
                return new BelegDateiErgebnis(null, null, NichtVerfuegbar: true);
        }

        antwort.EnsureSuccessStatusCode();
        var beleg = await antwort.Content.ReadFromJsonAsync<Beleg>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort beim Hochladen der Datei.");
        return new BelegDateiErgebnis(beleg, null);
    }

    /// <summary>Story 5-2: <c>DELETE …/belege/{belegId}/datei</c>: entfernt (der Beleg bleibt), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<FinanzenAktionErgebnis> DateiEntfernenAsync(Guid freizeitId, Guid belegId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(DateiPfad(freizeitId, belegId), abbruch);
        return antwort.IsSuccessStatusCode ? FinanzenAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Story 5-3: <c>GET …/finanzen/beitraege</c>: die Beiträge oder <c>null</c> bei <c>403</c>/<c>404</c> (auch ohne Modul Finanzen).</summary>
    public Task<Beitraege?> BeitraegeLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<Beitraege>(BeitraegePfad(freizeitId), abbruch);

    /// <summary>Story 5-3: <c>POST …/beitraege/{personId}/bezahlt</c>: der neue Stand, abgeschlossen oder ein anderer Fehler.</summary>
    public Task<BeitragAktion> BeitragBezahltAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default) =>
        BeitragUebergangAsync(BeitragBezahltPfad(freizeitId, personId), abbruch);

    /// <summary>Story 5-3: <c>POST …/beitraege/{personId}/offen</c>: der neue Stand, abgeschlossen oder ein anderer Fehler.</summary>
    public Task<BeitragAktion> BeitragOffenAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default) =>
        BeitragUebergangAsync(BeitragOffenPfad(freizeitId, personId), abbruch);

    private async Task<BeitragAktion> BeitragUebergangAsync(string pfad, CancellationToken abbruch)
    {
        using var antwort = await http.PostAsync(pfad, null, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var zeile = await antwort.Content.ReadFromJsonAsync<BeitragZeile>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zum Beitrag.");
            return new BeitragAktion(FinanzenAktionErgebnis.Erfolg, zeile);
        }

        return new BeitragAktion(await FehlerAsync(antwort, abbruch), null);
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
        return await antwort.Content.ReadFromJsonAsync<T>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException($"Leere Antwort von {pfad}.");
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
