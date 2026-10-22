using Zeltlotse.App.Browser;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Nachgebildeter Browser: Online-Status, Zeitpunkt, Neu laden, Drucken und Datei speichern ohne JavaScript. Story 6-1: Push-Status, Abo des Browsers,
/// Abonnieren (mit oder ohne Erlaubnis), Abmelden, eingehende Pushes und ans Ende scrollen. Story 6-5: Offline-Speicher als Wörterbuch (im Klartext, nur
/// im Test) mit Ablauf gegen die Test-Uhr.
/// </summary>
internal sealed class NachgebildeterBrowser(TimeProvider? uhr = null) : IBrowser
{
    private readonly TimeProvider _uhr = uhr ?? TimeProvider.System;

    private Func<bool, Task>? _geaendert;
    private Func<Task>? _push;

    public bool Online { get; set; } = true;

    public DateTimeOffset? Zuletzt { get; set; }

    public int NeuGeladen { get; private set; }

    public int Gedruckt { get; private set; }

    /// <summary>Die angebotenen Dateien: Name, Medientyp und Inhalt.</summary>
    public List<(string Name, string Medientyp, byte[] Inhalt)> Dateien { get; } = [];

    /// <summary>Hält das Lesen des Zeitpunkts an, bis der Test es freigibt.</summary>
    public TaskCompletionSource? LesenAnhalten { get; set; }

    /// <summary>Reihenfolge der Aufrufe beim Start der Verbindung.</summary>
    public List<string> Aufrufe { get; } = [];

    public Task MeldenAsync(bool online)
    {
        Online = online;
        return _geaendert?.Invoke(online) ?? Task.CompletedTask;
    }

    public ValueTask<bool> IstOnlineAsync()
    {
        Aufrufe.Add(nameof(IstOnlineAsync));
        return ValueTask.FromResult(Online);
    }

    public ValueTask<IAsyncDisposable> OnlineBeobachtenAsync(Func<bool, Task> geaendert)
    {
        Aufrufe.Add(nameof(OnlineBeobachtenAsync));
        _geaendert = geaendert;
        return ValueTask.FromResult<IAsyncDisposable>(new Ende(() => _geaendert = null));
    }

    public async ValueTask<DateTimeOffset?> ZuletztAktualisiertLesenAsync()
    {
        var zuletzt = Zuletzt;
        if (LesenAnhalten is not null)
        {
            await LesenAnhalten.Task;
        }

        return zuletzt;
    }

    public ValueTask ZuletztAktualisiertSchreibenAsync(DateTimeOffset zeitpunkt)
    {
        Zuletzt = zeitpunkt;
        return ValueTask.CompletedTask;
    }

    public ValueTask NeuLadenAsync()
    {
        NeuGeladen++;
        return ValueTask.CompletedTask;
    }

    public ValueTask DruckenAsync()
    {
        Gedruckt++;
        return ValueTask.CompletedTask;
    }

    public ValueTask DateiSpeichernAsync(string name, string medientyp, byte[] inhalt)
    {
        Dateien.Add((name, medientyp, inhalt));
        return ValueTask.CompletedTask;
    }

    /// <summary>Story 6-1: Was das Gerät mit Push kann; <see cref="PushAbonnierenAsync"/> und <see cref="PushAbmeldenAsync"/> ändern ihn wie ein Browser.</summary>
    public PushStatus Push { get; set; } = PushStatus.Aus;

    /// <summary>Story 6-1: Das Abo des Browsers (bei <see cref="PushStatus.An"/>).</summary>
    public PushAboDaten? Abo { get; set; }

    /// <summary>Story 6-1: Die Erlaubnis, die die Person auf die Frage gibt.</summary>
    public bool ErlaubnisGeben { get; set; } = true;

    /// <summary>Story 6-1: Wie oft nach der Erlaubnis gefragt wurde (nur nach einem Tipp).</summary>
    public int ErlaubnisGefragt { get; private set; }

    /// <summary>Story 6-1: Der Schlüssel des letzten Abonnierens.</summary>
    public string? Schluessel { get; private set; }

    /// <summary>Story 6-1: Die Elemente, an deren Ende gescrollt wurde.</summary>
    public List<string> AnsEnde { get; } = [];

    /// <summary>Story 6-1: Ob gerade jemand auf Pushes wartet.</summary>
    public bool PushBeobachtet => _push is not null;

    /// <summary>Story 6-1: Ein Push geht ein (der Service Worker meldet ihn).</summary>
    public Task PushEingangAsync() => _push?.Invoke() ?? Task.CompletedTask;

    public ValueTask<PushStatus> PushStatusAsync() => ValueTask.FromResult(Push);

    public ValueTask<PushAboDaten?> PushAboLesenAsync() => ValueTask.FromResult(Push == PushStatus.An ? Abo : null);

