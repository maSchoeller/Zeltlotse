using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Zeltlotse.E2E.Tests.E2EHilfe;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Kernablauf „Login“ (AD-30, Story 1-1) in Handygröße 375×812, hell: Login per Link aus der Mail, per Code, Passwort
/// festlegen → abmelden → mit Passwort anmelden, Vorname per Autosave. Auf jeder Seite axe ohne Verstoß gegen WCAG 2.2 AA
/// und keine CSP-Meldung in der Konsole.
/// </summary>
public sealed partial class LoginTests(VeroeffentlichterHost host)
{
    private async Task<(IBrowserContext Kontext, IPage Seite, List<string> Konsole)> HandyAsync()
    {
        var kontext = await host.KontextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 375, Height = 812 },
            ColorScheme = ColorScheme.Light,
            Locale = "de-DE",
            TimezoneId = "Europe/Berlin",
        });
        var seite = await kontext.NewPageAsync();
        var konsole = await KonsoleMitschreibenAsync(kontext, seite);
        return (kontext, seite, konsole);
    }

    private static string NeueMail(string art) => $"{art}-{Guid.NewGuid():N}@example.org";

    [Fact]
    public async Task Ohne_Sitzung_fuehrt_die_App_zur_Anmeldung_und_der_Login_Link_aus_der_Mail_meldet_an()
    {
        var (kontext, seite, konsole) = await HandyAsync();
        await using var _ = kontext;
        var mail = NeueMail("link");

        await seite.GotoAsync(new Uri(host.Adresse, "/app/").ToString());
        await Assertions.Expect(seite).ToHaveURLAsync(new Regex("/app/anmelden$"), new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmelden");
        await AxeAsync(seite);

        await seite.GetByLabel("Mail-Adresse").FillAsync(mail);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Login-Link schicken" }).ClickAsync();
        await Assertions.Expect(seite.GetByText("Login-Link ist verschickt. Bitte im Postfach nachsehen.")).ToBeVisibleAsync(Lange);
        await AxeAsync(seite);

        var link = (await host.Mails.WartenAufAsync(mail)).Link;
        Assert.StartsWith(host.Adresse + "app/anmelden/link/", link, StringComparison.Ordinal);
        await seite.GotoAsync(link);
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true })).ToBeVisibleAsync(Lange);
        await AxeAsync(seite);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Hallo", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("header a.zl-konto-link")).ToHaveTextAsync("Konto");
        await AxeAsync(seite);

        // Derselbe Link ein zweites Mal: nicht mehr gültig.
        await seite.GotoAsync(link);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.GetByText("Dieser Link ist nicht mehr gültig.")).ToBeVisibleAsync(Lange);
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Neuen Link schicken" })).ToBeVisibleAsync();
        await AxeAsync(seite);
        OhneCspMeldung(konsole);
    }

    [Fact]
    public async Task Login_Code_aus_der_Mail_meldet_an_und_ein_falscher_Code_zeigt_den_Fehler()
    {
        var (kontext, seite, konsole) = await HandyAsync();
        await using var _ = kontext;
        var mail = NeueMail("code");
        await seite.GotoAsync(new Uri(host.Adresse, "/app/anmelden").ToString());
        await seite.GetByLabel("Mail-Adresse").FillAsync(mail);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Login-Link schicken" }).ClickAsync();
        var code = (await host.Mails.WartenAufAsync(mail)).Code;

        await seite.GetByLabel("Login-Code").FillAsync(code == "000000" ? "000001" : "000000");
        await seite.GetByRole(AriaRole.Button, new() { Name = "Mit Code anmelden" }).ClickAsync();
        await Assertions.Expect(seite.GetByText("Der Code stimmt nicht oder ist abgelaufen.")).ToBeVisibleAsync(Lange);
        await AxeAsync(seite);

        await seite.GetByLabel("Login-Code").FillAsync(code);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Mit Code anmelden" }).ClickAsync();

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Hallo", new() { Timeout = 60_000 });
        await Assertions.Expect(seite).ToHaveURLAsync(new Regex("/app/$"));
        await AxeAsync(seite);
        OhneCspMeldung(konsole);
    }

    [Fact]
    public async Task Vorname_per_Autosave_Passwort_festlegen_abmelden_und_mit_Passwort_wieder_anmelden()
    {
        var (kontext, seite, konsole) = await HandyAsync();
        await using var _ = kontext;
        var mail = NeueMail("passwort");
        const string passwort = "Lagerfeuer-am-See-2027";
        await PerLinkAnmeldenAsync(seite, host, mail);

        await seite.Locator("header a.zl-konto-link").ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Konto", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText(mail)).ToBeVisibleAsync(Lange);
        await AxeAsync(seite);

        var vorname = seite.GetByLabel("Vorname");
        await vorname.FillAsync("Mara");
        await vorname.PressAsync("Tab");
        await Assertions.Expect(seite.GetByRole(AriaRole.Status).Filter(new() { HasText = "Gespeichert" })).ToBeVisibleAsync(Lange);
        await AxeAsync(seite);

        await seite.GetByLabel("Passwort", new() { Exact = true }).FillAsync(passwort);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Passwort speichern" }).ClickAsync();
        await Assertions.Expect(seite.GetByText("Passwort ist festgelegt.")).ToBeVisibleAsync(Lange);
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Passwort ändern" })).ToBeVisibleAsync();
        await AxeAsync(seite);

        await seite.GetByRole(AriaRole.Button, new() { Name = "Abmelden" }).ClickAsync();
        await Assertions.Expect(seite).ToHaveURLAsync(new Regex("/app/anmelden$"), new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("header a.zl-konto-link")).ToHaveCountAsync(0);

        await seite.GetByRole(AriaRole.Link, new() { Name = "Mit Passwort anmelden" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Mit Passwort anmelden", new() { Timeout = 60_000 });
        await seite.GetByLabel("Mail-Adresse").FillAsync(mail);
        await seite.GetByLabel("Passwort", new() { Exact = true }).FillAsync("falsches-Passwort-123");
        await seite.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.GetByText("Mail-Adresse oder Passwort stimmt nicht.")).ToBeVisibleAsync(Lange);
        await AxeAsync(seite);

        await seite.GetByLabel("Passwort", new() { Exact = true }).FillAsync(passwort);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Hallo, Mara", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("header a.zl-konto-link")).ToHaveTextAsync("Mara");
        await AxeAsync(seite);
        OhneCspMeldung(konsole);
    }
}
