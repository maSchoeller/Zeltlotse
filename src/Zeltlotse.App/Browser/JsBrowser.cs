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
        return new Beobachtung<OnlineEmpfaenger>(beobachtung, empfaenger);
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

    public async ValueTask DruckenAsync() =>
        await (await Modul()).InvokeVoidAsync("drucken");

    public async ValueTask DateiSpeichernAsync(string name, string medientyp, byte[] inhalt) =>
        await (await Modul()).InvokeVoidAsync("dateiSpeichern", name, medientyp, inhalt);

    public async ValueTask<PushStatus> PushStatusAsync() =>
        await (await Modul()).InvokeAsync<string>("pushStatus") switch
        {
            "an" => PushStatus.An,
            "aus" => PushStatus.Aus,
            "abgelehnt" => PushStatus.Abgelehnt,
            "homebildschirm" => PushStatus.NurMitHomeBildschirm,
            _ => PushStatus.NichtUnterstuetzt,
        };

    public async ValueTask<PushAboDaten?> PushAboLesenAsync() =>
        await (await Modul()).InvokeAsync<PushAboDaten?>("pushAboLesen");

    public async ValueTask<PushAboDaten?> PushAbonnierenAsync(string oeffentlicherSchluessel) =>
        await (await Modul()).InvokeAsync<PushAboDaten?>("pushAbonnieren", oeffentlicherSchluessel);

    /// <summary>Fehler des Browsers (JS) gelten als „kein Abo beendet“; so übersteht die Abmeldung der Sitzung sie.</summary>
    public async ValueTask<string?> PushAbmeldenAsync()
    {
        try
        {
            return await (await Modul()).InvokeAsync<string?>("pushAbmelden");
        }
        catch (Exception fehler) when (fehler is JSException or JSDisconnectedException)
        {
            return null;
        }
    }

    public async ValueTask<IAsyncDisposable> PushBeobachtenAsync(Func<Task> eingegangen)
    {
        var empfaenger = DotNetObjectReference.Create(new PushEmpfaenger(eingegangen));
        var beobachtung = await (await Modul()).InvokeAsync<IJSObjectReference>("pushBeobachten", empfaenger);
        return new Beobachtung<PushEmpfaenger>(beobachtung, empfaenger);
    }

    public async ValueTask<bool> KopierenAsync(string text) =>
        await (await Modul()).InvokeAsync<bool>("kopieren", text);

    public async ValueTask AnsEndeScrollenAsync(string elementId) =>
        await (await Modul()).InvokeVoidAsync("ansEndeScrollen", elementId);

    // Story 6-5: Offline-Speicher. Kann der Browser IndexedDB oder WebCrypto nicht (z. B. privater Modus), gilt: nichts gespeichert.
    public ValueTask OfflineSchreibenAsync(string schluessel, Guid freizeitId, long ablaufMs, string text) =>
        OhneFehlerAsync(m => m.InvokeVoidAsync("offlineSchreiben", schluessel, freizeitId.ToString("D"), ablaufMs, text));

    public async ValueTask<string?> OfflineLesenAsync(string schluessel)
    {
        try
        {
            return await (await Modul()).InvokeAsync<string?>("offlineLesen", schluessel);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async ValueTask<IReadOnlyList<OfflineDatensatz>> OfflineListeAsync(string praefix)
    {
        try
        {
            return await (await Modul()).InvokeAsync<List<OfflineDatensatz>>("offlineListe", praefix) ?? [];
        }
        catch (JSException)
        {
            return [];
        }
    }

    public ValueTask OfflineEntfernenAsync(string schluessel) => OhneFehlerAsync(m => m.InvokeVoidAsync("offlineEntfernen", schluessel));

    public ValueTask OfflineFreizeitLoeschenAsync(Guid freizeitId) =>
        OhneFehlerAsync(m => m.InvokeVoidAsync("offlineFreizeitLoeschen", freizeitId.ToString("D")));

    public ValueTask OfflineAllesLoeschenAsync() => OhneFehlerAsync(m => m.InvokeVoidAsync("offlineAllesLoeschen"));

    public ValueTask OfflineAufraeumenAsync() => OhneFehlerAsync(m => m.InvokeVoidAsync("offlineAufraeumen"));

    public ValueTask SpeicherHaltenAsync() => OhneFehlerAsync(m => m.InvokeVoidAsync("speicherHalten"));

    private async ValueTask OhneFehlerAsync(Func<IJSObjectReference, ValueTask> aufruf)
    {
        try
        {
            await aufruf(await Modul());
        }
        catch (JSException)
        {
            // Ohne Offline-Speicher im Browser: dann eben nichts gespeichert.
        }
    }

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

    /// <summary>Story 6-1: Ziel der Rückrufe aus <c>pushBeobachten</c>.</summary>
    public sealed class PushEmpfaenger(Func<Task> eingegangen)
    {
        [JSInvokable("PushEingegangen")]
        public Task PushEingegangen() => eingegangen();
    }

    private sealed class Beobachtung<TEmpfaenger>(IJSObjectReference beobachtung, DotNetObjectReference<TEmpfaenger> empfaenger) : IAsyncDisposable
        where TEmpfaenger : class
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
