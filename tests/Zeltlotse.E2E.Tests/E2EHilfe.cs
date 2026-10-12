using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Zeltlotse.E2E.Tests;

/// <summary>Gemeinsame Schritte der Ende-zu-Ende-Tests: Anmelden per Login-Link aus dem Mail-Ordner, axe, CSP-Mitschnitt.</summary>
internal static class E2EHilfe
{
    private static readonly string[] AxeTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    public static readonly LocatorAssertionsToBeVisibleOptions Lange = new() { Timeout = 60_000 };

    /// <summary>
    /// Meldet die Seite per Login-Link an: Link auf der Seite „Anmelden“ anfordern, Link aus der Mail öffnen, „Anmelden“.
    /// Endet auf der Startseite.
    /// </summary>
    public static async Task PerLinkAnmeldenAsync(IPage seite, VeroeffentlichterHost host, string mail)
    {
        var vorher = host.Mails.An(mail).Count;
        await seite.GotoAsync(new Uri(host.Adresse, "/app/anmelden").ToString());
        await seite.Locator("#anmelden-mail").FillAsync(mail);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Login-Link schicken" }).ClickAsync();
        await Assertions.Expect(seite.GetByText("Login-Link ist verschickt. Bitte im Postfach nachsehen.")).ToBeVisibleAsync(Lange);
        var link = (await host.Mails.WartenAufAsync(mail, vorher + 1)).Link;

        await seite.GotoAsync(link);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^Hallo"), new() { Timeout = 60_000 });
    }

    /// <summary>
    /// Der Anmeldelink der Demo-Freizeit (Story 2-1): Jonas (Leitung) meldet sich in einem eigenen Browser-Kontext an und liest den
    /// Link aus den Einstellungen der Freizeit. Kein Erneuern, damit die Demo-Daten unverändert bleiben.
    /// </summary>
    /// <param name="teilnehmende">Link für Teilnehmende (sonst der für Mitarbeitende).</param>
    public static async Task<string> AnmeldelinkAsync(VeroeffentlichterHost host, bool teilnehmende = true)
    {
        var leitung = await host.KontextAsync(new BrowserNewContextOptions { Locale = "de-DE", TimezoneId = "Europe/Berlin" });
        try
        {
            var seite = await leitung.NewPageAsync();
            await PerLinkAnmeldenAsync(seite, host, "jonas.huettner@example.org");
            await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
            await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
            await seite.GetByRole(AriaRole.Link, new() { Name = "Einstellungen" }).ClickAsync(new() { Timeout = 60_000 });
            await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Einstellungen", new() { Timeout = 60_000 });
            var links = seite.GetByRole(AriaRole.Link, new() { Name = "Freizeitseite öffnen" });
            await Assertions.Expect(links).ToHaveCountAsync(2, new() { Timeout = 60_000 });
            var link = await (teilnehmende ? links.First : links.Last).GetAttributeAsync("href");
            Assert.NotNull(link);
            return link;
        }
        finally
        {
            await leitung.CloseAsync();
        }
    }

    /// <summary>Das Token am Ende eines Anmeldelinks.</summary>
    public static string Token(string link) => link[(link.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Füllt das Gast-Formular (Story 2-2) aus, setzt den Haken und sendet es ab; endet auf der Antwortseite „Anmeldung für …“.
    /// </summary>
    public static async Task GastFormularAbsendenAsync(IPage seite, string kind, string elternVorname, string mail)
    {
        await seite.Locator("#kind-vorname").FillAsync(kind);
        await seite.Locator("#kind-nachname").FillAsync("Beispiel");
        await seite.Locator("#kind-geburtsdatum").FillAsync("2016-04-01");
        await seite.Locator("#eltern-vorname").FillAsync(elternVorname);
        await seite.Locator("#eltern-nachname").FillAsync("Beispiel");
        await seite.Locator("#eltern-mail").FillAsync(mail);
        await seite.GetByLabel("Teilnahmebedingungen gelesen und bestätigt *").CheckAsync();
        await seite.GetByRole(AriaRole.Button, new() { Name = "Anmeldung absenden" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync($"Anmeldung für {kind}", new() { Timeout = 60_000 });
    }

    /// <summary>axe ohne Verstoß gegen WCAG 2.2 AA.</summary>
    public static async Task AxeAsync(IPage seite)
    {
        var axe = await seite.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. AxeTags] } });
        Assert.True(axe.Violations.Length == 0, $"axe auf {seite.Url}: " + string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {string.Join(", ", v.Nodes.Select(n => string.Join(" ", n.Target)))}")));
    }

    /// <summary>Meldet jede CSP-Verletzung als Konsolenzeile (Playwright-Initialskripte laufen außerhalb der CSP) und schreibt die Konsole mit.</summary>
    public static async Task<List<string>> KonsoleMitschreibenAsync(IBrowserContext kontext, IPage seite)
    {
        await kontext.AddInitScriptAsync(
            "document.addEventListener('securitypolicyviolation', e => console.error('Content Security Policy verletzt: ' + e.violatedDirective + ' ' + e.blockedURI));");
        var konsole = new List<string>();
        seite.Console += (_, nachricht) =>
        {
            lock (konsole)
            {
                konsole.Add(nachricht.Text);
            }
        };
        seite.PageError += (_, fehler) =>
        {
            lock (konsole)
            {
                konsole.Add(fehler);
            }
        };
        return konsole;
    }

    public static void OhneCspMeldung(List<string> konsole)
    {
        lock (konsole)
        {
            Assert.DoesNotContain(konsole, n => n.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase));
        }
    }
}
