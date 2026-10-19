using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.Programm.Contracts.Mahlzeiten;
using Rezept = Zeltlotse.Kueche.Rezepte.Rezept;
using Zutat = Zeltlotse.Kueche.Rezepte.Zutat;

namespace Zeltlotse.Kueche.Einkauf;

/// <summary>Story 4-2: Ein berechneter Posten eines Gerichts: die Zutat, die geltende Menge und ob sie von Hand ist.</summary>
/// <param name="Version">Version der Hand-Menge, ohne Hand-Menge 0.</param>
internal sealed record RechenPosten(Zutat Zutat, decimal? Menge, bool VonHand, uint Version);

/// <summary>Story 4-2: Alles, was die Liste von einem Gericht braucht: Gericht, Mahlzeit, Rezept, dessen Zutaten (nach Kennung) und die Hand-Mengen.</summary>
internal sealed record GerichtDaten(Gericht Gericht, FreizeitMahlzeit Mahlzeit, Rezept Rezept, IReadOnlyList<Zutat> Zutaten, IReadOnlyList<MengeVonHand> HandMengen);

/// <summary>
/// Story 4-2 (CAP-29, FR-47 bis FR-50): Die Rechenregeln der Einkaufsliste an genau einer Stelle, ohne Datenbank. Die Liste wird bei jedem Abruf aus den
/// Gerichten berechnet (AD-13: Berechnungen laufen automatisch); gespeichert sind nur Hand-Mengen und Hand-Posten.
/// </summary>
internal static class EinkaufRechnen
{
    /// <summary>
    /// Die Posten eines Gerichts in der Reihenfolge der Zutaten: je Zutat die auf die Personenzahl hochgerechnete Menge (wie auf der Rezeptseite, ohne
    /// Abzug für Ersatzportionen); eine Hand-Menge ersetzt sie.
    /// </summary>
    public static IReadOnlyList<RechenPosten> Posten(Rezept rezept, IReadOnlyList<Zutat> zutaten, IReadOnlyCollection<MengeVonHand> handMengen, int personenzahl) =>
    [
        .. zutaten.Select(z => handMengen.FirstOrDefault(h => h.ZutatId == z.Id) is { } hand
            ? new RechenPosten(z, KuecheWerte.OhneNullen(hand.Menge), VonHand: true, hand.Version)
            : new RechenPosten(z, KuecheWerte.Hochrechnen(z.Menge, rezept.BasisPersonen, personenzahl), VonHand: false, 0)),
    ];

    /// <summary>
    /// FR-50, Tausch: Jede Hand-Menge wandert auf die erste Zutat des neuen Rezepts mit gleicher Bezeichnung (getrimmt, ohne Groß-/Kleinschreibung) und
    /// gleicher Einheit (ebenso getrimmt, ohne Groß-/Kleinschreibung); jede neue Zutat nimmt höchstens eine (die der ersten alten Zutat). Die übrigen Hand-Mengen entfallen. Ergebnis: neue Zutat →
    /// Menge.
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> TauschAbbilden(IReadOnlyList<Zutat> alteZutaten, IReadOnlyCollection<MengeVonHand> handMengen, IReadOnlyList<Zutat> neueZutaten)
    {
        var abgebildet = new Dictionary<Guid, decimal>();
        foreach (var alt in alteZutaten)
        {
            if (handMengen.FirstOrDefault(h => h.ZutatId == alt.Id) is not { } hand)
            {
                continue;
            }

            var ziel = neueZutaten.FirstOrDefault(n => GleicheZutat(alt, n));
            if (ziel is not null && !abgebildet.ContainsKey(ziel.Id))
            {
                abgebildet[ziel.Id] = KuecheWerte.OhneNullen(hand.Menge);
            }
        }

        return abgebildet;
    }

    private static bool GleicheZutat(Zutat alt, Zutat neu) =>
        string.Equals(alt.Bezeichnung.Trim(), neu.Bezeichnung.Trim(), StringComparison.OrdinalIgnoreCase)
        && GleicheEinheit(alt.Einheit, neu.Einheit);

