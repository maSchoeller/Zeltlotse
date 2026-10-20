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
                "Seite kueche/index.md fehlt.", "Seite daten.md fehlt.", "Seite index.md fehlt.", "Seite erste-schritte/zeltlotse-oeffnen.md fehlt.",
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

    private static IEnumerable<string> DokuDateien(string ordner) =>
        Directory.Exists(ordner)
            ? Directory.EnumerateFiles(ordner, "*.*", SearchOption.AllDirectories)
                .Where(d => d.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || d.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
            : [];
}
