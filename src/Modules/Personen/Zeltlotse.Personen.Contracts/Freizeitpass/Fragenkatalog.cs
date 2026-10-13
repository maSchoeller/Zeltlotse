using System.Globalization;

namespace Zeltlotse.Personen.Contracts.Freizeitpass;

/// <summary>Fester Antworttyp einer Katalogfrage (AD-10); unter ihm werden die Antworten gespeichert.</summary>
public enum Antworttyp
{
    /// <summary>Freier Text, höchstens <see cref="Fragenkatalog.FreitextMaximaleLaenge"/> Zeichen.</summary>
    Freitext,

    /// <summary>Ein Kalendertag im Format <see cref="Fragenkatalog.Datumsformat"/>.</summary>
    Datum,

    /// <summary><see cref="Fragenkatalog.Ja"/> oder <see cref="Fragenkatalog.Nein"/>.</summary>
    JaNein,

    /// <summary>Mehrfachauswahl aus festen Optionen, als Komma-Liste der Schlüssel.</summary>
    Auswahl,
}

/// <summary>Eine Option einer Auswahl- oder Ja/Nein-Frage: gespeicherter Schlüssel und Anzeigetext.</summary>
public sealed record FrageOption(string Wert, string Text);

/// <summary>Eine Frage des Katalogs mit stabiler Kennung (kebab-case, ohne Komma) und festem Antworttyp.</summary>
/// <param name="MitSonstiges">Die Frage hat zusätzlich ein Freitextfeld „Sonstiges“ (höchstens <see cref="Fragenkatalog.SonstigesMaximaleLaenge"/> Zeichen).</param>
public sealed record Katalogfrage(string Kennung, string Frage, Antworttyp Typ, IReadOnlyList<FrageOption> Optionen, bool MitSonstiges);

/// <summary>Ergebnis einer Prüfung: der normalisierte Wert (leer = <c>null</c>) oder ein Fehlertext.</summary>
public readonly record struct Pruefung(string? Wert, string? Fehler)
{
    public bool Gueltig => Fehler is null;

    public static Pruefung Ok(string? wert) => new(wert, null);

    public static Pruefung Falsch(string fehler) => new(null, fehler);
}

/// <summary>
/// Der feste Fragenkatalog des Freizeitpasses (AD-10, FR-22, FR-23), Besitzer ist Personen. Die Reihenfolge ist die
/// Anzeigereihenfolge; Allergien, Medikamente und Notfallkontakte stehen immer oben. Ändert sich die Bedeutung einer Frage, bekommt
/// sie eine neue Kennung. Das Wire-Format aller Werte ist Text: Ja/Nein <c>ja</c>/<c>nein</c>, Datum <c>yyyy-MM-dd</c>, Auswahl als
/// Komma-Liste der Schlüssel (normalisiert in Katalogreihenfolge ohne Doppelte); leer heißt „keine Angabe“. Freizeiten prüft die
/// Fragen-Auswahl einer Freizeit gegen diesen Katalog (<see cref="KennungenPruefen"/>).
/// </summary>
public static class Fragenkatalog
{
    public const int FreitextMaximaleLaenge = 2000;

    public const int SonstigesMaximaleLaenge = 500;

    public const string Datumsformat = "yyyy-MM-dd";

    public const string Ja = "ja";

    public const string Nein = "nein";

    public const string Allergien = "allergien";

    public const string Medikamente = "medikamente";

    public const string Notfallkontakte = "notfallkontakte";

    public const string Ernaehrung = "ernaehrung";

    public const string Krankheiten = "krankheiten";

    public const string Krankenversicherung = "krankenversicherung";

    public const string Tetanus = "tetanus";

    public const string Schwimmen = "schwimmen";

    public const string ZeckenEntfernen = "zecken-entfernen";

    public const string Hinweise = "hinweise";

