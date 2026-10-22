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

    /// <summary>Story 6-1: Was dieses Gerät mit Push kann und ob es ein Abo hat; fragt nie nach der Erlaubnis.</summary>
    ValueTask<PushStatus> PushStatusAsync();

    /// <summary>Story 6-1: Das bestehende Push-Abo des Browsers oder <c>null</c>; fragt nie nach der Erlaubnis.</summary>
    ValueTask<PushAboDaten?> PushAboLesenAsync();

    /// <summary>
    /// Story 6-1: Fragt nach der Erlaubnis (nur nach einem Tipp auf „Benachrichtigungen einschalten“) und abonniert Push mit dem öffentlichen VAPID-Schlüssel;
    /// <c>null</c>, wenn die Erlaubnis verweigert wurde oder das Abonnieren scheiterte.
    /// </summary>
    ValueTask<PushAboDaten?> PushAbonnierenAsync(string oeffentlicherSchluessel);

    /// <summary>Story 6-1: Beendet das Push-Abo des Browsers und liefert seinen Endpunkt; ohne Abo <c>null</c>.</summary>
    ValueTask<string?> PushAbmeldenAsync();

    /// <summary>Story 6-1: Meldet jeden eingegangenen Push (der Service Worker sagt den offenen Fenstern Bescheid); Entsorgen beendet die Beobachtung.</summary>
    ValueTask<IAsyncDisposable> PushBeobachtenAsync(Func<Task> eingegangen);

    /// <summary>Story 6-1: Scrollt das Element mit der Kennung ans untere Ende des sichtbaren Bereichs (die Kanal-Seite öffnet am Ende).</summary>
    ValueTask AnsEndeScrollenAsync(string elementId);

    /// <summary>Story 6-4: Legt den Text in die Zwischenablage (nur nach einem Tipp, z. B. „Link kopieren“); <c>false</c>, wenn der Browser es nicht erlaubt.</summary>
    ValueTask<bool> KopierenAsync(string text);

    /// <summary>
    /// Story 6-5: Legt einen Text verschlüsselt (AES-GCM, nicht exportierbarer Schlüssel des Geräts) in IndexedDB ab, mit der Freizeit und dem
    /// Ablauf (UTC-Millisekunden); ein vorhandener Datensatz mit demselben Schlüssel wird ersetzt.
    /// </summary>
    ValueTask OfflineSchreibenAsync(string schluessel, Guid freizeitId, long ablaufMs, string text);

    /// <summary>Story 6-5: Der entschlüsselte Text oder <c>null</c> (fehlt, abgelaufen und damit gelöscht oder nicht lesbar).</summary>
    ValueTask<string?> OfflineLesenAsync(string schluessel);

    /// <summary>Story 6-5: Alle nicht abgelaufenen Datensätze, deren Schlüssel mit <paramref name="praefix"/> beginnt, nach Schlüssel geordnet.</summary>
    ValueTask<IReadOnlyList<OfflineDatensatz>> OfflineListeAsync(string praefix);

    ValueTask OfflineEntfernenAsync(string schluessel);

    /// <summary>Story 6-5: Löscht alle Datensätze einer Freizeit, auch wartende Einträge.</summary>
    ValueTask OfflineFreizeitLoeschenAsync(Guid freizeitId);

    /// <summary>Story 6-5: Löscht die ganze Offline-Datenbank samt Schlüssel (Abmelden, Sitzung abgelaufen, Konto gelöscht).</summary>
    ValueTask OfflineAllesLoeschenAsync();

    /// <summary>Story 6-5: Löscht alle abgelaufenen Datensätze (nur auf dem Gerät, auch ohne Netz).</summary>
    ValueTask OfflineAufraeumenAsync();

    /// <summary>Story 6-5: Fragt <c>navigator.storage.persist()</c> an, damit der Browser die Offline-Daten nicht vorzeitig räumt.</summary>
    ValueTask SpeicherHaltenAsync();
}

/// <summary>Story 6-5: Ein entschlüsselter Datensatz des Offline-Speichers.</summary>
public sealed record OfflineDatensatz(string Schluessel, string Text);

/// <summary>Story 6-1: Was ein Gerät mit Push kann.</summary>
public enum PushStatus
{
    /// <summary>Der Browser kennt Web-Push nicht.</summary>
    NichtUnterstuetzt,

    /// <summary>iPhone oder iPad im Browser: Push geht erst, wenn Zeltlotse auf dem Home-Bildschirm liegt.</summary>
    NurMitHomeBildschirm,

    /// <summary>Die Erlaubnis für Benachrichtigungen ist auf dem Gerät verweigert.</summary>
    Abgelehnt,

    /// <summary>Möglich, aber noch kein Abo.</summary>
    Aus,

    /// <summary>Der Browser hat ein Abo.</summary>
    An,
}

/// <summary>Story 6-1: Das Push-Abo des Browsers (Web-Push-Standard): Endpunkt beim Push-Dienst und die Schlüssel (base64url).</summary>
public sealed record PushAboDaten(string Endpunkt, string P256dh, string Auth);