    /// <summary>Einheiten getrimmt und ohne Groß-/Kleinschreibung („g“ wie „G“); ohne Einheit gleich ohne Einheit.</summary>
    private static bool GleicheEinheit(string? a, string? b) => string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Die Posten nach einem Tausch, genau so, wie „Übernehmen“ sie speichert: das neue Rezept mit den abgebildeten Hand-Mengen. Ist das neue Rezept das
    /// alte, bleibt alles, wie es ist.
    /// </summary>
    public static IReadOnlyList<RechenPosten> PostenNachTausch(
        Rezept altesRezept, IReadOnlyList<Zutat> alteZutaten, IReadOnlyCollection<MengeVonHand> handMengen, Rezept neuesRezept, IReadOnlyList<Zutat> neueZutaten,
        int personenzahl)
    {
        if (altesRezept.Id == neuesRezept.Id)
        {
            return Posten(altesRezept, alteZutaten, handMengen, personenzahl);
        }

        var abgebildet = TauschAbbilden(alteZutaten, handMengen, neueZutaten);
        return
        [
            .. neueZutaten.Select(z => abgebildet.TryGetValue(z.Id, out var menge)
                ? new RechenPosten(z, menge, VonHand: true, 0)
                : new RechenPosten(z, KuecheWerte.Hochrechnen(z.Menge, neuesRezept.BasisPersonen, personenzahl), VonHand: false, 0)),
        ];
    }

    /// <summary>
    /// Vorher/Nachher für die Tausch-Vorschau: Eine Zeile gilt als geändert, wenn es auf der anderen Seite keine gleiche gibt (Bezeichnung ohne
    /// Groß-/Kleinschreibung, Menge, Einheit und „von Hand“).
    /// </summary>
    public static (IReadOnlyList<TauschZeile> Vorher, IReadOnlyList<TauschZeile> Nachher) Vergleich(IReadOnlyList<RechenPosten> vorher, IReadOnlyList<RechenPosten> nachher) =>
        ([.. vorher.Select(p => Zeile(p, nachher))], [.. nachher.Select(p => Zeile(p, vorher))]);

    private static TauschZeile Zeile(RechenPosten posten, IReadOnlyList<RechenPosten> andere) =>
        new(posten.Zutat.Bezeichnung, posten.Menge, posten.Zutat.Einheit, posten.VonHand, !andere.Any(a => Gleich(posten, a)));

    private static bool Gleich(RechenPosten a, RechenPosten b) =>
        string.Equals(a.Zutat.Bezeichnung.Trim(), b.Zutat.Bezeichnung.Trim(), StringComparison.OrdinalIgnoreCase)
        && a.Menge == b.Menge
        && GleicheEinheit(a.Zutat.Einheit, b.Zutat.Einheit)
        && a.VonHand == b.VonHand;

