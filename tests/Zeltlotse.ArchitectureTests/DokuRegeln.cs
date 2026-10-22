using System.Text.RegularExpressions;

namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// Regeln für die Anwender-Doku unter <c>doku/</c> (NFR-20, Konvention „Anwender-Doku“): Sprache ohne Anrede wie in der
/// App und die feste Gliederung nach Rolle. Jede Regel liefert die Verstöße als lesbare Sätze; leer heißt: eingehalten.
/// Reiner Repo-Scan, ohne Node.
/// </summary>
internal static partial class DokuRegeln
{
    /// <summary>Die sechs Rollen-Ordner der Gliederung; jeder hat eine Überblicksseite <c>index</c>.</summary>
    public static readonly IReadOnlyList<string> RollenOrdner = ["eltern", "mitarbeitende", "leitung", "kueche", "org-admin", "betreiber"];

    /// <summary>Seiten, die es immer geben muss (ohne Endung, relativ zu <c>doku/src/content/docs</c>).</summary>
    public static IEnumerable<string> PflichtSeiten =>
        RollenOrdner.Select(o => $"{o}/index").Append("daten").Append("index").Append("rollen").Append("erste-schritte/zeltlotse-oeffnen").Append("erste-schritte/anmelden")
            .Append("erste-schritte/meine-daten").Append("org-admin/loeschfristen").Append("eltern/freizeitseite").Append("leitung/freizeitseite-einrichten")
            .Append("eltern/kind-anmelden").Append("mitarbeitende/anmelden").Append("leitung/anmeldungen")
            .Append("eltern/freizeitpass").Append("leitung/freizeitpass-fragen").Append("mitarbeitende/freizeitpaesse").Append("leitung/cockpit")
            .Append("mitarbeitende/dokumente").Append("org-admin/org-dokumente").Append("eltern/elterninfo")
            .Append("mitarbeitende/notfall").Append("leitung/sicherheitskette")
            .Append("mitarbeitende/schutzkonzept").Append("leitung/schutzkonzept")
            .Append("mitarbeitende/vorfall-protokoll").Append("leitung/vorfall-notizen").Append("org-admin/vorfall-archiv")
            .Append("mitarbeitende/notfallordner").Append("mitarbeitende/programm").Append("mitarbeitende/material")
            .Append("org-admin/inventar").Append("mitarbeitende/inventar")
            .Append("mitarbeitende/schaeden-verluste").Append("org-admin/anschaffungswuensche")
            .Append("mitarbeitende/aufgaben").Append("org-admin/aufgaben-vorlagen")
            .Append("mitarbeitende/programmbibliothek").Append("org-admin/programmbibliothek")
            .Append("leitung/module-schalten").Append("leitung/freizeit-abschliessen")
            .Append("kueche/rezepte").Append("kueche/essensplan").Append("kueche/einkaufsliste").Append("kueche/einkauf")
            .Append("kueche/tagesansicht")
            .Append("finanzen/index").Append("finanzen/kalkulation").Append("finanzen/beitragsrechner")
            .Append("finanzen/belege").Append("finanzen/soll-ist").Append("finanzen/beitraege")
            .Append("mitarbeitende/kanaele").Append("leitung/teilnehmer-zugang");

    public static string DokuSeitenOrdner => Path.Combine(Loesung.Wurzel, "doku", "src", "content", "docs");

    /// <summary>Anrede (Du/Sie) als ganzes Wort: „du“, „dich“, „dir“, „dein…“, „euch“, „euer“, „eure…“, „Ihnen“, „Ihr…“.</summary>
    public static IEnumerable<string> AnredeVerstoesse(string datei, string inhalt) =>
        Anrede().Matches(inhalt).Select(treffer => $"{datei}: Anrede „{treffer.Value}“.");

