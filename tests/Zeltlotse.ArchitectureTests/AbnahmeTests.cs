namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// Abnahmebericht der MVP (Story 7-2): je CAP-1 bis CAP-40 Erfolgskriterium wörtlich aus SPEC.md, gültiges Urteil, vorhandene
/// Belege, dazu die Stories mit Status blocked.
/// </summary>
public sealed class AbnahmeTests
{
    [Fact]
    public void Abnahmebericht_belegt_jede_Capability_mit_Kriterium_Urteil_und_vorhandenen_Belegen()
    {
        var kriterien = AbnahmeRegeln.Erfolgskriterien(File.ReadAllText(AbnahmeRegeln.SpecPfad));
        var blocked = Directory.EnumerateFiles(AbnahmeRegeln.StoryOrdner, "*.md")
            .Select(d => AbnahmeRegeln.StoryStatus(Path.GetFileName(d), File.ReadAllText(d)))
            .OfType<(string Id, string Status)>()
            .Where(s => s.Status == "blocked")
            .Select(s => s.Id)
            .ToList();

        Assert.Equal(Enumerable.Range(1, 40), kriterien.Keys);
        Assert.True(File.Exists(AbnahmeRegeln.BerichtPfad), "Abnahmebericht fehlt: _bmad-output/specs/spec-zeltlotse/abnahmebericht.md");

        var verstoesse = AbnahmeRegeln.BerichtVerstoesse(
            File.ReadAllText(AbnahmeRegeln.BerichtPfad), kriterien, Existiert, Enthaelt, blocked).ToList();

        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Erfolgskriterien_werden_je_CAP_aus_der_success_Zeile_gelesen()
    {
        const string spec = "### V1\n\n- **CAP-1**\n  - **intent:** Absicht eins.\n  - **success:** Erfolg eins.\n- **CAP-12**\r\n  - **intent:** x\r\n  - **success:** Erfolg „zwölf“ (NFR-17).\r\n";

        var kriterien = AbnahmeRegeln.Erfolgskriterien(spec);

        Assert.Equal(new Dictionary<int, string> { [1] = "Erfolg eins.", [12] = "Erfolg „zwölf“ (NFR-17)." }, kriterien);
    }

    [Theory]
    [InlineData("2-2-anmeldung.md", "---\ntitle: x\nstatus: 'blocked'\n---\nstatus: done", "2-2", "blocked")]
    [InlineData("10-1-x.md", "---\nstatus: done\n---", "10-1", "done")]
    public void Story_Status_wird_aus_Dateiname_und_Frontmatter_gelesen(string datei, string inhalt, string id, string status)
    {
        Assert.Equal((id, status), AbnahmeRegeln.StoryStatus(datei, inhalt));
    }

    private static readonly Dictionary<int, string> Kriterien = new() { [1] = "Kriterium eins.", [2] = "Kriterium zwei." };

    private static readonly HashSet<string> Dateien = ["tests/A.cs", "doku/a.md", "doku/a-handy.png"];

    private static bool TestExistiert(string pfad) => Dateien.Contains(pfad);

    private static readonly Dictionary<string, string> Inhalte = new()
    {
        ["tests/A.cs"] = "public void Zeigt_etwas()\n{\n}",
        ["doku/a.md"] = "Aufruf: Nur_aufgerufen(); // Nur_kommentiert(",
    };

    private static bool TestEnthaelt(string pfad, string text) => Inhalte.TryGetValue(pfad, out var inhalt) && inhalt.Contains(text, StringComparison.Ordinal);

    private static string Abschnitt(int nummer, string kriterium = "", string urteil = "erfüllt", string belege = "- Test: `tests/A.cs::Zeigt_etwas` – zeigt etwas", string zusatz = "") =>
        $"## CAP-{nummer} – Titel\n\n**Erfolgskriterium:** {(kriterium.Length > 0 ? kriterium : Kriterien[nummer])}\n\n**Urteil:** {urteil}\n\n**Belege:**\n{belege}\n{zusatz}\n";

    private static string Bericht(string cap1, string? cap2 = null, string blocked = "## Stories mit Status blocked\n\nKeine.\n") =>
        $"# Abnahmebericht\n\n| CAP | Urteil |\n|---|---|\n| CAP-1 | erfüllt |\n\n{cap1}\n{cap2 ?? Abschnitt(2)}\n{blocked}\n## Offene Punkte aus den Stories\n\n- `2-2` – etwas.\n";

    private static List<string> Pruefe(string bericht, params string[] blocked) =>
        AbnahmeRegeln.BerichtVerstoesse(bericht, Kriterien, TestExistiert, TestEnthaelt, blocked).ToList();

    [Fact]
    public void Negativ_vollstaendiger_Bericht_liefert_keine_Verstoesse()
    {
        var cap1 = Abschnitt(1, belege: "- Test: `tests/A.cs::Zeigt_etwas` – zeigt etwas\n- Screenshot: `doku/a-handy.png`\n- Doku: `doku/a.md`");
        var cap2 = Abschnitt(2, urteil: "teilweise", zusatz: "\n**Begründung:** Die Zeitangabe ist nur von Hand prüfbar.");

        Assert.Empty(Pruefe(Bericht(cap1, cap2)));
    }

    [Fact]
    public void Negativ_fehlende_Capability_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1), "## Sonstiges\n\nCAP-2 wird hier nur erwähnt.\n");

        Assert.Equal(["CAP-2 fehlt im Abnahmebericht."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_Capability_die_nicht_in_der_SPEC_steht_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1) + "\n## CAP-41 – Zuviel\n\n**Urteil:** erfüllt\n");

        Assert.Equal(["CAP-41 steht nicht in SPEC.md."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_abweichendes_Erfolgskriterium_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1, kriterium: "Kriterium eins, umformuliert."));

        Assert.Equal(["CAP-1: Erfolgskriterium weicht von SPEC.md ab."], Pruefe(bericht));
    }

    [Theory]
    [InlineData("")]
    [InlineData("gut")]
    [InlineData("Erfüllt")]
    public void Negativ_fehlendes_oder_ungueltiges_Urteil_wird_erkannt(string urteil)
    {
        var bericht = Bericht(Abschnitt(1, urteil: urteil));

        Assert.Equal(["CAP-1: Urteil fehlt oder ist ungültig."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_Abschnitt_ohne_Test_Beleg_wird_erkannt_ausser_bei_offen()
    {
        var ohneTest = Bericht(Abschnitt(1, belege: "- Doku: `doku/a.md`"));
        var offen = Bericht(Abschnitt(1, urteil: "offen", belege: "- Doku: `doku/a.md`", zusatz: "\n**Begründung:** Für den Kern gibt es keinen Test."));

        Assert.Equal(["CAP-1: kein Test-Beleg."], Pruefe(ohneTest));
        Assert.Empty(Pruefe(offen));
    }

    [Theory]
    [InlineData("teilweise", "")]
    [InlineData("offen", "")]
    [InlineData("teilweise", "\n**Begründung:**   ")]
    public void Negativ_teilweise_oder_offen_ohne_Begruendung_wird_erkannt(string urteil, string zusatz)
    {
        var bericht = Bericht(Abschnitt(1, urteil: urteil, zusatz: zusatz));

        Assert.Equal([$"CAP-1: Urteil {urteil} ohne Begründung."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_fehlende_Beleg_Datei_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1, belege: "- Test: `tests/A.cs::Zeigt_etwas` – ok\n- Doku: `pfad/x.md`\n- Test: `tests/Weg.cs::Zeigt_etwas`"));

        Assert.Equal(["CAP-1: Beleg pfad/x.md fehlt.", "CAP-1: Beleg tests/Weg.cs fehlt."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_fehlende_Testmethode_wird_mit_CAP_und_Methode_erkannt()
    {
        var bericht = Bericht(Abschnitt(1, belege: "- Test: `tests/A.cs::Umbenannt` – zeigt nichts mehr"));

        Assert.Equal(["CAP-1: Test Umbenannt nicht in tests/A.cs."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_nur_ein_Aufruf_oder_Kommentar_gilt_nicht_als_Testmethode()
    {
        var bericht = Bericht(Abschnitt(1, belege: "- Test: `tests/A.cs::Zeigt_etwas` – ok\n- Test: `doku/a.md::Nur_aufgerufen` – nur Aufruf\n- Test: `doku/a.md::Nur_kommentiert` – nur Kommentar\n- Test: `tests/A.cs::etwas` – nur Namensende"));

        Assert.Equal(
            ["CAP-1: Test Nur_aufgerufen nicht in doku/a.md.", "CAP-1: Test Nur_kommentiert nicht in doku/a.md.", "CAP-1: Test etwas nicht in tests/A.cs."],
            Pruefe(bericht));
    }

    [Fact]
    public void Negativ_Test_Zeile_ohne_Methode_ist_kein_Test_Beleg()
    {
        var bericht = Bericht(Abschnitt(1, belege: "- Test: `tests/A.cs` – nur die Datei"));

        Assert.Equal(["CAP-1: kein Test-Beleg."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_doppelter_CAP_Abschnitt_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1) + "\n" + Abschnitt(1, urteil: "gut"));

        Assert.Equal(["CAP-1 steht mehrfach im Abnahmebericht."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_Backticks_ausserhalb_der_Belege_werden_nicht_geprueft()
    {
        var bericht = Bericht(Abschnitt(1, zusatz: "\nText mit `gibt/es/nicht.md` außerhalb der Belege."));

        Assert.Empty(Pruefe(bericht));
    }

    [Fact]
    public void Negativ_nicht_berichtete_blocked_Story_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1), blocked: "## Stories mit Status blocked\n\n- `3-4` – wartet auf Entscheidung.\n");

        Assert.Equal(["Story 2-2: Status blocked nicht berichtet."], Pruefe(bericht, "2-2", "3-4"));
    }

    [Fact]
    public void Negativ_genannte_Story_die_nicht_blocked_ist_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1), blocked: "## Stories mit Status blocked\n\n- `3-4` – war einmal blockiert.\n");

        Assert.Equal(["Story 3-4 ist nicht blocked.", "Abschnitt „Stories mit Status blocked“: ohne blocked-Story fehlt „Keine.“."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_ohne_blocked_Story_fehlt_Keine()
    {
        var bericht = Bericht(Abschnitt(1), blocked: "## Stories mit Status blocked\n\nNichts.\n");

        Assert.Equal(["Abschnitt „Stories mit Status blocked“: ohne blocked-Story fehlt „Keine.“."], Pruefe(bericht));
    }

    [Fact]
    public void Negativ_fehlender_blocked_Abschnitt_wird_erkannt()
    {
        var bericht = Bericht(Abschnitt(1), blocked: "");

        Assert.Equal(["Abschnitt „Stories mit Status blocked“ fehlt."], Pruefe(bericht));
    }

    private static bool Existiert(string pfad) => !Path.IsPathRooted(pfad) && File.Exists(Path.Combine(Loesung.Wurzel, pfad));

    private static bool Enthaelt(string pfad, string text) =>
        File.Exists(Path.Combine(Loesung.Wurzel, pfad)) && File.ReadAllText(Path.Combine(Loesung.Wurzel, pfad)).Contains(text, StringComparison.Ordinal);
}
