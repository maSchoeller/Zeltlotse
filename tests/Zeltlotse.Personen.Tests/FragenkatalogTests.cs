using System.Text.RegularExpressions;
using Zeltlotse.Personen.Contracts.Freizeitpass;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Der feste Fragenkatalog des Freizeitpasses (AD-10, FR-22, FR-23): stabile Kennungen in kebab-case, feste Reihenfolge mit Allergien,
/// Medikamenten und Notfallkontakten oben, die 14 EU-Hauptallergene und die Prüf- und Normalisierhilfen für das Wire-Format.
/// </summary>
public sealed partial class FragenkatalogTests
{
    [Fact]
    public void Kennungen_sind_eindeutig_kebab_case_und_in_fester_Reihenfolge()
    {
        var kennungen = Fragenkatalog.Fragen.Select(f => f.Kennung).ToList();

        Assert.Equal(
            ["allergien", "medikamente", "notfallkontakte", "ernaehrung", "krankheiten", "krankenversicherung", "tetanus", "schwimmen", "zecken-entfernen", "hinweise"],
            kennungen);
        Assert.Equal(kennungen.Count, kennungen.Distinct(StringComparer.Ordinal).Count());
        Assert.All(kennungen, k => Assert.Matches(KebabCase(), k));
        Assert.All(Fragenkatalog.Fragen.SelectMany(f => f.Optionen), o => Assert.Matches(KebabCase(), o.Wert));
    }

    [Fact]
    public void Antworttypen_Texte_und_Sonstiges_stehen_fest()
    {
        Assert.Equal(
            [
                ("allergien", "Allergien und Unverträglichkeiten", Antworttyp.Auswahl, true),
                ("medikamente", "Medikamente (Name, Dosierung, wann)", Antworttyp.Freitext, false),
                ("notfallkontakte", "Notfallkontakte (Name, Telefon)", Antworttyp.Freitext, false),
                ("ernaehrung", "Ernährung", Antworttyp.Auswahl, true),
                ("krankheiten", "Krankheiten und Einschränkungen", Antworttyp.Freitext, false),
                ("krankenversicherung", "Krankenversicherung", Antworttyp.Freitext, false),
                ("tetanus", "Letzte Tetanus-Impfung", Antworttyp.Datum, false),
                ("schwimmen", "Kann schwimmen", Antworttyp.JaNein, false),
                ("zecken-entfernen", "Zecken dürfen vom Team entfernt werden", Antworttyp.JaNein, false),
                ("hinweise", "Weitere Hinweise für das Team", Antworttyp.Freitext, false),
            ],
            Fragenkatalog.Fragen.Select(f => (f.Kennung, f.Frage, f.Typ, f.MitSonstiges)));
        Assert.Equal(["ja", "nein"], Fragenkatalog.Finden("schwimmen")!.Optionen.Select(o => o.Wert));
        Assert.Equal(["Ja", "Nein"], Fragenkatalog.Finden("schwimmen")!.Optionen.Select(o => o.Text));
        Assert.Equal(["vegetarisch", "vegan", "kein-schweinefleisch"], Fragenkatalog.Finden("ernaehrung")!.Optionen.Select(o => o.Wert));
        Assert.Empty(Fragenkatalog.Finden("medikamente")!.Optionen);
        Assert.Null(Fragenkatalog.Finden("lieblingsfarbe"));
        Assert.Null(Fragenkatalog.Finden(null));
    }

    [Fact]
    public void Die_Allergie_Frage_bietet_die_14_EU_Hauptallergene()
    {
        var allergien = Fragenkatalog.Finden("allergien")!;

        Assert.Equal(14, allergien.Optionen.Count);
        Assert.Equal(
            ["gluten", "krebstiere", "eier", "fisch", "erdnuesse", "soja", "milch", "schalenfruechte", "sellerie", "senf", "sesam", "sulfite", "lupinen", "weichtiere"],
            allergien.Optionen.Select(o => o.Wert));
        Assert.Equal("Milch (inkl. Laktose)", allergien.Optionen.Single(o => o.Wert == "milch").Text);
        Assert.Equal("Schwefeldioxid und Sulfite", allergien.Optionen.Single(o => o.Wert == "sulfite").Text);
    }

    public static TheoryData<string, string?, string?> Gueltig => new()
    {
        { "allergien", "milch,eier", "eier,milch" },
        { "allergien", " milch , eier ,milch,", "eier,milch" },
        { "allergien", "", null },
        { "allergien", null, null },
        { "ernaehrung", "vegan,vegetarisch", "vegetarisch,vegan" },
        { "schwimmen", "ja", "ja" },
        { "schwimmen", " nein ", "nein" },
        { "tetanus", "2024-05-17", "2024-05-17" },
        { "medikamente", "  Ibuprofen 200 mg bei Bedarf  ", "Ibuprofen 200 mg bei Bedarf" },
        { "hinweise", "   ", null },
    };

