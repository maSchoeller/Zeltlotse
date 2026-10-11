using Zeltlotse.App.Browser;

namespace Zeltlotse.Ui.Tests;

/// <summary>Nachgebildeter Browser: Online-Status, Zeitpunkt und Neu laden ohne JavaScript.</summary>
internal sealed class NachgebildeterBrowser : IBrowser
{
    private Func<bool, Task>? _geaendert;

    public bool Online { get; set; } = true;

    public DateTimeOffset? Zuletzt { get; set; }

    public int NeuGeladen { get; private set; }

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

    private sealed class Ende(Action ende) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            ende();
            return ValueTask.CompletedTask;
        }
    }
}
