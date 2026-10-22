using System.Text.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.App.Browser;
using ApiTagesansicht = Zeltlotse.Api.Contracts.Kueche.Tagesansicht;

namespace Zeltlotse.App.Offline;

/// <summary>Story 6-5: Der Kopf einer gespeicherten Freizeit: die Startseiten-Daten und der Kontext (Phase, Rollen, Schalter, erlaubte Aktionen).</summary>
public sealed record OfflineFreizeit(FreizeitAntwort Freizeit, FreizeitKontext Kontext);

/// <summary>Story 6-5: Ein offline geschriebener Eintrag des Vorfall-Protokolls, der auf Verbindung wartet.</summary>
public sealed record WartenderVorfall(Guid FreizeitId, VorfallEintragNeu Eintrag);

/// <summary>
/// Story 6-5: Typisierte Ablage für den Offline-Fall (AD-18) über <see cref="IBrowser"/>: je Freizeit Kopf, Sicherheitskette, Notfallkarte und die
/// Tagesansichten, dazu die Warteschlange des Vorfall-Protokolls. Alles liegt als JSON verschlüsselt in IndexedDB; jeder Datensatz läuft mit dem
/// Freizeitende ab (Tag nach dem letzten Freizeittag, 00:00 Uhr Europe/Berlin) und wird dann auf dem Gerät gelöscht, auch ohne Netz. Kein JSInterop
/// hier; der Browser verschlüsselt und prüft den Ablauf.
/// </summary>
public sealed class OfflineSpeicher(IBrowser browser)
{
    /// <summary>Satz der offline nutzbaren Seiten, wenn auf dem Gerät nichts gespeichert ist.</summary>
    public const string NichtGespeichertText = "Auf diesem Gerät nicht gespeichert.";

