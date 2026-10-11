using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Matrix-Zeile „Offline-Start“ (AD-18, AD-25, AD-30): Die Web-App wurde einmal online geladen; offline lädt sie neu
/// aus dem Service Worker und zeigt Offline-Leiste, Notfall-Box mit „Notruf 112“ und sonst „Offline nicht verfügbar.“.
/// Handygröße 375×812, hell und dunkel; ohne CSP-Meldung in der Konsole; axe ohne Verstoß gegen WCAG 2.2 AA.
/// </summary>
public sealed partial class OfflineStartTests(VeroeffentlichterHost host)
{
    private static readonly string[] AxeTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task App_startet_offline_aus_dem_Service_Worker_mit_Offline_Leiste_und_Notfall_Box(ColorScheme farbschema)
    {
        await using var kontext = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 375, Height = 812 },
            ColorScheme = farbschema,
            Locale = "de-DE",
            TimezoneId = "Europe/Berlin",
        });
        var konsole = await KonsoleMitschreibenAsync(kontext);
        var seite = await kontext.NewPageAsync();
        seite.Console += (_, nachricht) => konsole.Add(nachricht.Text);
        seite.PageError += (_, fehler) => konsole.Add(fehler);

        // Einmal online laden, bis der Service Worker alle Dateien abgelegt hat und die Seite steuert.
        await seite.GotoAsync(new Uri(host.Adresse, "/app/").ToString());
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await seite.EvaluateAsync("() => navigator.serviceWorker.ready");
        await seite.ReloadAsync();
        await seite.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null", null, new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await kontext.SetOfflineAsync(true);
        await seite.ReloadAsync();

        var leiste = seite.Locator(".zl-offline-leiste");
        await Assertions.Expect(leiste).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(leiste).ToHaveTextAsync(OfflineText());
        var notruf = seite.Locator(".zl-notfall-box a[href='tel:112']");
        await Assertions.Expect(notruf).ToHaveTextAsync("Notruf 112");
        await Assertions.Expect(seite.Locator(".zl-notfall-box")).ToContainTextAsync("Im Notfall:");
        await Assertions.Expect(seite.GetByText("Offline nicht verfügbar.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator(".zl-offline-bereich[inert]")).ToContainTextAsync("Noch keine Freizeit geöffnet.");
        var hoehe = await notruf.EvaluateAsync<double>("e => e.getBoundingClientRect().height");
        Assert.True(hoehe >= 44, $"Der Link „Notruf 112“ ist nur {hoehe} px hoch.");

        // Die API kommt nie aus dem Cache: offline scheitert der Aufruf.
        var api = await seite.EvaluateAsync<string>("() => fetch('/api/betrieb/antiforgery').then(() => 'beantwortet', () => 'gescheitert')");
        Assert.Equal("gescheitert", api);

        var axe = await seite.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. AxeTags] } });
        Assert.True(axe.Violations.Length == 0, "axe: " + string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {string.Join(", ", v.Nodes.Select(n => string.Join(" ", n.Target)))}")));
        Assert.DoesNotContain(konsole, n => n.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app/")]
    [InlineData("/gibt-es-nicht")]
    public async Task Seiten_laden_online_ohne_CSP_Meldung_und_ohne_axe_Verstoss(string pfad)
    {
        await using var kontext = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 375, Height = 812 },
        });
        var konsole = await KonsoleMitschreibenAsync(kontext);
        var seite = await kontext.NewPageAsync();
        seite.Console += (_, nachricht) => konsole.Add(nachricht.Text);
        seite.PageError += (_, fehler) => konsole.Add(fehler);

        await seite.GotoAsync(new Uri(host.Adresse, pfad).ToString());
        await Assertions.Expect(seite.Locator("h1")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var axe = await seite.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. AxeTags] } });
        Assert.True(axe.Violations.Length == 0, "axe: " + string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {string.Join(", ", v.Nodes.Select(n => string.Join(" ", n.Target)))}")));
        Assert.DoesNotContain(konsole, n => n.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Meldet jede CSP-Verletzung zusätzlich als Konsolenzeile (Playwright-Initialskripte laufen außerhalb der CSP).
    /// </summary>
    private static async Task<List<string>> KonsoleMitschreibenAsync(IBrowserContext kontext)
    {
        await kontext.AddInitScriptAsync(
            "document.addEventListener('securitypolicyviolation', e => console.error('Content Security Policy verletzt: ' + e.violatedDirective + ' ' + e.blockedURI));");
        return [];
    }

    [GeneratedRegex("^Offline( – zuletzt aktualisiert \\d\\d:\\d\\d\u00A0Uhr)?$")]
    private static partial Regex OfflineText();
}
