using Zeltlotse.App.Browser;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.App.Verbindung;

/// <summary>
/// Verbindungszustand der Web-App (AD-18): offline, wenn <c>navigator.onLine</c> falsch ist oder der letzte API-Aufruf
/// mit einem Netzfehler endete (bis zur nächsten Antwort). Hält den Zeitpunkt der letzten Antwort (auch über Neustarts,
/// in <c>localStorage</c>) für die Offline-Leiste. Kein globaler Zustandsspeicher: nur dieser eine Wert.
/// </summary>
public sealed class AppVerbindung(IBrowser browser, TimeProvider uhr) : IVerbindung, IAsyncDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private bool _browserOnline = true;
    private bool _netzfehler;
    private IAsyncDisposable? _beobachtung;
    private Task? _start;

    /// <summary>Wird bei jeder Änderung von <see cref="Offline"/> oder <see cref="ZuletztAktualisiert"/> ausgelöst.</summary>
    public event Action? Geaendert;

    public bool Offline => !_browserOnline || _netzfehler;

    public DateTimeOffset? ZuletztAktualisiert { get; private set; }

    /// <summary>Text der Offline-Leiste: „Offline – zuletzt aktualisiert HH:MM Uhr“ (Europe/Berlin) oder nur „Offline“.</summary>
    public string OfflineText => OfflineTextFuer(ZuletztAktualisiert);

    public static string OfflineTextFuer(DateTimeOffset? zeitpunkt) =>
        zeitpunkt is { } z
            ? $"Offline – zuletzt aktualisiert {TimeZoneInfo.ConvertTime(z, Berlin):HH:mm} Uhr"
            : "Offline";

    /// <summary>Liest den Ausgangszustand und beobachtet den Online-Status; mehrfacher Aufruf startet nur einmal.</summary>
    public Task StartenAsync() => _start ??= StartAsync();

    private async Task StartAsync()
    {
        ZuletztAktualisiert ??= await browser.ZuletztAktualisiertLesenAsync();
        _browserOnline = await browser.IstOnlineAsync();
        _beobachtung = await browser.OnlineBeobachtenAsync(online =>
        {
            _browserOnline = online;
            Geaendert?.Invoke();
            return Task.CompletedTask;
        });
        Geaendert?.Invoke();
    }

    public ValueTask NetzfehlerMeldenAsync()
    {
        if (!_netzfehler)
        {
            _netzfehler = true;
            Geaendert?.Invoke();
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask ErfolgMeldenAsync()
    {
        _netzfehler = false;
        ZuletztAktualisiert = uhr.GetUtcNow();
        Geaendert?.Invoke();
        await browser.ZuletztAktualisiertSchreibenAsync(ZuletztAktualisiert.Value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_beobachtung is not null)
        {
            await _beobachtung.DisposeAsync();
        }
    }
}
