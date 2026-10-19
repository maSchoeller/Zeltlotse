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
/// Story 4-3, Einkauf freigeben und abhaken (CAP-30, FR-50, FR-51, AD-6, AD-19): die zwei Ja/Nein-Felder <c>freigegeben</c> und <c>gekauft</c> an
/// Hand-Posten und am Stand eines berechneten Postens (unabhängig voneinander, Konflikt nach dem alten Wert), die Posten der Liste mit ihrem Stand, der
/// Filter „nur freigegeben“ für die Seite der Einkaufenden, die Tausch-Vorschau ohne gekaufte Posten, die Formprüfung und der Mandantenfilter.
/// </summary>
public sealed class EinkaufFreigabeTests(PostgresContainer postgres)
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Freizeit = Guid.CreateVersion7();
    private static readonly DateOnly Dienstag = new(2027, 8, 3);
    private static readonly DateOnly Mittwoch = new(2027, 8, 4);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Rezept NeuesRezept(string name, int basis = 4) => Rezept.Anlegen(Guid.CreateVersion7(), Org, Freizeit, name, basis);

    private static Zutat NeueZutat(Rezept rezept, string bezeichnung, decimal? menge, string? einheit) =>
        Zutat.Anlegen(Guid.CreateVersion7(), Org, Freizeit, rezept.Id, bezeichnung, menge, einheit);

    private static FreizeitMahlzeit Fruehstueck(DateOnly tag) => new(Guid.CreateVersion7(), tag, new TimeOnly(8, 0), "Frühstück", FreizeitMahlzeitArt.Fruehstueck);

    private static Gericht Vormerken(FreizeitMahlzeit mahlzeit, Rezept rezept) =>
        Gericht.Vormerken(Guid.CreateVersion7(), Org, Freizeit, mahlzeit.ProgrammpunktId, rezept.Id);

    private static PostenStand Stand(Gericht gericht, Zutat zutat, bool freigegeben, bool gekauft)
    {
        var stand = PostenStand.Anlegen(Guid.CreateVersion7(), Org, Freizeit, gericht.Id, zutat.Id);
        stand.Freigeben(freigegeben);
        Assert.True(stand.FeldAendern(StandFeld.Gekauft, "false", gekauft ? "true" : "false"));
        return stand;
    }

    private static string Zeile(EinkaufslistenPosten p) => $"{p.Bezeichnung}{(p.Freigegeben ? " freigegeben" : "")}{(p.Gekauft ? " gekauft" : "")}";

    // ---------- Ja/Nein-Felder ----------

    [Fact]
    public void Einkaufsposten_freigegeben_und_gekauft_sind_true_false_unabhaengig_und_kennen_den_Konflikt()
    {
        var posten = Einkaufsposten.Anlegen(Guid.CreateVersion7(), Org, Freizeit, "Äpfel", 3m, "kg", Dienstag);
        Assert.Equal(("false", "false"), (posten.Wert(EinkaufspostenFeld.Freigegeben), posten.Wert(EinkaufspostenFeld.Gekauft)));

        // Anderer alter Wert (auch keiner) ist ein Konflikt, nichts ändert sich.
        Assert.False(posten.FeldAendern(EinkaufspostenFeld.Freigegeben, "true", "true"));
        Assert.False(posten.FeldAendern(EinkaufspostenFeld.Freigegeben, null, "true"));
        Assert.False(posten.Freigegeben);

        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Gekauft, " false ", "true"));
        Assert.Equal((false, true), (posten.Freigegeben, posten.Gekauft));
        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Freigegeben, "false", "true"));
        Assert.True(posten.FeldAendern(EinkaufspostenFeld.Freigegeben, "true", "false"));
        // Keine Reihenfolge und kein Rückfall: gekauft bleibt, auch ohne Freigabe.
        Assert.Equal((false, true), (posten.Freigegeben, posten.Gekauft));
        Assert.Equal("true", posten.Wert(EinkaufspostenFeld.Gekauft));

        posten.Freigeben(true);
        Assert.Equal((true, true), (posten.Freigegeben, posten.Gekauft));
    }

    [Fact]
    public void PostenStand_beginnt_offen_und_nicht_freigegeben_ohne_Zeile_gilt_false()
    {
        var gericht = Vormerken(Fruehstueck(Dienstag), NeuesRezept("Pfannkuchen"));
        var zutat = Guid.CreateVersion7();

        var stand = PostenStand.Anlegen(Guid.CreateVersion7(), Org, Freizeit, gericht.Id, zutat);

        Assert.Equal((Org, Freizeit, gericht.Id, zutat, false, false), (stand.OrganisationId, stand.FreizeitId, stand.GerichtId, stand.ZutatId, stand.Freigegeben, stand.Gekauft));
        Assert.Equal("false", PostenStand.Wert(null, StandFeld.Freigegeben));
        Assert.Equal("false", PostenStand.Wert(null, StandFeld.Gekauft));
        Assert.False(stand.FeldAendern(StandFeld.Gekauft, "true", "true"));
        Assert.True(stand.FeldAendern(StandFeld.Gekauft, "false", "true"));
        Assert.Equal(("false", "true"), (PostenStand.Wert(stand, StandFeld.Freigegeben), PostenStand.Wert(stand, StandFeld.Gekauft)));
        Assert.True(stand.FeldAendern(StandFeld.Freigegeben, "false", "true"));
        Assert.True(stand.FeldAendern(StandFeld.Gekauft, "true", "false"));
        Assert.Equal((true, false), (stand.Freigegeben, stand.Gekauft));
        stand.Freigeben(false);
        Assert.False(stand.Freigegeben);
    }

    [Fact]
    public void Kueche_Entitaet_PostenStand_hat_keine_Eigenschaft_PersonId()
    {
        Assert.Null(typeof(PostenStand).GetProperty("PersonId"));
        Assert.Null(typeof(Einkaufsposten).GetProperty("PersonId"));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData(HandMengeFeld.Freigegeben, "true", null)]
    [InlineData(HandMengeFeld.Gekauft, "false", null)]
    [InlineData(HandMengeFeld.Gekauft, "ja", "gekauft")]
    [InlineData(HandMengeFeld.Freigegeben, "", "freigegeben")]
    [InlineData(HandMengeFeld.Freigegeben, null, "freigegeben")]
    [InlineData(HandMengeFeld.Gekauft, "1", "gekauft")]
    public void HandMenge_Felder_freigegeben_und_gekauft_nur_true_oder_false(HandMengeFeld feld, string? wert, string? fehlerFeld)
    {
        var ergebnis = new HandMengeAendernValidator().Validate(new HandMengeAendernCommand(Freizeit, Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, "false", wert));

        if (fehlerFeld is null)
        {
            Assert.True(ergebnis.IsValid);
        }
        else
        {
            Assert.Equal(fehlerFeld, Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    [Theory]
    [InlineData(EinkaufspostenFeld.Freigegeben, "true", true)]
    [InlineData(EinkaufspostenFeld.Gekauft, "false", true)]
    [InlineData(EinkaufspostenFeld.Gekauft, "ja", false)]
    [InlineData(EinkaufspostenFeld.Freigegeben, "", false)]
    public void Einkaufsposten_Felder_freigegeben_und_gekauft_nur_true_oder_false(EinkaufspostenFeld feld, string wert, bool gueltig)
    {
        var ergebnis = new EinkaufspostenFeldAendernValidator().Validate(new EinkaufspostenFeldAendernCommand(Freizeit, Guid.CreateVersion7(), feld, 0, "false", wert));

        Assert.Equal(gueltig, ergebnis.IsValid);
        if (!gueltig)
        {
            Assert.Equal(EinkaufspostenFelder.JsonName(feld), Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    [Fact]
    public void Tages_Freigabe_verlangt_freigegeben_und_einen_Tag_in_der_Form_JJJJ_MM_TT_oder_keinen()
    {
        static List<string> Fehler(string? tag, bool? freigegeben) =>
            [.. new EinkaufFreigebenValidator().Validate(new EinkaufFreigebenCommand(Freizeit, tag, freigegeben)).Errors.Select(f => f.PropertyName)];

        Assert.Empty(Fehler("2027-08-03", true));
        Assert.Empty(Fehler(null, false));
        Assert.Equal(["freigegeben"], Fehler("2027-08-03", null));
        Assert.Equal(["tag"], Fehler("03.08.2027", true));
        Assert.Equal(["tag", "freigegeben"], Fehler("morgen", null));
    }

    // ---------- Posten der Liste mit Stand ----------

    [Fact]
    public void Posten_tragen_den_Stand_ihrer_Zeile_ohne_Zeile_offen_und_nicht_freigegeben()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var mehl = NeueZutat(pfannkuchen, "Mehl", 400m, "g");
        var milch = NeueZutat(pfannkuchen, "Milch", 0.75m, "l");
        var salz = NeueZutat(pfannkuchen, "Salz", null, null);
        var gericht = Vormerken(Fruehstueck(Dienstag), pfannkuchen);

        var posten = EinkaufRechnen.Posten(pfannkuchen, [mehl, milch, salz], [], 8, [Stand(gericht, mehl, true, true), Stand(gericht, milch, false, true)]);

        Assert.Equal([(true, true), (false, true), (false, false)], posten.Select(p => (p.Freigegeben, p.Gekauft)));
        Assert.All(EinkaufRechnen.Posten(pfannkuchen, [mehl], [], 8), p => Assert.False(p.Freigegeben || p.Gekauft));
    }

    [Fact]
    public void Liste_traegt_Freigegeben_und_Gekauft_je_Posten_die_Einkaufsseite_zeigt_nur_freigegebene_ohne_Ersatzportionen()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var mehl = NeueZutat(pfannkuchen, "Mehl", 400m, "g");
        var milch = NeueZutat(pfannkuchen, "Milch", 0.75m, "l");
        var di = Fruehstueck(Dienstag);
        var mi = Fruehstueck(Mittwoch);
        var gerichtDi = Vormerken(di, pfannkuchen);
        var gerichtMi = Vormerken(mi, pfannkuchen);
        var aepfel = Einkaufsposten.Anlegen(Guid.CreateVersion7(), Org, Freizeit, "Äpfel", 3m, "kg", Dienstag);
        aepfel.Freigeben(true);
        var kaffee = Einkaufsposten.Anlegen(Guid.CreateVersion7(), Org, Freizeit, "Kaffee", null, null, null);
        GerichtDaten[] gerichte =
        [
            new(gerichtDi, di, pfannkuchen, [mehl, milch], [], [Stand(gerichtDi, mehl, true, true)]),
            new(gerichtMi, mi, pfannkuchen, [mehl, milch], [], [Stand(gerichtMi, milch, false, true)]),
        ];

        var tage = EinkaufRechnen.Tage(gerichte, [aepfel, kaffee], 4, [["gluten"]]);
        var einkauf = EinkaufRechnen.NurFreigegeben(tage);

        Assert.Equal(["Mehl freigegeben gekauft", "Milch", "Äpfel freigegeben"], tage[0].Posten.Select(Zeile));
        Assert.Equal(["Mehl", "Milch gekauft"], tage[1].Posten.Select(Zeile));
        Assert.Equal(["Kaffee"], tage[2].Posten.Select(Zeile));
        // Nur Dienstag hat Freigegebenes; Mittwoch (nur gekauft, nicht freigegeben) und „ohne Tag“ fehlen.
        var tag = Assert.Single(einkauf);
        Assert.Equal(Dienstag, tag.Tag);
        Assert.Equal(["Mehl freigegeben gekauft", "Äpfel freigegeben"], tag.Posten.Select(Zeile));
        Assert.Empty(tag.Ersatzportionen);
        Assert.Empty(EinkaufRechnen.NurFreigegeben([]));
    }

    // ---------- Tausch ----------

    [Fact]
    public void Tausch_Vorschau_zeigt_vorher_nur_nicht_gekaufte_Posten_und_zaehlt_die_gekauften()
    {
        var pfannkuchen = NeuesRezept("Pfannkuchen");
        var mehl = NeueZutat(pfannkuchen, "Mehl", 400m, "g");
        var milch = NeueZutat(pfannkuchen, "Milch", 0.75m, "l");
        var chili = NeuesRezept("Chili sin Carne");
        var bohnen = NeueZutat(chili, "Kidneybohnen", 500m, "g");
        var mahlzeit = Fruehstueck(Dienstag);
        var gericht = Vormerken(mahlzeit, pfannkuchen);
        var daten = new GerichtDaten(gericht, mahlzeit, pfannkuchen, [mehl, milch], [], [Stand(gericht, mehl, true, true), Stand(gericht, milch, true, false)]);

        var (vorher, nachher, schonGekauft) = EinkaufRechnen.Tauschvorschau(daten, chili, [bohnen], 4);
        var (gleichVorher, gleichNachher, gleichGekauft) = EinkaufRechnen.Tauschvorschau(daten, pfannkuchen, [mehl, milch], 4);

        Assert.Equal(["Milch"], vorher.Select(z => z.Bezeichnung));
        Assert.Equal(["Kidneybohnen"], nachher.Select(z => z.Bezeichnung));
        Assert.Equal(1, schonGekauft);
        // Gleiches Rezept: alles bleibt, der Vergleich zeigt beidseits nur das Offene.
        Assert.Equal(["Milch"], gleichVorher.Select(z => z.Bezeichnung));
        Assert.Equal(["Milch"], gleichNachher.Select(z => z.Bezeichnung));
        Assert.All(gleichVorher.Concat(gleichNachher), z => Assert.False(z.Geaendert));
        Assert.Equal(1, gleichGekauft);
    }

    // ---------- Datenbank: Mandantenfilter und Cascade ----------

    [Fact]
    public async Task Mandantenfilter_PostenStand_nur_im_eigenen_Mandanten_eindeutig_je_Gericht_und_Zutat_verschwindet_mit_dem_Gericht()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await DatenAsync(factory, orgA);
        var b = await DatenAsync(factory, orgB);

        Assert.Equal([a.Stand], await LesenAsync(factory, orgA, db => db.PostenStaende.Select(s => s.Id).ToListAsync(Abbruch)));
        Assert.Equal([b.Stand], await LesenAsync(factory, orgB, db => db.PostenStaende.Select(s => s.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.PostenStaende.ToListAsync(Abbruch)));

        // Eine zweite Zeile für dasselbe (Gericht, Zutat) verbietet der eindeutige Index.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgA);
            var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
            db.PostenStaende.Add(PostenStand.Anlegen(Guid.CreateVersion7(), orgA, a.Freizeit, a.Gericht, a.Zutat));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Abbruch));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgA);
            var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
            db.Gerichte.Remove(await db.Gerichte.SingleAsync(Abbruch));
            await db.SaveChangesAsync(Abbruch);
        }

        Assert.Empty(await LesenAsync(factory, orgA, db => db.PostenStaende.ToListAsync(Abbruch)));
        Assert.Single(await LesenAsync(factory, orgB, db => db.PostenStaende.ToListAsync(Abbruch)));
    }

    private static async Task<(Guid Freizeit, Guid Gericht, Guid Zutat, Guid Stand)> DatenAsync(ZeltlotseApiFactory factory, Guid organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
        var freizeit = Guid.CreateVersion7();
        var rezept = Rezept.Anlegen(Guid.CreateVersion7(), organisation, freizeit, "Pfannkuchen", 4);
        var zutat = Zutat.Anlegen(Guid.CreateVersion7(), organisation, freizeit, rezept.Id, "Mehl", 400m, "g");
        var gericht = Gericht.Vormerken(Guid.CreateVersion7(), organisation, freizeit, Guid.CreateVersion7(), rezept.Id);
        var stand = PostenStand.Anlegen(Guid.CreateVersion7(), organisation, freizeit, gericht.Id, zutat.Id);
        stand.Freigeben(true);
        db.Rezepte.Add(rezept);
        db.Zutaten.Add(zutat);
        db.Gerichte.Add(gericht);
        db.PostenStaende.Add(stand);
        await db.SaveChangesAsync(Abbruch);
        return (freizeit, gericht.Id, zutat.Id, stand.Id);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<KuecheLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<KuecheLeseDbContext>());
    }
}
