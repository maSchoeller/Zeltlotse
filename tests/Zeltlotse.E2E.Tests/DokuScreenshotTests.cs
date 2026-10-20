using System.Buffers.Binary;
using Microsoft.Playwright;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Doku-Screenshots (AD-30, NFR-20): Jede heutige Oberfläche in Handy- (375×812) und Laptop-Größe (1280×800), hell,
/// <c>de-DE</c>, <c>Europe/Berlin</c>, gegen den Host mit Demo-Daten. Die Bilder landen als <c>&lt;name&gt;-&lt;gerät&gt;.png</c>
/// unter <c>doku/src/assets/screenshots/</c> und werden bei jedem Lauf überschrieben. Vor dem Bild wird der sichtbare
/// Inhalt geprüft, danach Dateiformat und Breite (kein Pixelvergleich).
/// Gezielt: <c>dotnet test --project tests/Zeltlotse.E2E.Tests -- --filter-trait "Kategorie=Doku"</c>.
/// </summary>
[Trait("Kategorie", "Doku")]
public sealed class DokuScreenshotTests(VeroeffentlichterHost host)
{
    private static readonly byte[] PngSignatur = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Demo-Person aus dem Katalog (Leitung „Jonas Hüttner“); die Screenshots ändern ihre Daten nicht.</summary>
    private const string DemoMail = "jonas.huettner@example.org";

