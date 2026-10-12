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
        // Story 1-6: Miriam fehlen Pflicht-Nachweise (Führungszeugnis, Erste Hilfe); die Warnung steht über „Nachweise prüfen“.
        await Assertions.Expect(seite.Locator(".elkw-alert--warnung"))
            .ToContainTextAsync("Bei 1 Person im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab.", new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "freizeit-start", geraet);
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
        await Assertions.Expect(emma.Locator(".zl-statusliste .elkw-status")).ToHaveTextAsync("Eingegangen");
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
        await Assertions.Expect(seite.GetByLabel("Ort")).ToHaveValueAsync("Zeltplatz am Mühlbach, Musterhausen", new() { Timeout = 60_000 });
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
        await Assertions.Expect(team.Locator("li")).ToHaveCountAsync(2);
        await Assertions.Expect(team.Locator("li").Filter(new() { HasText = "Jonas" })).ToContainTextAsync("Leitung · aus Musterstadt");
        await Assertions.Expect(team.Locator("li").Filter(new() { HasText = "Miriam" })).ToContainTextAsync("Leitung · aus Musterstadt");
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Vertrauensseite" })).ToBeVisibleAsync();
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
        await Assertions.Expect(seite.GetByText("Gast-Link gilt bis 12.09.2027.")).ToBeVisibleAsync();
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Konto erstellen" })).ToBeVisibleAsync();
        await E2EHilfe.AxeAsync(seite);

        await BildAsync(seite, "gast-link", geraet);
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
        await seite.GetByRole(AriaRole.Link, new() { Name = "Freizeit öffnen" }).ClickAsync(new() { Timeout = 60_000 });
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
