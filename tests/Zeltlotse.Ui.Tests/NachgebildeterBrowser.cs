using Zeltlotse.App.Browser;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Nachgebildeter Browser: Online-Status, Zeitpunkt, Neu laden, Drucken und Datei speichern ohne JavaScript. Story 6-1: Push-Status, Abo des Browsers,
/// Abonnieren (mit oder ohne Erlaubnis), Abmelden, eingehende Pushes und ans Ende scrollen.
/// </summary>
internal sealed class NachgebildeterBrowser : IBrowser
{
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

    public ValueTask AnsEndeScrollenAsync(string elementId)
    {
        AnsEnde.Add(elementId);
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
