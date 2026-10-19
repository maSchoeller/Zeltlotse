using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Testing;
using Rezept = Zeltlotse.Kueche.Rezepte.Rezept;
using Zutat = Zeltlotse.Kueche.Rezepte.Zutat;

namespace Zeltlotse.Kueche.Tests;

/// <summary>
/// Story 4-1, Rezepte mit Skalierung (CAP-28, FR-46, AD-6, AD-10, AD-19, AD-20): Rezept und Zutat mit Feldänderung nach der Konfliktregel,
/// Normalisierung von Zahlen und Allergenen, die Formprüfung, die Hochrechnung samt Rundung, die Merkmale der Commands, der Mandantenfilter und die
/// Kopie der Allergene in den API-Verträgen.
/// </summary>
public sealed class RezeptTests(PostgresContainer postgres)
{
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Rezept NeuesRezept(string name = "Pfannkuchen", int basis = 4) =>
        Rezept.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), name, basis);

    private static Zutat NeueZutat(string bezeichnung = "Mehl", decimal? menge = 500m, string? einheit = "g") =>
        Zutat.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), bezeichnung, menge, einheit);

    // ---------- Rezept ----------

    [Fact]
    public void Rezept_Anlegen_trimmt_den_Namen_und_hat_keine_Zubereitung()
    {
        var rezept = Rezept.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "  Chili sin Carne ", 4);

        Assert.Equal(("Chili sin Carne", 4, (string?)null), (rezept.Name, rezept.BasisPersonen, rezept.Zubereitung));
        Assert.Equal(("Chili sin Carne", "4", (string?)null), (rezept.Wert(RezeptFeld.Name), rezept.Wert(RezeptFeld.BasisPersonen), rezept.Wert(RezeptFeld.Zubereitung)));
    }

    [Fact]
    public void Rezept_FeldAendern_mit_passendem_alten_Wert_speichert_normalisiert_ein_anderer_alter_Wert_ist_ein_Konflikt()
    {
        var rezept = NeuesRezept();

        Assert.True(rezept.FeldAendern(RezeptFeld.BasisPersonen, " 4 ", " 6 "));
        Assert.Equal((6, "6"), (rezept.BasisPersonen, rezept.Wert(RezeptFeld.BasisPersonen)));
        Assert.False(rezept.FeldAendern(RezeptFeld.BasisPersonen, "4", "8"));
        Assert.Equal(6, rezept.BasisPersonen);

        Assert.True(rezept.FeldAendern(RezeptFeld.Name, "Pfannkuchen", "  Eierkuchen "));
        Assert.Equal("Eierkuchen", rezept.Name);
        Assert.False(rezept.FeldAendern(RezeptFeld.Name, "Pfannkuchen", "Crêpes"));

        Assert.True(rezept.FeldAendern(RezeptFeld.Zubereitung, null, "Alles verrühren."));
        Assert.Equal("Alles verrühren.", rezept.Zubereitung);
        Assert.True(rezept.FeldAendern(RezeptFeld.Zubereitung, "Alles verrühren.", "   "));
        Assert.Null(rezept.Zubereitung);
        // Leer und null sind derselbe alte Wert.
        Assert.True(rezept.FeldAendern(RezeptFeld.Zubereitung, "", "Backen."));
    }

    // ---------- Zutat ----------

    [Fact]
    public void Zutat_Anlegen_trimmt_und_leere_Einheit_heisst_ohne_Einheit()
    {
        var zutat = NeueZutat("  Mehl ", 1.5m, "  ");

        Assert.Equal(("Mehl", (decimal?)1.5m, (string?)null, (string?)null), (zutat.Bezeichnung, zutat.Menge, zutat.Einheit, zutat.Allergene));
        Assert.Equal("1,5", zutat.Wert(ZutatFeld.Menge));
    }

    [Fact]
    public void Zutat_ohne_Menge_hat_keinen_Mengenwert()
    {
        var salz = NeueZutat("Salz", null, null);

        Assert.Null(salz.Menge);
        Assert.Null(salz.Wert(ZutatFeld.Menge));
    }

    [Theory]
    [InlineData("1.5", "1,5")]
    [InlineData("1,5", "1,5")]
    [InlineData(" 300 ", "300")]
    [InlineData("2,500", "2,5")]
    [InlineData("0,125", "0,125")]
    [InlineData("0.125", "0,125")]
    [InlineData("1000", "1000")]
    [InlineData("1000000", "1000000")]
    public void Zutat_Menge_wird_deutsch_mit_Komma_ohne_Null_Nachkommastellen_gespeichert_und_der_naechste_alte_Wert_passt(string eingabe, string gespeichert)
    {
        var zutat = NeueZutat(menge: null);

        Assert.True(zutat.FeldAendern(ZutatFeld.Menge, null, eingabe));
        Assert.Equal(gespeichert, zutat.Wert(ZutatFeld.Menge));
        Assert.True(zutat.FeldAendern(ZutatFeld.Menge, gespeichert, ""));
        Assert.Null(zutat.Menge);
    }

    [Fact]
    public void Zutat_alter_Wert_der_Menge_passt_in_beiden_Schreibweisen_ein_anderer_ist_ein_Konflikt()
    {
        var zutat = NeueZutat(menge: 1.5m);

        Assert.False(zutat.FeldAendern(ZutatFeld.Menge, "2", "3"));
        Assert.True(zutat.FeldAendern(ZutatFeld.Menge, "1.5", "2"));
        Assert.Equal(2m, zutat.Menge);
    }

    [Fact]
    public void Zutat_Allergene_werden_in_Katalogreihenfolge_ohne_Doppelte_gespeichert_leer_heisst_keine()
    {
        var zutat = NeueZutat();

        Assert.True(zutat.FeldAendern(ZutatFeld.Allergene, null, "senf,milch,milch"));
        Assert.Equal("milch,senf", zutat.Allergene);
        // Der alte Wert passt auch in anderer Reihenfolge.
        Assert.True(zutat.FeldAendern(ZutatFeld.Allergene, "senf, milch", "gluten,eier"));
        Assert.Equal("gluten,eier", zutat.Wert(ZutatFeld.Allergene));
        Assert.False(zutat.FeldAendern(ZutatFeld.Allergene, "milch,senf", ""));
        Assert.True(zutat.FeldAendern(ZutatFeld.Allergene, "gluten,eier", " , "));
        Assert.Null(zutat.Allergene);
    }

    [Fact]
    public void Zutat_Bezeichnung_und_Einheit_aendern_mit_Konfliktregel()
    {
        var zutat = NeueZutat();

        Assert.True(zutat.FeldAendern(ZutatFeld.Bezeichnung, "Mehl", " Weizenmehl "));
        Assert.Equal("Weizenmehl", zutat.Bezeichnung);
        Assert.False(zutat.FeldAendern(ZutatFeld.Einheit, "kg", "g"));
        Assert.True(zutat.FeldAendern(ZutatFeld.Einheit, "g", " kg "));
        Assert.Equal("kg", zutat.Einheit);
        Assert.True(zutat.FeldAendern(ZutatFeld.Einheit, "kg", ""));
        Assert.Null(zutat.Einheit);
    }

    [Fact]
    public void Eine_neue_Zutat_liegt_in_der_Reihenfolge_der_Kennungen_immer_hinter_der_letzten_auch_bei_stehender_Uhr()
    {
        var jetzt = new DateTimeOffset(2027, 8, 2, 8, 0, 0, TimeSpan.Zero);
        Guid? letzte = null;
        for (var i = 0; i < 50; i++)
        {
            var neu = ZutatAnlegenHandler.NeueKennung(jetzt, letzte);
            Assert.True(letzte is null || neu.CompareTo(letzte.Value) > 0);
            Assert.Equal(7, neu.Version);
            letzte = neu;
        }

        // Eine spätere Uhr bleibt bei ihrer eigenen Zeit.
        var spaeter = ZutatAnlegenHandler.NeueKennung(jetzt.AddMinutes(1), letzte);
        Assert.True(spaeter.CompareTo(letzte!.Value) > 0);
    }

    // ---------- Hochrechnung ----------

    [Theory]
    [InlineData("300", 3, 60, "6000")]
    [InlineData("1.5", 4, 6, "2.25")]
    [InlineData("1", 3, 1, "0.33")]
    [InlineData("2", 3, 1, "0.67")]
    [InlineData("0.005", 1, 1, "0.01")]
    [InlineData("0.125", 1, 1, "0.13")]
    public void Hochrechnen_rechnet_Menge_mal_Personen_durch_Basis_auf_zwei_Stellen_kaufmaennisch_gerundet(string menge, int basis, int personen, string erwartet)
    {
        var ergebnis = KuecheWerte.Hochrechnen(decimal.Parse(menge, System.Globalization.CultureInfo.InvariantCulture), basis, personen);

        Assert.Equal(decimal.Parse(erwartet, System.Globalization.CultureInfo.InvariantCulture), ergebnis);
    }

    [Fact]
    public void Hochrechnen_ohne_Menge_oder_ohne_Personen_ist_null()
    {
        Assert.Null(KuecheWerte.Hochrechnen(null, 4, 6));
        Assert.Null(KuecheWerte.Hochrechnen(300m, 4, 0));
    }

    [Fact]
    public void Hochrechnen_hat_keine_unnoetigen_Nachkommastellen()
    {
        Assert.Equal("6000", KuecheWerte.Hochrechnen(300.000m, 3, 60)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData(null, "4", "name", "Name fehlt.")]
    [InlineData("   ", "4", "name", "Name fehlt.")]
    [InlineData("Chili", null, "basisPersonen", "Basis-Personenzahl fehlt.")]
    [InlineData("Chili", " ", "basisPersonen", "Basis-Personenzahl fehlt.")]
    [InlineData("Chili", "0", "basisPersonen", "Nur eine ganze Zahl von 1 bis 1000.")]
    [InlineData("Chili", "-2", "basisPersonen", "Nur eine ganze Zahl von 1 bis 1000.")]
    [InlineData("Chili", "abc", "basisPersonen", "Nur eine ganze Zahl von 1 bis 1000.")]
    [InlineData("Chili", "1001", "basisPersonen", "Nur eine ganze Zahl von 1 bis 1000.")]
    [InlineData("Chili", "4,5", "basisPersonen", "Nur eine ganze Zahl von 1 bis 1000.")]
    public void RezeptAnlegen_verlangt_Name_und_gueltige_Basis(string? name, string? basis, string feld, string text)
    {
        var ergebnis = new RezeptAnlegenValidator().Validate(new RezeptAnlegenCommand(Guid.CreateVersion7(), name, basis));

        var fehler = Assert.Single(ergebnis.Errors);
        Assert.Equal((feld, text), (fehler.PropertyName, fehler.ErrorMessage));
    }

    [Fact]
    public void RezeptAnlegen_gueltig_mit_Grenzen()
    {
        Assert.True(new RezeptAnlegenValidator().Validate(new RezeptAnlegenCommand(Guid.CreateVersion7(), $" {new string('a', 200)} ", "1")).IsValid);
        Assert.True(new RezeptAnlegenValidator().Validate(new RezeptAnlegenCommand(Guid.CreateVersion7(), "Chili", " 1000 ")).IsValid);
        var zuLang = new RezeptAnlegenValidator().Validate(new RezeptAnlegenCommand(Guid.CreateVersion7(), new string('a', 201), "4"));
        Assert.Equal(("name", "Höchstens 200 Zeichen."), (Assert.Single(zuLang.Errors).PropertyName, zuLang.Errors[0].ErrorMessage));
    }

    [Theory]
    [InlineData(RezeptFeld.Name, "", false)]
    [InlineData(RezeptFeld.Name, "Chili", true)]
    [InlineData(RezeptFeld.BasisPersonen, "0", false)]
    [InlineData(RezeptFeld.BasisPersonen, "", false)]
    [InlineData(RezeptFeld.BasisPersonen, "12", true)]
    [InlineData(RezeptFeld.Zubereitung, "", true)]
    [InlineData(RezeptFeld.Zubereitung, null, true)]
    public void RezeptFeldAendern_prueft_den_neuen_Wert(RezeptFeld feld, string? wert, bool gueltig)
    {
        var ergebnis = new RezeptFeldAendernValidator().Validate(new RezeptFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, null, wert));

        Assert.Equal(gueltig, ergebnis.IsValid);
        if (!gueltig)
        {
            Assert.Equal(RezeptFelder.JsonName(feld), Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    [Fact]
    public void RezeptFeldAendern_Zubereitung_hoechstens_8000_Zeichen_unbekanntes_Feld_ist_ein_Fehler()
    {
        static FluentValidation.Results.ValidationResult Pruefen(RezeptFeld feld, string? wert) =>
            new RezeptFeldAendernValidator().Validate(new RezeptFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, null, wert));

        Assert.True(Pruefen(RezeptFeld.Zubereitung, new string('z', 8000)).IsValid);
        Assert.Equal("Höchstens 8000 Zeichen.", Assert.Single(Pruefen(RezeptFeld.Zubereitung, new string('z', 8001)).Errors).ErrorMessage);
        Assert.Equal("feld", Assert.Single(Pruefen((RezeptFeld)99, "x").Errors).PropertyName);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,2345")]
    [InlineData("x")]
    [InlineData("1000000,001")]
    [InlineData("1e3")]
    [InlineData("1.000,5")]
    [InlineData("1.000")]
    [InlineData("12.500")]
    [InlineData("1.000.000")]
    public void Ungueltige_Menge_ist_ein_Fehler_am_Feld_menge(string menge)
    {
        var anlegen = new ZutatAnlegenValidator().Validate(new ZutatAnlegenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "Mehl", menge, "g"));
        var aendern = ZutatPruefen(ZutatFeld.Menge, menge);

        Assert.Equal(("menge", "Nur eine Zahl größer 0 bis 1000000 mit höchstens 3 Nachkommastellen, keine Tausenderpunkte."), (Assert.Single(anlegen.Errors).PropertyName, anlegen.Errors[0].ErrorMessage));
        Assert.Equal("menge", Assert.Single(aendern.Errors).PropertyName);
    }

    [Fact]
    public void ZutatAnlegen_verlangt_eine_Bezeichnung_Menge_und_Einheit_sind_freiwillig()
    {
        static FluentValidation.Results.ValidationResult Anlegen(string? bezeichnung, string? menge, string? einheit) =>
            new ZutatAnlegenValidator().Validate(new ZutatAnlegenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), bezeichnung, menge, einheit));

        Assert.True(Anlegen("Salz", null, null).IsValid);
        Assert.True(Anlegen("Salz", "", " ").IsValid);
        Assert.True(Anlegen(new string('b', 200), "1.5", new string('e', 30)).IsValid);
        Assert.Equal(("bezeichnung", "Bezeichnung fehlt."), (Assert.Single(Anlegen(" ", "1", null).Errors).PropertyName, Anlegen(" ", "1", null).Errors[0].ErrorMessage));
        Assert.Equal(("bezeichnung", "Höchstens 200 Zeichen."), (Assert.Single(Anlegen(new string('b', 201), null, null).Errors).PropertyName, Anlegen(new string('b', 201), null, null).Errors[0].ErrorMessage));
        Assert.Equal(("einheit", "Höchstens 30 Zeichen."), (Assert.Single(Anlegen("Mehl", null, new string('e', 31)).Errors).PropertyName, Anlegen("Mehl", null, new string('e', 31)).Errors[0].ErrorMessage));
    }

    [Fact]
    public void ZutatFeldAendern_prueft_Bezeichnung_Einheit_und_Allergene()
    {
        Assert.False(ZutatPruefen(ZutatFeld.Bezeichnung, "").IsValid);
        Assert.True(ZutatPruefen(ZutatFeld.Einheit, "").IsValid);
        Assert.False(ZutatPruefen(ZutatFeld.Einheit, new string('e', 31)).IsValid);
        Assert.True(ZutatPruefen(ZutatFeld.Menge, "").IsValid);
        Assert.True(ZutatPruefen(ZutatFeld.Allergene, "senf,milch,milch").IsValid);
        Assert.True(ZutatPruefen(ZutatFeld.Allergene, null).IsValid);
        var unbekannt = Assert.Single(ZutatPruefen(ZutatFeld.Allergene, "milch,kaese").Errors);
        Assert.Equal(("allergene", "Unbekanntes Allergen: kaese."), (unbekannt.PropertyName, unbekannt.ErrorMessage));
        Assert.Equal("feld", Assert.Single(ZutatPruefen((ZutatFeld)99, "x").Errors).PropertyName);
    }

    private static FluentValidation.Results.ValidationResult ZutatPruefen(ZutatFeld feld, string? wert) =>
        new ZutatFeldAendernValidator().Validate(new ZutatFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, null, wert));

    // ---------- Merkmale der Commands ----------

    [Fact]
    public void Jeder_Benutzer_Command_gehoert_zur_Verpflegung_und_hat_keine_Ausnahme_nach_dem_Abschluss()
    {
        var commands = typeof(RezeptAnlegenCommand).Assembly.GetTypes().Where(t => typeof(IBenutzerCommand).IsAssignableFrom(t) && !t.IsInterface).ToList();

        Assert.Equal(
            [
                "EinkaufspostenAnlegenCommand", "EinkaufspostenFeldAendernCommand", "EinkaufspostenLoeschenCommand", "GerichtEntfernenCommand",
                "GerichtTauschenCommand", "GerichtVormerkenCommand", "HandMengeAendernCommand", "RezeptAnlegenCommand", "RezeptFeldAendernCommand",
                "RezeptLoeschenCommand", "ZutatAnlegenCommand", "ZutatFeldAendernCommand", "ZutatLoeschenCommand",
            ],
            commands.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(commands, t =>
        {
            Assert.True(typeof(ISchalterCommand).IsAssignableFrom(t), t.Name);
            Assert.True(typeof(IFreizeitCommand).IsAssignableFrom(t), t.Name);
            Assert.True(typeof(IModulCommand<KuecheDbContext>).IsAssignableFrom(t), t.Name);
            Assert.Empty(t.GetCustomAttributes(typeof(ErlaubtNachAbschlussAttribute), false));
        });
        Assert.Equal(FunktionsSchalter.Verpflegung, new ZutatLoeschenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()).Schalter);
    }

    [Fact]
    public void Kueche_Entitaeten_haben_keine_Eigenschaft_PersonId()
    {
        Assert.Null(typeof(Rezept).GetProperty("PersonId"));
        Assert.Null(typeof(Zutat).GetProperty("PersonId"));
    }

    // ---------- Allergene: Kopie in den API-Verträgen ----------

    [Fact]
    public void Die_Allergene_der_API_Vertraege_gleichen_dem_Fragenkatalog_in_Schluessel_Text_und_Reihenfolge()
    {
        Assert.Equal(
            Fragenkatalog.Allergene.Select(a => (a.Wert, a.Text)),
            Allergene.Alle.Select(a => (a.Wert, a.Text)));
        Assert.Equal(["Milch (inkl. Laktose)", "Senf"], Allergene.Texte("senf,milch,unbekannt"));
        // Story 4-2: kurze Namen für „enthält …“ und die Allergen-Übersicht.
        Assert.Equal(["Gluten", "Milch"], Allergene.Kurztexte(["milch", "gluten", "unbekannt"]));
        Assert.Equal(14, Allergene.Alle.Select(a => a.Kurz).Distinct().Count());
    }

    // ---------- Datenbank: Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen_Rezepte_und_Zutaten()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (rezeptA, zutatA) = await RezeptAsync(factory, orgA, "Chili sin Carne");
        var (rezeptB, zutatB) = await RezeptAsync(factory, orgB, "Pfannkuchen");

        Assert.Equal([rezeptA], await LesenAsync(factory, orgA, db => db.Rezepte.Select(r => r.Id).ToListAsync(Abbruch)));
        Assert.Equal([zutatA], await LesenAsync(factory, orgA, db => db.Zutaten.Select(z => z.Id).ToListAsync(Abbruch)));
        Assert.Equal([rezeptB], await LesenAsync(factory, orgB, db => db.Rezepte.Select(r => r.Id).ToListAsync(Abbruch)));
        Assert.Equal([zutatB], await LesenAsync(factory, orgB, db => db.Zutaten.Select(z => z.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Rezepte.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Zutaten.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
        Assert.Equal([rezeptB], await db.Rezepte.Select(r => r.Id).ToListAsync(Abbruch));
        Assert.Equal([zutatB], await db.Zutaten.Select(z => z.Id).ToListAsync(Abbruch));
    }

    [Fact]
    public async Task Ein_geloeschtes_Rezept_nimmt_seine_Zutaten_mit_und_die_Menge_bleibt_genau()
    {
        await using var factory = await FactoryAsync();
        var org = Guid.CreateVersion7();
        var (weg, _) = await RezeptAsync(factory, org, "Chili sin Carne");
        var (bleibt, zutat) = await RezeptAsync(factory, org, "Pfannkuchen");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(org);
            var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
            db.Rezepte.Remove(await db.Rezepte.SingleAsync(r => r.Id == weg, Abbruch));
            await db.SaveChangesAsync(Abbruch);
        }

        Assert.Equal([bleibt], await LesenAsync(factory, org, db => db.Rezepte.Select(r => r.Id).ToListAsync(Abbruch)));
        var uebrig = Assert.Single(await LesenAsync(factory, org, db => db.Zutaten.ToListAsync(Abbruch)));
        Assert.Equal((zutat, (decimal?)0.125m, "milch,senf"), (uebrig.Id, uebrig.Menge, uebrig.Allergene));
        Assert.NotEqual(0u, uebrig.Version);
    }

    // ---------- Helfer ----------

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        return factory;
    }

    /// <summary>Ein Rezept mit einer Zutat (0,125 l Milch mit Milch und Senf) in einer neuen Freizeit des Mandanten.</summary>
    private static async Task<(Guid Rezept, Guid Zutat)> RezeptAsync(ZeltlotseApiFactory factory, Guid organisation, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
        var freizeit = Guid.CreateVersion7();
        var rezept = Rezept.Anlegen(Guid.CreateVersion7(), organisation, freizeit, name, 4);
        var zutat = Zutat.Anlegen(Guid.CreateVersion7(), organisation, freizeit, rezept.Id, "Milch", 0.125m, "l");
        zutat.FeldAendern(ZutatFeld.Allergene, null, "senf,milch");
        db.Rezepte.Add(rezept);
        db.Zutaten.Add(zutat);
        await db.SaveChangesAsync(Abbruch);
        return (rezept.Id, zutat.Id);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<KuecheLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<KuecheLeseDbContext>());
    }
}
