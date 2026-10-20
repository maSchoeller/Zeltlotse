using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Kanaele;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 6-1: Ergebnis einer schreibenden Aktion in den Kanälen.</summary>
public enum KanalAktionErgebnis
{
    Erfolg,

    /// <summary><c>409 freizeit-abgeschlossen</c>: Die Freizeit ist nur noch lesbar.</summary>
    Abgeschlossen,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 6-1: Ergebnis von „Senden“; bei Erfolg die Nachricht, sonst die Fehler je Feld (<c>text</c>, <c>bestaetigt</c>).</summary>
public sealed record KanalSendenAktion(KanalAktionErgebnis Ergebnis, KanalNachricht? Nachricht, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>
/// Typisierter Client des Moduls Kanäle (AD-29, handgeschrieben). Story 6-1: die Nachrichten eines Kanals laden und senden („Alle“ nur mit Bestätigung),
/// den öffentlichen VAPID-Schlüssel laden, das Push-Abo des Browsers speichern und abmelden.
/// </summary>
public sealed class KanaeleApiClient(HttpClient http)
{
    /// <summary>Pfad der Nachrichten eines Kanals.</summary>
    public static string NachrichtenPfad(Guid freizeitId, Kanal kanal) => $"/api/freizeiten/{freizeitId}/kanaele/{KanalFelder.InRoute(kanal)}/nachrichten";

    /// <summary>Pfad des öffentlichen VAPID-Schlüssels.</summary>
    public static string SchluesselPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/kanaele/push/schluessel";

    /// <summary>Pfad des Push-Abos.</summary>
    public static string AboPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/kanaele/push/abo";

    /// <summary>Pfad zum Abmelden des Push-Abos.</summary>
    public static string AbmeldenPfad(Guid freizeitId) => $"{AboPfad(freizeitId)}/abmelden";

    /// <summary><c>GET …/kanaele/{kanal}/nachrichten</c>: die Ansicht oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public async Task<KanalAnsicht?> LadenAsync(Guid freizeitId, Kanal kanal, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(NachrichtenPfad(freizeitId, kanal), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<KanalAnsicht>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort beim Laden: der Kanal.");
    }

    /// <summary><c>POST …/kanaele/{kanal}/nachrichten</c>: gesendet mit der Nachricht, Feldfehler (<c>400</c>), abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KanalSendenAktion> SendenAsync(Guid freizeitId, Kanal kanal, NachrichtNeu nachricht, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(NachrichtenPfad(freizeitId, kanal), nachricht, ApiJson.Optionen, abbruch);
        if (antwort.IsSuccessStatusCode)
        {
            var gesendet = await antwort.Content.ReadFromJsonAsync<KanalNachricht>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort zur Nachricht.");
            return new KanalSendenAktion(KanalAktionErgebnis.Erfolg, gesendet, new Dictionary<string, string>());
        }

        return new KanalSendenAktion(await FehlerAsync(antwort, abbruch), null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
    }

    /// <summary><c>GET …/kanaele/push/schluessel</c>: der öffentliche VAPID-Schlüssel oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public async Task<string?> SchluesselLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(SchluesselPfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return (await antwort.Content.ReadFromJsonAsync<PushSchluessel>(ApiJson.Optionen, abbruch))?.OeffentlicherSchluessel;
    }

    /// <summary><c>POST …/kanaele/push/abo</c>: gespeichert, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KanalAktionErgebnis> AboSpeichernAsync(Guid freizeitId, PushAboNeu abo, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(AboPfad(freizeitId), abo, ApiJson.Optionen, abbruch);
        return antwort.IsSuccessStatusCode ? KanalAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary><c>POST …/kanaele/push/abo/abmelden</c>: abgemeldet, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KanalAktionErgebnis> AbmeldenAsync(Guid freizeitId, string endpunkt, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(AbmeldenPfad(freizeitId), new PushAbmelden(endpunkt), ApiJson.Optionen, abbruch);
        return antwort.IsSuccessStatusCode ? KanalAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Ordnet eine Fehlerantwort zu: <c>409 freizeit-abgeschlossen</c> ist „abgeschlossen“, alles andere ein Fehler.</summary>
    private static async Task<KanalAktionErgebnis> FehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode != HttpStatusCode.Conflict)
        {
            return KanalAktionErgebnis.Fehler;
        }

        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var code)
                   && code.ValueKind == JsonValueKind.String
                   && code.GetString() == Problemcodes.FreizeitAbgeschlossen
                ? KanalAktionErgebnis.Abgeschlossen
                : KanalAktionErgebnis.Fehler;
        }
        catch (JsonException)
        {
            return KanalAktionErgebnis.Fehler;
        }
    }
}