    /// <summary>
    /// FR-48: Ersatzportionen eines Rezepts: die Profile, die mindestens ein Allergen des Rezepts (Vereinigung der Zutaten) enthalten, jede Person einmal;
    /// dazu die getroffenen Allergene in Katalogreihenfolge.
    /// </summary>
    public static Ersatzportionen Ersatz(IReadOnlyList<IReadOnlyList<string>> profile, IEnumerable<Zutat> zutaten)
    {
        var rezept = zutaten.SelectMany(z => (z.Allergene ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
        var betroffen = profile.Where(p => p.Any(rezept.Contains)).ToList();
        var getroffen = betroffen.SelectMany(p => p).Where(rezept.Contains).ToHashSet(StringComparer.Ordinal);
        return new Ersatzportionen(betroffen.Count, [.. Allergene.Alle.Select(a => a.Wert).Where(getroffen.Contains)]);
    }

    /// <summary>Allergen-Übersicht: je Allergen die Zahl der Profile, die es enthalten, in Katalogreihenfolge; nur Allergene mit mindestens einem.</summary>
    public static IReadOnlyList<AllergenAnzahl> Uebersicht(IReadOnlyList<IReadOnlyList<string>> profile) =>
    [
        .. Allergene.Alle
            .Select(a => new AllergenAnzahl(a.Wert, profile.Count(p => p.Contains(a.Wert))))
            .Where(a => a.Anzahl > 0),
    ];

    /// <summary>
    /// Die Tage der Einkaufsliste: je Gericht und Zutat ein Posten (nichts zusammengefasst), gruppiert nach dem Tag der Mahlzeit, darin nach Mahlzeit
    /// (Uhrzeit, Art), Gericht und Zutat; die Hand-Posten eines Tags danach, Hand-Posten ohne Tag zuletzt. Tage ohne Posten und ohne Ersatzportionen
    /// fehlen.
    /// </summary>
    public static IReadOnlyList<EinkaufsTag> Tage(
        IReadOnlyList<GerichtDaten> gerichte, IReadOnlyList<Einkaufsposten> handPosten, int personenzahl, IReadOnlyList<IReadOnlyList<string>> profile)
    {
        var geordnet = gerichte
            .OrderBy(g => g.Mahlzeit.Tag)
            .ThenBy(g => g.Mahlzeit.Beginn is null)
            .ThenBy(g => g.Mahlzeit.Beginn)
            .ThenBy(g => g.Mahlzeit.MahlzeitArt)
            .ThenBy(g => g.Mahlzeit.ProgrammpunktId)
            .ThenBy(g => g.Gericht.Id)
            .ToList();
        var tage = geordnet.Select(g => (DateOnly?)g.Mahlzeit.Tag)
            .Concat(handPosten.Select(p => p.Tag))
            .Distinct()
            .OrderBy(t => t is null)
            .ThenBy(t => t)
            .ToList();
        var ergebnis = new List<EinkaufsTag>();
        foreach (var tag in tage)
        {
            var amTag = geordnet.Where(g => g.Mahlzeit.Tag == tag).ToList();
            List<EinkaufslistenPosten> posten =
            [
                .. amTag.SelectMany(g => Posten(g.Rezept, g.Zutaten, g.HandMengen, personenzahl).Select(p => AlsApi(g, p))),
                .. handPosten.Where(p => p.Tag == tag).OrderBy(p => p.Id).Select(AlsApi),
            ];
            List<GerichtErsatz> ersatz =
            [
                .. amTag
                    .Select(g => new GerichtErsatz(g.Gericht.Id, g.Rezept.Name, g.Mahlzeit.Titel, Ersatz(profile, g.Zutaten)))
                    .Where(e => e.Ersatzportionen.Anzahl > 0),
            ];
            if (posten.Count > 0 || ersatz.Count > 0)
            {
                ergebnis.Add(new EinkaufsTag(tag, posten, ersatz));
            }
        }

        return ergebnis;
    }

    private static EinkaufslistenPosten AlsApi(GerichtDaten gericht, RechenPosten posten) => new(
        gericht.Gericht.Id,
        posten.Zutat.Id,
        null,
        posten.Zutat.Bezeichnung,
        posten.Menge,
        posten.Zutat.Einheit,
        gericht.Rezept.Name,
        gericht.Mahlzeit.Titel,
        posten.VonHand,
        posten.Version);

    public static EinkaufslistenPosten AlsApi(Einkaufsposten posten) => new(
        null,
        null,
        posten.Id,
        posten.Bezeichnung,
        posten.Menge is { } menge ? KuecheWerte.OhneNullen(menge) : null,
        posten.Einheit,
        null,
        null,
        VonHand: true,
        posten.Version);

    /// <summary>Der angezeigte Text einer Menge für den Autosave (deutsch mit Komma) oder <c>null</c>.</summary>
    public static string? MengeText(decimal? menge) => menge is { } wert ? ZutatFelder.ZahlText(wert) : null;
}
