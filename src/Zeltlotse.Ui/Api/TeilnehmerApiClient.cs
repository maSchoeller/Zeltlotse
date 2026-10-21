using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Api.Contracts.Programm;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 6-4: Ergebnis von <see cref="TeilnehmerApiClient.EinloesenAsync"/>.</summary>
public enum TeilnehmerEinloesung
{
    /// <summary><c>204</c>: Der Browser hat jetzt das Gast-Cookie.</summary>
    Eingeloest,

    /// <summary><c>404</c>: Der QR-Link gilt nicht (mehr).</summary>
    NichtGefunden,

    /// <summary><c>429</c>: Zu viele Anfragen von dieser IP; gleich noch einmal versuchen.</summary>
    ZuVieleAnfragen,
}

/// <summary>
/// Story 6-4: Typisierter Client des Teilnehmer-Zugangs per QR-Code (AD-29, handgeschrieben): alle Aufrufe unter <c>/api/teilnehmer</c>, quer über die Module
/// Freizeiten, Programm und Kanäle. Das Token des QR-Links geht nur einmal per POST an den Server (Einlösen); danach trägt das Gast-Cookie des Browsers die
/// Sitzung. Jedes <c>404</c> heißt: Der Zugang gilt nicht (mehr) (Freizeit zu Ende, Link erneuert, Modul aus oder noch nicht eingelöst).
/// </summary>
public sealed class TeilnehmerApiClient(HttpClient http)
{
    public const string Basis = "/api/teilnehmer";

    /// <summary>Pfad der Datei eines Dokuments unter „Infos“ (ein Link, den der Browser mit dem Gast-Cookie öffnet).</summary>
    public static string InfoDateiPfad(Guid dokumentId) => $"{Basis}/infos/{dokumentId}";

    /// <summary>
    /// <c>POST /api/teilnehmer/einloesen</c>: eingelöst mit gesetztem Gast-Cookie (<c>204</c>), nicht (mehr) gültig (<c>404</c>) oder zu viele Anfragen
    /// (<c>429</c>, die Zugriffsbegrenzung je IP greift, wenn im Lager-WLAN viele gleichzeitig scannen).
    /// </summary>
    public async Task<TeilnehmerEinloesung> EinloesenAsync(string token, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{Basis}/einloesen", new TeilnehmerEinloesen(token), ApiJson.Optionen, abbruch);
        switch (antwort.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return TeilnehmerEinloesung.NichtGefunden;
            case HttpStatusCode.TooManyRequests:
                return TeilnehmerEinloesung.ZuVieleAnfragen;
            default:
                antwort.EnsureSuccessStatusCode();
                return TeilnehmerEinloesung.Eingeloest;
        }
    }

    /// <summary><c>GET /api/teilnehmer</c>: Name und Zeitraum der Freizeit oder <c>null</c> ohne gültige Gast-Sitzung.</summary>
    public Task<TeilnehmerFreizeit?> LadenAsync(CancellationToken abbruch = default) => LadenAsync<TeilnehmerFreizeit>(Basis, abbruch);

    /// <summary><c>GET /api/teilnehmer/infos</c>: die Dokumente für Eltern und Teilnehmende oder <c>null</c>.</summary>
    public Task<TeilnehmerInfos?> InfosLadenAsync(CancellationToken abbruch = default) => LadenAsync<TeilnehmerInfos>($"{Basis}/infos", abbruch);

    /// <summary><c>GET /api/teilnehmer/programm</c>: die Wochenübersicht (Tag, Uhrzeit, Titel) oder <c>null</c>.</summary>
    public Task<TeilnehmerWoche?> WocheLadenAsync(CancellationToken abbruch = default) => LadenAsync<TeilnehmerWoche>($"{Basis}/programm", abbruch);

    /// <summary><c>GET /api/teilnehmer/kanal</c>: die Nachrichten in „Alle“ oder <c>null</c>.</summary>
    public Task<TeilnehmerKanal?> KanalLadenAsync(CancellationToken abbruch = default) => LadenAsync<TeilnehmerKanal>($"{Basis}/kanal", abbruch);

    /// <summary><c>GET /api/teilnehmer/push/schluessel</c>: der öffentliche VAPID-Schlüssel oder <c>null</c>.</summary>
    public async Task<string?> SchluesselLadenAsync(CancellationToken abbruch = default) =>
        (await LadenAsync<PushSchluessel>($"{Basis}/push/schluessel", abbruch))?.OeffentlicherSchluessel;

    /// <summary><c>POST /api/teilnehmer/push/abo</c>: gespeichert, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KanalAktionErgebnis> AboSpeichernAsync(PushAboNeu abo, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{Basis}/push/abo", abo, ApiJson.Optionen, abbruch);
        return Ergebnis(antwort);
    }

    /// <summary><c>POST /api/teilnehmer/push/abo/abmelden</c>: abgemeldet, abgeschlossen oder ein anderer Fehler.</summary>
    public async Task<KanalAktionErgebnis> AbmeldenAsync(string endpunkt, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{Basis}/push/abo/abmelden", new PushAbmelden(endpunkt), ApiJson.Optionen, abbruch);
        return Ergebnis(antwort);
    }

    private static KanalAktionErgebnis Ergebnis(HttpResponseMessage antwort) => antwort.IsSuccessStatusCode
        ? KanalAktionErgebnis.Erfolg
        : antwort.StatusCode == HttpStatusCode.Conflict ? KanalAktionErgebnis.Abgeschlossen : KanalAktionErgebnis.Fehler;

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
}
