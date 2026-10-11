using Zeltlotse.App.Browser;

namespace Zeltlotse.Ui.Tests;

/// <summary>Nachgebildeter Browser: Online-Status, Zeitpunkt und Neu laden ohne JavaScript.</summary>
internal sealed class NachgebildeterBrowser : IBrowser
{
    private Func<bool, Task>? _geaendert;

    public bool Online { get; set; } = true;

    public DateTimeOffset? Zuletzt { get; set; }

    public int NeuGeladen { get; private set; }

    public Task MeldenAsync(bool online)
    {
        Online = online;
        return _geaendert?.Invoke(online) ?? Task.CompletedTask;
    }

    public ValueTask<bool> IstOnlineAsync() => ValueTask.FromResult(Online);

    public ValueTask<IAsyncDisposable> OnlineBeobachtenAsync(Func<bool, Task> geaendert)
    {
        _geaendert = geaendert;
        return ValueTask.FromResult<IAsyncDisposable>(new Ende(() => _geaendert = null));
    }

    public ValueTask<DateTimeOffset?> ZuletztAktualisiertLesenAsync() => ValueTask.FromResult(Zuletzt);

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
