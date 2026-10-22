using System.Text.Json;
using Microsoft.Playwright;
using static Zeltlotse.E2E.Tests.E2EHilfe;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Story 6-5, Offline-Modus (FR-65, AD-18, AD-30) in Handygröße 375×812, hell, gegen den veröffentlichten Host: Eine eigene laufende Freizeit
/// (<see cref="LaufendeFreizeit"/>) wird online geöffnet; der Abgleich legt sie verschlüsselt in IndexedDB ab (kein Klartext, z. B. das Medikament nicht im
/// Rohinhalt). Im Flugmodus lädt die App neu: Notfall-Box „Notfallhilfe öffnen“ zur Sicherheitskette, Suche nach dem Kind zur Notfallkarte mit den
/// Notfallangaben, Küchen-Tagesansicht; andere Bereiche zeigen „Offline nicht verfügbar.“. Ein Vorfall-Eintrag offline wartet („1 Eintrag wartet auf
/// Verbindung“), mit Verbindung steht er in der Liste vom Server. Mit der Browser-Uhr nach dem Freizeitende ist offline alles gelöscht; Abmelden leert
/// IndexedDB. Auf jeder Seite axe ohne Verstoß gegen WCAG 2.2 AA und keine CSP-Meldung.
/// </summary>
public sealed class OfflineModusTests(VeroeffentlichterHost host)
{
    private static BrowserNewContextOptions Handy() => new()
    {
        ViewportSize = new ViewportSize { Width = 375, Height = 812 },
        ColorScheme = ColorScheme.Light,
        Locale = "de-DE",
        TimezoneId = "Europe/Berlin",
    };