    /// <summary>Die 14 EU-Hauptallergene (Lebensmittelinformations-Verordnung, Anhang II).</summary>
    public static readonly IReadOnlyList<FrageOption> Allergene =
    [
        new("gluten", "Glutenhaltiges Getreide"),
        new("krebstiere", "Krebstiere"),
        new("eier", "Eier"),
        new("fisch", "Fisch"),
        new("erdnuesse", "Erdnüsse"),
        new("soja", "Soja"),
        new("milch", "Milch (inkl. Laktose)"),
        new("schalenfruechte", "Schalenfrüchte (Nüsse)"),
        new("sellerie", "Sellerie"),
        new("senf", "Senf"),
        new("sesam", "Sesam"),
        new("sulfite", "Schwefeldioxid und Sulfite"),
        new("lupinen", "Lupinen"),
        new("weichtiere", "Weichtiere"),
    ];

    private static readonly IReadOnlyList<FrageOption> JaNeinOptionen = [new(Ja, "Ja"), new(Nein, "Nein")];

    /// <summary>Alle Fragen in Anzeigereihenfolge.</summary>
    public static readonly IReadOnlyList<Katalogfrage> Fragen =
    [
        new(Allergien, "Allergien und Unverträglichkeiten", Antworttyp.Auswahl, Allergene, MitSonstiges: true),
        new(Medikamente, "Medikamente (Name, Dosierung, wann)", Antworttyp.Freitext, [], MitSonstiges: false),
        new(Notfallkontakte, "Notfallkontakte (Name, Telefon)", Antworttyp.Freitext, [], MitSonstiges: false),
        new(
            Ernaehrung,
            "Ernährung",
            Antworttyp.Auswahl,
            [new("vegetarisch", "Vegetarisch"), new("vegan", "Vegan"), new("kein-schweinefleisch", "Kein Schweinefleisch")],
            MitSonstiges: true),
        new(Krankheiten, "Krankheiten und Einschränkungen", Antworttyp.Freitext, [], MitSonstiges: false),
        new(Krankenversicherung, "Krankenversicherung", Antworttyp.Freitext, [], MitSonstiges: false),
        new(Tetanus, "Letzte Tetanus-Impfung", Antworttyp.Datum, [], MitSonstiges: false),
        new(Schwimmen, "Kann schwimmen", Antworttyp.JaNein, JaNeinOptionen, MitSonstiges: false),
        new(ZeckenEntfernen, "Zecken dürfen vom Team entfernt werden", Antworttyp.JaNein, JaNeinOptionen, MitSonstiges: false),
        new(Hinweise, "Weitere Hinweise für das Team", Antworttyp.Freitext, [], MitSonstiges: false),
    ];

    /// <summary>Die Frage zur Kennung oder <c>null</c>.</summary>
    public static Katalogfrage? Finden(string? kennung) =>
        kennung is null ? null : Fragen.FirstOrDefault(f => string.Equals(f.Kennung, kennung, StringComparison.Ordinal));

    /// <summary>Prüft und normalisiert einen Wert nach dem Antworttyp der Frage; leer oder <c>null</c> ist „keine Angabe“.</summary>
    public static Pruefung WertPruefen(Katalogfrage frage, string? wert)
    {
        ArgumentNullException.ThrowIfNull(frage);
        var text = Leer(wert);
        if (text is null)
        {
            return Pruefung.Ok(null);
        }

        switch (frage.Typ)
        {
            case Antworttyp.Freitext:
                return text.Length > FreitextMaximaleLaenge ? Pruefung.Falsch($"Höchstens {FreitextMaximaleLaenge} Zeichen.") : Pruefung.Ok(text);
            case Antworttyp.Datum:
                return DateOnly.TryParseExact(text, Datumsformat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag) && tag.Year >= 1900
                    ? Pruefung.Ok(tag.ToString(Datumsformat, CultureInfo.InvariantCulture))
                    : Pruefung.Falsch("Das Datum ist ungültig.");
            case Antworttyp.JaNein:
                return text is Ja or Nein ? Pruefung.Ok(text) : Pruefung.Falsch("Nur „ja“ oder „nein“.");
            case Antworttyp.Auswahl:
                var schluessel = Liste(text);
                var unbekannt = schluessel.FirstOrDefault(s => frage.Optionen.All(o => o.Wert != s));
                if (unbekannt is not null)
                {
                    return Pruefung.Falsch("Unbekannte Auswahl.");
                }

                var normalisiert = string.Join(',', frage.Optionen.Where(o => schluessel.Contains(o.Wert)).Select(o => o.Wert));
                return Pruefung.Ok(normalisiert.Length == 0 ? null : normalisiert);
            default:
                throw new ArgumentOutOfRangeException(nameof(frage), frage.Typ, null);
        }
    }