    private const string VorfallPraefix = "vorfall/";

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Das Freizeitende als UTC-Millisekunden: der Tag nach <paramref name="letzterTag"/>, 00:00 Uhr Europe/Berlin.</summary>
    public static long Ablauf(DateOnly letzterTag) =>
        new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(letzterTag.AddDays(1).ToDateTime(TimeOnly.MinValue), Berlin)).ToUnixTimeMilliseconds();

    public static string FreizeitSchluessel(Guid freizeitId) => $"{freizeitId:D}/freizeit";

    public static string SicherheitsketteSchluessel(Guid freizeitId) => $"{freizeitId:D}/sicherheitskette";

    public static string NotfallkarteSchluessel(Guid freizeitId) => $"{freizeitId:D}/notfallkarte";

    public static string TagesansichtSchluessel(Guid freizeitId, DateOnly tag) => $"{TagesansichtPraefix(freizeitId)}{tag:yyyy-MM-dd}";

    public static string VorfallSchluessel(Guid freizeitId, Guid eintragId) => $"{VorfallPraefix}{freizeitId:D}/{eintragId:D}";

    private static string TagesansichtPraefix(Guid freizeitId) => $"{freizeitId:D}/tagesansicht/";

    public ValueTask FreizeitSchreibenAsync(OfflineFreizeit freizeit) =>
        SchreibenAsync(FreizeitSchluessel(freizeit.Freizeit.Id), freizeit.Freizeit, freizeit);

    public ValueTask SicherheitsketteSchreibenAsync(FreizeitAntwort freizeit, Sicherheitskette kette) =>
        SchreibenAsync(SicherheitsketteSchluessel(freizeit.Id), freizeit, kette);

    public ValueTask NotfallkarteSchreibenAsync(FreizeitAntwort freizeit, IReadOnlyList<NotfallkartePerson> karte) =>
        SchreibenAsync(NotfallkarteSchluessel(freizeit.Id), freizeit, karte);

    public ValueTask TagesansichtSchreibenAsync(FreizeitAntwort freizeit, ApiTagesansicht ansicht) =>
        SchreibenAsync(TagesansichtSchluessel(freizeit.Id, ansicht.Tag), freizeit, ansicht);

    public Task<OfflineFreizeit?> FreizeitLesenAsync(Guid freizeitId) => LesenAsync<OfflineFreizeit>(FreizeitSchluessel(freizeitId));

    public Task<Sicherheitskette?> SicherheitsketteLesenAsync(Guid freizeitId) => LesenAsync<Sicherheitskette>(SicherheitsketteSchluessel(freizeitId));

    public Task<List<NotfallkartePerson>?> NotfallkarteLesenAsync(Guid freizeitId) => LesenAsync<List<NotfallkartePerson>>(NotfallkarteSchluessel(freizeitId));

    public Task<ApiTagesansicht?> TagesansichtLesenAsync(Guid freizeitId, DateOnly tag) => LesenAsync<ApiTagesansicht>(TagesansichtSchluessel(freizeitId, tag));

    public ValueTask NotfallkarteEntfernenAsync(Guid freizeitId) => browser.OfflineEntfernenAsync(NotfallkarteSchluessel(freizeitId));

    /// <summary>Entfernt alle Tagesansichten einer Freizeit (Schalter Verpflegung aus).</summary>
    public async Task TagesansichtenEntfernenAsync(Guid freizeitId)
    {
        foreach (var datensatz in await browser.OfflineListeAsync(TagesansichtPraefix(freizeitId)))
        {
            await browser.OfflineEntfernenAsync(datensatz.Schluessel);
        }
    }

    /// <summary>Alle gespeicherten Freizeiten, nach Name geordnet.</summary>
    public async Task<List<OfflineFreizeit>> FreizeitenAsync()
    {
        var freizeiten = new List<OfflineFreizeit>();
        foreach (var datensatz in await browser.OfflineListeAsync(""))
        {
            if (datensatz.Schluessel.EndsWith("/freizeit", StringComparison.Ordinal) && Lesen<OfflineFreizeit>(datensatz.Text) is { } freizeit)
            {
                freizeiten.Add(freizeit);
            }
        }

        return [.. freizeiten.OrderBy(f => f.Freizeit.Name, StringComparer.CurrentCulture).ThenBy(f => f.Freizeit.Id)];
    }

    /// <summary>
    /// Legt einen Eintrag in die Warteschlange, mit dem Ablauf der gespeicherten Freizeit. Ist die Freizeit nicht gespeichert (vor Phase „läuft“
    /// oder ohne Abgleich) oder lässt sich der Eintrag danach nicht lesen (Schreiben gescheitert), <c>false</c>.
    /// </summary>
    public async Task<bool> VorfallEinreihenAsync(Guid freizeitId, VorfallEintragNeu eintrag)
    {
        if (await FreizeitLesenAsync(freizeitId) is not { } freizeit)
        {
            return false;
        }

        var schluessel = VorfallSchluessel(freizeitId, eintrag.Id);
        await SchreibenAsync(schluessel, freizeit.Freizeit, eintrag);
        // Der Browser übergeht Fehler beim Schreiben (Speicher voll, kein IndexedDB/WebCrypto): Nur was wieder lesbar ist, gilt als eingereiht.
        return await browser.OfflineLesenAsync(schluessel) is not null;
    }

    /// <summary>Die wartenden Einträge (ohne Angabe aller Freizeiten), in der Reihenfolge ihrer Kennung (UUID v7, also des Schreibens).</summary>
    public async Task<List<WartenderVorfall>> WartendeAsync(Guid? freizeitId = null)
    {
        var praefix = freizeitId is { } id ? $"{VorfallPraefix}{id:D}/" : VorfallPraefix;
        var wartend = new List<WartenderVorfall>();
        foreach (var datensatz in await browser.OfflineListeAsync(praefix))
        {
            var teile = datensatz.Schluessel.Split('/');
            if (teile.Length == 3 && Guid.TryParse(teile[1], out var freizeit) && Lesen<VorfallEintragNeu>(datensatz.Text) is { } eintrag)
            {
                wartend.Add(new WartenderVorfall(freizeit, eintrag));
            }
        }

        return wartend;
    }

    public ValueTask VorfallEntfernenAsync(Guid freizeitId, Guid eintragId) => browser.OfflineEntfernenAsync(VorfallSchluessel(freizeitId, eintragId));

    /// <summary>Löscht alle Daten einer Freizeit, auch die wartenden Einträge.</summary>
    public ValueTask FreizeitLoeschenAsync(Guid freizeitId) => browser.OfflineFreizeitLoeschenAsync(freizeitId);

    /// <summary>
    /// Zählt jedes Löschen der ganzen Datenbank. Ein Abgleich, der vorher begonnen hat, schreibt danach nichts mehr (sonst legte er nach dem Abmelden
    /// Daten und Schlüssel neu an).
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>Löscht die ganze Offline-Datenbank samt Schlüssel (Abmelden, Sitzung abgelaufen, Konto gelöscht).</summary>
    public ValueTask AllesLoeschenAsync()
    {
        Generation++;
        return browser.OfflineAllesLoeschenAsync();
    }

    /// <summary>Löscht alles Abgelaufene.</summary>
    public ValueTask AufraeumenAsync() => browser.OfflineAufraeumenAsync();

    public ValueTask SpeicherHaltenAsync() => browser.SpeicherHaltenAsync();

    private ValueTask SchreibenAsync<T>(string schluessel, FreizeitAntwort freizeit, T wert) =>
        browser.OfflineSchreibenAsync(schluessel, freizeit.Id, Ablauf(freizeit.LetzterTag), JsonSerializer.Serialize(wert, Json));

    private async Task<T?> LesenAsync<T>(string schluessel)
        where T : class =>
        await browser.OfflineLesenAsync(schluessel) is { } text ? Lesen<T>(text) : null;

    /// <summary>Ein unlesbarer Stand (z. B. aus einer älteren App-Version) gilt als nicht gespeichert.</summary>
    private static T? Lesen<T>(string text)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Ob die Freizeit offline gespeichert werden darf: nur in Phase „läuft“ (wie Notfall-Box und Eintrag „Notfall“).</summary>
    public static bool Speicherbar(FreizeitKontext kontext) => kontext.Phase == Phase.Laeuft;
}
