using System.Text.Json;
using Microsoft.Playwright;
using static Zeltlotse.E2E.Tests.E2EHilfe;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Kernablauf „Notfall“ (AD-30, Story 2-8) in Handygröße 375×812, hell. Der veröffentlichte Host läuft mit der Systemuhr, die
/// Demo-Freizeit liegt 2027; darum legt der Test über die echten Endpunkte eine eigene Freizeit an, die gerade läuft (vom Vortag bis in
/// drei Tagen): Andrea (Org-Admin der Demo-Organisation) legt sie an und lädt eine neue Leitung ein, die über den Link aus der Mail
/// annimmt und die Sicherheitskette per Autosave-PATCH einrichtet; ein Gast meldet über den Anmeldelink dieser Freizeit ein Kind an.
/// Dann: Startseite mit Notfall-Box und Eintrag „Notfall“, „Notfallhilfe öffnen“ zur Sicherheitskette mit „Notruf 112“ und dem Telefon
/// der Leitung, die ersten Buchstaben des Kindes in „Betroffene Person suchen“ und der Treffer öffnet den Freizeitpass; Story 2-10: dort
/// „Vorfall-Protokoll“, ein Eintrag in „Was ist passiert?“ wird beim Verlassen des Felds gespeichert. Auf jeder Seite
/// axe ohne Verstoß gegen WCAG 2.2 AA und keine CSP-Meldung in der Konsole.
/// </summary>
/// <remarks>
/// Die neue Leitung hat eine eigene Mail-Adresse statt Jonas: Die Doku-Screenshots und andere Tests desselben Laufs nutzen Jonas mit genau
/// einer Freizeit („Freizeit öffnen“); eine zweite, laufende Freizeit bei ihm würde sie je nach Reihenfolge verändern.
/// </remarks>
public sealed class NotfallTests(VeroeffentlichterHost host)
{
    private const string AndreaMail = "andrea.beispiel@example.org";
    private const string LeitungTelefon = "0123 456790";

    private static BrowserNewContextOptions Handy() => new()
    {
        ViewportSize = new ViewportSize { Width = 375, Height = 812 },
        ColorScheme = ColorScheme.Light,
        Locale = "de-DE",
        TimezoneId = "Europe/Berlin",
    };

    [Fact]
    public async Task Notfall_von_der_Startseite_ueber_die_Sicherheitskette_zum_Freizeitpass_am_Handy()
    {
        var kennung = Guid.NewGuid().ToString("N")[..8];
        var name = $"Notfall-Probe {kennung}";
        var leitungMail = $"notfall-leitung-{kennung}@example.org";
        var kind = "Ronja";

        // Aufbau 1: Andrea legt die laufende Freizeit an und lädt die Leitung ein.
        var freizeitId = await FreizeitAnlegenAsync(name, leitungMail);

        // Aufbau 2: Die Leitung meldet sich an, setzt ihren Namen, nimmt über den Link aus der Mail an und richtet die Kette ein.
        var kontext = await host.KontextAsync(Handy());
        await using var _ = kontext;
        var seite = await kontext.NewPageAsync();
        var konsole = await KonsoleMitschreibenAsync(kontext, seite);
        // Erst die Einladung abwarten, damit die Anmeldung danach den Login-Link als neueste Mail liest.
        var einladung = (await host.Mails.WartenAufAsync(leitungMail, 1, TimeSpan.FromSeconds(60))).Link;
        await PerLinkAnmeldenAsync(seite, host, leitungMail);
        await NameSetzenAsync(kontext, "Nora", "Notfall");
        await seite.GotoAsync(einladung);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Einladung annehmen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(name, new() { Timeout = 60_000 });
        foreach (var (feld, wert) in new[]
                 {
                     ("wasTun", "Ruhe bewahren.\nBei Lebensgefahr sofort den Notruf 112 wählen.\nLeitung anrufen."),
                     ("leitungTelefon", LeitungTelefon),
                     ("heimatTelefon", "0123 456791"),
                 })
        {
            await SendenAsync(kontext, "PATCH", $"/api/freizeiten/{freizeitId}/sicherheit/sicherheitskette", new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, [feld] = wert });
        }

