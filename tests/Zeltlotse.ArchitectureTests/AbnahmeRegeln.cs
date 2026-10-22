using System.Text.RegularExpressions;

namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// Regeln für den Abnahmebericht der MVP (<c>_bmad-output/specs/spec-zeltlotse/abnahmebericht.md</c>): je Capability das
/// Erfolgskriterium wörtlich aus <c>SPEC.md</c>, ein Urteil, vorhandene Belege und die Stories mit Status <c>blocked</c>.
/// Reine Funktionen; Dateizugriff nur über übergebene Funktionen. Jede Regel liefert Verstöße als lesbare Sätze.
/// </summary>
internal static partial class AbnahmeRegeln
{
    public static string BerichtPfad => Path.Combine(Loesung.Wurzel, "_bmad-output", "specs", "spec-zeltlotse", "abnahmebericht.md");

    public static string SpecPfad => Path.Combine(Loesung.Wurzel, "_bmad-output", "specs", "spec-zeltlotse", "SPEC.md");

    public static string StoryOrdner => Path.Combine(Loesung.Wurzel, "_bmad-output", "specs", "spec-zeltlotse", "stories");

    private static readonly string[] Urteile = ["erfüllt", "teilweise", "offen"];

    private const string BlockedUeberschrift = "Stories mit Status blocked";

    /// <summary>Liest je <c>- **CAP-n**</c> die folgende Zeile <c>  - **success:** …</c> aus der SPEC.</summary>
    public static IReadOnlyDictionary<int, string> Erfolgskriterien(string specInhalt)
    {
        var kriterien = new SortedDictionary<int, string>();
        int? aktuell = null;
        foreach (var zeile in Zeilen(specInhalt))
        {
            var cap = CapZeile().Match(zeile);
            if (cap.Success)
            {
                aktuell = int.Parse(cap.Groups[1].Value);
                continue;
            }

            var erfolg = SuccessZeile().Match(zeile);
            if (erfolg.Success && aktuell is { } nummer)
            {
                kriterien[nummer] = erfolg.Groups[1].Value.Trim();
                aktuell = null;
            }
        }

        return kriterien;
    }

    /// <summary>Story-Kennung (z. B. <c>2-2</c>) aus dem Dateinamen und Status aus der Zeile <c>status:</c> der Frontmatter.</summary>
    public static (string Id, string Status)? StoryStatus(string dateiname, string inhalt)
    {
        var id = StoryId().Match(dateiname);
        var status = StatusZeile().Match(inhalt);
        return id.Success && status.Success ? (id.Groups[1].Value, status.Groups[1].Value) : null;
    }

    /// <summary>Prüft den Bericht gegen die Erfolgskriterien, die Belege im Repo und die Stories mit Status <c>blocked</c>.</summary>
    /// <param name="bericht">Inhalt des Abnahmeberichts.</param>
    /// <param name="kriterien">CAP-Nummer → success-Text aus <c>SPEC.md</c>.</param>
    /// <param name="existiert">Gibt es den Pfad (relativ zur Lösungswurzel, mit <c>/</c>)?</param>
    /// <param name="enthaelt">Enthält die Datei (Pfad wie oben) den Text? Eine Testmethode gilt als vorhanden, wenn die Datei <c>void Methode(</c> oder <c>Task Methode(</c> enthält.</param>
    /// <param name="blockedStories">Kennungen der Stories mit Status <c>blocked</c>.</param>
    public static IEnumerable<string> BerichtVerstoesse(
        string bericht,
        IReadOnlyDictionary<int, string> kriterien,
        Func<string, bool> existiert,
        Func<string, string, bool> enthaelt,
        IReadOnlyCollection<string> blockedStories)
    {
        var abschnitte = Abschnitte(bericht);
        var capAbschnitte = new Dictionary<int, List<string>>();
        var doppelt = new List<int>();
        foreach (var (ueberschrift, zeilen) in abschnitte)
        {
            var cap = CapUeberschrift().Match(ueberschrift);
            if (cap.Success && !capAbschnitte.TryAdd(int.Parse(cap.Groups[1].Value), zeilen))
            {
                doppelt.Add(int.Parse(cap.Groups[1].Value));
            }
        }

        foreach (var nummer in doppelt.Distinct().Order())
        {
            yield return $"CAP-{nummer} steht mehrfach im Abnahmebericht.";
        }

        foreach (var (nummer, kriterium) in kriterien.OrderBy(k => k.Key))
        {
            if (!capAbschnitte.TryGetValue(nummer, out var zeilen))
            {
                yield return $"CAP-{nummer} fehlt im Abnahmebericht.";
                continue;
            }

            foreach (var verstoss in CapVerstoesse(nummer, kriterium, zeilen, existiert, enthaelt))
            {
                yield return verstoss;
            }
        }

        foreach (var nummer in capAbschnitte.Keys.Where(n => !kriterien.ContainsKey(n)).Order())
        {
            yield return $"CAP-{nummer} steht nicht in SPEC.md.";
        }

        foreach (var verstoss in BlockedVerstoesse(abschnitte, blockedStories))
        {
            yield return verstoss;
        }
    }

