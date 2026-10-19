using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Einkauf;
using Zeltlotse.Programm.Contracts.Mahlzeiten;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using Gericht = Zeltlotse.Kueche.Einkauf.Gericht;
using Rezept = Zeltlotse.Kueche.Rezepte.Rezept;
using Zutat = Zeltlotse.Kueche.Rezepte.Zutat;

namespace Zeltlotse.Kueche.Tests;

/// <summary>
/// Story 4-2, Gerichte vormerken und Einkaufsliste (CAP-29, FR-47 bis FR-50, AD-6, AD-19): die Berechnung der Posten (Hochrechnung, Hand-Menge vor
/// berechneter Menge), Gruppierung und Reihenfolge der Liste, die Abbildung der Hand-Mengen beim Tausch samt Vorher/Nachher-Markierung, die
/// Ersatzportionen (Schnittmenge, jede Person einmal) und die Allergen-Übersicht, der Hand-Posten mit Feldänderung, die Formprüfung der neuen Commands
/// und der Mandantenfilter der neuen Entitäten.
/// </summary>
public sealed class EinkaufTests(PostgresContainer postgres)
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Freizeit = Guid.CreateVersion7();
    private static readonly DateOnly Montag = new(2027, 8, 2);
    private static readonly DateOnly Dienstag = new(2027, 8, 3);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Rezept NeuesRezept(string name, int basis = 4) => Rezept.Anlegen(Guid.CreateVersion7(), Org, Freizeit, name, basis);

    private static Zutat NeueZutat(Rezept rezept, string bezeichnung, decimal? menge, string? einheit, string? allergene = null)
    {
        var zutat = Zutat.Anlegen(Guid.CreateVersion7(), Org, Freizeit, rezept.Id, bezeichnung, menge, einheit);
        if (allergene is not null)
        {
            zutat.FeldAendern(ZutatFeld.Allergene, null, allergene);
        }

        return zutat;
    }

    private static FreizeitMahlzeit Mahlzeit(DateOnly tag, FreizeitMahlzeitArt art) => art switch
    {
        FreizeitMahlzeitArt.Fruehstueck => new(Guid.CreateVersion7(), tag, new TimeOnly(8, 0), "Frühstück", art),
        FreizeitMahlzeitArt.Mittagessen => new(Guid.CreateVersion7(), tag, new TimeOnly(12, 30), "Mittagessen", art),
        _ => new(Guid.CreateVersion7(), tag, new TimeOnly(18, 30), "Abendessen", art),
    };

    private static Gericht Vormerken(FreizeitMahlzeit mahlzeit, Rezept rezept) =>
        Gericht.Vormerken(Guid.CreateVersion7(), Org, Freizeit, mahlzeit.ProgrammpunktId, rezept.Id);

    private static MengeVonHand Hand(Gericht gericht, Zutat zutat, decimal menge) =>
        MengeVonHand.Anlegen(Guid.CreateVersion7(), Org, Freizeit, gericht.Id, zutat.Id, menge);

    private static string Zeile(EinkaufslistenPosten p) =>
        $"{p.Bezeichnung} {(p.Menge is { } m ? ZutatFelder.ZahlText(m) : "-")} {p.Einheit ?? "-"} {p.RezeptName ?? "-"} {p.MahlzeitName ?? "-"}{(p.VonHand ? " von Hand" : "")}";

    // ---------- Posten eines Gerichts ----------

    [Fact]
    public void Posten_rechnen_jede_Zutat_auf_die_Personenzahl_hoch_eine_Hand_Menge_ersetzt_die_berechnete_Menge()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var mehl = NeueZutat(pfannkuchen, "Mehl", 400m, "g", "gluten");
        var milch = NeueZutat(pfannkuchen, "Milch", 0.75m, "l", "milch");
        var salz = NeueZutat(pfannkuchen, "Salz", null, null);
        var gericht = Vormerken(Mahlzeit(Dienstag, FreizeitMahlzeitArt.Fruehstueck), pfannkuchen);

        var ohne = EinkaufRechnen.Posten(pfannkuchen, [mehl, milch, salz], [], 8);
        var mitHand = EinkaufRechnen.Posten(pfannkuchen, [mehl, milch, salz], [Hand(gericht, mehl, 1m)], 8);
        var andereZahl = EinkaufRechnen.Posten(pfannkuchen, [mehl, milch, salz], [Hand(gericht, mehl, 1m)], 40);

        Assert.Equal([(mehl.Id, (decimal?)800m, false), (milch.Id, 1.5m, false), (salz.Id, null, false)], ohne.Select(p => (p.Zutat.Id, p.Menge, p.VonHand)));
        Assert.Equal([(decimal?)1m, 1.5m, null], mitHand.Select(p => p.Menge));
        Assert.Equal([true, false, false], mitHand.Select(p => p.VonHand));
        // Die Hand-Menge bleibt bei geänderter Personenzahl erhalten.
        Assert.Equal([(decimal?)1m, 7.5m, null], andereZahl.Select(p => p.Menge));
    }

    [Fact]
    public void Posten_ohne_Personen_haben_keine_berechnete_Menge_eine_Hand_Menge_gilt_trotzdem()
    {
        var chili = NeuesRezept("Chili sin Carne");
        var bohnen = NeueZutat(chili, "Kidneybohnen", 500m, "g");
        var zwiebeln = NeueZutat(chili, "Zwiebeln", 2m, "Stück");
        var gericht = Vormerken(Mahlzeit(Dienstag, FreizeitMahlzeitArt.Mittagessen), chili);

        var posten = EinkaufRechnen.Posten(chili, [zwiebeln, bohnen], [Hand(gericht, zwiebeln, 5m)], 0);

        // Die Reihenfolge ist die der übergebenen Zutaten (aus der Datenbank nach Kennung).
        Assert.Equal([(zwiebeln.Id, (decimal?)5m), (bohnen.Id, null)], posten.Select(p => (p.Zutat.Id, p.Menge)));
    }

    // ---------- Liste: Gruppierung und Reihenfolge ----------

    [Fact]
    public void Liste_gruppiert_nach_Tag_darin_nach_Mahlzeit_Hand_Posten_ohne_Tag_zuletzt_nichts_zusammengefasst()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var mehl = NeueZutat(pfannkuchen, "Mehl", 400m, "g", "gluten");
        var chili = NeuesRezept("Chili sin Carne");
        var zwiebeln = NeueZutat(chili, "Zwiebeln", 2m, "Stück");
        var mehlImChili = NeueZutat(chili, "Mehl", 10m, "g", "gluten");
        var abendDi = Mahlzeit(Dienstag, FreizeitMahlzeitArt.Abendessen);
        var fruehDi = Mahlzeit(Dienstag, FreizeitMahlzeitArt.Fruehstueck);
        var mittagMo = Mahlzeit(Montag, FreizeitMahlzeitArt.Mittagessen);
        var gerichtAbend = Vormerken(abendDi, chili);
        var gerichtFrueh = Vormerken(fruehDi, pfannkuchen);
        var gerichtMo = Vormerken(mittagMo, pfannkuchen);
        var aepfel = Einkaufsposten.Anlegen(Guid.CreateVersion7(), Org, Freizeit, "Äpfel", 3m, "kg", Dienstag);
        var kaffee = Einkaufsposten.Anlegen(Guid.CreateVersion7(), Org, Freizeit, "Kaffee", null, null, null);
        GerichtDaten[] gerichte =
        [
            new(gerichtAbend, abendDi, chili, [zwiebeln, mehlImChili], [], []),
            new(gerichtFrueh, fruehDi, pfannkuchen, [mehl], [Hand(gerichtFrueh, mehl, 500m)], []),
            new(gerichtMo, mittagMo, pfannkuchen, [mehl], [], []),
        ];

        var tage = EinkaufRechnen.Tage(gerichte, [kaffee, aepfel], 8, []);

        Assert.Equal([Montag, Dienstag, (DateOnly?)null], tage.Select(t => t.Tag));
        Assert.Equal(["Mehl 800 g Pfannkuchen Mittagessen"], tage[0].Posten.Select(Zeile));
        Assert.Equal(
            [
                "Mehl 500 g Pfannkuchen Frühstück von Hand",
                "Zwiebeln 4 Stück Chili sin Carne Abendessen",
                "Mehl 20 g Chili sin Carne Abendessen",
                "Äpfel 3 kg - - von Hand",
            ],
            tage[1].Posten.Select(Zeile));
        Assert.Equal(["Kaffee - - - - von Hand"], tage[2].Posten.Select(Zeile));
        var berechnet = tage[1].Posten[0];
        Assert.Equal((gerichtFrueh.Id, mehl.Id, (Guid?)null), (berechnet.GerichtId, berechnet.ZutatId, berechnet.PostenId));
        Assert.Equal(((Guid?)null, (Guid?)null, aepfel.Id), (tage[1].Posten[3].GerichtId, tage[1].Posten[3].ZutatId, tage[1].Posten[3].PostenId));
        Assert.All(tage, t => Assert.Empty(t.Ersatzportionen));
    }

    [Fact]
    public void Liste_ohne_Gerichte_und_Posten_hat_keine_Tage()
    {
        Assert.Empty(EinkaufRechnen.Tage([], [], 8, []));
    }

    // ---------- Ersatzportionen und Übersicht ----------

    [Fact]
    public void Ersatzportionen_zaehlen_jede_Person_mit_einem_Allergen_des_Rezepts_einmal()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        Zutat[] zutaten = [NeueZutat(pfannkuchen, "Mehl", 400m, "g", "gluten"), NeueZutat(pfannkuchen, "Milch", 0.75m, "l", "milch"), NeueZutat(pfannkuchen, "Eier", 4m, "Stück", "eier")];
        IReadOnlyList<string>[] profile = [["gluten", "milch"], ["gluten"], ["sellerie"], ["eier", "sesam"]];

        var ersatz = EinkaufRechnen.Ersatz(profile, zutaten);

        Assert.Equal(3, ersatz.Anzahl);
        Assert.Equal(["gluten", "eier", "milch"], ersatz.Allergene);
        var ohneAllergene = EinkaufRechnen.Ersatz(profile, [NeueZutat(pfannkuchen, "Salz", null, null)]);
        Assert.Equal(0, ohneAllergene.Anzahl);
        Assert.Empty(ohneAllergene.Allergene);
        Assert.Equal(0, EinkaufRechnen.Ersatz([], zutaten).Anzahl);
    }

    [Fact]
    public void Liste_nennt_die_Ersatzportionen_je_Gericht_am_Tag_nur_mit_mindestens_einer()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var mehl = NeueZutat(pfannkuchen, "Mehl", 400m, "g", "gluten");
        var chili = NeuesRezept("Chili sin Carne");
        var bruehe = NeueZutat(chili, "Gemüsebrühe", 0.5m, "l", "sellerie");
        var frueh = Mahlzeit(Dienstag, FreizeitMahlzeitArt.Fruehstueck);
        var mittag = Mahlzeit(Dienstag, FreizeitMahlzeitArt.Mittagessen);
        var gericht = Vormerken(frueh, pfannkuchen);

        var tage = EinkaufRechnen.Tage([new(gericht, frueh, pfannkuchen, [mehl], [], []), new(Vormerken(mittag, chili), mittag, chili, [bruehe], [], [])], [], 4, [["gluten"]]);

        var ersatz = Assert.Single(Assert.Single(tage).Ersatzportionen);
        Assert.Equal((gericht.Id, "Pfannkuchen", "Frühstück", 1), (ersatz.GerichtId, ersatz.RezeptName, ersatz.MahlzeitName, ersatz.Ersatzportionen.Anzahl));
        Assert.Equal(["gluten"], ersatz.Ersatzportionen.Allergene);
    }

    [Fact]
    public void Uebersicht_zaehlt_je_Allergen_die_Profile_in_Katalogreihenfolge()
    {
        var uebersicht = EinkaufRechnen.Uebersicht([["milch"], ["gluten", "milch"], ["gluten"]]);

        Assert.Equal([new AllergenAnzahl("gluten", 2), new AllergenAnzahl("milch", 2)], uebersicht);
        Assert.Empty(EinkaufRechnen.Uebersicht([]));
    }

    // ---------- Tausch ----------

    [Fact]
    public void Tausch_Hand_Mengen_wandern_auf_die_Zutat_mit_gleicher_Bezeichnung_ohne_Gross_Klein_und_gleicher_Einheit_die_uebrigen_entfallen()
    {
        var alt = NeuesRezept("Gemüsepfanne");
        var zwiebelnAlt = NeueZutat(alt, " Zwiebeln ", 1m, "kg");
        var paprikaAlt = NeueZutat(alt, "Paprika", 2m, "Stück");
        var reisAlt = NeueZutat(alt, "Reis", 500m, "g");
        var eierAlt = NeueZutat(alt, "Eier", 4m, "Stück");
        var neu = NeuesRezept("Chili sin Carne");
        var zwiebelnNeu = NeueZutat(neu, "zwiebeln", 2m, "kg");
        var paprikaNeu = NeueZutat(neu, "Paprika", 3m, "Stk.");
        var reisNeu = NeueZutat(neu, "REIS", 250m, "G");
        var eierNeu = NeueZutat(neu, "eier", 2m, " stück ");
        var gericht = Vormerken(Mahlzeit(Dienstag, FreizeitMahlzeitArt.Mittagessen), alt);

        var abgebildet = EinkaufRechnen.TauschAbbilden(
            [zwiebelnAlt, paprikaAlt, reisAlt, eierAlt],
            [Hand(gericht, zwiebelnAlt, 3m), Hand(gericht, paprikaAlt, 5m), Hand(gericht, reisAlt, 1m), Hand(gericht, eierAlt, 6m)],
            [zwiebelnNeu, paprikaNeu, reisNeu, eierNeu]);

        // Die Einheit zählt ebenso ohne Groß-/Kleinschreibung: „g“ wie „G“, „Stück“ wie „stück“.
        Assert.Equal(new Dictionary<Guid, decimal> { [zwiebelnNeu.Id] = 3m, [reisNeu.Id] = 1m, [eierNeu.Id] = 6m }, abgebildet);
        Assert.DoesNotContain(paprikaNeu.Id, abgebildet.Keys);
    }

    [Fact]
    public void Tausch_bei_zwei_passenden_Zutaten_nimmt_die_erste_und_jede_neue_Zutat_hoechstens_eine_Hand_Menge()
    {
        var alt = NeuesRezept("Alt");
        var mehlA = NeueZutat(alt, "Mehl", 1m, "kg");
        var mehlB = NeueZutat(alt, "mehl", 1m, "kg");
        var neu = NeuesRezept("Neu");
        var mehl1 = NeueZutat(neu, "Mehl", 1m, "kg");
        var mehl2 = NeueZutat(neu, "Mehl", 2m, "kg");
        var gericht = Vormerken(Mahlzeit(Dienstag, FreizeitMahlzeitArt.Mittagessen), alt);

        var abgebildet = EinkaufRechnen.TauschAbbilden([mehlA, mehlB], [Hand(gericht, mehlB, 7m), Hand(gericht, mehlA, 6m)], [mehl1, mehl2]);

        Assert.Equal(new Dictionary<Guid, decimal> { [mehl1.Id] = 6m }, abgebildet);
    }

    [Fact]
    public void Vergleich_markiert_Zeilen_ohne_Gegenstueck_als_geaendert_gleiche_Zeilen_nicht()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var mehl = NeueZutat(pfannkuchen, "Mehl", 400m, "g");
        var salz = NeueZutat(pfannkuchen, "Salz", null, null);
        var chili = NeuesRezept("Chili sin Carne");
        var zwiebeln = NeueZutat(chili, "Zwiebeln", 2m, "kg");
        var salzImChili = NeueZutat(chili, "salz", null, null);
        var gericht = Vormerken(Mahlzeit(Dienstag, FreizeitMahlzeitArt.Fruehstueck), pfannkuchen);
        var vorher = EinkaufRechnen.Posten(pfannkuchen, [mehl, salz], [Hand(gericht, mehl, 1m)], 8);
        var nachher = EinkaufRechnen.Posten(chili, [zwiebeln, salzImChili], [], 8);

        var (links, rechts) = EinkaufRechnen.Vergleich(vorher, nachher);
        var (gleichLinks, gleichRechts) = EinkaufRechnen.Vergleich(vorher, vorher);

        Assert.Equal([("Mehl", (decimal?)1m, "g", true, true), ("Salz", null, null, false, false)], links.Select(z => (z.Bezeichnung, z.Menge, z.Einheit, z.VonHand, z.Geaendert)));
        Assert.Equal([("Zwiebeln", (decimal?)4m, "kg", false, true), ("salz", null, null, false, false)], rechts.Select(z => (z.Bezeichnung, z.Menge, z.Einheit, z.VonHand, z.Geaendert)));
        Assert.All(gleichLinks.Concat(gleichRechts), z => Assert.False(z.Geaendert));
    }

    [Fact]
    public void Gericht_Tauschen_setzt_das_neue_Rezept()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var chili = NeuesRezept("Chili sin Carne");
        var mahlzeit = Mahlzeit(Dienstag, FreizeitMahlzeitArt.Fruehstueck);
        var gericht = Vormerken(mahlzeit, pfannkuchen);

        gericht.Tauschen(chili.Id);

        Assert.Equal((chili.Id, mahlzeit.ProgrammpunktId, Freizeit, Org), (gericht.RezeptId, gericht.ProgrammpunktId, gericht.FreizeitId, gericht.OrganisationId));
    }

    // ---------- Hand-Posten ----------

    [Fact]
    public void Einkaufsposten_FeldAendern_normalisiert_und_kennt_den_Konflikt()
    {
        var posten = Einkaufsposten.Anlegen(Guid.CreateVersion7(), Org, Freizeit, "  Äpfel ", 3m, " kg ", Dienstag);
        Assert.Equal(("Äpfel", "3", "kg", "2027-08-03"), (posten.Wert(EinkaufspostenFeld.Bezeichnung), posten.Wert(EinkaufspostenFeld.Menge), posten.Wert(EinkaufspostenFeld.Einheit), posten.Wert(EinkaufspostenFeld.Tag)));

        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Menge, "3", "2.5"));
        Assert.Equal((decimal?)2.5m, posten.Menge);
        Assert.False(posten.FeldAendern(EinkaufspostenFeld.Menge, "3", "4"));
        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Menge, "2.5", ""));
        Assert.Null(posten.Menge);
        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Tag, "2027-08-03", ""));
        Assert.Null(posten.Tag);
        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Tag, null, "2027-08-02"));
        Assert.Equal((DateOnly?)Montag, posten.Tag);
        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Bezeichnung, "Äpfel", " Birnen "));
        Assert.Equal("Birnen", posten.Bezeichnung);
        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Einheit, "kg", " "));
        Assert.Null(posten.Einheit);
    }

    [Fact]
    public void MengeVonHand_Setzen_ohne_Nullen()
    {
        var gericht = Vormerken(Mahlzeit(Dienstag, FreizeitMahlzeitArt.Fruehstueck), NeuesRezept("Pfannkuchen"));
        var hand = MengeVonHand.Anlegen(Guid.CreateVersion7(), Org, Freizeit, gericht.Id, Guid.CreateVersion7(), 1.500m);

        Assert.Equal("1.5", hand.Menge.ToString(CultureInfo.InvariantCulture));
        hand.Setzen(2.000m);
        Assert.Equal("2", hand.Menge.ToString(CultureInfo.InvariantCulture));
    }

    // ---------- Formprüfung ----------

    [Fact]
    public void GerichtVormerken_verlangt_Mahlzeit_und_Rezept()
    {
        var fehler = new GerichtVormerkenValidator().Validate(new GerichtVormerkenCommand(Freizeit, null, null)).Errors;

        Assert.Equal([("programmpunktId", "Mahlzeit fehlt."), ("rezeptId", "Rezept fehlt.")], fehler.Select(f => (f.PropertyName, f.ErrorMessage)));
        Assert.True(new GerichtVormerkenValidator().Validate(new GerichtVormerkenCommand(Freizeit, Guid.CreateVersion7(), Guid.CreateVersion7())).IsValid);
        Assert.Equal("rezeptId", Assert.Single(new GerichtTauschenValidator().Validate(new GerichtTauschenCommand(Freizeit, Guid.CreateVersion7(), 1, null)).Errors).PropertyName);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("0", false)]
    [InlineData("x", false)]
    [InlineData("1.000", false)]
    public void HandMenge_prueft_die_Menge_leer_heisst_zurueck_zur_berechneten(string? menge, bool gueltig)
    {
        var ergebnis = new HandMengeAendernValidator().Validate(new HandMengeAendernCommand(Freizeit, Guid.CreateVersion7(), Guid.CreateVersion7(), HandMengeFeld.Menge, 0, null, menge));

        Assert.Equal(gueltig, ergebnis.IsValid);
        if (!gueltig)
        {
            Assert.Equal("menge", Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    [Theory]
    [InlineData(null, null, null, null, "bezeichnung")]
    [InlineData(" ", null, null, null, "bezeichnung")]
    [InlineData("Äpfel", "x", null, null, "menge")]
    [InlineData("Äpfel", null, "123456789012345678901234567890x", null, "einheit")]
    [InlineData("Äpfel", null, null, "03.08.2027", "tag")]
    [InlineData("Äpfel", null, null, "morgen", "tag")]
    public void EinkaufspostenAnlegen_verlangt_eine_Bezeichnung_und_gueltige_Felder(string? bezeichnung, string? menge, string? einheit, string? tag, string feld)
    {
        var ergebnis = new EinkaufspostenAnlegenValidator().Validate(new EinkaufspostenAnlegenCommand(Freizeit, bezeichnung, menge, einheit, tag));

        Assert.Equal(feld, Assert.Single(ergebnis.Errors).PropertyName);
    }

    [Fact]
    public void EinkaufspostenAnlegen_und_Aendern_gueltig()
    {
        Assert.True(new EinkaufspostenAnlegenValidator().Validate(new EinkaufspostenAnlegenCommand(Freizeit, "Äpfel", "3", "kg", "2027-08-03")).IsValid);
        Assert.True(new EinkaufspostenAnlegenValidator().Validate(new EinkaufspostenAnlegenCommand(Freizeit, "Kaffee", "", " ", "")).IsValid);

        static bool Gueltig(EinkaufspostenFeld feld, string? wert) =>
            new EinkaufspostenFeldAendernValidator().Validate(new EinkaufspostenFeldAendernCommand(Freizeit, Guid.CreateVersion7(), feld, 0, null, wert)).IsValid;

        Assert.False(Gueltig(EinkaufspostenFeld.Bezeichnung, ""));
        Assert.True(Gueltig(EinkaufspostenFeld.Tag, ""));
        Assert.False(Gueltig(EinkaufspostenFeld.Tag, "2027-13-01"));
        Assert.False(Gueltig(EinkaufspostenFeld.Menge, "-1"));
        Assert.True(Gueltig(EinkaufspostenFeld.Einheit, "kg"));
        Assert.False(Gueltig((EinkaufspostenFeld)99, "x"));
    }

    [Fact]
    public void Kueche_Entitaeten_von_Story_4_2_haben_keine_Eigenschaft_PersonId()
    {
        Assert.Null(typeof(Gericht).GetProperty("PersonId"));
        Assert.Null(typeof(MengeVonHand).GetProperty("PersonId"));
        Assert.Null(typeof(Einkaufsposten).GetProperty("PersonId"));
    }

    // ---------- Datenbank: Mandantenfilter und Cascade ----------

    [Fact]
    public async Task Mandantenfilter_Gerichte_Hand_Mengen_und_Hand_Posten_nur_im_eigenen_Mandanten_ein_geloeschtes_Rezept_nimmt_alles_mit()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await DatenAsync(factory, orgA);
        var b = await DatenAsync(factory, orgB);

        Assert.Equal([a.Gericht], await LesenAsync(factory, orgA, db => db.Gerichte.Select(g => g.Id).ToListAsync(Abbruch)));
        Assert.Equal([a.Hand], await LesenAsync(factory, orgA, db => db.MengenVonHand.Select(h => h.Id).ToListAsync(Abbruch)));
        Assert.Equal([a.Posten], await LesenAsync(factory, orgA, db => db.Einkaufsposten.Select(p => p.Id).ToListAsync(Abbruch)));
        Assert.Equal([b.Gericht], await LesenAsync(factory, orgB, db => db.Gerichte.Select(g => g.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Gerichte.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.MengenVonHand.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Einkaufsposten.ToListAsync(Abbruch)));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgA);
            var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
            db.Rezepte.Remove(await db.Rezepte.SingleAsync(Abbruch));
            await db.SaveChangesAsync(Abbruch);
        }

        Assert.Empty(await LesenAsync(factory, orgA, db => db.Gerichte.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, orgA, db => db.MengenVonHand.ToListAsync(Abbruch)));
        Assert.Single(await LesenAsync(factory, orgA, db => db.Einkaufsposten.ToListAsync(Abbruch)));
        Assert.Single(await LesenAsync(factory, orgB, db => db.MengenVonHand.ToListAsync(Abbruch)));
    }

    private static async Task<(Guid Gericht, Guid Hand, Guid Posten)> DatenAsync(ZeltlotseApiFactory factory, Guid organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
        var freizeit = Guid.CreateVersion7();
        var rezept = Rezept.Anlegen(Guid.CreateVersion7(), organisation, freizeit, "Pfannkuchen", 4);
        var zutat = Zutat.Anlegen(Guid.CreateVersion7(), organisation, freizeit, rezept.Id, "Mehl", 400m, "g");
        var gericht = Gericht.Vormerken(Guid.CreateVersion7(), organisation, freizeit, Guid.CreateVersion7(), rezept.Id);
        var hand = MengeVonHand.Anlegen(Guid.CreateVersion7(), organisation, freizeit, gericht.Id, zutat.Id, 500m);
        var posten = Einkaufsposten.Anlegen(Guid.CreateVersion7(), organisation, freizeit, "Äpfel", 3m, "kg", null);
        db.Rezepte.Add(rezept);
        db.Zutaten.Add(zutat);
        db.Gerichte.Add(gericht);
        db.MengenVonHand.Add(hand);
        db.Einkaufsposten.Add(posten);
        await db.SaveChangesAsync(Abbruch);
        return (gericht.Id, hand.Id, posten.Id);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<KuecheLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<KuecheLeseDbContext>());
    }
}
