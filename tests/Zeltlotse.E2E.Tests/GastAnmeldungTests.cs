using Microsoft.Playwright;
using static Zeltlotse.E2E.Tests.E2EHilfe;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Kernablauf „Gast-Anmeldung“ (AD-30, Story 2-2) in Handygröße 375×812, hell: Anmeldelink der Demo-Freizeit öffnen, „Anmelden“,
/// Absenden ohne Angaben zeigt die Fehlerübersicht, ausfüllen und absenden zeigt „Anmeldung eingegangen“, der Gast-Link aus der
/// Mail zeigt den Status „Eingegangen“. Auf jeder Seite axe ohne Verstoß gegen WCAG 2.2 AA und keine CSP-Meldung in der Konsole.
/// </summary>
public sealed class GastAnmeldungTests(VeroeffentlichterHost host)
{
    [Fact]
    public async Task Gast_meldet_ein_Kind_am_Handy_ohne_Konto_an_und_sieht_den_Status_ueber_den_Gast_Link()
    {
        var link = await AnmeldelinkAsync(host);
        var kontext = await host.KontextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 375, Height = 812 },
            ColorScheme = ColorScheme.Light,
            Locale = "de-DE",
            TimezoneId = "Europe/Berlin",
        });
        await using var _ = kontext;
        var seite = await kontext.NewPageAsync();
        var konsole = await KonsoleMitschreibenAsync(kontext, seite);
        var mail = $"gast-{Guid.NewGuid():N}@example.org";

        // 1. Freizeitseite über den Anmeldelink, „Anmelden“ tippen.
        await seite.GotoAsync(link);
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await AxeAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldung", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Ohne Konto möglich. Gefragt wird nur das Nötigste.")).ToBeVisibleAsync();

        // 2. Absenden ohne Angaben: Fehlerübersicht oben, Fehler an den Feldern.
        await seite.GetByRole(AriaRole.Button, new() { Name = "Anmeldung absenden" }).ClickAsync();
        var uebersicht = seite.Locator(".zl-fehleruebersicht");
        await Assertions.Expect(uebersicht).ToBeVisibleAsync(Lange);
        await Assertions.Expect(uebersicht.GetByRole(AriaRole.Link, new() { Name = "Kind: Vorname fehlt." })).ToBeVisibleAsync();
        await Assertions.Expect(uebersicht.GetByRole(AriaRole.Link, new() { Name = "Teilnahmebedingungen nicht bestätigt." })).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("#kind-vorname")).ToHaveAttributeAsync("aria-invalid", "true");
        await AxeAsync(seite);

        // 3. Ausfüllen, Haken setzen, „Anmeldung absenden“.
        await GastFormularAbsendenAsync(seite, "Mila", "Sabine", mail);
        await Assertions.Expect(seite.GetByText("Anmeldung eingegangen:")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Die Leitung meldet sich in Kürze.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Konto erstellen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Weiteres Kind anmelden" })).ToBeVisibleAsync();
        await AxeAsync(seite);

        // 4. Der Gast-Link aus der Mail zeigt den Status „Eingegangen“.
        var eingang = await host.Mails.WartenAufAsync(mail);
        Assert.Equal("Anmeldung eingegangen: Zeltlager Sommer 2027", eingang.Betreff);
        Assert.StartsWith(host.Adresse + "gast/", eingang.Link, StringComparison.Ordinal);
        await seite.GotoAsync(eingang.Link);
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldungen", new() { Timeout = 60_000 });
        var karte = seite.Locator(".elkw-card").Filter(new() { HasText = "Mila" });
        await Assertions.Expect(karte).ToContainTextAsync("Zeltlager Sommer 2027");
        await Assertions.Expect(karte.Locator(".elkw-status")).ToHaveTextAsync("Eingegangen");
        await AxeAsync(seite);

        // 5. Keine CSP-Meldung.
        OhneCspMeldung(konsole);
    }
}
