namespace Zeltlotse.ArchitectureTests;

/// <summary>Regeln der Anwender-Doku (NFR-20): keine Anrede, Rollen-Ordner mit Überblicksseite, Seite „daten“.</summary>
public sealed class DokuTests
{
    [Fact]
    public void Doku_Seiten_enthalten_keine_Anrede()
    {
        var ordner = DokuRegeln.DokuSeitenOrdner;
        var dateien = DokuDateien(ordner).ToList();

        var verstoesse = dateien.SelectMany(d => DokuRegeln.AnredeVerstoesse(Path.GetRelativePath(ordner, d), File.ReadAllText(d)));

        Assert.NotEmpty(dateien);
        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Doku_hat_die_fuenf_Rollen_Ordner_mit_index_und_die_Seite_daten()
    {
        var ordner = DokuRegeln.DokuSeitenOrdner;

        var fehlend = DokuRegeln.FehlendeSeiten(DokuDateien(ordner).Select(d => Path.GetRelativePath(ordner, d)));

        Assert.Empty(fehlend);
    }

    [Theory]
    [InlineData("Melde dich an.", "dich")]
    [InlineData("Das zeigt dir die App.", "dir")]
    [InlineData("Du findest hier alles.", "Du")]
    [InlineData("Deine Kinder anmelden.", "Deine")]
    [InlineData("Wie geht es euch?", "euch")]
    [InlineData("Eure Daten.", "Eure")]
    [InlineData("Wir zeigen Ihnen die App.", "Ihnen")]
    [InlineData("Ihre Anmeldung ist eingegangen.", "Ihre")]
    [InlineData("Ihr Konto.", "Ihr")]
    public void Negativ_Anrede_wird_mit_Datei_und_Wort_erkannt(string text, string wort)
    {
        var verstoesse = DokuRegeln.AnredeVerstoesse("eltern/index.md", text).ToList();

        Assert.Equal([$"eltern/index.md: Anrede „{wort}“."], verstoesse);
    }

    [Theory]
    [InlineData("Eltern melden ihre Kinder an.")]
    [InlineData("Durch die Freigabe sieht die Leitung den Freizeitpass.")]
    [InlineData("Die App deinstallieren oder dicht machen.")]
    [InlineData("Dubai, Ihringen und Dichtung sind keine Anrede.")]
    public void Negativ_Woerter_die_nur_aehnlich_sind_gelten_nicht_als_Anrede(string text)
    {
        Assert.Empty(DokuRegeln.AnredeVerstoesse("x.md", text));
    }

    [Fact]
    public void Negativ_fehlende_Pflichtseiten_werden_erkannt_und_Endungen_ohne_Gross_Klein()
    {
        string[] dateien = ["eltern/index.MD", "mitarbeitende/index.mdx", "leitung/index.md", "kueche/uebersicht.md", @"org-admin\index.md", "betreiber/index.md"];

        var fehlend = DokuRegeln.FehlendeSeiten(dateien).ToList();

        Assert.Equal(
            [
                "Seite kueche/index.md fehlt.", "Seite daten.md fehlt.", "Seite index.md fehlt.", "Seite rollen.md fehlt.", "Seite erste-schritte/zeltlotse-oeffnen.md fehlt.",
                "Seite erste-schritte/anmelden.md fehlt.", "Seite erste-schritte/meine-daten.md fehlt.", "Seite org-admin/loeschfristen.md fehlt.",
                "Seite eltern/freizeitseite.md fehlt.", "Seite leitung/freizeitseite-einrichten.md fehlt.", "Seite eltern/kind-anmelden.md fehlt.",
                "Seite mitarbeitende/anmelden.md fehlt.", "Seite leitung/anmeldungen.md fehlt.", "Seite eltern/freizeitpass.md fehlt.",
                "Seite leitung/freizeitpass-fragen.md fehlt.", "Seite mitarbeitende/freizeitpaesse.md fehlt.", "Seite leitung/cockpit.md fehlt.",
                "Seite mitarbeitende/dokumente.md fehlt.", "Seite org-admin/org-dokumente.md fehlt.", "Seite eltern/elterninfo.md fehlt.",
                "Seite mitarbeitende/notfall.md fehlt.", "Seite leitung/sicherheitskette.md fehlt.",
                "Seite mitarbeitende/schutzkonzept.md fehlt.", "Seite leitung/schutzkonzept.md fehlt.",
                "Seite mitarbeitende/vorfall-protokoll.md fehlt.", "Seite leitung/vorfall-notizen.md fehlt.", "Seite org-admin/vorfall-archiv.md fehlt.",
                "Seite mitarbeitende/notfallordner.md fehlt.", "Seite mitarbeitende/programm.md fehlt.", "Seite mitarbeitende/material.md fehlt.",
                "Seite org-admin/inventar.md fehlt.", "Seite mitarbeitende/inventar.md fehlt.",
                "Seite mitarbeitende/schaeden-verluste.md fehlt.", "Seite org-admin/anschaffungswuensche.md fehlt.",
                "Seite mitarbeitende/aufgaben.md fehlt.", "Seite org-admin/aufgaben-vorlagen.md fehlt.",
                "Seite mitarbeitende/programmbibliothek.md fehlt.", "Seite org-admin/programmbibliothek.md fehlt.", "Seite leitung/module-schalten.md fehlt.",
                "Seite leitung/freizeit-abschliessen.md fehlt.",
                "Seite kueche/rezepte.md fehlt.", "Seite kueche/essensplan.md fehlt.", "Seite kueche/einkaufsliste.md fehlt.", "Seite kueche/einkauf.md fehlt.",
                "Seite kueche/tagesansicht.md fehlt.",
                "Seite finanzen/index.md fehlt.", "Seite finanzen/kalkulation.md fehlt.", "Seite finanzen/beitragsrechner.md fehlt.", "Seite finanzen/belege.md fehlt.", "Seite finanzen/soll-ist.md fehlt.",
                "Seite finanzen/beitraege.md fehlt.",
                "Seite mitarbeitende/kanaele.md fehlt.", "Seite leitung/teilnehmer-zugang.md fehlt.",
            ],
            fehlend);
    }

    [Fact]
    public void Screenshot_Links_der_Doku_haben_eine_Datei_und_jedes_Bild_ist_verlinkt()
    {
        var ordner = DokuRegeln.DokuSeitenOrdner;
        var bildOrdner = Path.Combine(Loesung.Wurzel, "doku", "src", "assets", "screenshots");
        var seiten = DokuDateien(ordner).Select(d => (Path.GetRelativePath(ordner, d), File.ReadAllText(d))).ToList();
        var bilder = Directory.Exists(bildOrdner) ? Directory.EnumerateFiles(bildOrdner).Select(Path.GetFileName).OfType<string>().ToList() : [];

        var verstoesse = DokuRegeln.ScreenshotVerstoesse(seiten, bilder);

        Assert.NotEmpty(bilder);
        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Negativ_Link_ohne_Bild_und_Bild_ohne_Link_werden_erkannt()
    {
        (string, string)[] seiten =
        [
            ("a.md", "![Start](../../assets/screenshots/app-start-handy.png)\n![Alt](../../assets/screenshots/umbenannt-handy.png)"),
            ("b.mdx", "<img src=\"../assets/screenshots/app-start-handy.png\" />"),
        ];
        string[] bilder = ["app-start-handy.png", "veraltet-laptop.png", "notiz.txt"];

        var verstoesse = DokuRegeln.ScreenshotVerstoesse(seiten, bilder).ToList();

        Assert.Equal(
            ["a.md: Bild umbenannt-handy.png fehlt in doku/src/assets/screenshots.", "Bild veraltet-laptop.png wird von keiner Seite verlinkt."],
            verstoesse);
    }

    [Fact]
    public void Jede_Capability_mit_Oberflaeche_hat_eine_Doku_Seite_mit_Handy_und_Laptop_Screenshot()
    {
        var ordner = DokuRegeln.DokuSeitenOrdner;
        var seiten = DokuDateien(ordner).Select(d => (Path.GetRelativePath(ordner, d), File.ReadAllText(d))).ToList();

        var verstoesse = DokuRegeln.CapabilityVerstoesse(DokuRegeln.CapabilitySeiten, seiten);

        Assert.NotEmpty(seiten);
        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Jedes_Doku_Bild_wird_von_einem_Playwright_Test_erzeugt()
    {
        var bildOrdner = Path.Combine(Loesung.Wurzel, "doku", "src", "assets", "screenshots");
        var testOrdner = Path.Combine(Loesung.Wurzel, "tests", "Zeltlotse.E2E.Tests");
        var bilder = Directory.Exists(bildOrdner) ? Directory.EnumerateFiles(bildOrdner).Select(Path.GetFileName).OfType<string>().ToList() : [];
        var quellen = Directory.Exists(testOrdner) ? Directory.EnumerateFiles(testOrdner, "*.cs").Select(File.ReadAllText).ToList() : [];

        var verstoesse = DokuRegeln.ErzeugerVerstoesse(bilder, quellen);

        Assert.NotEmpty(bilder);
        Assert.NotEmpty(quellen);
        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Die_Zuordnung_nennt_genau_CAP_1_bis_39()
    {
        Assert.Equal(Enumerable.Range(1, 39), DokuRegeln.CapabilitySeiten.Keys.Order());
    }

    private static readonly (string, string)[] VollstaendigeSeiten =
    [
        ("eltern/a.md", "![H](../../assets/screenshots/a-handy.png)\n![L](../../assets/screenshots/a-laptop.png)"),
        (@"leitung\b.mdx", "<img src=\"../../assets/screenshots/b-handy.png\" /> <img src=\"../../assets/screenshots/b-laptop.png\" />"),
    ];

    private static Dictionary<int, IReadOnlyList<string>> VollstaendigeZuordnung() =>
        Enumerable.Range(1, 39).ToDictionary(n => n, n => (IReadOnlyList<string>)(n % 2 == 0 ? ["eltern/a"] : ["eltern/a", "leitung/b"]));

    [Fact]
    public void Negativ_vollstaendige_Zuordnung_liefert_keine_Verstoesse()
    {
        Assert.Empty(DokuRegeln.CapabilityVerstoesse(VollstaendigeZuordnung(), VollstaendigeSeiten));
    }

    [Fact]
    public void Negativ_Capability_ohne_Seite_wird_erkannt()
    {
        var zuordnung = VollstaendigeZuordnung();
        zuordnung.Remove(7);
        zuordnung[12] = [];

        var verstoesse = DokuRegeln.CapabilityVerstoesse(zuordnung, VollstaendigeSeiten).ToList();

        Assert.Equal(["CAP-7 hat keine Doku-Seite.", "CAP-12 hat keine Doku-Seite."], verstoesse);
    }

    [Fact]
    public void Negativ_fehlende_Seite_der_Zuordnung_wird_erkannt()
    {
        var zuordnung = VollstaendigeZuordnung();
        zuordnung[3] = ["eltern/a", "leitung/gibt-es-nicht"];

        var verstoesse = DokuRegeln.CapabilityVerstoesse(zuordnung, VollstaendigeSeiten).ToList();

        Assert.Equal(["CAP-3: Seite leitung/gibt-es-nicht.md fehlt."], verstoesse);
    }

    [Fact]
    public void Negativ_Seite_ohne_Handy_oder_Laptop_Screenshot_wird_erkannt()
    {
        (string, string)[] seiten =
        [
            ("eltern/a.md", "![L](../../assets/screenshots/a-laptop.png)"),
            ("leitung/b.md", "![H](../../assets/screenshots/b-handy.png) Text ohne Bild für den Laptop."),
            ("leitung/c.md", "![H](../../assets/screenshots/c-handy.png)\n![L](../../assets/screenshots/c-laptop.png)"),
        ];
        var zuordnung = Enumerable.Range(1, 39).ToDictionary(n => n, n => (IReadOnlyList<string>)(n == 5 ? ["eltern/a", "leitung/b"] : ["leitung/c"]));

        var verstoesse = DokuRegeln.CapabilityVerstoesse(zuordnung, seiten).ToList();

        Assert.Equal(["CAP-5: Seite eltern/a.md hat keinen Handy-Screenshot.", "CAP-5: Seite leitung/b.md hat keinen Laptop-Screenshot."], verstoesse);
    }

    [Fact]
    public void Negativ_Bild_ohne_Erzeuger_und_Erzeuger_ohne_Bildpaar_werden_erkannt()
    {
        string[] bilder = ["app-start-handy.png", "app-start-laptop.png", "foo-handy.png", "bar-handy.png", "notiz.txt"];
        string[] quellen =
        [
            "await BildAsync(seite, \"app-start\", geraet);\n        await BildAsync(neueSeite, \"bar\", geraet);",
            "// keine Bilder hier",
        ];

        var verstoesse = DokuRegeln.ErzeugerVerstoesse(bilder, quellen).ToList();

        Assert.Equal(["Bild foo-handy.png wird von keinem Playwright-Test erzeugt.", "Bild bar-laptop.png fehlt."], verstoesse);
    }

    [Fact]
    public void Negativ_Bilder_mit_Erzeuger_und_Paar_liefern_keine_Verstoesse()
    {
        string[] bilder = ["a-handy.png", "a-laptop.png"];

        Assert.Empty(DokuRegeln.ErzeugerVerstoesse(bilder, ["await BildAsync(await NeueSeiteAsync(geraet), \"a\", geraet);"]));
    }

    [Fact]
    public void Negativ_auskommentierter_Erzeuger_zaehlt_nicht()
    {
        string[] bilder = ["alt-handy.png", "alt-laptop.png", "block-handy.png", "block-laptop.png", "url-handy.png", "url-laptop.png"];
        string[] quellen =
        [
            "// await BildAsync(seite, \"alt\", geraet);\n/* await BildAsync(seite, \"block\", geraet); */\n"
            + "await seite.GotoAsync(\"http://localhost/\"); await BildAsync(seite, \"url\", geraet);",
        ];

        var verstoesse = DokuRegeln.ErzeugerVerstoesse(bilder, quellen).ToList();

        Assert.Equal(
            [
                "Bild alt-handy.png wird von keinem Playwright-Test erzeugt.", "Bild alt-laptop.png wird von keinem Playwright-Test erzeugt.",
                "Bild block-handy.png wird von keinem Playwright-Test erzeugt.", "Bild block-laptop.png wird von keinem Playwright-Test erzeugt.",
            ],
            verstoesse);
    }

    [Fact]
    public void Negativ_Seite_mit_Handy_und_Laptop_Bild_ohne_gemeinsames_Paar_wird_erkannt()
    {
        (string, string)[] seiten =
        [
            ("eltern/a.md", "![H](../../assets/screenshots/a-handy.png)\n![L](../../assets/screenshots/b-laptop.png)"),
            ("leitung/c.md", "![H](../../assets/screenshots/c-handy.png)\n![L](../../assets/screenshots/c-laptop.png)"),
        ];
        var zuordnung = Enumerable.Range(1, 39).ToDictionary(n => n, n => (IReadOnlyList<string>)(n == 9 ? ["eltern/a"] : ["leitung/c"]));

        var verstoesse = DokuRegeln.CapabilityVerstoesse(zuordnung, seiten).ToList();

        Assert.Equal(["CAP-9: Seite eltern/a.md hat kein Bildpaar aus Handy und Laptop."], verstoesse);
    }

    [Fact]
    public void Interne_Links_der_Doku_zeigen_auf_vorhandene_Seiten_und_Anker()
    {
        var ordner = DokuRegeln.DokuSeitenOrdner;
        var seiten = DokuDateien(ordner).Select(d => (Path.GetRelativePath(ordner, d), File.ReadAllText(d))).ToList();

        var verstoesse = DokuRegeln.LinkVerstoesse(seiten);

        Assert.NotEmpty(seiten);
        Assert.Empty(verstoesse);
    }

    [Theory]
    [InlineData("Freigabe: wer den Freizeitpass sieht", "freigabe-wer-den-freizeitpass-sieht")]
    [InlineData("Org-Überblick", "org-überblick")]
    [InlineData("Erstattet – auch nach dem Abschluss", "erstattet--auch-nach-dem-abschluss")]
    [InlineData("„Stimmt das noch?“", "stimmt-das-noch")]
    [InlineData("Profil mit 18 übergeben", "profil-mit-18-übergeben")]
    public void Ueberschrift_wird_wie_in_Starlight_zum_Anker(string ueberschrift, string anker)
    {
        Assert.Equal(anker, DokuRegeln.Anker(ueberschrift));
    }

    [Fact]
    public void Negativ_Links_auf_fehlende_Seiten_und_Anker_werden_erkannt()
    {
        (string, string)[] seiten =
        [
            ("index.md", "---\ntitle: Start\n---\n\n## Erste Schritte\n\n[Rollen](/rollen/) [Eltern](/eltern/#ohne-konto) [Weg](/gibt/es-nicht/) [Oben](#erste-schritte) [Fehlt](#nirgends)"),
            (@"eltern\index.md", "## Ohne Konto\n\n## Ohne Konto\n\n[Start](/) [Zwei](/eltern/#ohne-konto-1) [Extern](https://example.org/x/) ![Bild](../../assets/screenshots/a-handy.png)\n\n```\n## Kein Anker im Code\n```\n[Code](/eltern/#kein-anker-im-code)"),
            ("rollen.mdx", "Text [Eltern](/eltern/) [Kaputt](/rollen/#weg)"),
        ];

        var verstoesse = DokuRegeln.LinkVerstoesse(seiten).ToList();

        Assert.Equal(
            [
                "index.md: Link /gibt/es-nicht/ zeigt auf keine Seite.", "index.md: Link #nirgends – Anker fehlt auf Seite index.md.",
                "eltern/index.md: Link /eltern/#kein-anker-im-code – Anker fehlt auf Seite eltern/index.md.",
                "rollen.mdx: Link /rollen/#weg – Anker fehlt auf Seite rollen.mdx.",
            ],
            verstoesse);
    }

    private static IEnumerable<string> DokuDateien(string ordner) =>
        Directory.Exists(ordner)
            ? Directory.EnumerateFiles(ordner, "*.*", SearchOption.AllDirectories)
                .Where(d => d.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || d.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
            : [];
}