    [Theory]
    [MemberData(nameof(Gueltig))]
    public void Gueltige_Werte_werden_normalisiert(string kennung, string? wert, string? erwartet)
    {
        var pruefung = Fragenkatalog.WertPruefen(Fragenkatalog.Finden(kennung)!, wert);

        Assert.Null(pruefung.Fehler);
        Assert.Equal(erwartet, pruefung.Wert);
    }

    public static TheoryData<string, string> Ungueltig => new()
    {
        { "schwimmen", "vielleicht" },
        { "schwimmen", "Ja" },
        { "tetanus", "31.02." },
        { "tetanus", "2024-02-31" },
        { "allergien", "nuss" },
        { "allergien", "milch,nuss" },
        { "ernaehrung", "milch" },
    };

    [Theory]
    [MemberData(nameof(Ungueltig))]
    public void Ungueltige_Werte_haben_einen_Fehler(string kennung, string wert)
    {
        var pruefung = Fragenkatalog.WertPruefen(Fragenkatalog.Finden(kennung)!, wert);

        Assert.NotNull(pruefung.Fehler);
        Assert.Null(pruefung.Wert);
    }

    [Fact]
    public void Freitext_hoechstens_2000_Zeichen_Sonstiges_hoechstens_500_und_nur_bei_Auswahl_mit_Sonstiges()
    {
        var medikamente = Fragenkatalog.Finden("medikamente")!;
        var allergien = Fragenkatalog.Finden("allergien")!;

        Assert.Null(Fragenkatalog.WertPruefen(medikamente, new string('x', 2000)).Fehler);
        Assert.Equal("Höchstens 2000 Zeichen.", Fragenkatalog.WertPruefen(medikamente, new string('x', 2001)).Fehler);
        Assert.Equal(("Kiwi", (string?)null), (Fragenkatalog.SonstigesPruefen(allergien, " Kiwi ").Wert, Fragenkatalog.SonstigesPruefen(allergien, " Kiwi ").Fehler));
        Assert.Null(Fragenkatalog.SonstigesPruefen(allergien, " ").Wert);
        Assert.Equal("Höchstens 500 Zeichen.", Fragenkatalog.SonstigesPruefen(allergien, new string('x', 501)).Fehler);
        Assert.Equal("Diese Frage hat kein Feld „Sonstiges“.", Fragenkatalog.SonstigesPruefen(medikamente, "x").Fehler);
        Assert.Null(Fragenkatalog.SonstigesPruefen(medikamente, null).Fehler);
    }

    [Fact]
    public void Eine_Fragen_Auswahl_wird_in_Katalogreihenfolge_ohne_Doppelte_normalisiert()
    {
        Assert.Equal(("allergien,medikamente", (string?)null), Paar(Fragenkatalog.KennungenPruefen("medikamente,allergien,allergien")));
        Assert.Equal(((string?)null, (string?)null), Paar(Fragenkatalog.KennungenPruefen(" ")));
        Assert.Equal(((string?)null, (string?)null), Paar(Fragenkatalog.KennungenPruefen(null)));
        var falsch = Fragenkatalog.KennungenPruefen("allergien,lieblingsfarbe");
        Assert.Equal("Unbekannte Frage: lieblingsfarbe.", falsch.Fehler);
        Assert.Equal(["allergien", "schwimmen"], Fragenkatalog.Kennungen("schwimmen,allergien"));
        Assert.Empty(Fragenkatalog.Kennungen(null));
    }

    [Fact]
    public void Die_Grenzwerte_der_API_Vertraege_stimmen_mit_dem_Katalog_ueberein()
    {
        Assert.Equal(Fragenkatalog.FreitextMaximaleLaenge, Zeltlotse.Api.Contracts.Personen.FreizeitpassFelder.FreitextMaximaleLaenge);
        Assert.Equal(Fragenkatalog.SonstigesMaximaleLaenge, Zeltlotse.Api.Contracts.Personen.FreizeitpassFelder.SonstigesMaximaleLaenge);
        Assert.Equal(Fragenkatalog.Datumsformat, Zeltlotse.Api.Contracts.Personen.FreizeitpassFelder.Datumsformat);
    }

    private static (string? Wert, string? Fehler) Paar(Pruefung pruefung) => (pruefung.Wert, pruefung.Fehler);

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$")]
    private static partial Regex KebabCase();
}
