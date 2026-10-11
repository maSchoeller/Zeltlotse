using System.Text.RegularExpressions;

namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// Regeln für die Anwender-Doku unter <c>doku/</c> (NFR-20, Konvention „Anwender-Doku“): Sprache ohne Anrede wie in der
/// App und die feste Gliederung nach Rolle. Jede Regel liefert die Verstöße als lesbare Sätze; leer heißt: eingehalten.
/// Reiner Repo-Scan, ohne Node.
/// </summary>
internal static partial class DokuRegeln
{
    /// <summary>Die fünf Rollen-Ordner der Gliederung; jeder hat eine Überblicksseite <c>index</c>.</summary>
    public static readonly IReadOnlyList<string> RollenOrdner = ["eltern", "mitarbeitende", "leitung", "kueche", "org-admin"];

    /// <summary>Seiten, die es immer geben muss (ohne Endung, relativ zu <c>doku/src/content/docs</c>).</summary>
    public static IEnumerable<string> PflichtSeiten =>
        RollenOrdner.Select(o => $"{o}/index").Append("daten").Append("index").Append("erste-schritte/zeltlotse-oeffnen").Append("erste-schritte/anmelden");

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

    [GeneratedRegex(@"assets/screenshots/([^/\s)""']+\.png)")]
    private static partial Regex ScreenshotLink();

    [GeneratedRegex(@"\b(?:[Dd]u|[Dd]ich|[Dd]ir|[Dd]ein(?:e[mnrs]?)?|[Ee]uch|[Ee]uer|[Ee]ure[mnrs]?|Ihnen|Ihr(?:e[mnrs]?)?)\b")]
    private static partial Regex Anrede();
}