    public ValueTask<PushAboDaten?> PushAbonnierenAsync(string oeffentlicherSchluessel)
    {
        ErlaubnisGefragt++;
        Schluessel = oeffentlicherSchluessel;
        if (!ErlaubnisGeben)
        {
            Push = PushStatus.Abgelehnt;
            return ValueTask.FromResult<PushAboDaten?>(null);
        }

        Push = PushStatus.An;
        Abo ??= new PushAboDaten("https://fcm.googleapis.com/fcm/send/geraet", "p256dh-geraet", "auth-geraet");
        return ValueTask.FromResult<PushAboDaten?>(Abo);
    }

    public ValueTask<string?> PushAbmeldenAsync()
    {
        var endpunkt = Push == PushStatus.An ? Abo?.Endpunkt : null;
        Push = PushStatus.Aus;
        Abo = null;
        return ValueTask.FromResult(endpunkt);
    }

    public ValueTask<IAsyncDisposable> PushBeobachtenAsync(Func<Task> eingegangen)
    {
        _push = eingegangen;
        return ValueTask.FromResult<IAsyncDisposable>(new Ende(() => _push = null));
    }

    /// <summary>Story 6-4: Die kopierten Texte (Zwischenablage).</summary>
    public List<string> Kopiert { get; } = [];

    /// <summary>Story 6-4: Ob der Browser das Kopieren erlaubt.</summary>
    public bool KopierenErlaubt { get; set; } = true;

    public ValueTask<bool> KopierenAsync(string text)
    {
        if (KopierenErlaubt)
        {
            Kopiert.Add(text);
        }

        return ValueTask.FromResult(KopierenErlaubt);
    }

    public ValueTask AnsEndeScrollenAsync(string elementId)
    {
        AnsEnde.Add(elementId);
        return ValueTask.CompletedTask;
    }

    /// <summary>Story 6-5: Der Offline-Speicher: Schlüssel → Freizeit, Ablauf (UTC-Millisekunden) und Text.</summary>
    public SortedDictionary<string, (Guid FreizeitId, long Ablauf, string Text)> Offline { get; } = new(StringComparer.Ordinal);

    /// <summary>Story 6-5: Wie oft die ganze Offline-Datenbank gelöscht wurde.</summary>
    public int OfflineAllesGeloescht { get; private set; }

    /// <summary>Story 6-5: Wie oft <c>navigator.storage.persist()</c> angefragt wurde.</summary>
    public int SpeicherGehalten { get; private set; }

    private bool Abgelaufen(long ablauf) => ablauf <= _uhr.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Story 6-5: Schreiben scheitert still (wie der echte Browser bei vollem Speicher oder ohne IndexedDB/WebCrypto): nichts wird gespeichert.</summary>
    public bool SchreibenScheitert { get; set; }

    public ValueTask OfflineSchreibenAsync(string schluessel, Guid freizeitId, long ablaufMs, string text)
    {
        if (!SchreibenScheitert)
        {
            Offline[schluessel] = (freizeitId, ablaufMs, text);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> OfflineLesenAsync(string schluessel)
    {
        if (!Offline.TryGetValue(schluessel, out var datensatz))
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (Abgelaufen(datensatz.Ablauf))
        {
            Offline.Remove(schluessel);
            return ValueTask.FromResult<string?>(null);
        }

        return ValueTask.FromResult<string?>(datensatz.Text);
    }

    public ValueTask<IReadOnlyList<OfflineDatensatz>> OfflineListeAsync(string praefix)
    {
        foreach (var abgelaufen in Offline.Where(d => d.Key.StartsWith(praefix, StringComparison.Ordinal) && Abgelaufen(d.Value.Ablauf)).Select(d => d.Key).ToList())
        {
            Offline.Remove(abgelaufen);
        }

        return ValueTask.FromResult<IReadOnlyList<OfflineDatensatz>>(
            [.. Offline.Where(d => d.Key.StartsWith(praefix, StringComparison.Ordinal)).Select(d => new OfflineDatensatz(d.Key, d.Value.Text))]);
    }

    public ValueTask OfflineEntfernenAsync(string schluessel)
    {
        Offline.Remove(schluessel);
        return ValueTask.CompletedTask;
    }

    public ValueTask OfflineFreizeitLoeschenAsync(Guid freizeitId)
    {
        foreach (var schluessel in Offline.Where(d => d.Value.FreizeitId == freizeitId).Select(d => d.Key).ToList())
        {
            Offline.Remove(schluessel);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OfflineAllesLoeschenAsync()
    {
        Offline.Clear();
        OfflineAllesGeloescht++;
        return ValueTask.CompletedTask;
    }

    public ValueTask OfflineAufraeumenAsync()
    {
        foreach (var schluessel in Offline.Where(d => Abgelaufen(d.Value.Ablauf)).Select(d => d.Key).ToList())
        {
            Offline.Remove(schluessel);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SpeicherHaltenAsync()
    {
        SpeicherGehalten++;
        return ValueTask.CompletedTask;
    }

    private sealed class Ende(Action ende) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            ende();
            return ValueTask.CompletedTask;
        }
    }
}