    private static IEnumerable<string> CapVerstoesse(
        int nummer, string kriterium, List<string> zeilen, Func<string, bool> existiert, Func<string, string, bool> enthaelt)
    {
        var text = WertNach(zeilen, "**Erfolgskriterium:**");
        if (text != kriterium)
        {
            yield return $"CAP-{nummer}: Erfolgskriterium weicht von SPEC.md ab.";
        }

        var urteil = WertNach(zeilen, "**Urteil:**");
        if (urteil is null || !Urteile.Contains(urteil, StringComparer.Ordinal))
        {
            yield return $"CAP-{nummer}: Urteil fehlt oder ist ungültig.";
            urteil = null;
        }

        var belege = BelegZeilen(zeilen);
        if (urteil != "offen" && !belege.Any(b => b.StartsWith("- Test:", StringComparison.Ordinal) && Backticks().Matches(b).Any(m => m.Groups[1].Value.Contains("::", StringComparison.Ordinal))))
        {
            yield return $"CAP-{nummer}: kein Test-Beleg.";
        }

        if (urteil is "teilweise" or "offen" && string.IsNullOrWhiteSpace(WertNach(zeilen, "**Begründung:**")))
        {
            yield return $"CAP-{nummer}: Urteil {urteil} ohne Begründung.";
        }

        foreach (var beleg in belege.SelectMany(b => Backticks().Matches(b).Select(m => m.Groups[1].Value)))
        {
            var trenner = beleg.IndexOf("::", StringComparison.Ordinal);
            var pfad = trenner < 0 ? beleg : beleg[..trenner];
            if (!existiert(pfad))
            {
                yield return $"CAP-{nummer}: Beleg {pfad} fehlt.";
            }
            else if (trenner >= 0)
            {
                var methode = beleg[(trenner + 2)..];
                if (methode.Length == 0 || !(enthaelt(pfad, $"void {methode}(") || enthaelt(pfad, $"Task {methode}(")))
                {
                    yield return $"CAP-{nummer}: Test {methode} nicht in {pfad}.";
                }
            }
        }
    }

    private static IEnumerable<string> BlockedVerstoesse(List<(string Ueberschrift, List<string> Zeilen)> abschnitte, IReadOnlyCollection<string> blockedStories)
    {
        var abschnitt = abschnitte.FirstOrDefault(a => a.Ueberschrift == BlockedUeberschrift);
        if (abschnitt.Zeilen is null)
        {
            yield return $"Abschnitt „{BlockedUeberschrift}“ fehlt.";
            yield break;
        }

        var genannt = abschnitt.Zeilen.Select(z => BlockedZeile().Match(z)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        foreach (var id in blockedStories.Where(s => !genannt.Contains(s)).Order(StringComparer.Ordinal))
        {
            yield return $"Story {id}: Status blocked nicht berichtet.";
        }

        foreach (var id in genannt.Where(g => !blockedStories.Contains(g)))
        {
            yield return $"Story {id} ist nicht blocked.";
        }

        if (blockedStories.Count == 0 && !abschnitt.Zeilen.Any(z => z.Trim() == "Keine."))
        {
            yield return $"Abschnitt „{BlockedUeberschrift}“: ohne blocked-Story fehlt „Keine.“.";
        }
    }

    /// <summary>Text nach einer Marke wie <c>**Urteil:**</c> am Zeilenanfang, getrimmt; <c>null</c>, wenn die Marke fehlt.</summary>
    private static string? WertNach(List<string> zeilen, string marke) =>
        zeilen.Where(z => z.StartsWith(marke, StringComparison.Ordinal)).Select(z => z[marke.Length..].Trim()).FirstOrDefault();

    /// <summary>Listenzeilen <c>- …</c> nach <c>**Belege:**</c> bis zur nächsten fetten Marke.</summary>
    private static List<string> BelegZeilen(List<string> zeilen)
    {
        var start = zeilen.FindIndex(z => z.StartsWith("**Belege:**", StringComparison.Ordinal));
        if (start < 0)
        {
            return [];
        }

        return zeilen.Skip(start + 1).TakeWhile(z => !z.StartsWith("**", StringComparison.Ordinal))
            .Where(z => z.StartsWith("- ", StringComparison.Ordinal)).ToList();
    }

    private static List<(string Ueberschrift, List<string> Zeilen)> Abschnitte(string bericht)
    {
        var abschnitte = new List<(string, List<string>)>();
        List<string>? aktuell = null;
        foreach (var zeile in Zeilen(bericht))
        {
            if (zeile.StartsWith("## ", StringComparison.Ordinal))
            {
                aktuell = [];
                abschnitte.Add((zeile[3..].Trim(), aktuell));
            }
            else
            {
                aktuell?.Add(zeile);
            }
        }

        return abschnitte;
    }

    private static IEnumerable<string> Zeilen(string inhalt) => inhalt.Replace("\r\n", "\n").Split('\n');

    [GeneratedRegex(@"^- \*\*CAP-(\d+)\*\*\s*$")]
    private static partial Regex CapZeile();

    [GeneratedRegex(@"^\s+- \*\*success:\*\*\s*(.+)$")]
    private static partial Regex SuccessZeile();

    [GeneratedRegex(@"^CAP-(\d+)\b")]
    private static partial Regex CapUeberschrift();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Backticks();

    [GeneratedRegex(@"^- `([^`]+)`")]
    private static partial Regex BlockedZeile();

    [GeneratedRegex(@"^(\d+-\d+)-")]
    private static partial Regex StoryId();

    [GeneratedRegex(@"^status:\s*'?""?([^'""\r\n]+?)'?""?\s*$", RegexOptions.Multiline)]
    private static partial Regex StatusZeile();
}