    /// <summary>Fehlende Pflichtseiten in einer Liste relativer Dateipfade (mit <c>/</c>, Endung <c>.md</c> oder <c>.mdx</c>).</summary>
    public static IEnumerable<string> FehlendeSeiten(IEnumerable<string> dateien)
    {
        var vorhanden = dateien
            .Select(d => d.Replace('\\', '/'))
            .Where(d => d.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || d.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
            .Select(d => d[..d.LastIndexOf('.')])
            .ToHashSet(StringComparer.Ordinal);
        return PflichtSeiten.Where(s => !vorhanden.Contains(s)).Select(s => $"Seite {s}.md fehlt.");
    }

    /// <summary>
    /// Screenshot-Links der Seiten gegen die Bilddateien: Jeder Link <c>assets/screenshots/&lt;name&gt;.png</c> braucht eine Datei,
    /// jede PNG-Datei im Bildordner braucht mindestens einen Link.
    /// </summary>
    /// <param name="seiten">Seiten als (relativer Pfad, Inhalt).</param>
    /// <param name="bilder">Dateinamen im Ordner <c>doku/src/assets/screenshots</c>.</param>
    public static IEnumerable<string> ScreenshotVerstoesse(IEnumerable<(string Datei, string Inhalt)> seiten, IEnumerable<string> bilder)
    {
        var vorhanden = bilder.Where(b => b.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.Ordinal);
        var verlinkt = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (datei, inhalt) in seiten)
        {
            foreach (Match link in ScreenshotLink().Matches(inhalt))
            {
                var name = link.Groups[1].Value;
                verlinkt.Add(name);
                if (!vorhanden.Contains(name))
                {
                    yield return $"{datei}: Bild {name} fehlt in doku/src/assets/screenshots.";
                }
            }
        }

        foreach (var bild in vorhanden.Where(b => !verlinkt.Contains(b)).Order(StringComparer.Ordinal))
        {
            yield return $"Bild {bild} wird von keiner Seite verlinkt.";
        }
    }

    /// <summary>
    /// Zuordnung jeder Capability mit Oberfläche (CAP-1 bis CAP-39) zu den Doku-Seiten, die sie erklären (ohne Endung, relativ zu
    /// <c>doku/src/content/docs</c>). CAP-40 ist die Doku selbst und über Pflichtseiten und diese Regeln abgedeckt.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> CapabilitySeiten = new Dictionary<int, IReadOnlyList<string>>
    {
        [1] = ["erste-schritte/anmelden"],
        [2] = ["eltern/familienkonto", "eltern/profil-uebergeben"],
        [3] = ["betreiber/index", "org-admin/rollen-vergeben", "org-admin/freizeit-anlegen", "leitung/freizeit-oeffnen"],
        [4] = ["mitarbeitende/nachweise", "leitung/nachweise-pruefen", "org-admin/nachweis-richtlinie"],
        [5] = ["leitung/nachweise-erinnern"],
        [6] = ["erste-schritte/meine-daten"],
        [7] = ["eltern/freizeitseite", "leitung/freizeitseite-einrichten"],
        [8] = ["eltern/kind-anmelden", "mitarbeitende/anmelden"],
        [9] = ["leitung/anmeldungen"],
        [10] = ["eltern/elterninfo"],
        [11] = ["leitung/freizeitpass-fragen"],
        [12] = ["eltern/freizeitpass"],
        [13] = ["mitarbeitende/freizeitpaesse"],
        [14] = ["leitung/cockpit"],
        [15] = ["mitarbeitende/notfall", "leitung/sicherheitskette"],
        [16] = ["leitung/schutzkonzept", "mitarbeitende/schutzkonzept"],
        [17] = ["mitarbeitende/vorfall-protokoll", "leitung/vorfall-notizen", "org-admin/vorfall-archiv"],
        [18] = ["mitarbeitende/notfallordner"],
        [19] = ["mitarbeitende/programm"],
        [20] = ["mitarbeitende/material"],
        [21] = ["org-admin/inventar", "mitarbeitende/inventar"],
        [22] = ["mitarbeitende/schaeden-verluste", "org-admin/anschaffungswuensche"],
        [23] = ["mitarbeitende/aufgaben", "org-admin/aufgaben-vorlagen"],
        [24] = ["mitarbeitende/programmbibliothek", "org-admin/programmbibliothek"],
        [25] = ["mitarbeitende/dokumente", "org-admin/org-dokumente"],
        [26] = ["leitung/module-schalten"],
        [27] = ["leitung/freizeit-abschliessen"],
        [28] = ["kueche/rezepte"],
        [29] = ["kueche/essensplan", "kueche/einkaufsliste"],
        [30] = ["kueche/einkauf"],
        [31] = ["kueche/tagesansicht"],
        [32] = ["finanzen/kalkulation", "finanzen/beitragsrechner"],
        [33] = ["finanzen/belege", "finanzen/soll-ist"],
        [34] = ["finanzen/beitraege"],
        [35] = ["mitarbeitende/kanaele"],
        [36] = ["mitarbeitende/programm"],
        [37] = ["mitarbeitende/kanaele"],
        [38] = ["leitung/teilnehmer-zugang"],
        [39] = ["mitarbeitende/offline"],
    };