    public static TheoryData<string> Geraete => ["handy", "laptop"];

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task App_Start(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Hallo, Jonas");
        // Story 1-3: Jonas ist Leitung der Demo-Freizeit; die Startseite zeigt sie als Card statt „Noch keine Freizeit geöffnet.“
        var card = seite.Locator(".elkw-card").Filter(new() { HasText = "Zeltlager Sommer 2027" });
        await Assertions.Expect(card).ToContainTextAsync("Rolle: Leitung", new() { Timeout = 60_000 });
        await Assertions.Expect(card).ToContainTextAsync("Zeitraum: 02.08.2027 – 13.08.2027");
        await Assertions.Expect(card.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToHaveCountAsync(0);
        await Assertions.Expect(seite.Locator("header a.zl-konto-link")).ToHaveTextAsync("Jonas");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "app-start", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task App_Offline(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Angemeldet online laden, bis der Service Worker die Seite steuert; dann offline neu laden (wie OfflineStartTests).
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.EvaluateAsync("() => navigator.serviceWorker.ready");
        await seite.ReloadAsync();
        await seite.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null", null, new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Hallo, Jonas", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" })).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // Offline lädt die Startseite keine Freizeiten (keine API-Antworten im Speicher, AD-18) und bleibt beim Hinweis.
        await kontext.SetOfflineAsync(true);
        await seite.ReloadAsync();

        await Assertions.Expect(seite.Locator(".zl-offline-leiste")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".zl-notfall-box a[href='tel:112']")).ToHaveTextAsync("Notruf 112");
        await Assertions.Expect(seite.GetByText("Offline nicht verfügbar.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await BildAsync(seite, "app-offline", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmelden(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await seite.GotoAsync(new Uri(host.Adresse, "/app/anmelden").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmelden", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Login-Link schicken" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await BildAsync(seite, "anmelden", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmelden_Code(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await seite.GotoAsync(new Uri(host.Adresse, "/app/anmelden").ToString());
        await seite.GetByLabel("Mail-Adresse").FillAsync("katrin.muster@example.org");
        await seite.GetByRole(AriaRole.Button, new() { Name = "Login-Link schicken" }).ClickAsync();
        await Assertions.Expect(seite.GetByText("Login-Link ist verschickt. Bitte im Postfach nachsehen.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Login-Code")).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await BildAsync(seite, "anmelden-code", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Konto(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.Locator("header a.zl-konto-link").ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Konto", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Vorname")).ToHaveValueAsync("Jonas", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Nachname")).ToHaveValueAsync("Hüttner");
        // Story 2-1: die Herkunft erscheint auf den Freizeitseiten.
        await Assertions.Expect(seite.GetByLabel("Herkunft (Wohnort)")).ToHaveValueAsync("Musterstadt");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await BildAsync(seite, "konto", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Oeffentliche_Startseite(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await seite.GotoAsync(new Uri(host.Adresse, "/").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlotse", new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await BildAsync(seite, "oeffentliche-startseite", geraet);
    }

    /// <summary>Global Admin der Demo-Daten (Story 1-2).</summary>
    private const string BetreiberMail = "gerd.betreiber@example.org";

    /// <summary>Org-Admin der Demo-Organisation „Jugendwerk Musterstadt“ (Story 1-2).</summary>
    private const string OrgAdminMail = "andrea.beispiel@example.org";

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Organisationen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, BetreiberMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisationen verwalten" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Organisationen", new() { Timeout = 60_000 });
        var zeile = seite.Locator("tbody tr").Filter(new() { HasText = "Jugendwerk Musterstadt" });
        await Assertions.Expect(zeile).ToContainTextAsync("Eingerichtet", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Anlegen und einladen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "organisationen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Organisation(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Rolle: Org-Admin")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "Andrea Beispiel" })).ToContainTextAsync("Org-Admin", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "vorstand@example.org" })).ToContainTextAsync("gültig bis");
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Einladung schicken" })).ToBeVisibleAsync();
        // Story 1-3: Card „Freizeiten“ mit der Demo-Freizeit und das Formular „Freizeit anlegen“.
        var freizeit = seite.Locator("tbody tr").Filter(new() { HasText = "Zeltlager Sommer 2027" });
        await Assertions.Expect(freizeit).ToContainTextAsync("02.08.2027 – 13.08.2027");
        await Assertions.Expect(freizeit).ToContainTextAsync("in Vorbereitung");
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Freizeit anlegen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        // Story 2-8: Der Notfall-Test desselben Laufs legt in der Demo-Organisation eine eigene, laufende Freizeit an; das Bild zeigt nur
        // die Demo-Freizeit. Geprüft ist die Seite vorher vollständig.
        await seite.EvaluateAsync(
            """
            () => document.querySelectorAll('table').forEach(t => {
                if (t.caption && t.caption.textContent.trim() === 'Freizeiten')
                    t.querySelectorAll('tbody tr').forEach(z => { if (!z.textContent.includes('Zeltlager Sommer 2027')) z.remove(); });
            })
            """);

        await BildAsync(seite, "organisation", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Einladung(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Die Seite vor dem Klick: Der Aufruf nimmt nichts an, das Token im Link ist darum beliebig.
        await seite.GotoAsync(new Uri(host.Adresse, "/app/einladung/" + new string('x', 43)).ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Einladung annehmen", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Einladung annehmen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "einladung", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task App_Start_Org_Admin(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Hallo, Andrea");
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Rolle: Org-Admin")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "app-start-org-admin", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeit_Verwalten(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await seite.GetByRole(AriaRole.Link, new() { Name = "Zeltlager Sommer 2027" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Zeitraum: 02.08.2027 – 13.08.2027")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "Jonas Hüttner" })).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "Miriam Zeltner" })).ToHaveCountAsync(1);
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Einladung schicken" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Zur Organisation" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeit-verwalten", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Leitung_Einladung(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Die Seite vor dem Klick: Der Aufruf nimmt nichts an, das Token im Link ist darum beliebig.
        await seite.GotoAsync(new Uri(host.Adresse, "/app/leitung/" + new string('x', 43)).ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Leitung übernehmen", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Einladung annehmen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "leitung-einladung", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeit_Start(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Rolle: Leitung")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Zeitraum: 02.08.2027 – 13.08.2027")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Leitung: Jonas Hüttner, Miriam Zeltner")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Einsicht in Freizeitpässe bis 12.09.2027")).ToBeVisibleAsync();
        // Story 2-5: Leitung und Team sehen den Knopf „Freizeitpässe“.
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Freizeitpässe", Exact = true })).ToBeVisibleAsync();
        // Story 2-6: das Leitungs-Cockpit. Card „Anmeldungen“ mit Balken und Zahlen je Art.
        var anmeldungen = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Anmeldungen", Exact = true }) });
        await Assertions.Expect(anmeldungen.Locator(".zl-balken")).ToContainTextAsync("Plätze", new() { Timeout = 60_000 });
        await Assertions.Expect(anmeldungen.Locator(".zl-zahl")).ToHaveTextAsync("1 von 40");
        // Gast-Anmeldungen anderer Tests desselben Laufs (Familie „Beispiel“) zählen als eingegangene Teilnehmende und unvollständige
        // Freizeitpässe mit; ihre Anzahl hängt von der Reihenfolge der Tests ab.
        var tabelle = seite.Locator("#freizeitpaesse-offen tbody tr");
        await Assertions.Expect(tabelle.Filter(new() { HasText = "Tobias Wanderer" })).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        var gaeste = await tabelle.Filter(new() { HasText = "Beispiel" }).CountAsync();
        await Assertions.Expect(anmeldungen).ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"Teilnehmende: \d+ eingegangen, 1 bestätigt"));
        await Assertions.Expect(anmeldungen).ToContainTextAsync("Mitarbeitende: 1 eingegangen, 1 bestätigt");
        // Warnhinweise: vier unvollständige Freizeitpässe und, aus Story 1-6, Miriam und Lea (Team) fehlen Pflicht-Nachweise.
        var warnung = seite.Locator(".elkw-alert--warnung");
        await Assertions.Expect(warnung).ToHaveCountAsync(1);
        await Assertions.Expect(warnung).ToContainTextAsync("Achtung:");
        await Assertions.Expect(warnung).ToContainTextAsync("2 Punkte sind offen.");
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = $"{4 + gaeste} Freizeitpässe unvollständig" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Bei 2 Personen im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab.")).ToBeVisibleAsync();
        await Assertions.Expect(tabelle).ToHaveCountAsync(4 + gaeste);
        foreach (var (name, stand) in new[]
                 {
                     ("Lea Feuerstein", "Freizeitpass fehlt noch"),
                     ("Emma Muster", "5 von 8 Angaben offen"),
                     // Story 2-11: Mia hat zusätzlich Medikamente angegeben; Story 4-4: dazu „vegetarisch“.
                     ("Mia Probst", "3 von 8 Angaben offen"),
                     ("Tobias Wanderer", "Freizeitpass fehlt noch"),
                 })
        {
            await Assertions.Expect(tabelle.Filter(new() { HasText = name })).ToContainTextAsync(stand);
        }

        var demo = tabelle.Filter(new() { HasNotText = "Beispiel" });
        await Assertions.Expect(demo.Nth(0)).ToContainTextAsync("Lea Feuerstein");
        await Assertions.Expect(demo.Nth(3)).ToContainTextAsync("Tobias Wanderer");
        await Assertions.Expect(seite.Locator("#vorfall-notizen div[id^=vorfall-]")).ToHaveCountAsync(3, new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Das Bild zeigt nur die Demo-Daten: Gast-Zeilen weg, die Zahlen wie ohne die Gast-Anmeldungen anderer Tests.
        await GastZeilenAusblendenAsync(seite);
        await seite.EvaluateAsync(
            """
            () => {
                for (const p of document.querySelectorAll('.elkw-card p')) {
                    if (p.textContent.trim().startsWith('Teilnehmende:')) p.textContent = 'Teilnehmende: 1 eingegangen, 1 bestätigt';
                }
                const link = document.querySelector('.elkw-card li a');
                if (link) link.textContent = '4 Freizeitpässe unvollständig';
            }
            """);

        // Story 2-10: die Card „Vorfall-Notizen“ mit den drei Demo-Einträgen; ihre Zeitpunkte hängen vom Einspielen ab, im Bild stehen feste.
        await VorfallZeitenFestAsync(seite.Locator("#vorfall-notizen div[id^=vorfall-] a"));

        // Das Bild zeigt das Cockpit: am Laptop ab „Anmeldungen“ (mit den Warnhinweisen), am Handy ab „Warnhinweise“.
        var ziel = seite.Locator(".elkw-card").Filter(new()
        {
            Has = seite.GetByRole(AriaRole.Heading, new() { Name = geraet == "handy" ? "Warnhinweise" : "Anmeldungen", Exact = true }),
        });
        await ziel.EvaluateAsync("karte => karte.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "freizeit-start", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeitpass_Erinnern(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Story 2-6: Jonas klappt im Leitungs-Cockpit „Erinnern“ bei Emma auf; gesendet wird nichts.
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        var zeile = seite.Locator("#freizeitpaesse-offen tbody tr").Filter(new() { HasText = "Emma Muster" });
        await Assertions.Expect(zeile).ToContainTextAsync("5 von 8 Angaben offen", new() { Timeout = 60_000 });
        await zeile.GetByRole(AriaRole.Button, new() { Name = "Erinnern" }).ClickAsync();
        await Assertions.Expect(zeile).ToContainTextAsync("Erinnerung zum Freizeitpass von Emma Muster senden?");
        await Assertions.Expect(zeile.GetByRole(AriaRole.Button, new() { Name = "Ja, senden" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await GastZeilenAusblendenAsync(seite);
        await zeile.GetByRole(AriaRole.Button, new() { Name = "Ja, senden" }).ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "freizeitpass-erinnern", geraet);
    }

    /// <summary>Elternteil der Demo-Familie „Muster“ mit Paul (volljährig) und Emma (Story 1-4).</summary>
    private const string ElternMail = "katrin.muster@example.org";

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Familie(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, ElternMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Familie öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Familie", new() { Timeout = 60_000 });
        var paul = seite.Locator(".elkw-card").Filter(new() { HasText = "Paul Muster" });
        var emma = seite.Locator(".elkw-card").Filter(new() { HasText = "Emma Muster" });
        await Assertions.Expect(paul).ToContainTextAsync("Geboren am 14.03.2008", new() { Timeout = 60_000 });
        await Assertions.Expect(emma).ToContainTextAsync("Geboren am 02.09.2017");
        // Story 2-2: Katrin hat Emma mit Konto angemeldet; die Card zeigt die Freizeit mit dem Status.
        await Assertions.Expect(emma.Locator(".zl-statusliste li")).ToContainTextAsync("Zeltlager Sommer 2027, 02.08.2027 – 13.08.2027");
        await Assertions.Expect(emma.Locator(".zl-statusliste li > .elkw-status")).ToHaveTextAsync("Eingegangen");
        // Story 2-3: darunter der Kontakt der Leitung für Absagen.
        await Assertions.Expect(emma.Locator(".zl-absage-kontakt")).ToHaveTextAsync("Absage nur über die Leitung: zeltlager@example.org · 0123 456789");
        // Story 2-4: der Weg zum Freizeitpass; die Demo-Freizeit fragt mehr ab, als Emmas Pass beantwortet.
        await Assertions.Expect(emma.GetByRole(AriaRole.Link, new() { Name = "Freizeitpass", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(emma.Locator(".zl-statusliste__darunter .elkw-status--warnung")).ToHaveTextAsync("Freizeitpass prüfen");
        // Story 2-5: bis wann die Freizeit Einsicht hat.
        await Assertions.Expect(emma.Locator(".zl-statusliste__darunter")).ToContainTextAsync("Einsicht bis 12.09.2027");
        // Nur bei Paul (volljährig) steht die Übergabe.
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Profil an Paul übergeben" })).ToHaveCountAsync(1);
        await Assertions.Expect(emma.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "Katrin Muster" })).ToContainTextAsync("Elternteil");
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Kind hinzufügen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Einladung schicken" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "familie", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Kind_Angaben(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, ElternMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Familie öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await seite.Locator(".elkw-card").Filter(new() { HasText = "Emma Muster" })
            .GetByRole(AriaRole.Link, new() { Name = "Angaben ändern" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Emma Muster", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Vorname")).ToHaveValueAsync("Emma", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Nachname")).ToHaveValueAsync("Muster");
        await Assertions.Expect(seite.GetByLabel("Geburtsdatum")).ToHaveValueAsync("2017-09-02");
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Zurück zur Familie" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "kind-angaben", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Familie_Einladung(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Die Seite vor dem Klick: Der Aufruf nimmt nichts an, das Token im Link ist darum beliebig.
        await seite.GotoAsync(new Uri(host.Adresse, "/app/familie/einladung/" + new string('x', 43)).ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Familienkonto beitreten", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Einladung annehmen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "familie-einladung", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Profil_Uebernehmen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Die Seite vor dem Klick: Der Aufruf übernimmt nichts, das Token im Link ist darum beliebig.
        await seite.GotoAsync(new Uri(host.Adresse, "/app/uebergabe/" + new string('x', 43)).ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Profil übernehmen", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Profil übernehmen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "profil-uebernehmen", geraet);
    }

    /// <summary>Leitung der Demo-Freizeit, prüft Jonas' Nachweise (Story 1-5).</summary>
    private const string PruefMail = "miriam.zeltner@example.org";

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Nachweise(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.Locator("header a.zl-konto-link").ClickAsync();
        await seite.GetByRole(AriaRole.Link, new() { Name = "Nachweise öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Nachweise", new() { Timeout = 60_000 });
        var fuehrungszeugnis = seite.Locator(".elkw-card").Filter(new() { HasText = "Führungszeugnis" });
        var ersteHilfe = seite.Locator(".elkw-card").Filter(new() { HasText = "Erste Hilfe" });
        await Assertions.Expect(fuehrungszeugnis.GetByLabel("Ausgestellt am")).ToHaveValueAsync("2025-11-03", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".elkw-card").Filter(new() { HasText = "JuLeiCa" })).ToContainTextAsync("Hochgeladen am");
        // Die Bestätigung kommt über die Outbox (Zustelldienst) zu Organisationen; neu laden, bis sie da ist.
        await Assertions.Expect(seite.Locator(".elkw-alert--hinweis")).ToContainTextAsync("Freigegeben bis", new() { Timeout = 60_000 });
        for (var versuch = 0; versuch < 30 && !(await ersteHilfe.TextContentAsync() ?? "").Contains("Bestätigt", StringComparison.Ordinal); versuch++)
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            await seite.ReloadAsync();
            await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Nachweise", new() { Timeout = 60_000 });
            await Assertions.Expect(ersteHilfe).ToBeVisibleAsync(new() { Timeout = 60_000 });
        }

        await Assertions.Expect(ersteHilfe).ToContainTextAsync("Bestätigt · gültig bis 15.04.2027 (Jugendwerk Musterstadt)");
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Nachweis hinzufügen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        if (geraet == "laptop")
        {
            // Am Laptop die Card mit der Bestätigung zeigen, am Handy den Anfang der Seite mit dem Hinweis.
            await ersteHilfe.ScrollIntoViewIfNeededAsync();
        }

        await BildAsync(seite, "nachweise", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Nachweise_Pruefen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await PruefListeAsync(seite);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "nachweise-pruefen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Nachweis_Einsicht(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // „Prüfen“ übernimmt die Prüfung (Miriam ist schon gebunden, nichts ändert sich) und öffnet die Einsicht; entschieden wird nichts.
        var zeile = await PruefListeAsync(seite);
        await zeile.GetByRole(AriaRole.Button, new() { Name = "Prüfen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Nachweise von Jonas Hüttner", new() { Timeout = 60_000 });
        var fuehrungszeugnis = seite.Locator(".elkw-card").Filter(new() { HasText = "Führungszeugnis" });
        await Assertions.Expect(fuehrungszeugnis).ToContainTextAsync("Ausgestellt am 03.11.2025", new() { Timeout = 60_000 });
        await Assertions.Expect(fuehrungszeugnis.GetByRole(AriaRole.Link, new() { Name = "Datei öffnen" })).ToBeVisibleAsync();
        await Assertions.Expect(fuehrungszeugnis.GetByRole(AriaRole.Button, new() { Name = "Bestätigen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator(".elkw-card").Filter(new() { HasText = "Erste Hilfe" }).Locator(".elkw-status")).ToHaveTextAsync("Bestätigt");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "nachweis-einsicht", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Nachweis_Richtlinie(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        var card = seite.Locator(".elkw-card").Filter(new() { HasText = "Richtlinie für Nachweise" });
        var fuehrungszeugnis = card.Locator("tbody tr").Filter(new() { HasText = "Führungszeugnis" });
        await Assertions.Expect(fuehrungszeugnis.GetByLabel("Pflicht")).ToBeCheckedAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(fuehrungszeugnis.GetByLabel("Gültig (Monate)")).ToHaveValueAsync("60");
        await Assertions.Expect(card.Locator("tbody tr").Filter(new() { HasText = "Erste Hilfe" }).GetByLabel("Gültig (Monate)")).ToHaveValueAsync("24");
        // Story 1-6: In der Demo ist auch Erste Hilfe Pflicht.
        await Assertions.Expect(card.Locator("tbody tr").Filter(new() { HasText = "Erste Hilfe" }).GetByLabel("Pflicht")).ToBeCheckedAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Nachweise prüfen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "nachweis-richtlinie", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Nachweis_Erinnern(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Story 1-6: Warnhinweise bei Jonas, „Erinnern“ aufgeklappt; gesendet wird nichts.
        var zeile = await PruefListeAsync(seite);
        await Assertions.Expect(zeile).ToContainTextAsync("Führungszeugnis nicht bestätigt", new() { Timeout = 60_000 });
        await Assertions.Expect(zeile).ToContainTextAsync("Erste Hilfe läuft am 15.04.2027 ab");
        await zeile.GetByRole(AriaRole.Button, new() { Name = "Erinnern" }).ClickAsync();
        await Assertions.Expect(zeile).ToContainTextAsync("Erinnerung an Jonas Hüttner senden? Dabei wird die Einsicht in die Nachweise angefragt.");
        await Assertions.Expect(zeile.GetByRole(AriaRole.Button, new() { Name = "Ja, senden" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await zeile.GetByRole(AriaRole.Button, new() { Name = "Ja, senden" }).ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "nachweis-erinnern", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Meine_Daten(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Story 1-7: Jonas sieht Miriams Einsicht (Demo-Daten) und lädt mit frischer Anmeldung die Auskunft.
        await MeineDatenAsync(seite);
        var zeile = seite.Locator("tbody tr").Filter(new() { HasText = "Miriam Zeltner" });
        await Assertions.Expect(zeile).ToContainTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        await Assertions.Expect(zeile).ToContainTextAsync("Nachweise eingesehen");
        await Assertions.Expect(zeile).ToContainTextAsync("Jonas Hüttner");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "meine-daten-zugriffsprotokoll", geraet);

        await seite.GetByRole(AriaRole.Button, new() { Name = "Auskunft anzeigen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h3").First).ToHaveTextAsync("Konto", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("td").Filter(new() { HasText = DemoMail }).First).ToBeVisibleAsync();
        var pdf = seite.GetByRole(AriaRole.Button, new() { Name = "Als PDF speichern" });
        await Assertions.Expect(pdf).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Als CSV herunterladen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        // Den Abschnitt „Auskunft und Export“ oben zeigen (der Arbeitsbereich scrollt, nicht die Seite).
        await seite.Locator("h2").Filter(new() { HasText = "Auskunft und Export" }).EvaluateAsync("e => e.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "meine-daten-auskunft", geraet);

        // Der echte Download-Weg über zeltlotse.js (Blob, Objekt-URL, Link-Klick), nach dem Bild.
        var download = await seite.RunAndWaitForDownloadAsync(
            () => seite.GetByRole(AriaRole.Button, new() { Name = "Als CSV herunterladen" }).ClickAsync());
        Assert.StartsWith("zeltlotse-meine-daten-", download.SuggestedFilename, StringComparison.Ordinal);
        Assert.EndsWith(".csv", download.SuggestedFilename, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Konto_Loeschen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Story 1-7: Die Inline-Bestätigung ist offen; bestätigt wird nicht (die Demo-Daten bleiben).
        await MeineDatenAsync(seite);
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "Miriam Zeltner" })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Button, new() { Name = "Konto löschen" }).ClickAsync();
        var bestaetigung = seite.Locator(".zl-bestaetigung");
        await Assertions.Expect(bestaetigung).ToContainTextAsync("Konto und alle eigenen Daten endgültig löschen?");
        await Assertions.Expect(bestaetigung.GetByRole(AriaRole.Button, new() { Name = "Ja, endgültig löschen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await bestaetigung.ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "konto-loeschen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Loeschfristen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        var card = seite.Locator(".elkw-card").Filter(new() { HasText = "Löschfristen" });
        await Assertions.Expect(card.GetByLabel("Daten einer Freizeit (Monate nach Freizeitende)")).ToHaveValueAsync("12", new() { Timeout = 60_000 });
        await Assertions.Expect(card.GetByLabel("Abgelaufene Nachweis-Bestätigungen (Monate nach Ablauf)")).ToHaveValueAsync("12");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "loeschfristen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeit_Einstellungen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await EinstellungenAsync(seite);
        await Assertions.Expect(seite.GetByLabel("Ort", new() { Exact = true })).ToHaveValueAsync("Zeltplatz am Mühlbach, Musterhausen", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Höchstzahl Teilnehmende")).ToHaveValueAsync("40");
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Für Teilnehmende" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Freizeitseite öffnen" })).ToHaveCountAsync(2);
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Neuen Link erzeugen" })).ToHaveCountAsync(2);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeit-einstellungen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeitseite(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await FreizeitseiteAsync(seite);
        await Assertions.Expect(seite.Locator("header .zl-wortmarke")).ToHaveTextAsync("Jugendwerk Musterstadt");
        await Assertions.Expect(seite.GetByText("Freizeit · 2. bis 13. August 2027")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Ort: Zeltplatz am Mühlbach, Musterhausen")).ToBeVisibleAsync();
        var team = seite.Locator(".elkw-card").Filter(new() { HasText = "Das Team" });
        // Story 2-3: Lea ist als bestätigte Mitarbeitende im Team.
        await Assertions.Expect(team.Locator("li")).ToHaveCountAsync(3);
        await Assertions.Expect(team.Locator("li").Filter(new() { HasText = "Lea" })).ToContainTextAsync("Team");
        await Assertions.Expect(team.Locator("li").Filter(new() { HasText = "Jonas" })).ToContainTextAsync("Leitung · aus Musterstadt");
        await Assertions.Expect(team.Locator("li").Filter(new() { HasText = "Miriam" })).ToContainTextAsync("Leitung · aus Musterstadt");
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Vertrauensseite" })).ToBeVisibleAsync();
        // Story 2-7: nur das öffentliche Dokument der Demo-Freizeit.
        var dokumente = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Dokumente", Exact = true }) });
        await Assertions.Expect(dokumente.Locator("li a")).ToHaveTextAsync(["Anfahrt zum Zeltplatz (PDF)"]);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeitseite", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Vertrauensseite(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await FreizeitseiteAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Vertrauensseite" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Vertrauensseite", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Welche Daten erhoben werden" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Wer die Daten sieht" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Wann die Daten verschwinden" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("Führungszeugnis und Erste Hilfe");
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("12.09.2027");
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("12 Monate");
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "vertrauensseite", geraet);
    }

    // ---------- Story 2-2: Anmeldung ----------

    /// <summary>Fester Gast der Doku-Bilder: Elternteil ohne Konto mit Kind „Lina Beispiel“.</summary>
    private const string GastMail = "lina.eltern@example.org";

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmeldung_Formular(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await GastFormularAsync(seite);
        await Assertions.Expect(seite.GetByText("Ohne Konto möglich. Gefragt wird nur das Nötigste.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Kind" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Elternteil" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Dorthin gehen Bestätigung oder Absage.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Die Lagerregeln und die Anweisungen der Mitarbeitenden gelten für alle Teilnehmenden.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Anmeldung absenden" })).ToBeVisibleAsync();
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "anmeldung-formular", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmeldung_Eingegangen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await GastFormularAsync(seite);
        await E2EHilfe.GastFormularAbsendenAsync(seite, "Lina", "Sabine", GastMail);
        await Assertions.Expect(seite.GetByText("Anmeldung eingegangen:")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText($"Eine Mail mit dem Gast-Link geht an {GastMail}.", new() { Exact = false })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Konto aus diesen Daten erstellen?" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Weiteres Kind anmelden" })).ToBeVisibleAsync();
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "anmeldung-eingegangen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Gast_Link(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();
        var vorher = host.Mails.An(GastMail).Count;
        await GastFormularAsync(seite);
        await E2EHilfe.GastFormularAbsendenAsync(seite, "Lina", "Sabine", GastMail);
        var link = (await host.Mails.WartenAufAsync(GastMail, vorher + 1)).Link;

        // Der Zustelldienst legt die Freigabe nach wenigen Sekunden an; ab dann gilt der Gast-Link bis zum Ende des Freigabefensters.
        await seite.GotoAsync(link);
        var bis = DateTime.UtcNow.AddSeconds(60);
        while (!(await seite.Locator("main").InnerTextAsync()).Contains("Gast-Link gilt bis 12.09.2027", StringComparison.Ordinal) && DateTime.UtcNow < bis)
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            await seite.ReloadAsync();
        }

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldungen");
        var karte = seite.Locator(".elkw-card").Filter(new() { HasText = "Lina" });
        await Assertions.Expect(karte.Locator(".elkw-status")).ToHaveTextAsync("Eingegangen");
        // Story 2-3: Absagen nur über die Leitung, mit Mail- und Telefon-Link.
        await Assertions.Expect(karte.Locator(".zl-absage-kontakt")).ToHaveTextAsync("Absage nur über die Leitung: zeltlager@example.org · 0123 456789");
        // Story 2-5: bis wann die Freizeit Einsicht in den Freizeitpass hat.
        await Assertions.Expect(karte).ToContainTextAsync("Einsicht bis 12.09.2027");
        await Assertions.Expect(seite.GetByText("Gast-Link gilt bis 12.09.2027.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Konto erstellen" })).ToBeVisibleAsync();
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "gast-link", geraet);

        // Story 2-7: Über den Gast-Link stehen dieselben Dokumente für Eltern bereit wie im Konto.
        await karte.GetByRole(AriaRole.Link, new() { Name = "Elterninfo" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Elterninfo", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".elkw-kicker")).ToHaveTextAsync("Zeltlager Sommer 2027");
        await Assertions.Expect(seite.Locator("main li a")).ToHaveTextAsync(DemoElterninfo);
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Zurück zu den Anmeldungen" })).ToBeVisibleAsync();
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "gast-elterninfo", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmeldung_Mit_Konto(string geraet)
    {
        var link = await E2EHilfe.AnmeldelinkAsync(host);
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, ElternMail);
        await seite.GotoAsync(new Uri(host.Adresse, $"/app/anmeldung/{E2EHilfe.Token(link)}").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldung", new() { Timeout = 60_000 });
        var auswahl = seite.Locator("fieldset");
        await Assertions.Expect(auswahl.GetByLabel("Emma Muster")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(auswahl.GetByLabel("Selbst")).ToBeVisibleAsync();
        await Assertions.Expect(auswahl.GetByText("Paul")).ToHaveCountAsync(0);
        await Assertions.Expect(seite.GetByLabel("Teilnahmebedingungen gelesen und bestätigt *")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Anmeldung absenden" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "anmeldung-mit-konto", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmeldung_Fuer_Das_Team(string geraet)
    {
        var link = await E2EHilfe.AnmeldelinkAsync(host, teilnehmende: false);
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, PruefMail);
        await seite.GotoAsync(new Uri(host.Adresse, $"/app/anmeldung/{E2EHilfe.Token(link)}").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldung", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Angemeldet wird die eigene Person für das Team der Freizeit.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("fieldset")).ToHaveCountAsync(0);
        await Assertions.Expect(seite.GetByLabel("Teilnahmebedingungen gelesen und bestätigt *")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Anmeldung absenden" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "anmeldung-team", geraet);
    }

    // ---------- Story 2-4: Freizeitpass ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeitpass_Fragen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, PruefMail);
        await FreizeitOeffnenAsync(seite, DemoFreizeitName);
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Einstellungen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Einstellungen", new() { Timeout = 60_000 });
        var card = seite.Locator(".elkw-card").Filter(new() { HasText = "Ohne Auswahl fragt der Freizeitpass nichts ab." });
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 60_000 });
        var teilnehmende = card.Locator("fieldset").Filter(new() { HasText = "Fragen für Teilnehmende" });
        var mitarbeitende = card.Locator("fieldset").Filter(new() { HasText = "Fragen für Mitarbeitende" });
        await Assertions.Expect(teilnehmende.GetByLabel("Allergien und Unverträglichkeiten")).ToBeCheckedAsync();
        await Assertions.Expect(teilnehmende.GetByLabel("Kann schwimmen")).ToBeCheckedAsync();
        await Assertions.Expect(teilnehmende.GetByLabel("Krankenversicherung")).Not.ToBeCheckedAsync();
        await Assertions.Expect(teilnehmende.Locator("input:checked")).ToHaveCountAsync(8);
        await Assertions.Expect(mitarbeitende.Locator("input:checked")).ToHaveCountAsync(4);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.ScrollIntoViewIfNeededAsync();
        await card.EvaluateAsync("k => k.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "freizeitpass-fragen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeitpass_Ausfuellen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, ElternMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Familie öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        var emma = seite.Locator(".elkw-card").Filter(new() { HasText = "Emma Muster" });
        await emma.GetByRole(AriaRole.Link, new() { Name = "Freizeitpass", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Freizeitpass für Emma", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Zeltlager Sommer 2027 · 02.08.2027 – 13.08.2027")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByText("Einsicht bis 12.09.2027")).ToBeVisibleAsync();
        // Allergien, Medikamente und Notfallkontakte zuerst, nie zugeklappt; nur die Fragen der Freizeit.
        await Assertions.Expect(seite.Locator("legend, label:not(.elkw-choice)").First).ToHaveTextAsync("Allergien und Unverträglichkeiten");
        await Assertions.Expect(seite.GetByLabel("Milch (inkl. Laktose)")).ToBeCheckedAsync();
        await Assertions.Expect(seite.GetByLabel("Medikamente (Name, Dosierung, wann)")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByLabel("Notfallkontakte (Name, Telefon)")).ToHaveValueAsync("Katrin Muster (Mutter), 0123 456780");
        await Assertions.Expect(seite.Locator("fieldset").Filter(new() { HasText = "Kann schwimmen" }).GetByLabel("Ja")).ToBeCheckedAsync();
        await Assertions.Expect(seite.GetByLabel("Krankenversicherung")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeitpass", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Gast_Freizeitpass(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();
        var vorher = host.Mails.An(GastMail).Count;
        await GastFormularAsync(seite);
        await E2EHilfe.GastFormularAbsendenAsync(seite, "Lina", "Sabine", GastMail);
        var link = (await host.Mails.WartenAufAsync(GastMail, vorher + 1)).Link;
        await seite.GotoAsync(link);
        var bis = DateTime.UtcNow.AddSeconds(60);
        while (!(await seite.Locator("main").InnerTextAsync()).Contains("Gast-Link gilt bis 12.09.2027", StringComparison.Ordinal) && DateTime.UtcNow < bis)
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            await seite.ReloadAsync();
        }

        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeitpass für Lina" }).Last.ClickAsync();

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Freizeitpass für Lina", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("legend").First).ToHaveTextAsync("Allergien und Unverträglichkeiten");
        await Assertions.Expect(seite.GetByLabel("Notfallkontakte (Name, Telefon)")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByLabel("Letzte Tetanus-Impfung")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Speichern" })).ToBeVisibleAsync();
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "gast-freizeitpass", geraet);
    }

    // ---------- Story 2-3: Anmeldungen bestätigen und absagen ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmeldungen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await AnmeldungenAsync(seite);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await GastZeilenAusblendenAsync(seite);

        await BildAsync(seite, "anmeldungen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Anmeldung_Absage(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Die Inline-Bestätigung bei Tobias ist aufgeklappt; abgesagt wird nichts, die Demo-Daten bleiben.
        await AnmeldungenAsync(seite);
        var tobias = seite.Locator("tbody tr").Filter(new() { HasText = "Tobias Wanderer" });
        await tobias.GetByRole(AriaRole.Button, new() { Name = "Absage eintragen" }).ClickAsync();
        await Assertions.Expect(tobias).ToContainTextAsync("Absage für Tobias eintragen? Eine Absage-Mail geht raus.");
        await Assertions.Expect(tobias.GetByRole(AriaRole.Button, new() { Name = "Ja, Absage eintragen" })).ToBeVisibleAsync();
        await Assertions.Expect(tobias.GetByRole(AriaRole.Button, new() { Name = "Nein" })).ToBeFocusedAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await GastZeilenAusblendenAsync(seite);
        await tobias.ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "anmeldung-absage", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Kontakt_Fuer_Absagen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await EinstellungenAsync(seite);
        var abschnitt = seite.Locator(".elkw-card").Filter(new() { HasText = "Kontakt für Absagen" });
        await Assertions.Expect(abschnitt.GetByLabel("Mail-Adresse")).ToHaveValueAsync("zeltlager@example.org", new() { Timeout = 60_000 });
        await Assertions.Expect(abschnitt.GetByLabel("Telefon")).ToHaveValueAsync("0123 456789");
        await Assertions.Expect(abschnitt).ToContainTextAsync("Erscheint bei Eltern und Mitarbeitenden neben dem Anmeldestatus.");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await abschnitt.ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "kontakt-fuer-absagen", geraet);
    }

    // ---------- Story 2-5: Freizeitpässe, Freigabe und Team-Notizen ----------

    /// <summary>Demo-Person aus dem Katalog, die als Mitarbeitende bestätigt ist (Team, Story 2-3).</summary>
    private const string TeamMail = "lea.feuerstein@example.org";

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeitpaesse(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await FreizeitpaesseAsync(seite);
        foreach (var (name, status) in new[] { ("Emma Muster", "Eingegangen"), ("Mia Probst", "Bestätigt"), ("Lea Feuerstein", "Bestätigt"), ("Tobias Wanderer", "Eingegangen") })
        {
            var zeile = seite.Locator("tbody tr").Filter(new() { HasText = name });
            await Assertions.Expect(zeile.Locator(".elkw-status")).ToHaveTextAsync(status);
            await Assertions.Expect(zeile.GetByRole(AriaRole.Link, new() { Name = name })).ToBeVisibleAsync();
        }

        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await GastZeilenAusblendenAsync(seite);

        await BildAsync(seite, "freizeitpaesse", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeitpass_Einsicht(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Mia ist bestätigt: Lea (Team) sieht die abgefragten Angaben und ihre eigene Team-Notiz.
        await FreizeitpaesseAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Mia Probst" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Mia Probst", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Einsicht bis 12.09.2027")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        var angaben = seite.Locator(".elkw-card").Filter(new() { HasText = "Angaben" });
        await Assertions.Expect(angaben.Locator("dt").First).ToHaveTextAsync("Allergien und Unverträglichkeiten");
        await Assertions.Expect(angaben.Locator("dd").First).ToHaveTextAsync("Glutenhaltiges Getreide");
        await Assertions.Expect(angaben).ToContainTextAsync("Daniel Probst (Vater), 0123 456781");
        await Assertions.Expect(angaben.Locator("input, textarea")).ToHaveCountAsync(0);
        var notizen = seite.Locator(".elkw-card").Filter(new() { HasText = "Team-Notizen" });
        await Assertions.Expect(notizen).ToContainTextAsync("Bleiben in der Freizeit und erscheinen nicht im Freizeitpass.");
        await Assertions.Expect(notizen).ToContainTextAsync("Hatte letztes Jahr am ersten Abend Heimweh; ein Gespräch am Lagerfeuer hat geholfen.");
        await Assertions.Expect(notizen).ToContainTextAsync("Lea Feuerstein ·");
        await Assertions.Expect(notizen.GetByLabel("Neue Notiz")).ToBeVisibleAsync();
        await Assertions.Expect(notizen.GetByRole(AriaRole.Button, new() { Name = "Notiz speichern" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeitpass-einsicht", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeitpass_Vor_Bestaetigung(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Emma ist nur eingegangen: Das Team sieht statt der Angaben den Hinweis.
        await FreizeitpaesseAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Emma Muster" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Emma Muster", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Freizeitpass sichtbar ab Bestätigung der Anmeldung.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Einsicht bis 12.09.2027")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator(".elkw-card").Filter(new() { HasText = "Angaben" })).ToHaveCountAsync(0);
        await Assertions.Expect(seite.Locator(".elkw-card").Filter(new() { HasText = "Team-Notizen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeitpass-vor-bestaetigung", geraet);
    }

    // ---------- Story 2-7: Dokumente und Elterninfo ----------

    /// <summary>Die Dokumente der Demo-Freizeit nach Titel, mit Sichtbarkeit und Herkunft.</summary>
    private static readonly (string Titel, string Sichtbarkeit, string Herkunft)[] DemoDokumente =
    [
        ("Anfahrt zum Zeltplatz", "Öffentlich", "Freizeit"),
        ("Hausordnung Zeltplatz", "Eltern und Teilnehmende", "Organisation"),
        ("Infobrief Zeltlager Sommer 2027", "Eltern und Teilnehmende", "Freizeit"),
        ("Notfallnummern", "Eltern und Teilnehmende", "Freizeit"),
        ("Packliste", "Eltern und Teilnehmende", "Freizeit"),
        ("Programmideen", "Team", "Freizeit"),
        // Story 2-9: das eingebundene Schutzkonzept.
        ("Schutzkonzept Jugendwerk Musterstadt", "Team", "Organisation"),
    ];

    /// <summary>Die Elterninfo der Demo-Freizeit: die vier Dokumente für Eltern und Teilnehmende.</summary>
    private static readonly string[] DemoElterninfo =
        ["Hausordnung Zeltplatz (PDF)", "Infobrief Zeltlager Sommer 2027 (PDF)", "Notfallnummern (PDF)", "Packliste (PDF)"];

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Dokumente(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await DokumenteAsync(seite);
        var zeilen = seite.Locator(".elkw-card").Filter(new() { HasText = "Dokumente der Freizeit" }).Locator("tbody tr");
        await Assertions.Expect(zeilen).ToHaveCountAsync(DemoDokumente.Length);
        for (var i = 0; i < DemoDokumente.Length; i++)
        {
            var (titel, sichtbarkeit, herkunft) = DemoDokumente[i];
            await Assertions.Expect(zeilen.Nth(i).GetByRole(AriaRole.Link, new() { Name = titel, Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(zeilen.Nth(i)).ToContainTextAsync(sichtbarkeit);
            await Assertions.Expect(zeilen.Nth(i)).ToContainTextAsync(herkunft);
        }

        var einbinden = seite.Locator(".elkw-card").Filter(new() { HasText = "Aus der Organisation einbinden" });
        await Assertions.Expect(einbinden.Locator("tbody tr")).ToHaveCountAsync(1);
        await Assertions.Expect(einbinden).ToContainTextAsync("Leitfaden Aufsichtspflicht");
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Dokument hochladen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "dokumente", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Dokumente_Suche(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await DokumenteAsync(seite);
        await seite.GetByLabel("Dokumente suchen").FillAsync("pack");

        var zeilen = seite.Locator(".elkw-card").Filter(new() { HasText = "Dokumente der Freizeit" }).Locator("tbody tr");
        await Assertions.Expect(zeilen).ToHaveCountAsync(1);
        var packliste = zeilen.GetByRole(AriaRole.Link, new() { Name = "Packliste", Exact = true });
        await Assertions.Expect(packliste).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator(".elkw-card").Filter(new() { HasText = "Aus der Organisation einbinden" })).ToContainTextAsync("Keine Dokumente gefunden.");
        // Der Titel öffnet die PDF-Datei als Download (mit der Sitzung des Browsers).
        var href = await packliste.GetAttributeAsync("href");
        Assert.NotNull(href);
        var datei = await seite.Context.APIRequest.GetAsync(new Uri(new Uri(seite.Url), href).ToString());
        Assert.Equal(200, datei.Status);
        Assert.Equal("application/pdf", datei.Headers["content-type"]);
        Assert.StartsWith("attachment", datei.Headers["content-disposition"], StringComparison.Ordinal);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "dokumente-suche", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Dokument_Bearbeiten(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await DokumenteAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Bearbeiten: Packliste" }).ClickAsync();

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Packliste", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Titel", new() { Exact = true })).ToHaveValueAsync("Packliste");
        await Assertions.Expect(seite.GetByLabel("Eltern und Teilnehmende")).ToBeCheckedAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Datei öffnen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Entfernen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Zurück zu den Dokumenten" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "dokument-bearbeiten", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Org_Dokumente(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Org-Dokumente öffnen" }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Org-Dokumente", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Freizeiten der Organisation binden diese Dokumente ein, ohne sie zu kopieren.")).ToBeVisibleAsync();
        var zeilen = seite.Locator("tbody tr");
        // Story 2-9: dazu das Schutzkonzept der Organisation.
        await Assertions.Expect(zeilen).ToHaveCountAsync(3, new() { Timeout = 60_000 });
        await Assertions.Expect(zeilen.Nth(0).GetByRole(AriaRole.Link, new() { Name = "Hausordnung Zeltplatz" })).ToBeVisibleAsync();
        await Assertions.Expect(zeilen.Nth(1).GetByRole(AriaRole.Link, new() { Name = "Leitfaden Aufsichtspflicht" })).ToBeVisibleAsync();
        await Assertions.Expect(zeilen.Nth(2).GetByRole(AriaRole.Link, new() { Name = "Schutzkonzept Jugendwerk Musterstadt" })).ToBeVisibleAsync();
        await Assertions.Expect(zeilen.Nth(0).GetByRole(AriaRole.Button, new() { Name = "Entfernen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Heading, new() { Name = "Dokument hochladen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "org-dokumente", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Elterninfo(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, ElternMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Familie öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        var emma = seite.Locator(".elkw-card").Filter(new() { HasText = "Emma Muster" });
        await emma.GetByRole(AriaRole.Link, new() { Name = "Elterninfo" }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Elterninfo", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main")).ToContainTextAsync("Zeltlager Sommer 2027");
        await Assertions.Expect(seite.Locator("main li a")).ToHaveTextAsync(DemoElterninfo, new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Zurück zur Familienübersicht" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "elterninfo", geraet);
    }

    // ---------- Story 2-8: Sicherheitskette und Schnellsuche ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Sicherheitskette(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await SicherheitsketteAsync(seite);
        var schritte = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Was jetzt zu tun ist" }) }).Locator("ol li");
        await Assertions.Expect(schritte).ToHaveTextAsync(
            [
                "Ruhe bewahren und bei der verletzten Person bleiben.", "Bei Lebensgefahr sofort den Notruf 112 wählen.",
                "Leitung anrufen und Bescheid geben.", "Erste Hilfe leisten.",
            ]);
        await Assertions.Expect(seite.Locator("a[href='tel:112']")).ToHaveTextAsync("Notruf 112");
        await Assertions.Expect(seite.GetByText("Leitung: Jonas Hüttner, Miriam Zeltner")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("a[href='tel:0123456782']")).ToHaveTextAsync("Leitung anrufen: 0123 456782");
        await Assertions.Expect(seite.GetByText("Ansprechperson am Heimatort: Petra Heimann (Gemeindebüro)")).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator("a[href='tel:0123456783']")).ToHaveTextAsync("Ansprechperson anrufen: 0123 456783");
        // Die Demo-Freizeit ist in Vorbereitung: kein Eintrag „Notfall“ in der App-Leiste.
        await Assertions.Expect(seite.Locator("header a.zl-notfall-eintrag")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "sicherheitskette", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Sicherheitskette_Suche(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await SicherheitsketteAsync(seite);
        await seite.GetByLabel("Betroffene Person suchen").FillAsync("em");
        var treffer = seite.Locator(".elkw-people li");
        await Assertions.Expect(treffer).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        await Assertions.Expect(treffer.GetByRole(AriaRole.Link, new() { Name = "Emma Muster" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("freizeitpaesse/"));
        await Assertions.Expect(treffer).ToContainTextAsync("Teilnehmende");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "sicherheitskette-suche", geraet);

        // Der Treffer öffnet den Freizeitpass: zwei Schritte ab der Startseite.
        await treffer.GetByRole(AriaRole.Link, new() { Name = "Emma Muster" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Emma Muster", new() { Timeout = 60_000 });
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Sicherheitskette_Einrichten(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await EinstellungenAsync(seite);
        var card = seite.Locator("#sicherheitskette");
        await Assertions.Expect(card).ToContainTextAsync("Erscheint im Notfall bei Leitung und Team.", new() { Timeout = 60_000 });
        await Assertions.Expect(card.GetByLabel("Was jetzt zu tun ist")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^Ruhe bewahren"));
        await Assertions.Expect(card.GetByLabel("Telefon der Leitung im Notfall")).ToHaveValueAsync("0123 456782");
        await Assertions.Expect(card.GetByLabel("Ansprechperson am Heimatort", new() { Exact = true })).ToHaveValueAsync("Petra Heimann (Gemeindebüro)");
        await Assertions.Expect(card.GetByLabel("Telefon der Ansprechperson am Heimatort")).ToHaveValueAsync("0123 456783");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.EvaluateAsync("karte => karte.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "sicherheitskette-einrichten", geraet);
    }

    // ---------- Story 2-9: Schutzkonzept ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Schutzkonzept(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await LeaStartseiteAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Schutzkonzept", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Schutzkonzept", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".elkw-kicker")).ToHaveTextAsync("Sicherheit");
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Schutzkonzept Jugendwerk Musterstadt (PDF)" })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        // Nur lesend: Lea bestätigt nicht, damit der Demo-Stand für die anderen Bilder bleibt.
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Als gelesen bestätigen" })).ToBeVisibleAsync();
        var team = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Team", Exact = true }) });
        await Assertions.Expect(team.Locator(".zl-zahl")).ToHaveTextAsync("1 von 3 gelesen");
        var personen = team.Locator(".elkw-people li");
        await Assertions.Expect(personen).ToHaveCountAsync(3);
        await Assertions.Expect(personen.Nth(0)).ToContainTextAsync("Lea Feuerstein");
        await Assertions.Expect(personen.Nth(0)).ToContainTextAsync("Team · noch nicht gelesen");
        await Assertions.Expect(personen.Nth(1)).ToContainTextAsync("Jonas Hüttner");
        await Assertions.Expect(personen.Nth(1)).ToContainTextAsync("Leitung · gelesen am");
        await Assertions.Expect(personen.Nth(2)).ToContainTextAsync("Miriam Zeltner");
        await Assertions.Expect(personen.Nth(2)).ToContainTextAsync("Leitung · noch nicht gelesen");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        // Das Datum der Bestätigung hängt vom Tag des Einspielens ab; im Bild steht ein festes.
        await personen.Nth(1).Locator(".elkw-people__who span").EvaluateAsync("z => z.textContent = 'Leitung · gelesen am 12.10.2026'");

        await BildAsync(seite, "schutzkonzept", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Schutzkonzept_Hinweis(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await LeaStartseiteAsync(seite);
        var card = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Schutzkonzept", Exact = true }) });
        await Assertions.Expect(card).ToContainTextAsync("Das Schutzkonzept ist noch nicht als gelesen bestätigt.", new() { Timeout = 60_000 });
        await Assertions.Expect(card.GetByRole(AriaRole.Link, new() { Name = "Schutzkonzept lesen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Schutzkonzept", Exact = true })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "schutzkonzept-hinweis", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Schutzkonzept_Einrichten(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await EinstellungenAsync(seite);
        var card = seite.Locator("#schutzkonzept");
        await Assertions.Expect(card).ToContainTextAsync("Jedes Teammitglied bestätigt es als gelesen.", new() { Timeout = 60_000 });
        await Assertions.Expect(card.GetByLabel("Schutzkonzept Jugendwerk Musterstadt")).ToBeCheckedAsync();
        await Assertions.Expect(card.GetByLabel("Kein Schutzkonzept")).Not.ToBeCheckedAsync();
        await Assertions.Expect(card.GetByLabel("Packliste")).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByRole(AriaRole.Link, new() { Name = "Wer hat gelesen?" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.EvaluateAsync("karte => karte.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "schutzkonzept-einrichten", geraet);
    }

    // ---------- Story 3-7: Module pro Freizeit ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Module_Schalten(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await EinstellungenAsync(seite);
        var card = seite.Locator("#module");
        await Assertions.Expect(card).ToContainTextAsync("Abgeschaltete Module erscheinen nirgends in Zeltlotse.", new() { Timeout = 60_000 });
        await Assertions.Expect(card.GetByLabel("Verpflegung")).ToBeCheckedAsync();
        await Assertions.Expect(card.GetByLabel("Finanzen")).ToBeCheckedAsync();
        await Assertions.Expect(card.GetByLabel("Teilnehmer-Zugang")).Not.ToBeCheckedAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.EvaluateAsync("karte => karte.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "module-schalten", geraet);
    }

    // ---------- Story 2-10: Vorfall-Protokoll und Vorfall-Notizen ----------

    /// <summary>Feste Zeitpunkte für die drei Demo-Einträge (neueste oben); die echten hängen vom Tag des Einspielens ab.</summary>
    private static readonly string[] VorfallZeiten = ["12.10.2026 16:45", "12.10.2026 16:32", "12.10.2026 16:30"];

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Vorfall_Protokoll(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Lea (Team) öffnet über „Freizeitpässe“ den Pass von Mia und dort „Vorfall-Protokoll“; nur lesend.
        await FreizeitpaesseAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Mia Probst" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Mia Probst", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Vorfall-Protokoll", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Vorfall-Protokoll", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".elkw-kicker")).ToHaveTextAsync("Notfall");
        await Assertions.Expect(seite.GetByText("Mia Probst", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Jeder Eintrag ist sofort für die Leitung sichtbar.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        foreach (var feld in new[] { "Was ist passiert?", "Medikament gegeben (welches, wie viel)", "Aus dem Erste-Hilfe-Kasten entnommen" })
        {
            await Assertions.Expect(seite.GetByLabel(feld, new() { Exact = true })).ToBeVisibleAsync();
        }

        var eintraege = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Protokoll", Exact = true }) })
            .Locator("div[id^=vorfall-]");
        await Assertions.Expect(eintraege).ToHaveCountAsync(3);
        await Assertions.Expect(eintraege.Nth(0)).ToContainTextAsync("Medikament gegeben");
        await Assertions.Expect(eintraege.Nth(0)).ToContainTextAsync("Kühlpack, kein Medikament");
        await Assertions.Expect(eintraege.Nth(0)).ToContainTextAsync("von Jonas Hüttner");
        await Assertions.Expect(eintraege.Nth(1)).ToContainTextAsync("2 Pflaster, 1 sterile Kompresse");
        await Assertions.Expect(eintraege.Nth(2)).ToContainTextAsync("Beim Geländespiel am linken Knie geschnitten.");
        await Assertions.Expect(eintraege.Nth(2)).ToContainTextAsync("von Lea Feuerstein");
        await Assertions.Expect(seite.Locator("main button")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await VorfallZeitenFestAsync(eintraege.Locator("strong"));
        // Am Laptop zeigt das Bild die Card „Protokoll“, am Handy den Anfang mit Hinweis und Feldern.
        if (geraet == "laptop")
        {
            await eintraege.First.EvaluateAsync("eintrag => eintrag.closest('.elkw-card').scrollIntoView({ block: 'start' })");
        }

        await BildAsync(seite, "vorfall-protokoll", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Vorfall_Notizen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        var card = seite.Locator("#vorfall-notizen");
        await Assertions.Expect(card.Locator(".elkw-card__title")).ToHaveTextAsync("Vorfall-Notizen", new() { Timeout = 60_000 });
        var eintraege = card.Locator("div[id^=vorfall-]");
        await Assertions.Expect(eintraege).ToHaveCountAsync(3);
        await Assertions.Expect(eintraege.Nth(0).GetByRole(AriaRole.Link)).ToContainTextAsync("Mia Probst");
        await Assertions.Expect(eintraege.Nth(0)).ToContainTextAsync("Medikament gegeben: Kühlpack, kein Medikament");
        await Assertions.Expect(eintraege.Nth(1)).ToContainTextAsync("Aus dem Erste-Hilfe-Kasten entnommen: 2 Pflaster, 1 sterile Kompresse");
        await Assertions.Expect(eintraege.Nth(2)).ToContainTextAsync("Was passiert ist: Beim Geländespiel am linken Knie geschnitten.");
        await Assertions.Expect(eintraege.Nth(2)).ToContainTextAsync("von Lea Feuerstein");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await VorfallZeitenFestAsync(eintraege.Locator("a"));
        // Gast-Anmeldungen anderer Tests desselben Laufs erscheinen unter der Card; das Bild zeigt nur die Demo-Daten.
        await GastZeilenAusblendenAsync(seite);
        await card.EvaluateAsync("karte => karte.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "vorfall-notizen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Vorfall_Archiv(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        var card = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Vorfall-Notizen", Exact = true }) });
        await Assertions.Expect(card).ToContainTextAsync("Nach dem Abschluss einer Freizeit liegen ihre Vorfall-Notizen hier", new() { Timeout = 60_000 });
        await card.GetByRole(AriaRole.Link, new() { Name = "Archiv öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Archiv der Vorfall-Notizen", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".elkw-kicker")).ToHaveTextAsync("Organisation");
        // Story 3-8: Das Pfingstlager ist abgeschlossen; seine Vorfall-Notiz liegt im Archiv.
        var pfingstlager = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Pfingstlager 2026", Exact = true }) });
        await Assertions.Expect(pfingstlager).ToContainTextAsync("Was passiert ist: Beim Zeltabbau an der Hand geschnitten.", new() { Timeout = 60_000 });
        await Assertions.Expect(pfingstlager).ToContainTextAsync("Miriam Zeltner");
        await Assertions.Expect(seite.GetByText("Noch keine archivierten Vorfall-Notizen.")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        // Der Zeitpunkt der Notiz ist der des Einspielens: im Bild fest.
        await pfingstlager.Locator("strong").EvaluateAllAsync<object?>("elemente => elemente.forEach(e => { e.textContent = '29.05.2026 18:20' + e.textContent.slice(16); })");

        await BildAsync(seite, "vorfall-archiv", geraet);
    }

    /// <summary>Ersetzt in den gefundenen Texten den Zeitpunkt („TT.MM.JJJJ HH:mm“ am Anfang) der Reihe nach durch <see cref="VorfallZeiten"/>.</summary>
    private static async Task VorfallZeitenFestAsync(ILocator texte)
    {
        Assert.Equal(VorfallZeiten.Length, await texte.CountAsync());
        await texte.EvaluateAllAsync<object?>(
            "(elemente, zeiten) => elemente.forEach((e, i) => { e.textContent = zeiten[i] + e.textContent.slice(16); })",
            VorfallZeiten);
    }

    // ---------- Story 2-11: Notfallordner drucken ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Notfallordner(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Zwei Schritte ab der Startseite: „Freizeitpässe“, dann „Notfallordner drucken“.
        await FreizeitpaesseAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Notfallordner drucken" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Notfallordner", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Je Kind eine Seite mit Notfallkontakten und den Angaben aus dem Freizeitpass. Ausdrucke nach der Freizeit vernichten.")).ToBeVisibleAsync();
        var drucken = seite.GetByRole(AriaRole.Button, new() { Name = "Drucken", Exact = true });
        await Assertions.Expect(drucken).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Zurück zur Sicherheitskette" })).ToBeVisibleAsync();

        // Nur Mia ist bestätigt: genau eine Druckseite.
        var druckseiten = seite.Locator(".zl-druck-seite");
        await Assertions.Expect(druckseiten).ToHaveCountAsync(1);
        await Assertions.Expect(druckseiten.Locator(".elkw-kicker")).ToHaveTextAsync("Zeltlager Sommer 2027");
        await Assertions.Expect(druckseiten.Locator("h2")).ToHaveTextAsync("Mia Probst");
        var kasten = druckseiten.Locator(".zl-infokasten");
        await Assertions.Expect(kasten.Locator("h3")).ToHaveTextAsync("Notfallkontakte");
        await Assertions.Expect(kasten).ToContainTextAsync("Daniel Probst (Vater), 0123 456781");
        var angaben = druckseiten.Locator("dl");
        await Assertions.Expect(angaben.Locator("dt").First).ToHaveTextAsync("Allergien und Unverträglichkeiten");
        await Assertions.Expect(angaben.Locator("dd").First).ToHaveTextAsync("Glutenhaltiges Getreide");
        await Assertions.Expect(angaben).ToContainTextAsync("Medikamente (Name, Dosierung, wann)");
        await Assertions.Expect(angaben).ToContainTextAsync("Asthmaspray bei Bedarf, liegt im Kulturbeutel");
        await Assertions.Expect(angaben.Locator("dt").Filter(new() { HasText = "Notfallkontakte" })).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "notfallordner", geraet);

        // Im Druckmedium fallen App-Leiste, Kopf und Knöpfe weg; der Ausdruck (A4) hat je Kind genau eine Seite.
        await seite.EmulateMediaAsync(new() { Media = Media.Print });
        await Assertions.Expect(seite.Locator("header")).ToBeHiddenAsync();
        await Assertions.Expect(drucken).ToBeHiddenAsync();
        await Assertions.Expect(seite.Locator("h1")).ToBeHiddenAsync();
        await Assertions.Expect(druckseiten.Locator("h2")).ToBeVisibleAsync();
        var pdf = await seite.PdfAsync(new() { Format = "A4" });
        Assert.Equal(1, PdfSeiten(pdf));

        // Ab dem zweiten Kind beginnt jede Druckseite auf einem neuen Blatt: eine Kopie der Seite im DOM ergibt genau zwei A4-Seiten.
        await druckseiten.First.EvaluateAsync("seite => seite.after(seite.cloneNode(true))");
        await Assertions.Expect(druckseiten).ToHaveCountAsync(2);
        Assert.Equal(2, PdfSeiten(await seite.PdfAsync(new() { Format = "A4" })));
    }

    // ---------- Story 3-1: Wochenraster und Programmpunkte ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Wochenraster(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await ProgrammAsync(seite);
        if (geraet == "handy")
        {
            // Handy: Reiter je Tag; die Demo-Freizeit liegt in der Zukunft, also ist der erste Tag gewählt.
            var reiter = seite.Locator(".zl-reiter nav a");
            await Assertions.Expect(reiter).ToHaveCountAsync(12);
            await Assertions.Expect(reiter.First).ToHaveTextAsync("Mo 02.08.");
            await Assertions.Expect(reiter.First).ToHaveAttributeAsync("aria-current", "page");
            var tag = seite.Locator(".zl-reiter__inhalt");
            await Assertions.Expect(tag.Locator("h2")).ToHaveTextAsync("Montag, 2. August 2027");
            await Assertions.Expect(tag.Locator(".zl-termin h3")).ToHaveTextAsync(["Frühstück", "Mittagessen", "Anreise und Zeltaufbau", "Abendessen", "Lagerfeuer"]);
            await Assertions.Expect(TerminIn(tag, "Lagerfeuer").Locator(".elkw-status")).ToHaveTextAsync("Übernommen: Lea Feuerstein");
            await Assertions.Expect(TerminIn(tag, "Anreise und Zeltaufbau").GetByRole(AriaRole.Button, new() { Name = "Ich übernehme" })).ToBeVisibleAsync();
            await Assertions.Expect(seite.Locator(".zl-tagesspalten")).ToBeHiddenAsync();
        }
        else
        {
            // Laptop: alle zwölf Tage nebeneinander, der Bereich scrollt in sich.
            var spalten = seite.Locator(".zl-tagesspalte");
            await Assertions.Expect(spalten).ToHaveCountAsync(12);
            await Assertions.Expect(spalten.Locator("h2").First).ToHaveTextAsync("Mo 02.08.");
            var dienstag = spalten.Nth(1);
            await Assertions.Expect(dienstag.Locator("h2")).ToHaveTextAsync("Di 03.08.");
            await Assertions.Expect(TerminIn(dienstag, "Geländespiel").Locator(".elkw-kicker")).ToHaveTextAsync("14:30");
            await Assertions.Expect(TerminIn(dienstag, "Geländespiel").Locator(".elkw-status")).ToHaveTextAsync("Übernommen: Jonas Hüttner");
            await Assertions.Expect(TerminIn(dienstag, "Geländespiel").GetByRole(AriaRole.Button, new() { Name = "Abgeben" })).ToBeVisibleAsync();
            await Assertions.Expect(TerminIn(spalten.First, "Lagerfeuer").Locator(".elkw-status")).ToHaveTextAsync("Übernommen: Lea Feuerstein");
            await Assertions.Expect(seite.Locator(".zl-reiter")).ToBeHiddenAsync();
        }

        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Programmpunkt anlegen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "wochenraster", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Programmpunkt(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await ProgrammAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Lagerfeuer", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Programmpunkt", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".elkw-kicker")).ToHaveTextAsync("Programm");
        await Assertions.Expect(seite.GetByText("Montag, 2. August 2027", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(seite.Locator(".elkw-status").First).ToHaveTextAsync("Übernommen: Lea Feuerstein");
        await Assertions.Expect(seite.GetByLabel("Titel", new() { Exact = true })).ToHaveValueAsync("Lagerfeuer");
        await Assertions.Expect(seite.GetByLabel("Uhrzeit", new() { Exact = true })).ToHaveValueAsync("20:00");
        // Nur lesend: Jonas löscht nicht, damit der Demo-Stand für die anderen Bilder bleibt.
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Programmpunkt löschen" })).ToBeVisibleAsync();
        // Story 3-6: Das Lagerfeuer steht in der Programmbibliothek.
        await Assertions.Expect(seite.Locator("#programmbibliothek")).ToContainTextAsync("Steht in der Programmbibliothek der Organisation.");
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Zurück zum Programm" })).ToHaveAttributeAsync(
            "href", new System.Text.RegularExpressions.Regex(@"programm\?tag=2027-08-02$"));
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "programmpunkt", geraet);
    }

    // ---------- Story 3-2: Material am Programmpunkt und Materialliste ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task MaterialAmProgrammpunkt(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await ProgrammAsync(seite);
        if (geraet == "handy")
        {
            // Am Handy ist der erste Tag gewählt; das Geländespiel liegt am zweiten.
            await seite.Locator(".zl-reiter nav a").Nth(1).ClickAsync();
            await Assertions.Expect(seite.Locator(".zl-reiter__inhalt h2")).ToHaveTextAsync("Dienstag, 3. August 2027", new() { Timeout = 60_000 });
        }

        await seite.GetByRole(AriaRole.Link, new() { Name = "Geländespiel", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Programmpunkt", new() { Timeout = 60_000 });
        var card = seite.Locator("#material");
        await Assertions.Expect(card.Locator(".zl-termin h3")).ToHaveTextAsync(["Fahnen", "Seile", "Taschenlampen"], new() { Timeout = 60_000 });
        var seile = TerminIn(card, "Seile");
        await Assertions.Expect(seile.GetByLabel("Menge", new() { Exact = true })).ToHaveValueAsync("4 × 10 m");
        await Assertions.Expect(seile.GetByRole(AriaRole.Button, new() { Name = "gebraucht", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(TerminIn(card, "Fahnen").GetByRole(AriaRole.Button, new() { Name = "vorhanden", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        // Miriam bringt die Taschenlampen mit: bei Jonas steht ihr Name, ein Knopf fehlt.
        var taschenlampen = TerminIn(card, "Taschenlampen");
        await Assertions.Expect(taschenlampen.Locator(".elkw-status")).ToHaveTextAsync("Bringt mit: Miriam Zeltner");
        await Assertions.Expect(taschenlampen.GetByRole(AriaRole.Button, new() { Name = "Ich bringe das mit" })).ToHaveCountAsync(0);
        await Assertions.Expect(seile.GetByRole(AriaRole.Button, new() { Name = "Ich bringe das mit" })).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByRole(AriaRole.Button, new() { Name = "Material hinzufügen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt; im Bild stehen die Taschenlampen, die Miriam mitbringt.
        await taschenlampen.ScrollIntoViewIfNeededAsync();
        await BildAsync(seite, "material-programmpunkt", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Materialliste(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await ProgrammAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Materialliste", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Materialliste", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".zl-balken .zl-zahl")).ToHaveTextAsync("1 von 5 eingepackt", new() { Timeout = 60_000 });
        var cards = seite.Locator("section.elkw-card");
        await Assertions.Expect(cards.Locator("h2")).ToHaveTextAsync(["Lagerfeuer", "Geländespiel"]);
        await Assertions.Expect(cards.Locator(".elkw-kicker")).ToHaveTextAsync(["Mo 02.08. · 20:00", "Di 03.08. · 14:30"]);
        await Assertions.Expect(cards.Nth(0).Locator(".zl-termin h3")).ToHaveTextAsync(["Feuerholz", "Stockbrot-Teig"]);
        await Assertions.Expect(TerminIn(cards.Nth(0), "Stockbrot-Teig").GetByText("Menge: 3 kg")).ToBeVisibleAsync();
        await Assertions.Expect(TerminIn(cards.Nth(0), "Stockbrot-Teig").GetByRole(AriaRole.Button, new() { Name = "eingepackt", Exact = true }))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(TerminIn(cards.Nth(1), "Taschenlampen").Locator(".elkw-status")).ToHaveTextAsync("Bringt mit: Miriam Zeltner");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "materialliste", geraet);
    }

    // ---------- Story 3-3: Inventar und Reservierung ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Org_Inventar(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Inventar öffnen" }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Inventar", new() { Timeout = 60_000 });
        var cards = seite.Locator("section.elkw-card");
        await Assertions.Expect(cards.Locator("h2")).ToHaveTextAsync(["Fahnen", "Gaskocher", "Großzelt", "Kanus", "Artikel hinzufügen"], new() { Timeout = 60_000 });
        var grosszelt = cards.Nth(2);
        await Assertions.Expect(grosszelt.Locator("li")).ToHaveCountAsync(2);
        await Assertions.Expect(grosszelt.Locator("li").Nth(0)).ToContainTextAsync("Zeltlager Sommer 2027 · 02.08.–13.08.2027");
        await Assertions.Expect(grosszelt.Locator("li").Nth(1)).ToContainTextAsync("Kinderbibeltage 2027 · 10.08.–12.08.2027");
        await Assertions.Expect(grosszelt.Locator(".elkw-status")).ToHaveTextAsync(["Konflikt", "Konflikt"]);
        await Assertions.Expect(grosszelt.GetByLabel("Menge", new() { Exact = true })).ToHaveValueAsync("1 Stück");
        await Assertions.Expect(grosszelt.GetByRole(AriaRole.Button, new() { Name = "Artikel löschen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt; im Bild steht das Großzelt mit dem Konflikt.
        await grosszelt.ScrollIntoViewIfNeededAsync();
        await BildAsync(seite, "org-inventar", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Inventar_Reservieren(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Inventar", Exact = true }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Inventar", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-kicker").First).ToHaveTextAsync("Planen");
        var cards = seite.Locator("section.elkw-card");
        await Assertions.Expect(cards.Locator("h2")).ToHaveTextAsync(["Fahnen", "Gaskocher", "Großzelt", "Kanus"], new() { Timeout = 60_000 });
        var grosszelt = cards.Nth(2);
        await Assertions.Expect(grosszelt.Locator(".elkw-alert--warnung")).ToContainTextAsync(
            "Zur gleichen Zeit auch für Kinderbibeltage 2027 reserviert. Absprache mit der anderen Leitung nötig.");
        await Assertions.Expect(grosszelt.Locator("li")).ToContainTextAsync("02.08.–13.08.2027");
        await Assertions.Expect(grosszelt.GetByRole(AriaRole.Button, new() { Name = "Reservierung aufheben" })).ToBeVisibleAsync();
        await Assertions.Expect(cards.Nth(1).GetByText("Für diese Freizeit nicht reserviert.")).ToBeVisibleAsync();
        await Assertions.Expect(cards.Nth(1).GetByLabel("Von", new() { Exact = true })).ToHaveValueAsync("2027-08-02");
        await Assertions.Expect(cards.Nth(1).GetByLabel("Bis", new() { Exact = true })).ToHaveValueAsync("2027-08-13");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt; im Bild steht der Konflikthinweis am Großzelt.
        await grosszelt.ScrollIntoViewIfNeededAsync();
        await BildAsync(seite, "inventar", geraet);
    }

    // ---------- Story 3-4: Schäden, Verluste und Anschaffungswünsche ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Schaeden_Verluste(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Schäden & Verluste", Exact = true }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Schäden & Verluste", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-kicker").First).ToHaveTextAsync("Ablage");
        var liste = seite.Locator("section.elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Liste", Exact = true }) });
        await Assertions.Expect(liste.Locator("h3").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
        // Den Eintrag aus Leas Entnahme legt der Zustelldienst über das Ereignis an; neu laden, bis er da ist.
        const string entnahme = "Erste-Hilfe-Entnahme · Erste-Hilfe-Kasten";
        for (var versuch = 0; versuch < 30 && !(await liste.Locator("h3").AllTextContentsAsync()).Contains(entnahme); versuch++)
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            await seite.ReloadAsync();
            await Assertions.Expect(liste.Locator("h3").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
        }

        await Assertions.Expect(liste.Locator("h3")).ToHaveTextAsync(["Verlust · Taschenlampe", "Schaden · Großzelt: Stange verbogen", entnahme]);
        var kasten = liste.Locator("li").Filter(new() { HasText = entnahme });
        await Assertions.Expect(kasten).ToContainTextAsync("Laut Vorfall-Protokoll: 2 Pflaster, 1 sterile Kompresse", new() { Timeout = 60_000 });
        await Assertions.Expect(kasten.GetByRole(AriaRole.Button, new() { Name = "Entfernen" })).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Radio, new() { Name = "Schaden", Exact = true })).ToBeCheckedAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt; im Bild steht die Entnahme mit dem Text aus dem Vorfall-Protokoll.
        await kasten.ScrollIntoViewIfNeededAsync();
        await BildAsync(seite, "schaeden-verluste", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Org_Anschaffungswuensche(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Anschaffungswünsche öffnen" }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anschaffungswünsche", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-kicker").First).ToHaveTextAsync("Organisation");
        // Story 3-8: Das Pfingstlager ist abgeschlossen; seine Schäden und Verluste sind Anschaffungswünsche (nur lesend, nichts wird erledigt).
        var pfingstlager = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Pfingstlager 2026", Exact = true }) });
        var eintraege = pfingstlager.Locator("li");
        await Assertions.Expect(eintraege).ToHaveCountAsync(2, new() { Timeout = 60_000 });
        await Assertions.Expect(pfingstlager).ToContainTextAsync("Schaden · Kochtopf: Henkel abgebrochen");
        await Assertions.Expect(pfingstlager).ToContainTextAsync("Verlust · Zeltheringe (12 Stück)");
        await Assertions.Expect(pfingstlager.GetByRole(AriaRole.Button, new() { Name = "Erledigt" })).ToHaveCountAsync(2);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        // Das Datum der Einträge ist das des Einspielens: im Bild fest.
        await eintraege.EvaluateAllAsync<object?>(
            "elemente => elemente.forEach(e => { for (const k of e.childNodes) { if (k.nodeType === 3) { k.textContent = k.textContent.replace(/\\d{2}\\.\\d{2}\\.\\d{4}/, '30.05.2026'); } } })");

        await BildAsync(seite, "anschaffungswuensche", geraet);
    }

    // ---------- Story 3-8: Freizeit abschließen ----------

    /// <summary>Name der Demo-Freizeit „Zeltlager Sommer 2027“.</summary>
    private const string DemoFreizeitName = "Zeltlager Sommer 2027";

    /// <summary>
    /// Öffnet auf der Startseite die Freizeit mit diesem Namen. Seit Story 3-8 ist Miriam Leitung dreier Freizeiten (Zeltlager, Herbstfreizeit,
    /// Pfingstlager); jede Card hat ihr eigenes „Freizeit öffnen“.
    /// </summary>
    private static async Task FreizeitOeffnenAsync(IPage seite, string name)
    {
        var card = seite.Locator(".elkw-card").Filter(new() { HasText = name });
        await card.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(name, new() { Timeout = 60_000 });
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeit_Abschliessen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, PruefMail);
        await FreizeitOeffnenAsync(seite, "Herbstfreizeit 2026");
        await Assertions.Expect(seite.Locator("main .elkw-status").First).ToHaveTextAsync("Nachbereitung");
        var card = seite.Locator("#abschluss");
        await Assertions.Expect(card.Locator(".elkw-card__title")).ToHaveTextAsync("Abschluss", new() { Timeout = 60_000 });
        await Assertions.Expect(card).ToContainTextAsync("2 Aufgaben sind noch offen:", new() { Timeout = 60_000 });
        await Assertions.Expect(card.Locator("li a")).ToHaveTextAsync(["Abrechnung bei der Organisation abgeben", "Fotos an die Familien schicken"]);
        await Assertions.Expect(card.GetByRole(AriaRole.Link, new() { Name = "Schäden & Verluste" })).ToBeVisibleAsync();

        // Nur die Inline-Bestätigung aufklappen; „Ja, abschließen“ bleibt ungetippt, damit der Demo-Stand für die anderen Bilder bleibt.
        await card.GetByRole(AriaRole.Button, new() { Name = "Freizeit abschließen" }).ClickAsync();
        await Assertions.Expect(card.Locator(".zl-bestaetigung__satz")).ToHaveTextAsync("2 Aufgaben offen – trotzdem abschließen?");
        await Assertions.Expect(card.GetByRole(AriaRole.Button, new() { Name = "Ja, abschließen" })).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByRole(AriaRole.Button, new() { Name = "Nein" })).ToBeFocusedAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.EvaluateAsync("karte => karte.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "freizeit-abschliessen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Freizeit_Gelaufen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, PruefMail);
        await FreizeitOeffnenAsync(seite, "Pfingstlager 2026");
        await Assertions.Expect(seite.GetByText("Diese Freizeit ist abgeschlossen und nur lesbar.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-status").First).ToHaveTextAsync("gelaufen");
        await Assertions.Expect(seite.Locator("#abschluss")).ToHaveCountAsync(0);
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Freizeit abschließen" })).ToHaveCountAsync(0);
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Schäden & Verluste" })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeit-gelaufen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Org_Freizeiten_Filter(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        var card = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Freizeiten", Exact = true }) });
        // Andere Tests desselben Laufs legen weitere Freizeiten an; geprüft werden die vier der Demo-Daten.
        foreach (var name in new[] { "Zeltlager Sommer 2027", "Kinderbibeltage 2027", "Herbstfreizeit 2026", "Pfingstlager 2026" })
        {
            await Assertions.Expect(card.Locator("tbody tr").Filter(new() { HasText = name })).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        }

        var filter = card.GetByRole(AriaRole.Group, new() { Name = "Freizeiten nach Phase" });
        await Assertions.Expect(filter.GetByRole(AriaRole.Button)).ToHaveTextAsync(["Alle", "in Vorbereitung", "läuft", "Nachbereitung", "gelaufen"]);

        await filter.GetByRole(AriaRole.Button, new() { Name = "gelaufen", Exact = true }).ClickAsync();

        await Assertions.Expect(card.Locator("tbody tr")).ToHaveCountAsync(1);
        await Assertions.Expect(card.Locator("tbody tr")).ToContainTextAsync("Pfingstlager 2026");
        await Assertions.Expect(filter.GetByRole(AriaRole.Button, new() { Name = "gelaufen", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.EvaluateAsync("karte => karte.scrollIntoView({ block: 'start' })");

        await BildAsync(seite, "freizeiten-filter", geraet);
    }

    // ---------- Story 3-5: Aufgaben und Aufgaben-Vorlagen ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Aufgaben(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await LeaStartseiteAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Aufgaben", Exact = true }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Aufgaben", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-kicker").First).ToHaveTextAsync("Planen");
        await Assertions.Expect(seite.Locator(".zl-reiter nav a")).ToHaveTextAsync(["Alle", "Meine Aufgaben"], new() { Timeout = 60_000 });
        // Die Vorlagen der Organisation stehen schon drin (Zeltplatz-Vertrag, Erste-Hilfe-Kasten, Zeltplatz übergeben), dazu die eigenen Aufgaben.
        await Assertions.Expect(seite.Locator("section.elkw-card h2")).ToHaveTextAsync(
            ["Vorbereitung", "Während der Freizeit", "Nachbereitung", "Aufgabe anlegen"], new() { Timeout = 60_000 });
        var andacht = TerminIn(seite.Locator("main"), "Andacht am Lagerfeuer vorbereiten");
        await Assertions.Expect(andacht.Locator(".elkw-status")).ToHaveTextAsync("Übernommen: Lea Feuerstein");
        await Assertions.Expect(andacht.GetByRole(AriaRole.Link, new() { Name = "Am Programmpunkt: Lagerfeuer" })).ToBeVisibleAsync();
        await Assertions.Expect(andacht.GetByRole(AriaRole.Button, new() { Name = "Abgeben" })).ToBeVisibleAsync();
        var vertrag = TerminIn(seite.Locator("main"), "Zeltplatz-Vertrag prüfen");
        await Assertions.Expect(vertrag.Locator(".elkw-status")).ToHaveTextAsync(["Übernommen: Jonas Hüttner", "Erledigt"]);
        var fotos = TerminIn(seite.Locator("main"), "Fotos sammeln");
        await Assertions.Expect(fotos.Locator(".elkw-status")).ToHaveTextAsync("Offen");
        await Assertions.Expect(fotos.GetByRole(AriaRole.Button, new() { Name = "Ich übernehme" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt.
        await BildAsync(seite, "aufgaben", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Meine_Aufgaben(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await LeaStartseiteAsync(seite);
        var card = seite.Locator(".elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Meine Aufgaben", Exact = true }) });
        await Assertions.Expect(card.Locator("li a")).ToHaveTextAsync(
            ["Erste-Hilfe-Kasten auffüllen", "Andacht am Lagerfeuer vorbereiten"], new() { Timeout = 60_000 });
        await Assertions.Expect(card.GetByRole(AriaRole.Link, new() { Name = "Alle Aufgaben" })).ToBeVisibleAsync();
        // Das Team sieht keinen Warnhinweis zu unbesetzten Aufgaben.
        await Assertions.Expect(seite.GetByText("noch nicht übernommen")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await card.ScrollIntoViewIfNeededAsync();
        await BildAsync(seite, "meine-aufgaben", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Org_Aufgaben_Vorlagen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Aufgaben-Vorlagen öffnen" }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Aufgaben-Vorlagen", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-kicker").First).ToHaveTextAsync("Organisation");
        await Assertions.Expect(seite.Locator("section.elkw-card h2")).ToHaveTextAsync(
            ["Vorlage hinzufügen", "Vorbereitung", "Nachbereitung"], new() { Timeout = 60_000 });
        var vorbereitung = seite.Locator("section.elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Vorbereitung", Exact = true }) });
        await Assertions.Expect(vorbereitung.Locator("input").Nth(0)).ToHaveValueAsync("Erste-Hilfe-Kasten auffüllen");
        await Assertions.Expect(vorbereitung.Locator("input").Nth(1)).ToHaveValueAsync("Zeltplatz-Vertrag prüfen");
        await Assertions.Expect(vorbereitung.GetByRole(AriaRole.Button, new() { Name = "Vorlage löschen" })).ToHaveCountAsync(2);
        await Assertions.Expect(seite.GetByRole(AriaRole.Radio, new() { Name = "Vorbereitung", Exact = true })).ToBeCheckedAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "aufgaben-vorlagen", geraet);
    }

    // ---------- Story 3-6: Programmbibliothek ----------

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Programmbibliothek(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await ProgrammAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Programmbibliothek", Exact = true }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Programmbibliothek", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-kicker").First).ToHaveTextAsync("Programm");
        // Das Team hat „Geländespiel“ und „Lagerfeuer“ abgelegt; Material und Aufgaben ohne Status und Personen.
        await Assertions.Expect(seite.Locator("section.elkw-card h2")).ToHaveTextAsync(["Geländespiel", "Lagerfeuer"], new() { Timeout = 60_000 });
        var gelaendespiel = seite.Locator("section.elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Geländespiel", Exact = true }) });
        await Assertions.Expect(gelaendespiel.Locator("li")).ToHaveTextAsync(["Fahnen · 8 Stück", "Seile · 4 × 10 m", "Taschenlampen · 10 Stück"]);
        await Assertions.Expect(gelaendespiel.GetByText("Keine Aufgaben.")).ToBeVisibleAsync();
        var lagerfeuer = seite.Locator("section.elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = "Lagerfeuer", Exact = true }) });
        await Assertions.Expect(lagerfeuer.Locator("li")).ToHaveTextAsync(
            ["Feuerholz · 2 Kisten", "Stockbrot-Teig · 3 kg", "Andacht am Lagerfeuer vorbereiten · Während der Freizeit"]);
        await Assertions.Expect(gelaendespiel.GetByLabel("Tag *")).ToHaveAttributeAsync("min", "2027-08-02");
        await Assertions.Expect(gelaendespiel.GetByLabel("Tag *")).ToHaveAttributeAsync("max", "2027-08-13");
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "In diese Freizeit übernehmen" })).ToHaveCountAsync(2);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt; das Datum des Ablegens hängt vom Einspielen ab und steht im Bild fest.
        await AbgelegtAmFestAsync(seite);
        await BildAsync(seite, "programmbibliothek", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Org_Programmbibliothek(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, OrgAdminMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Organisation öffnen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Jugendwerk Musterstadt", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Programmbibliothek öffnen" }).ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Programmbibliothek", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("main .elkw-kicker").First).ToHaveTextAsync("Organisation");
        await Assertions.Expect(seite.Locator("section.elkw-card h2")).ToHaveTextAsync(["Geländespiel", "Lagerfeuer"], new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "Aus der Bibliothek entfernen" })).ToHaveCountAsync(2);
        await Assertions.Expect(seite.GetByRole(AriaRole.Button, new() { Name = "In diese Freizeit übernehmen" })).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend: Andrea entfernt nichts, damit der Demo-Stand für die anderen Bilder bleibt.
        await AbgelegtAmFestAsync(seite);
        await BildAsync(seite, "org-programmbibliothek", geraet);
    }

    // ---------- Story 4-1: Rezepte mit Skalierung ----------

    /// <summary>
    /// Lea (Team) öffnet die Demo-Freizeit und tippt „Küche“ (das Zeltlager hat Verpflegung an). Die Freizeit verpflegt 4 Personen: Jonas und Miriam
    /// (Leitung), Lea (Team) und Mia (bestätigt).
    /// </summary>
    private async Task KuecheAsync(IPage seite)
    {
        await LeaStartseiteAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Küche", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Rezepte", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Hochgerechnet auf 4 Personen der Freizeit.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Rezepte(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await KuecheAsync(seite);
        var cards = seite.Locator("section.elkw-card").Filter(new() { Has = seite.Locator("h2 a") });
        await Assertions.Expect(cards.Locator("h2")).ToHaveTextAsync(["Chili sin Carne", "Pfannkuchen"], new() { Timeout = 60_000 });
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("für 4 Personen · 7 Zutaten");
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("Allergene: Sellerie");
        await Assertions.Expect(cards.Nth(1)).ToContainTextAsync("Allergene: Glutenhaltiges Getreide, Eier, Milch (inkl. Laktose)");
        await Assertions.Expect(seite.Locator("#rezept-anlegen")).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "rezepte", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Rezept(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await KuecheAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Pfannkuchen", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Pfannkuchen", new() { Timeout = 60_000 });
        var zutaten = seite.Locator("#zutaten");
        await Assertions.Expect(zutaten.Locator("th")).ToHaveTextAsync(["Menge", "Einheit", "Zutat", "Allergene", "Für 4 Personen"], new() { Timeout = 60_000 });
        await Assertions.Expect(zutaten.Locator("tbody tr").Nth(0).Locator("td")).ToHaveTextAsync(["400", "g", "Mehl", "Glutenhaltiges Getreide", "400 g"]);
        await Assertions.Expect(zutaten.Locator("tbody tr").Nth(3).Locator("td")).ToHaveTextAsync(["", "", "Salz", "", ""]);
        await Assertions.Expect(seite.Locator("#zubereitung")).ToContainTextAsync("In der Pfanne dünn ausbacken.");
        await Assertions.Expect(seite.GetByLabel("Basis-Personenzahl")).ToHaveValueAsync("4");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt.
        await BildAsync(seite, "rezept", geraet);
    }

    // ---------- Story 4-2: Gerichte vormerken und Einkaufsliste ----------

    /// <summary>
    /// Lea (Team) öffnet die Küche, tippt den Reiter „Essensplan“ und den Tag „Di 03.08.“. Dort hängen in der Demo Pfannkuchen (Frühstück) und Chili sin
    /// Carne (Mittagessen); Mia (bestätigt) hat Gluten im Freizeitpass.
    /// </summary>
    private async Task EssensplanAsync(IPage seite)
    {
        await KuecheAsync(seite);
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Küche" }).GetByRole(AriaRole.Link, new() { Name = "Essensplan", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Essensplan", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Tage" }).GetByRole(AriaRole.Link, new() { Name = "Di 03.08.", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator(".zl-reiter__inhalt h2")).ToHaveTextAsync("Dienstag, 3. August 2027", new() { Timeout = 60_000 });
    }

    private static ILocator MahlzeitCard(IPage seite, string titel) =>
        seite.Locator("section.elkw-card").Filter(new() { Has = seite.GetByRole(AriaRole.Heading, new() { Name = titel, Exact = true, Level = 3 }) });

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Essensplan(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await EssensplanAsync(seite);
        var fruehstueck = MahlzeitCard(seite, "Frühstück");
        await Assertions.Expect(fruehstueck).ToContainTextAsync("Pfannkuchen", new() { Timeout = 60_000 });
        await Assertions.Expect(fruehstueck).ToContainTextAsync("1 Ersatzportion einplanen (enthält Gluten)");
        await Assertions.Expect(MahlzeitCard(seite, "Mittagessen")).ToContainTextAsync("Chili sin Carne");
        await Assertions.Expect(MahlzeitCard(seite, "Abendessen")).ToContainTextAsync("Noch nichts vorgemerkt.");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt.
        await BildAsync(seite, "essensplan", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Gericht_tauschen(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await EssensplanAsync(seite);
        var fruehstueck = MahlzeitCard(seite, "Frühstück");
        await fruehstueck.GetByRole(AriaRole.Button, new() { Name = "Tauschen", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        var chili = fruehstueck.GetByRole(AriaRole.Group, new() { Name = "Tauschen gegen" }).GetByLabel("Chili sin Carne");
        await chili.ClickAsync();
        var vergleich = fruehstueck.Locator(".zl-vergleich");
        await Assertions.Expect(vergleich.Locator(".zl-vergleich__text")).ToHaveTextAsync(["Pfannkuchen", "Chili sin Carne"], new() { Timeout = 60_000 });
        // Story 4-3: Mehl und Milch sind in der Demo schon gekauft und stehen nicht im Vergleich.
        await Assertions.Expect(vergleich.Locator(".zl-vergleich__spalte").Nth(0)).ToContainTextAsync("4 Stück Eier");
        await Assertions.Expect(vergleich.Locator(".zl-vergleich__spalte").Nth(0)).Not.ToContainTextAsync("Mehl");
        await Assertions.Expect(vergleich.Locator(".zl-vergleich__spalte").Nth(1)).ToContainTextAsync("500 g Kidneybohnen");
        await Assertions.Expect(fruehstueck).ToContainTextAsync("2 schon gekaufte Posten stehen nicht im Vergleich.");
        await Assertions.Expect(fruehstueck.GetByRole(AriaRole.Button, new() { Name = "Übernehmen", Exact = true })).ToBeEnabledAsync();
        await Assertions.Expect(chili).ToBeCheckedAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur die Vorschau, ohne „Übernehmen“: Der Demo-Stand bleibt für die anderen Bilder.
        await vergleich.ScrollIntoViewIfNeededAsync();
        await BildAsync(seite, "gericht-tauschen", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Einkaufsliste(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await KuecheAsync(seite);
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Küche" }).GetByRole(AriaRole.Link, new() { Name = "Einkaufsliste", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Einkaufsliste", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("#allergen-abgleich li")).ToHaveTextAsync(["Gluten: 1 Person"], new() { Timeout = 60_000 });
        var dienstag = seite.Locator("#tag-2027-08-03");
        await Assertions.Expect(dienstag.Locator("h2")).ToHaveTextAsync("Dienstag, 3. August 2027");
        await Assertions.Expect(dienstag.Locator("tbody tr").First).ToContainTextAsync("Pfannkuchen · Frühstück");
        await Assertions.Expect(dienstag.Locator("tbody tr").First).ToContainTextAsync("von Hand");
        // Story 4-3: Spalten „Freigegeben“ und „Gekauft“, Zählzeile und Tages-Knöpfe.
        await Assertions.Expect(dienstag.Locator("tbody tr").First).ToContainTextAsync("gekauft");
        await Assertions.Expect(dienstag.Locator("tbody tr").First.GetByRole(AriaRole.Checkbox)).ToBeCheckedAsync();
        await Assertions.Expect(dienstag).ToContainTextAsync("11 von 12 freigegeben · 2 gekauft");
        await Assertions.Expect(dienstag.GetByRole(AriaRole.Button, new() { Name = "Ganzen Tag freigeben" })).ToBeVisibleAsync();
        await Assertions.Expect(dienstag).ToContainTextAsync("Pfannkuchen · Frühstück: 1 Ersatzportion einplanen (enthält Gluten)");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt.
        await BildAsync(seite, "einkaufsliste", geraet);
    }

    // ---------- Story 4-3: Einkauf freigeben und abhaken ----------

    /// <summary>
    /// Lea (Team) kauft ein: Küche → Reiter „Einkauf“. In der Demo ist Dienstag (03.08.) freigegeben (ohne die Äpfel), Mehl und Milch der Pfannkuchen sind
    /// schon abgehakt. Nur lesend, damit der Demo-Stand für die anderen Bilder bleibt.
    /// </summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Einkauf(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await KuecheAsync(seite);
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Küche" }).GetByRole(AriaRole.Link, new() { Name = "Einkauf", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Einkauf", new() { Timeout = 60_000 });
        var dienstag = seite.Locator("#tag-2027-08-03");
        await Assertions.Expect(dienstag.Locator("h2")).ToHaveTextAsync("Dienstag, 3. August 2027", new() { Timeout = 60_000 });
        await Assertions.Expect(dienstag.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(11);
        await Assertions.Expect(dienstag.GetByLabel("Mehl 500 g · Pfannkuchen · Frühstück")).ToBeCheckedAsync();
        await Assertions.Expect(dienstag.GetByLabel("Milch 0,75 l · Pfannkuchen · Frühstück")).ToBeCheckedAsync();
        await Assertions.Expect(dienstag.GetByLabel("Eier 4 Stück · Pfannkuchen · Frühstück")).Not.ToBeCheckedAsync();
        await Assertions.Expect(seite.GetByText("Äpfel")).ToHaveCountAsync(0);
        await Assertions.Expect(seite.GetByText("Allergen")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "einkauf", geraet);
    }

    // ---------- Story 4-4: Küchen-Tagesansicht und Restetag ----------

    /// <summary>
    /// Lea (Team) öffnet Küche → Reiter „Tagesansicht“ und wählt Dienstag (03.08.): Pfannkuchen zum Frühstück mit einer Ersatzportion (Gluten), Chili
    /// sin Carne zum Mittagessen, das Abendessen ist in der Demo ein Restetag; dazu „Gluten: 1 Person“ und „Vegetarisch: 1 Person“ (Mia, bestätigt). Nur
    /// lesend, damit der Demo-Stand für die anderen Bilder bleibt.
    /// </summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Tagesansicht(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await KuecheAsync(seite);
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Küche" }).GetByRole(AriaRole.Link, new() { Name = "Tagesansicht", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Tagesansicht", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Tage" }).GetByRole(AriaRole.Link, new() { Name = "Di 03.08." }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".zl-reiter__inhalt h2")).ToHaveTextAsync("Dienstag, 3. August 2027", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("#allergien-sonderkost li")).ToHaveTextAsync(["Gluten: 1 Person", "Vegetarisch: 1 Person"], new() { Timeout = 60_000 });
        var fruehstueck = seite.Locator("section.elkw-card").Filter(new() { Has = seite.Locator("h3", new() { HasTextString = "Frühstück" }) });
        await Assertions.Expect(fruehstueck).ToContainTextAsync("Pfannkuchen");
        await Assertions.Expect(fruehstueck).ToContainTextAsync("1 Ersatzportion einplanen (enthält Gluten)");
        var abendessen = seite.Locator("section.elkw-card").Filter(new() { Has = seite.Locator("h3", new() { HasTextString = "Abendessen" }) });
        await Assertions.Expect(abendessen.GetByLabel("Restetag")).ToBeCheckedAsync();
        await Assertions.Expect(abendessen).ToContainTextAsync("Restetag: Reste verwerten.");
        await Assertions.Expect(seite.GetByText("Mia")).ToHaveCountAsync(0);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Am Laptop zeigt das Bild die Mahlzeiten bis zum Restetag, am Handy die Übersicht oben.
        if (geraet == "laptop")
        {
            await abendessen.ScrollIntoViewIfNeededAsync();
        }

        await BildAsync(seite, "tagesansicht", geraet);
    }

    // ---------- Story 5-1: Kalkulation und Beitragsrechner ----------

    /// <summary>
    /// Lea (Team) öffnet die Demo-Freizeit und tippt „Finanzen“ (das Zeltlager hat Finanzen an). Die Demo-Kalkulation: 40 Teilnehmende, 10 Mitarbeitende,
    /// 12 Tage, 14.200 € Kosten, 2.200 € Einnahmen, 300,00 € Teilnehmerbeitrag.
    /// </summary>
    private async Task FinanzenAsync(IPage seite)
    {
        await LeaStartseiteAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Finanzen", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Kalkulation", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("#ergebnis .zl-balken--gross .zl-zahl")).ToHaveTextAsync("300,00 €", new() { Timeout = 60_000 });
    }

    /// <summary>Die Kalkulation: Ergebnis mit Beitrag und Anteilen, darunter Planzahlen, die sechs Kategorien und die Einnahmen. Nur lesend.</summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Kalkulation(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await FinanzenAsync(seite);
        var ergebnis = seite.Locator("#ergebnis");
        await Assertions.Expect(ergebnis).ToContainTextAsync("14.200,00 €");
        await Assertions.Expect(ergebnis).ToContainTextAsync("2.200,00 €");
        await Assertions.Expect(ergebnis.Locator(".zl-balken strong")).ToHaveTextAsync(
            ["Teilnehmerbeitrag", "Unterkunft", "Verpflegung", "Fahrt", "Programm und Material", "Mitarbeitende", "Sonstiges"]);
        await Assertions.Expect(seite.GetByLabel("Teilnehmende", new() { Exact = true })).ToHaveValueAsync("40");
        await Assertions.Expect(seite.GetByLabel("Tagessatz pro Person und Tag in €")).ToHaveValueAsync("7");
        await Assertions.Expect(seite.Locator("#kategorie-Verpflegung")).ToContainTextAsync("Kosten: 4.200,00 €");
        await Assertions.Expect(seite.Locator("#einnahmen h3")).ToHaveTextAsync(["Zuschuss Landesjugendplan", "Rabatt der Organisation"]);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "kalkulation", geraet);
    }

    /// <summary>
    /// Der Beitragsrechner: Lea tippt den Reiter „Beitragsrechner“ und schiebt den Regler „Teilnehmende“ mit der Pfeiltaste von 40 auf 35. Mit Reglern ergibt
    /// das 315,43 € statt 300,00 € („Teurer“, Differenz +15,43 €); gespeichert wird nichts.
    /// </summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Beitragsrechner(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await FinanzenAsync(seite);
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Finanzen" }).GetByRole(AriaRole.Link, new() { Name = "Beitragsrechner", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Beitragsrechner", new() { Timeout = 60_000 });
        var regler = seite.Locator("#rechner-teilnehmende");
        await Assertions.Expect(regler).ToHaveValueAsync("40", new() { Timeout = 60_000 });
        await regler.FocusAsync();
        for (var i = 0; i < 5; i++)
        {
            await regler.PressAsync("ArrowLeft");
        }

        await Assertions.Expect(seite.Locator("#rechner-teilnehmende-wert")).ToHaveTextAsync("35");
        var balken = seite.Locator("#vergleich .zl-balken");
        await Assertions.Expect(balken.Nth(0).Locator(".zl-zahl")).ToHaveTextAsync("300,00 €");
        await Assertions.Expect(balken.Nth(1).Locator(".zl-zahl")).ToHaveTextAsync("315,43 €");
        await Assertions.Expect(balken.Nth(1).Locator(".elkw-status")).ToHaveTextAsync("Teurer");
        await Assertions.Expect(seite.Locator("#vergleich")).ToContainTextAsync("Differenz: +15,43 €");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Das Bild zeigt den Vergleich oben (der Fokus hatte zum Regler gescrollt).
        await regler.BlurAsync();
        await seite.EvaluateAsync("() => window.scrollTo(0, 0)");

        await BildAsync(seite, "beitragsrechner", geraet);
    }

    // ---------- Story 5-2: Belege, Soll-Ist und Erstattung ----------

    /// <summary>Lea (Team) öffnet „Finanzen“ und tippt einen Finanzen-Reiter.</summary>
    private async Task FinanzenReiterAsync(IPage seite, string reiter)
    {
        await FinanzenAsync(seite);
        await seite.GetByRole(AriaRole.Navigation, new() { Name = "Finanzen" }).GetByRole(AriaRole.Link, new() { Name = reiter, Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(reiter, new() { Timeout = 60_000 });
    }

    /// <summary>Die sechs Demo-Belege in der Liste (neueste zuerst), mit Namen, Foto und Haken; darüber die Card „Beleg erfassen“. Nur lesend.</summary>
    private async Task BelegeListeAsync(IPage seite)
    {
        await FinanzenReiterAsync(seite, "Belege");
        await Assertions.Expect(seite.Locator("#belege tbody tr")).ToHaveCountAsync(6, new() { Timeout = 60_000 });
    }

    /// <summary>
    /// Die Belege: Card „Beleg erfassen“ (am Handy im Bild) und die Liste mit „Brötchen und Obst“ von Lea Feuerstein mit Foto, Jonas' Großeinkauf als
    /// erstattet und der Summe 7.530,00 € (am Laptop im Bild).
    /// </summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Belege(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await BelegeListeAsync(seite);
        await Assertions.Expect(seite.Locator("#beleg-erfassen")).ToContainTextAsync("Betrag in € *");
        await Assertions.Expect(seite.GetByLabel("Ich", new() { Exact = true })).ToBeCheckedAsync();
        var brot = seite.Locator("#belege tbody tr").Filter(new() { HasText = "Brötchen und Obst" });
        await Assertions.Expect(brot).ToContainTextAsync("Lea Feuerstein");
        await Assertions.Expect(brot.GetByRole(AriaRole.Link, new() { Name = "Foto: Brötchen und Obst" })).ToBeVisibleAsync();
        await Assertions.Expect(brot.GetByLabel("Erstattet")).Not.ToBeCheckedAsync();
        var einkauf = seite.Locator("#belege tbody tr").Filter(new() { HasText = "Großeinkauf Lebensmittel" });
        await Assertions.Expect(einkauf).ToContainTextAsync("3.854,20 €");
        await Assertions.Expect(einkauf.GetByLabel("Erstattet")).ToBeCheckedAsync();
        await Assertions.Expect(seite.Locator("#belege")).ToContainTextAsync("Summe: 7.530,00 €");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Am Handy zeigt das Bild das Formular oben, am Laptop die Liste.
        if (geraet == "laptop")
        {
            await seite.Locator("#belege").ScrollIntoViewIfNeededAsync();
        }

        await BildAsync(seite, "belege", geraet);
    }

    /// <summary>
    /// Ein Beleg: Lea tippt „Brötchen und Obst“. Betrag, Kategorie, Bezeichnung und „Bezahlt von“ als Felder, der Haken „Erstattet“ und das Foto vom
    /// Kassenbon (am Laptop im Bild). Nur lesend.
    /// </summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Beleg(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await BelegeListeAsync(seite);
        await seite.Locator("#belege").GetByRole(AriaRole.Link, new() { Name = "Brötchen und Obst", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Brötchen und Obst", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByLabel("Betrag in €")).ToHaveValueAsync("23,4", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("#beleg-kategorie").GetByLabel("Verpflegung")).ToBeCheckedAsync();
        await Assertions.Expect(seite.Locator("#beleg-bezahlt-von").GetByLabel("Lea Feuerstein")).ToBeCheckedAsync();
        await Assertions.Expect(seite.Locator("#beleg-erstattet")).Not.ToBeCheckedAsync();
        var foto = seite.Locator("#datei img");
        await Assertions.Expect(foto).ToBeVisibleAsync();
        await seite.WaitForFunctionAsync("() => { const b = document.querySelector('#datei img'); return b && b.complete && b.naturalWidth > 0; }", null, new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        // Am Handy zeigt das Bild die Felder oben, am Laptop das Foto.
        if (geraet == "laptop")
        {
            await seite.Locator("#datei").ScrollIntoViewIfNeededAsync();
        }

        await BildAsync(seite, "beleg", geraet);
    }

    /// <summary>
    /// Soll-Ist: Verpflegung 3.900 € von 4.200 € „Knapp“, Fahrt 1.950 € von 1.800 € „Überschritten“, der Rest „Im Plan“; bei den Erstattungen zuerst Lea mit
    /// „noch 45,80 €“ („Bekommt noch Geld“), Jonas und Miriam „Alles erstattet“. Nur lesend.
    /// </summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Soll_Ist(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await FinanzenReiterAsync(seite, "Soll-Ist");
        var balken = seite.Locator("#soll-ist .zl-balken");
        await Assertions.Expect(balken).ToHaveCountAsync(7, new() { Timeout = 60_000 });
        await Assertions.Expect(balken.Locator(".elkw-status")).ToHaveTextAsync(["Im Plan", "Im Plan", "Knapp", "Überschritten", "Im Plan", "Im Plan", "Im Plan"]);
        await Assertions.Expect(balken.Nth(2).Locator(".zl-zahl")).ToHaveTextAsync("3.900,00 € von 4.200,00 € (93 %)");
        await Assertions.Expect(balken.Nth(3).Locator(".zl-zahl")).ToHaveTextAsync("1.950,00 € von 1.800,00 € (108 %)");
        var erstattungen = seite.Locator("#erstattungen .zl-balken");
        await Assertions.Expect(erstattungen.Locator("strong")).ToHaveTextAsync(["Lea Feuerstein", "Jonas Hüttner", "Miriam Zeltner"]);
        await Assertions.Expect(erstattungen.Nth(0).Locator(".zl-zahl")).ToHaveTextAsync("noch 45,80 €");
        await Assertions.Expect(erstattungen.Locator(".elkw-status")).ToHaveTextAsync(["Bekommt noch Geld", "Alles erstattet", "Alles erstattet"]);
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "soll-ist", geraet);
    }

    // ---------- Story 5-3: Beitrag bezahlt abhaken ----------

    /// <summary>
    /// Beiträge: Lea tippt den Reiter „Beiträge“. Der Balken zeigt „1 von 1 bezahlt“ mit „Alle bezahlt“, darunter Mia Probst mit gesetztem Haken und
    /// „Abgehakt von Lea Feuerstein am …“ (das Demo-Datum hängt vom Tag des Einspielens ab; im Bild steht ein festes Datum). Nur lesend.
    /// </summary>
    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Beitraege(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await FinanzenReiterAsync(seite, "Beiträge");
        var balken = seite.Locator("#beitraege .zl-balken");
        await Assertions.Expect(balken.Locator(".zl-zahl")).ToHaveTextAsync("1 von 1 bezahlt", new() { Timeout = 60_000 });
        await Assertions.Expect(balken.Locator(".elkw-status")).ToHaveTextAsync("Alle bezahlt");
        var mia = seite.GetByLabel("Mia Probst", new() { Exact = true });
        await Assertions.Expect(mia).ToBeCheckedAsync();
        await Assertions.Expect(mia).ToBeEnabledAsync();
        await Assertions.Expect(seite.Locator("#beitraege .elkw-hint")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^Abgehakt von Lea Feuerstein am \d{2}\.\d{2}\.\d{4}$"));
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await seite.EvaluateAsync("() => document.querySelectorAll('#beitraege .elkw-hint').forEach(h => { if (h.textContent.startsWith('Abgehakt von ')) h.textContent = h.textContent.replace(/am .*$/, 'am 20.10.2026'); })");
        await BildAsync(seite, "beitraege", geraet);
    }

    /// <summary>Story 3-6: „Abgelegt am …“ hängt vom Tag des Einspielens ab; im Bild steht ein festes Datum.</summary>
    private static Task AbgelegtAmFestAsync(IPage seite) =>
        seite.EvaluateAsync("() => document.querySelectorAll('.elkw-card p').forEach(p => { if (p.textContent.startsWith('Abgelegt am ')) p.textContent = 'Abgelegt am 14.10.2026'; })");

    /// <summary>Der Termin mit diesem Titel innerhalb eines Bereichs (ein Tag am Handy oder eine Spalte am Laptop).</summary>
    private static ILocator TerminIn(ILocator bereich, string titel) =>
        bereich.Locator(".zl-termin").Filter(new() { Has = bereich.Page.GetByRole(AriaRole.Heading, new() { Name = titel, Exact = true }) });

    /// <summary>Jonas (Leitung) meldet sich an, öffnet die Demo-Freizeit und tippt „Programm“.</summary>
    private async Task ProgrammAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Programm", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Programm", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".zl-termin").First).ToBeAttachedAsync(new() { Timeout = 60_000 });
    }

    /// <summary>Zählt die Seiten eines PDF an den Seitenobjekten (<c>/Type /Page</c>, nicht <c>/Pages</c>).</summary>
    private static int PdfSeiten(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![a-zA-Z])").Count;

    /// <summary>Lea (Team) meldet sich an und öffnet die Startseite der Demo-Freizeit.</summary>
    private async Task LeaStartseiteAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, TeamMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Rolle: Team")).ToBeVisibleAsync();
    }

    /// <summary>Lea (Team) meldet sich an, öffnet die Demo-Freizeit und tippt „Sicherheitskette“.</summary>
    private async Task SicherheitsketteAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, TeamMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Sicherheitskette", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Sicherheitskette", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".elkw-kicker")).ToHaveTextAsync("Notfall");
        await Assertions.Expect(seite.GetByLabel("Betroffene Person suchen")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>Lea (Team) meldet sich an und öffnet in der Demo-Freizeit „Dokumente“.</summary>
    private async Task DokumenteAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, TeamMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Dokumente", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Dokumente", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "Packliste" })).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>Lea (Team) meldet sich an und öffnet in der Demo-Freizeit „Freizeitpässe“.</summary>
    private async Task FreizeitpaesseAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, TeamMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Rolle: Team")).ToBeVisibleAsync();
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeitpässe", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Freizeitpässe", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("tbody tr").Filter(new() { HasText = "Mia Probst" })).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }


    /// <summary>
    /// Blendet vor dem Bild die Zeilen der Gast-Anmeldungen aus, die andere Tests desselben Laufs über den Demo-Link anlegen (Familie
    /// „Beispiel“); sie gehören nicht zu den Demo-Daten und ihre Anzahl hängt von der Reihenfolge der Tests ab. Geprüft ist die Seite
    /// vorher vollständig.
    /// </summary>
    private static Task GastZeilenAusblendenAsync(IPage seite) =>
        seite.EvaluateAsync("() => document.querySelectorAll('tbody tr').forEach(z => { if (z.textContent.includes('Beispiel')) z.remove(); })");

    /// <summary>
    /// Miriam öffnet in der Demo-Freizeit „Anmeldungen“: Balken „1 von 40“, Emma und Tobias eingegangen, Mia und Lea bestätigt, mit den
    /// erlaubten Aktionen je Zeile.
    /// </summary>
    private async Task AnmeldungenAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, PruefMail);
        await FreizeitOeffnenAsync(seite, DemoFreizeitName);
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Anmeldungen", Exact = true }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldungen", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".zl-balken .zl-zahl")).ToHaveTextAsync("1 von 40", new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".zl-balken .elkw-status")).ToHaveTextAsync("Belegt");
        foreach (var (name, status, knoepfe) in new[]
                 {
                     ("Emma Muster", "Eingegangen", 2), ("Mia Probst", "Bestätigt", 1), ("Lea Feuerstein", "Bestätigt", 1), ("Tobias Wanderer", "Eingegangen", 2),
                 })
        {
            var zeile = seite.Locator("tbody tr").Filter(new() { HasText = name });
            await Assertions.Expect(zeile.Locator(".elkw-status")).ToHaveTextAsync(status);
            await Assertions.Expect(zeile.GetByRole(AriaRole.Button)).ToHaveCountAsync(knoepfe);
        }
    }

    /// <summary>Öffnet die Freizeitseite über den Anmeldelink für Teilnehmende und tippt „Anmelden“.</summary>
    private async Task GastFormularAsync(IPage seite)
    {
        await FreizeitseiteAsync(seite);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Anmeldung", new() { Timeout = 60_000 });
    }

    /// <summary>Jonas (Leitung) meldet sich an und öffnet in der Demo-Freizeit „Einstellungen“.</summary>
    private async Task EinstellungenAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Einstellungen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Einstellungen", new() { Timeout = 60_000 });
    }

    /// <summary>
    /// Liest den Anmeldelink für Teilnehmende aus Jonas' Einstellungen und öffnet ihn in einem neuen Browser-Kontext ohne Sitzung
    /// (die Freizeitseite braucht keinen Login). Kein Erneuern, damit die Demo-Daten unverändert bleiben.
    /// </summary>
    private async Task FreizeitseiteAsync(IPage seite)
    {
        var leitung = await host.KontextAsync(new BrowserNewContextOptions { Locale = "de-DE", TimezoneId = "Europe/Berlin" });
        try
        {
            var einstellungen = await leitung.NewPageAsync();
            await EinstellungenAsync(einstellungen);
            var link = await einstellungen.GetByRole(AriaRole.Link, new() { Name = "Freizeitseite öffnen" }).First.GetAttributeAsync("href");
            Assert.NotNull(link);
            await seite.GotoAsync(link);
        }
        finally
        {
            await leitung.CloseAsync();
        }

        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
    }

    /// <summary>Jonas meldet sich an und öffnet Konto → „Meine Daten öffnen“.</summary>
    private async Task MeineDatenAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, DemoMail);
        await seite.Locator("header a.zl-konto-link").ClickAsync();
        await seite.GetByRole(AriaRole.Link, new() { Name = "Meine Daten öffnen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zugriffsprotokoll und Meine Daten", new() { Timeout = 60_000 });
    }

    /// <summary>Miriam öffnet in der Demo-Freizeit „Nachweise prüfen“; liefert Jonas' Zeile (freigegeben, mit „Prüfen“).</summary>
    private async Task<ILocator> PruefListeAsync(IPage seite)
    {
        await E2EHilfe.PerLinkAnmeldenAsync(seite, host, PruefMail);
        await FreizeitOeffnenAsync(seite, DemoFreizeitName);
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlager Sommer 2027", new() { Timeout = 60_000 });
        await seite.GetByRole(AriaRole.Link, new() { Name = "Nachweise prüfen" }).ClickAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Nachweise prüfen", new() { Timeout = 60_000 });
        var zeile = seite.Locator("tbody tr").Filter(new() { HasText = "Jonas Hüttner" });
        await Assertions.Expect(zeile).ToContainTextAsync("Freigegeben bis", new() { Timeout = 60_000 });
        await Assertions.Expect(zeile.GetByRole(AriaRole.Button, new() { Name = "Prüfen" })).ToBeVisibleAsync();
        return zeile;
    }

    private static (int Breite, int Hoehe) Groesse(string geraet) => geraet switch
    {
        "handy" => (375, 812),
        "laptop" => (1280, 800),
        _ => throw new ArgumentOutOfRangeException(nameof(geraet), geraet, "Unbekanntes Gerät."),
    };

    private Task<IBrowserContext> KontextAsync(string geraet)
    {
        var (breite, hoehe) = Groesse(geraet);
        return host.KontextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = breite, Height = hoehe },
            DeviceScaleFactor = 1,
            ColorScheme = ColorScheme.Light,
            Locale = "de-DE",
            TimezoneId = "Europe/Berlin",
        });
    }

    /// <summary>Schreibt das Bild des sichtbaren Bereichs und prüft danach PNG-Signatur und Breite aus dem IHDR-Block.</summary>
    private static async Task BildAsync(IPage seite, string name, string geraet)
    {
        var ordner = Path.Combine(VeroeffentlichterHost.Wurzel(), "doku", "src", "assets", "screenshots");
        Directory.CreateDirectory(ordner);
        var pfad = Path.Combine(ordner, $"{name}-{geraet}.png");
        var vorher = DateTime.UtcNow.AddSeconds(-1);

        await seite.ScreenshotAsync(new PageScreenshotOptions { Path = pfad, Type = ScreenshotType.Png, Animations = ScreenshotAnimations.Disabled });

        var bytes = await File.ReadAllBytesAsync(pfad, TestContext.Current.CancellationToken);
        Assert.True(File.GetLastWriteTimeUtc(pfad) >= vorher, $"{pfad} wurde nicht neu geschrieben.");
        Assert.True(bytes.Length > 24 && bytes.AsSpan(0, 8).SequenceEqual(PngSignatur), $"{pfad} ist kein PNG.");
        Assert.Equal(Groesse(geraet).Breite, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
    }
}