        // Aufbau 3: Ein Gast meldet über den Anmeldelink dieser Freizeit ein Kind an; die Leitung sieht den Pass ab dem Eingang.
        var seiteJson = await LesenAsync(kontext, $"/api/freizeiten/{freizeitId}/freizeitseite");
        var anmeldelink = seiteJson.GetProperty("anmeldelinks").EnumerateArray().Single(l => l.GetProperty("art").GetString() == "Teilnehmende").GetProperty("adresse").GetString()!;
        await GastAnmeldenAsync(anmeldelink, kind);
        await PassSichtbarAsync(kontext, freizeitId, kind);

        // 1. Startseite: Notfall-Box oben, Eintrag „Notfall“ in der App-Leiste.
        await seite.GotoAsync(new Uri(host.Adresse, $"/app/freizeiten/{freizeitId}").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(name, new() { Timeout = 60_000 });
        var box = seite.Locator(".zl-notfall-box");
        await Assertions.Expect(box).ToBeVisibleAsync(Lange);
        await Assertions.Expect(box.GetByRole(AriaRole.Link, new() { Name = "Notruf 112" })).ToHaveAttributeAsync("href", "tel:112");
        await Assertions.Expect(seite.Locator("header a.zl-notfall-eintrag")).ToHaveTextAsync("Notfall", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Schnellsuche")).ToBeVisibleAsync(Lange);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AxeAsync(seite);

        // 2. „Notfallhilfe öffnen“ führt zur Sicherheitskette mit „Notruf 112“ und dem Telefon der Leitung.
        await box.GetByRole(AriaRole.Link, new() { Name = "Notfallhilfe öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Sicherheitskette", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("a[href='tel:112']")).ToHaveTextAsync("Notruf 112", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("a[href='tel:0123456790']")).ToHaveTextAsync($"Leitung anrufen: {LeitungTelefon}");
        await Assertions.Expect(seite.GetByText("Leitung: Nora Notfall")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("header a.zl-notfall-eintrag")).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AxeAsync(seite);

        // 3. Die ersten Buchstaben des Kindes in „Betroffene Person suchen“; der Treffer öffnet den Freizeitpass.
        await seite.GetByLabel("Betroffene Person suchen").FillAsync(kind[..3]);
        var treffer = seite.Locator(".elkw-people").GetByRole(AriaRole.Link, new() { Name = $"{kind} Beispiel" });
        await Assertions.Expect(treffer).ToBeVisibleAsync(Lange);
        await treffer.ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync($"{kind} Beispiel", new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AxeAsync(seite);

        // 4. Story 2-10: Im Pass „Vorfall-Protokoll“ öffnen, „Was ist passiert?“ ausfüllen und das Feld verlassen: Der Eintrag steht ohne
        //    Senden-Knopf oben im Protokoll, das Feld ist wieder leer.
        await seite.GetByRole(AriaRole.Link, new() { Name = "Vorfall-Protokoll", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Vorfall-Protokoll", new() { Timeout = 60_000 });
        var geschehen = seite.GetByLabel("Was ist passiert?", new() { Exact = true });
        await Assertions.Expect(geschehen).ToBeVisibleAsync(Lange);
        const string vorfall = "Beim Fußball umgeknickt, Knöchel gekühlt.";
        await geschehen.FillAsync(vorfall);
        await geschehen.PressAsync("Tab");
        var protokoll = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Protokoll", Exact = true }) });
        var neuester = protokoll.Locator("div[id^=vorfall-]").First;
        await Assertions.Expect(neuester).ToContainTextAsync(vorfall, new() { Timeout = 60_000 });
        await Assertions.Expect(neuester).ToContainTextAsync("Was passiert ist");
        await Assertions.Expect(neuester).ToContainTextAsync("von Nora Notfall");
        await Assertions.Expect(seite.GetByLabel("Was ist passiert?", new() { Exact = true })).ToHaveValueAsync("");
        await Assertions.Expect(seite.Locator("main button")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AxeAsync(seite);

        // 5. Keine CSP-Meldung.
        OhneCspMeldung(konsole);
    }

    /// <summary>Andrea legt die Freizeit vom Vortag bis in drei Tagen an und lädt die Leitung ein; liefert die Kennung der Freizeit.</summary>
    private async Task<Guid> FreizeitAnlegenAsync(string name, string leitungMail)
    {
        var kontext = await host.KontextAsync(new BrowserNewContextOptions { Locale = "de-DE", TimezoneId = "Europe/Berlin" });
        await using var _ = kontext;
        var seite = await kontext.NewPageAsync();
        await PerLinkAnmeldenAsync(seite, host, AndreaMail);
        var organisation = (await LesenAsync(kontext, "/api/ich/organisationen")).EnumerateArray()
            .Single(o => o.GetProperty("rollen").EnumerateArray().Any(r => r.GetString() == "OrgAdmin"))
            .GetProperty("organisationId").GetGuid();
        var heute = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);
        var angelegt = await SendenAsync(
            kontext, "POST", $"/api/organisationen/{organisation}/freizeiten",
            new { name, ersterTag = heute.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), letzterTag = heute.AddDays(3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });
        var freizeitId = angelegt.GetProperty("id").GetGuid();
        await SendenAsync(kontext, "POST", $"/api/organisationen/{organisation}/freizeiten/{freizeitId}/leitung/einladungen", new { mail = leitungMail });
        return freizeitId;
    }

    /// <summary>Ein Gast meldet über den Anmeldelink ein Kind der Familie „Beispiel“ an.</summary>
    private async Task GastAnmeldenAsync(string anmeldelink, string kind)
    {
        var kontext = await host.KontextAsync(Handy());
        await using var _ = kontext;
        var seite = await kontext.NewPageAsync();
        await seite.GotoAsync(anmeldelink);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Anmelden", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldung", new() { Timeout = 60_000 });
        await GastFormularAbsendenAsync(seite, kind, "Sabine", $"notfall-gast-{Guid.NewGuid():N}@example.org");
    }

    /// <summary>Wartet, bis die Freigabe (Outbox) zugestellt ist und die Leitung den Pass des Kindes sieht.</summary>
    private async Task PassSichtbarAsync(IBrowserContext kontext, Guid freizeitId, string kind)
    {
        for (var versuch = 0; versuch < 60; versuch++)
        {
            var person = (await LesenAsync(kontext, $"/api/freizeiten/{freizeitId}/personenliste")).EnumerateArray()
                .FirstOrDefault(p => p.GetProperty("vorname").GetString() == kind);
            if (person.ValueKind == JsonValueKind.Object)
            {
                var antwort = await kontext.APIRequest.GetAsync(Adresse($"/api/freizeiten/{freizeitId}/personen/freizeitpass/{person.GetProperty("personId").GetGuid()}"));
                if (antwort.Ok && (await antwort.JsonAsync())?.GetProperty("zustand").GetString() == "sichtbar")
                {
                    return;
                }
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Der Freizeitpass des Kindes wurde für die Leitung nicht sichtbar.");
    }

    /// <summary>Setzt Vor- und Nachname im eigenen Profil (Autosave-PATCH, AD-19).</summary>
    private async Task NameSetzenAsync(IBrowserContext kontext, string vorname, string nachname)
    {
        var version = (await SendenAsync(kontext, "POST", "/api/ich/profil", null)).GetProperty("version").GetUInt32();
        version = (await SendenAsync(kontext, "PATCH", "/api/ich/profil", new Dictionary<string, object?> { ["version"] = version, ["alterWert"] = null, ["vorname"] = vorname }))
            .GetProperty("version").GetUInt32();
        await SendenAsync(kontext, "PATCH", "/api/ich/profil", new Dictionary<string, object?> { ["version"] = version, ["alterWert"] = null, ["nachname"] = nachname });
    }

    private string Adresse(string pfad) => new Uri(host.Adresse, pfad).ToString();

    private async Task<JsonElement> LesenAsync(IBrowserContext kontext, string pfad)
    {
        var antwort = await kontext.APIRequest.GetAsync(Adresse(pfad));
        Assert.True(antwort.Ok, $"GET {pfad}: {antwort.Status}");
        return (await antwort.JsonAsync())!.Value;
    }

    /// <summary>Ändernder Aufruf mit der Sitzung des Kontexts und frisch geholtem Antiforgery-Token (AD-25); liefert die Antwort als JSON.</summary>
    private async Task<JsonElement> SendenAsync(IBrowserContext kontext, string methode, string pfad, object? koerper)
    {
        var token = (await LesenAsync(kontext, "/api/betrieb/antiforgery")).GetProperty("token").GetString()!;
        var antwort = await kontext.APIRequest.FetchAsync(Adresse(pfad), new()
        {
            Method = methode,
            DataObject = koerper,
            Headers = new Dictionary<string, string> { ["X-Zeltlotse-Antiforgery"] = token },
        });
        Assert.True(antwort.Ok, $"{methode} {pfad}: {antwort.Status} {await antwort.TextAsync()}");
        var text = await antwort.TextAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
