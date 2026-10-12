using Zeltlotse.App.Browser;

namespace Zeltlotse.Ui.Tests;

/// <summary>Nachgebildeter Browser: Online-Status, Zeitpunkt, Neu laden, Drucken und Datei speichern ohne JavaScript.</summary>
internal sealed class NachgebildeterBrowser : IBrowser
{
    private Func<bool, Task>? _geaendert;

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

    private sealed class Ende(Action ende) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            ende();
            return ValueTask.CompletedTask;
        }
    }
}