    [Fact]
    public async Task Flugmodus_Notfallkarte_Tagesansicht_und_Vorfall_Warteschlange_Ablauf_und_Abmelden()
    {
        var kennung = Guid.NewGuid().ToString("N")[..8];
        var freizeit = new LaufendeFreizeit(host);
        var kontext = await host.KontextAsync(Handy());
        await using var _ = kontext;
        var seite = await kontext.NewPageAsync();
        var konsole = await KonsoleMitschreibenAsync(kontext, seite);
        await freizeit.AufbauenAsync(kontext, seite, $"Offline-Probe {kennung}", $"offline-leitung-{kennung}@example.org", $"offline-eltern-{kennung}@example.org");
        var id = freizeit.FreizeitId;
        var kind = $"{LaufendeFreizeit.Kind} {LaufendeFreizeit.Familienname}";

        // 1. Online öffnen: Der Abgleich speichert Freizeit, Kette, Notfallkarte und jeden Tag; der Service Worker steuert die Seite.
        await seite.GotoAsync(new Uri(host.Adresse, $"/app/freizeiten/{id}").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(freizeit.Name, new() { Timeout = 60_000 });
        await seite.EvaluateAsync("() => navigator.serviceWorker.ready");
        await freizeit.GespeichertAsync(seite);
        await seite.ReloadAsync();
        await seite.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null", null, new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(freizeit.Name, new() { Timeout = 60_000 });

        // 2. Kein Klartext in IndexedDB: weder Medikament noch Name, und genau ein Geräte-Schlüssel.
        var roh = await seite.EvaluateAsync<JsonElement>(LaufendeFreizeit.OfflineRohJs);
        Assert.Contains($"{id:D}/notfallkarte", roh.GetProperty("schluessel").EnumerateArray().Select(s => s.GetString()));
        Assert.DoesNotContain("Salbutamol", roh.GetProperty("roh").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(LaufendeFreizeit.Kind, roh.GetProperty("roh").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, roh.GetProperty("geraet").GetInt32());

        // 3. Flugmodus und neu laden: Notfall-Box führt zur gespeicherten Sicherheitskette.
        await kontext.SetOfflineAsync(true);
        await seite.ReloadAsync();
        var box = seite.Locator(".zl-notfall-box");
        await Assertions.Expect(box).ToContainTextAsync("Die Sicherheitskette ist auf diesem Gerät gespeichert.", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Offline nicht verfügbar.")).ToBeVisibleAsync();
        await box.GetByRole(AriaRole.Link, new() { Name = "Notfallhilfe öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Sicherheitskette", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("a[href='tel:0123456792']")).ToHaveTextAsync($"Leitung anrufen: {LaufendeFreizeit.LeitungTelefon}", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Offline nicht verfügbar.")).ToHaveCountAsync(0);
        await Assertions.Expect(seite.Locator("a.elkw-btn", new() { HasText = "Notfallordner drucken" })).ToHaveAttributeAsync("aria-disabled", "true");
        await AxeAsync(seite);

        // 4. Suche nach dem Kind öffnet die Notfallkarte mit den Notfallangaben.
        await seite.GetByLabel("Betroffene Person suchen").FillAsync(LaufendeFreizeit.Kind[..3]);
        var treffer = seite.Locator(".elkw-people").GetByRole(AriaRole.Link, new() { Name = kind });
        await Assertions.Expect(treffer).ToBeVisibleAsync(Lange);
        await treffer.ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Notfallkarte", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync(kind, new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("Milch");
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync(LaufendeFreizeit.Medikament);
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("Karin Sommer (Mutter), 0123 456794");
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("keine Angabe");
        await AxeAsync(seite);

        // 5. Vorfall offline: Feld verlassen → wartet auf dem Gerät, Offline-Leiste zählt.
        await seite.GetByRole(AriaRole.Link, new() { Name = "Vorfall-Protokoll", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Vorfall-Protokoll", new() { Timeout = 60_000 });
        var geschehen = seite.GetByLabel("Was ist passiert?", new() { Exact = true });
        await Assertions.Expect(geschehen).ToBeVisibleAsync(Lange);
        const string vorfall = "Beim Wandern gestolpert, Knie gekühlt.";
        await geschehen.FillAsync(vorfall);
        await geschehen.PressAsync("Tab");
        var protokoll = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Protokoll", Exact = true }) });
        await Assertions.Expect(protokoll.Locator("div[id^=vorfall-]").First).ToContainTextAsync("wartet auf Verbindung", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Was ist passiert?", new() { Exact = true })).ToHaveValueAsync("");
        await Assertions.Expect(seite.Locator(".zl-offline-leiste")).ToContainTextAsync("1 Eintrag wartet auf Verbindung");
        Assert.DoesNotContain("gestolpert", (await seite.EvaluateAsync<JsonElement>(LaufendeFreizeit.OfflineRohJs)).GetProperty("roh").GetString(), StringComparison.Ordinal);
        await AxeAsync(seite);

        // 6. Küchen-Tagesansicht offline (neu geladen aus dem Service Worker), der Restetag ist ausgegraut.
        await seite.GotoAsync(new Uri(host.Adresse, $"/app/freizeiten/{id}/kueche/tagesansicht").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Tagesansicht", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("Frühstück", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Restetag").First).ToBeDisabledAsync();
        await Assertions.Expect(seite.GetByText("Offline nicht verfügbar.")).ToHaveCountAsync(0);
        await AxeAsync(seite);

        // 7. Ein anderer Bereich ist ausgegraut.
        await seite.GotoAsync(new Uri(host.Adresse, $"/app/freizeiten/{id}/kueche/einkaufsliste").ToString());
        await Assertions.Expect(seite.GetByText("Offline nicht verfügbar.")).ToBeVisibleAsync(Lange);

        // 8. Wieder online: Der Eintrag geht an den Server, die Leiste verschwindet, die Liste kommt vom Server.
        await seite.GotoAsync(new Uri(host.Adresse, $"/app/freizeiten/{id}/notfall").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Sicherheitskette", new() { Timeout = 60_000 });
        await kontext.SetOfflineAsync(false);
        await Assertions.Expect(seite.Locator(".zl-offline-leiste")).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        var eintraege = await freizeit.LesenAsync(kontext, $"/api/freizeiten/{id}/sicherheit/vorfaelle");
        var eintrag = Assert.Single(eintraege.EnumerateArray(), e => e.GetProperty("text").GetString() == vorfall);
        Assert.Equal("Nora", eintrag.GetProperty("verfasserVorname").GetString());
        Assert.True(DateTimeOffset.UtcNow - eintrag.GetProperty("erfasstAm").GetDateTimeOffset() < TimeSpan.FromMinutes(10));

        // 9. Browser-Uhr nach dem Freizeitende, offline neu geladen: Die Daten der Freizeit sind gelöscht.
        await freizeit.GespeichertAsync(seite);
        var ende = new DateTimeOffset(freizeit.LetzterTag.AddDays(2).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        await kontext.SetOfflineAsync(true);
        await seite.Clock.SetFixedTimeAsync(ende.UtcDateTime);
        await seite.ReloadAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Sicherheitskette", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Auf diesem Gerät nicht gespeichert.")).ToBeVisibleAsync(Lange);
        await Assertions.Expect(seite.Locator(".zl-notfall-box")).ToContainTextAsync("Offline gilt der ausgedruckte Notfallordner.");
        Assert.DoesNotContain(await LaufendeFreizeit.SchluesselAsync(seite), s => s.StartsWith(id.ToString("D"), StringComparison.Ordinal));

        // 10. Abmelden leert IndexedDB (Datensätze und Geräte-Schlüssel).
        await kontext.SetOfflineAsync(false);
        await seite.GotoAsync(new Uri(host.Adresse, "/app/konto").ToString());
        await seite.GetByRole(AriaRole.Button, new() { Name = "Abmelden" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmelden", new() { Timeout = 60_000 });
        var nachher = await seite.EvaluateAsync<JsonElement>(LaufendeFreizeit.OfflineRohJs);
        Assert.Empty(nachher.GetProperty("schluessel").EnumerateArray());
        Assert.Equal(0, nachher.GetProperty("geraet").GetInt32());

        OhneCspMeldung(konsole);
    }
}
