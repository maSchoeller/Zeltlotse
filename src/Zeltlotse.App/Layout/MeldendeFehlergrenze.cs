using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Ui.Api;

namespace Zeltlotse.App.Layout;

/// <summary>
/// Fehlergrenze im Layout der App (Konvention „Logging“, AD-22): meldet einen unerwarteten Fehler an
/// <c>POST /api/betrieb/fehler</c>, nur mit dem Typnamen der Ausnahme, dem Pfad ohne Abfrage und Fragment und einer neu
/// erzeugten Trace-ID aus 32 Hex-Zeichen. Scheitert die Meldung selbst, wird sie verworfen.
/// </summary>
public sealed partial class MeldendeFehlergrenze : ErrorBoundary
{
    [Inject] private BetriebApiClient Betrieb { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnErrorAsync(Exception exception)
    {
        // Protokollierung der Fehlergrenze in der Browser-Konsole wie bei ErrorBoundary.
        await base.OnErrorAsync(exception);
        var meldung = new BrowserFehler(Fehlerart(exception), Seite(Navigation.Uri), NeueTraceId());
        try
        {
            await Betrieb.FehlerMeldenAsync(meldung);
        }
#pragma warning disable CA1031 // Eine gescheiterte Meldung darf die Fehleranzeige nicht verhindern.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Verworfen: kein Wiederholen, kein Zwischenspeichern.
        }
    }

    // Längengrenzen der Formprüfung von POST /api/betrieb/fehler.
    private const int FehlerartLaenge = 100;
    private const int SeitenLaenge = 2000;

    /// <summary>Typname der Ausnahme, auf die erlaubten Zeichen und die Länge der API beschränkt.</summary>
    internal static string Fehlerart(Exception ausnahme) =>
        Kuerzen(UnerlaubteZeichen().Replace(ausnahme.GetType().Name, "_"), FehlerartLaenge);

    /// <summary>Pfad der Seite ohne Abfrage und Fragment, auf die erlaubten Zeichen und die Länge der API beschränkt.</summary>
    internal static string Seite(string adresse) =>
        Uri.TryCreate(adresse, UriKind.Absolute, out var uri)
            ? Kuerzen(UnerlaubteSeitenZeichen().Replace(uri.AbsolutePath, "_"), SeitenLaenge)
            : "/";

    private static string Kuerzen(string text, int laenge) => text.Length > laenge ? text[..laenge] : text;

    /// <summary>Neue Trace-ID: 16 Zufallsbytes als 32 Hex-Zeichen.</summary>
    internal static string NeueTraceId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    [GeneratedRegex("[^A-Za-z0-9._:-]")]
    private static partial Regex UnerlaubteZeichen();

    [GeneratedRegex("[^A-Za-z0-9/._~%-]")]
    private static partial Regex UnerlaubteSeitenZeichen();
}