    /// <summary>
    /// Jede Capability 1–39 braucht mindestens eine Seite; jede genannte Seite muss existieren und je einen Screenshot
    /// <c>…-handy.png</c> und <c>…-laptop.png</c> verlinken.
    /// </summary>
    /// <param name="zuordnung">CAP-Nummer → Seiten (ohne Endung).</param>
    /// <param name="seiten">Seiten als (relativer Pfad mit Endung <c>.md</c>/<c>.mdx</c>, Inhalt).</param>
    public static IEnumerable<string> CapabilityVerstoesse(
        IReadOnlyDictionary<int, IReadOnlyList<string>> zuordnung, IEnumerable<(string Datei, string Inhalt)> seiten)
    {
        var inhalte = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (datei, inhalt) in seiten)
        {
            var pfad = datei.Replace('\\', '/');
            var punkt = pfad.LastIndexOf('.');
            if (punkt > 0)
            {
                inhalte[pfad[..punkt]] = inhalt;
            }
        }

        for (var nummer = 1; nummer <= 39; nummer++)
        {
            if (!zuordnung.TryGetValue(nummer, out var namen) || namen.Count == 0)
            {
                yield return $"CAP-{nummer} hat keine Doku-Seite.";
                continue;
            }

            foreach (var name in namen)
            {
                if (!inhalte.TryGetValue(name, out var inhalt))
                {
                    yield return $"CAP-{nummer}: Seite {name}.md fehlt.";
                    continue;
                }

                var bilder = ScreenshotLink().Matches(inhalt).Select(m => m.Groups[1].Value).ToList();
                if (!bilder.Any(b => b.EndsWith("-handy.png", StringComparison.Ordinal)))
                {
                    yield return $"CAP-{nummer}: Seite {name}.md hat keinen Handy-Screenshot.";
                }

                var handy = bilder.Where(b => b.EndsWith("-handy.png", StringComparison.Ordinal)).Select(Basisname).ToHashSet(StringComparer.Ordinal);
                var laptop = bilder.Where(b => b.EndsWith("-laptop.png", StringComparison.Ordinal)).Select(Basisname).ToList();
                if (laptop.Count == 0)
                {
                    yield return $"CAP-{nummer}: Seite {name}.md hat keinen Laptop-Screenshot.";
                }
                else if (handy.Count > 0 && !laptop.Any(handy.Contains))
                {
                    yield return $"CAP-{nummer}: Seite {name}.md hat kein Bildpaar aus Handy und Laptop.";
                }
            }
        }
    }

    /// <summary>
    /// Jedes Bild im Bildordner braucht einen Erzeuger <c>BildAsync(…, "&lt;name&gt;", …)</c> in den E2E-Tests (sonst wäre es
    /// veraltet), und zu jedem Erzeuger müssen <c>&lt;name&gt;-handy.png</c> und <c>&lt;name&gt;-laptop.png</c> vorliegen.
    /// </summary>
    /// <param name="bilder">Dateinamen im Ordner <c>doku/src/assets/screenshots</c>.</param>
    /// <param name="testQuellen">Inhalte der C#-Dateien in <c>tests/Zeltlotse.E2E.Tests</c>.</param>
    public static IEnumerable<string> ErzeugerVerstoesse(IEnumerable<string> bilder, IEnumerable<string> testQuellen)
    {
        var vorhanden = bilder.Where(b => b.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
        var namen = testQuellen.Select(OhneKommentare)
            .SelectMany(q => BildErzeuger().Matches(q).Select(m => m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);

        foreach (var bild in vorhanden.Order(StringComparer.Ordinal).Where(b => !namen.Contains(Basisname(b))))
        {
            yield return $"Bild {bild} wird von keinem Playwright-Test erzeugt.";
        }

        var dateien = vorhanden.ToHashSet(StringComparer.Ordinal);
        foreach (var name in namen.Order(StringComparer.Ordinal))
        {
            foreach (var geraet in (string[])["handy", "laptop"])
            {
                if (!dateien.Contains($"{name}-{geraet}.png"))
                {
                    yield return $"Bild {name}-{geraet}.png fehlt.";
                }
            }
        }
    }

    private static string Basisname(string bild)
    {
        foreach (var endung in (string[])["-handy.png", "-laptop.png"])
        {
            if (bild.EndsWith(endung, StringComparison.Ordinal))
            {
                return bild[..^endung.Length];
            }
        }

        return bild;
    }

    /// <summary>Entfernt <c>//</c>- und <c>/* */</c>-Kommentare aus C#-Quelltext; Zeichenketten (auch mit <c>//</c> darin) bleiben stehen.</summary>
    private static string OhneKommentare(string quelle) =>
        CSharpKommentarOderText().Replace(quelle, m => m.Value.StartsWith('/') ? "" : m.Value);

    [GeneratedRegex(@"@""(?:[^""]|"""")*""|""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'|//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CSharpKommentarOderText();

    /// <summary>
    /// Interne Links der Doku (<c>](/ordner/seite/)</c>, <c>](/ordner/seite/#anker)</c>, <c>](#anker)</c>) müssen auf eine vorhandene Seite
    /// zeigen, und der Anker muss zu einer Überschrift dieser Seite passen. Externe Links und Bilder zählen nicht.
    /// </summary>
    /// <param name="seiten">Seiten als (relativer Pfad mit Endung <c>.md</c>/<c>.mdx</c>, Inhalt).</param>
    public static IEnumerable<string> LinkVerstoesse(IEnumerable<(string Datei, string Inhalt)> seiten)
    {
        var liste = seiten.Select(s => (Datei: s.Datei.Replace('\\', '/'), s.Inhalt)).ToList();
        var nachName = new Dictionary<string, (string Datei, HashSet<string> Anker)>(StringComparer.Ordinal);
        foreach (var (datei, inhalt) in liste)
        {
            var punkt = datei.LastIndexOf('.');
            if (punkt > 0)
            {
                nachName[datei[..punkt]] = (datei, AnkerDerSeite(inhalt));
            }
        }

        foreach (var (datei, inhalt) in liste)
        {
            var eigene = datei[..Math.Max(0, datei.LastIndexOf('.'))];
            foreach (Match link in InternerLink().Matches(OhneCodeBloecke(inhalt)))
            {
                var pfad = link.Groups[1].Success ? link.Groups[1].Value : null;
                var anker = link.Groups[2].Success ? Uri.UnescapeDataString(link.Groups[2].Value) : null;
                var ziel = (pfad ?? "") + (anker is null ? "" : "#" + anker);
                string? name = eigene;
                if (pfad is not null)
                {
                    var kern = pfad.Trim('/');
                    name = kern.Length == 0 ? "index" : nachName.ContainsKey(kern) ? kern : nachName.ContainsKey(kern + "/index") ? kern + "/index" : null;
                }

                if (name is null || !nachName.TryGetValue(name, out var seite))
                {
                    yield return $"{datei}: Link {ziel} zeigt auf keine Seite.";
                    continue;
                }

                if (anker is not null && !seite.Anker.Contains(anker))
                {
                    yield return $"{datei}: Link {ziel} – Anker fehlt auf Seite {seite.Datei}.";
                }
            }
        }
    }

    /// <summary>Anker einer Überschrift wie bei Starlight (github-slugger): klein, Satzzeichen weg, Leerzeichen zu <c>-</c>.</summary>
    public static string Anker(string ueberschrift)
    {
        var text = MarkdownLink().Replace(ueberschrift.Trim(), "$1").ToLowerInvariant();
        return KeinSlugZeichen().Replace(text, "").Replace(' ', '-');
    }

    private static HashSet<string> AnkerDerSeite(string inhalt)
    {
        var anker = new HashSet<string>(StringComparer.Ordinal) { "_top" };
        var zaehler = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match ueberschrift in Ueberschrift().Matches(OhneCodeBloecke(inhalt)))
        {
            var basis = Anker(ueberschrift.Groups[1].Value);
            var n = zaehler.GetValueOrDefault(basis);
            zaehler[basis] = n + 1;
            anker.Add(n == 0 ? basis : $"{basis}-{n}");
        }

        return anker;
    }

    private static string OhneCodeBloecke(string inhalt) => CodeBlock().Replace(inhalt, "");

    [GeneratedRegex(@"^```.*?^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"^#{1,6}[ \t]+(.+?)[ \t]*#*[ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex Ueberschrift();

    [GeneratedRegex(@"(?<!!)\[[^\]\n]*\]\((?=[/#])(/[^)\s#]*)?(?:#([^)\s]+))?\)")]
    private static partial Regex InternerLink();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"[^\p{L}\p{M}\p{N}\p{Pc} \-]")]
    private static partial Regex KeinSlugZeichen();

    /// <summary><c>BildAsync(&lt;Ausdruck&gt;, "&lt;name&gt;"</c>; der Ausdruck darf einfach geklammerte Aufrufe enthalten.</summary>
    [GeneratedRegex(@"\bBildAsync\((?:[^""()]|\([^""()]*\))*?,\s*""([^""]+)""")]
    private static partial Regex BildErzeuger();

    [GeneratedRegex(@"assets/screenshots/([^/\s)""']+\.png)")]
    private static partial Regex ScreenshotLink();

    [GeneratedRegex(@"\b(?:[Dd]u|[Dd]ich|[Dd]ir|[Dd]ein(?:e[mnrs]?)?|[Ee]uch|[Ee]uer|[Ee]ure[mnrs]?|Ihnen|Ihr(?:e[mnrs]?)?)\b")]
    private static partial Regex Anrede();
}
