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
        await Assertions.Expect(seite.GetByRole(AriaRole.Link, new() { Name = "Nachweise prüfen" })).ToBeVisibleAsync();
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await E2EHilfe.AxeAsync(seite);
        await card.ScrollIntoViewIfNeededAsync();

        await BildAsync(seite, "nachweis-richtlinie", geraet);
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
