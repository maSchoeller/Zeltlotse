namespace Zeltlotse.App.Browser;

/// <summary>
/// Die einzige C#-Schnittstelle zum JavaScript der Web-App (AD-29): Modul <c>wwwroot/js/zeltlotse.js</c>.
/// Nur die Umsetzung <see cref="JsBrowser"/> hängt von <c>IJSRuntime</c> ab.
/// </summary>
public interface IBrowser
{
    /// <summary>Wert von <c>navigator.onLine</c>.</summary>
    ValueTask<bool> IstOnlineAsync();

    /// <summary>Meldet jeden Wechsel des Online-Status; Entsorgen beendet die Beobachtung.</summary>
    ValueTask<IAsyncDisposable> OnlineBeobachtenAsync(Func<bool, Task> geaendert);

    /// <summary>Zeitpunkt der letzten erfolgreichen API-Antwort aus <c>localStorage</c>; <c>null</c>, wenn es keinen gibt.</summary>
    ValueTask<DateTimeOffset?> ZuletztAktualisiertLesenAsync();

    ValueTask ZuletztAktualisiertSchreibenAsync(DateTimeOffset zeitpunkt);

    /// <summary>Lädt die App neu und aktiviert vorher einen wartenden Service Worker.</summary>
    ValueTask NeuLadenAsync();

    /// <summary>Öffnet den Druckdialog des Browsers (z. B. „Als PDF speichern“); das Druck-CSS blendet App-Leiste, Navigation und Knöpfe aus.</summary>
    ValueTask DruckenAsync();

    /// <summary>Bietet Bytes als Datei zum Speichern an (Blob, Objekt-URL, Link-Klick; ohne Pop-up, CSP-konform).</summary>
    ValueTask DateiSpeichernAsync(string name, string medientyp, byte[] inhalt);
}
