using System.Globalization;
using Microsoft.JSInterop;

namespace Zeltlotse.App.Browser;

/// <summary>Umsetzung von <see cref="IBrowser"/> über das Modul <c>js/zeltlotse.js</c>.</summary>
public sealed class JsBrowser(IJSRuntime js) : IBrowser, IAsyncDisposable
{
    private Task<IJSObjectReference>? _modul;

    private Task<IJSObjectReference> Modul() =>
        _modul ??= js.InvokeAsync<IJSObjectReference>("import", "./js/zeltlotse.js").AsTask();

    public async ValueTask<bool> IstOnlineAsync() =>
        await (await Modul()).InvokeAsync<bool>("istOnline");

    public async ValueTask<IAsyncDisposable> OnlineBeobachtenAsync(Func<bool, Task> geaendert)
    {
        var empfaenger = DotNetObjectReference.Create(new OnlineEmpfaenger(geaendert));
        var beobachtung = await (await Modul()).InvokeAsync<IJSObjectReference>("onlineBeobachten", empfaenger);
        return new Beobachtung(beobachtung, empfaenger);
    }

    public async ValueTask<DateTimeOffset?> ZuletztAktualisiertLesenAsync()
    {
        var wert = await (await Modul()).InvokeAsync<string?>("zuletztAktualisiertLesen");
        return DateTimeOffset.TryParse(wert, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var zeitpunkt)
            ? zeitpunkt
            : null;
    }

    public async ValueTask ZuletztAktualisiertSchreibenAsync(DateTimeOffset zeitpunkt) =>
        await (await Modul()).InvokeVoidAsync("zuletztAktualisiertSchreiben", zeitpunkt.ToString("O", CultureInfo.InvariantCulture));

    public async ValueTask NeuLadenAsync() =>
        await (await Modul()).InvokeVoidAsync("neuLaden");

    public async ValueTask DisposeAsync()
    {
        if (_modul is not null)
        {
            try
            {
                await (await _modul).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Die Seite ist schon weg.
            }
        }
    }

    /// <summary>Ziel der Rückrufe aus <c>onlineBeobachten</c>.</summary>
    public sealed class OnlineEmpfaenger(Func<bool, Task> geaendert)
    {
        [JSInvokable("OnlineGeaendert")]
        public Task OnlineGeaendert(bool online) => geaendert(online);
    }

    private sealed class Beobachtung(IJSObjectReference beobachtung, DotNetObjectReference<OnlineEmpfaenger> empfaenger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await beobachtung.InvokeVoidAsync("beenden");
                await beobachtung.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Die Seite ist schon weg.
            }

            empfaenger.Dispose();
        }
    }
}