    /// <summary>Prüft und normalisiert „Sonstiges“; nur Fragen mit <see cref="Katalogfrage.MitSonstiges"/> nehmen einen Wert an.</summary>
    public static Pruefung SonstigesPruefen(Katalogfrage frage, string? wert)
    {
        ArgumentNullException.ThrowIfNull(frage);
        var text = Leer(wert);
        if (text is null)
        {
            return Pruefung.Ok(null);
        }

        if (!frage.MitSonstiges)
        {
            return Pruefung.Falsch("Diese Frage hat kein Feld „Sonstiges“.");
        }

        return text.Length > SonstigesMaximaleLaenge ? Pruefung.Falsch($"Höchstens {SonstigesMaximaleLaenge} Zeichen.") : Pruefung.Ok(text);
    }

    /// <summary>
    /// Prüft eine Fragen-Auswahl (Kennungen mit Komma) und normalisiert sie in Katalogreihenfolge ohne Doppelte; leer = keine Fragen
    /// (<c>null</c>). Eine unbekannte Kennung ist ein Fehler.
    /// </summary>
    public static Pruefung KennungenPruefen(string? liste)
    {
        var kennungen = Liste(Leer(liste) ?? "");
        var unbekannt = kennungen.FirstOrDefault(k => Finden(k) is null);
        if (unbekannt is not null)
        {
            return Pruefung.Falsch($"Unbekannte Frage: {unbekannt}.");
        }

        var normalisiert = string.Join(',', Ordnen(kennungen));
        return Pruefung.Ok(normalisiert.Length == 0 ? null : normalisiert);
    }

    /// <summary>Die bekannten Kennungen einer Komma-Liste in Katalogreihenfolge ohne Doppelte; Unbekanntes fällt weg.</summary>
    public static IReadOnlyList<string> Kennungen(string? liste) => Ordnen(Liste(liste ?? ""));

    /// <summary>Die bekannten Kennungen einer Menge in Katalogreihenfolge ohne Doppelte; Unbekanntes fällt weg.</summary>
    public static IReadOnlyList<string> Ordnen(IEnumerable<string> kennungen)
    {
        var menge = kennungen.ToHashSet(StringComparer.Ordinal);
        return [.. Fragen.Where(f => menge.Contains(f.Kennung)).Select(f => f.Kennung)];
    }

    /// <summary>
    /// Story 2-5: die PascalCase-Form einer Kennung (<c>zecken-entfernen</c> → <c>ZeckenEntfernen</c>), der Name des Enum-Mitglieds, unter
    /// dem Ereignisse die Frage tragen (AD-4).
    /// </summary>
    public static string PascalName(string kennung)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        return string.Concat(kennung.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(t => char.ToUpperInvariant(t[0]) + t[1..]));
    }

    /// <summary>Story 2-5: die Kennung zur PascalCase-Form (<c>ZeckenEntfernen</c> → <c>zecken-entfernen</c>); unbekannt <c>null</c>.</summary>
    public static string? KennungAusPascalName(string name) =>
        Fragen.Select(f => f.Kennung).FirstOrDefault(k => string.Equals(PascalName(k), name, StringComparison.Ordinal));

    private static string? Leer(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();

    private static List<string> Liste(string text) =>
        [.. text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
}
