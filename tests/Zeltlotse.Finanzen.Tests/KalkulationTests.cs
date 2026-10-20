using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Testing;

namespace Zeltlotse.Finanzen.Tests;

using Einnahmeposten = Zeltlotse.Finanzen.Kalkulation.Einnahmeposten;
using Kalkulation = Zeltlotse.Finanzen.Kalkulation.Kalkulation;
using KategoriePosten = Zeltlotse.Finanzen.Kalkulation.KategoriePosten;

/// <summary>
/// Story 5-1, Kalkulation und Beitragsrechner (CAP-32, FR-54 bis FR-57, AD-6, AD-19, AD-20): die eine Rechenregel mit allen Zeilen der Matrix, die
/// Wert-Parser für Planzahlen und Beträge, die Feldänderung nach der Konfliktregel, die Formprüfung, die Merkmale der Commands und der Mandantenfilter.
/// </summary>
public sealed class KalkulationTests(PostgresContainer postgres)
{
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Kostenansatz Null => new(0m, 0m);

    /// <summary>Die sechs Kategorien in fester Reihenfolge; nur Unterkunft und Verpflegung tragen Werte.</summary>
    private static Kostenansatz[] Kategorien(Kostenansatz unterkunft, Kostenansatz verpflegung) => [unterkunft, verpflegung, Null, Null, Null, Null];

    // ---------- Rechenregel ----------

    [Fact]
    public void Beitrag_rechnen_12_Tage_40_TN_10_MA_ergibt_275_Euro()
    {
        var unterkunft = new Kostenansatz(2000m, 10m);
        var verpflegung = new Kostenansatz(0m, 7m);

        var ergebnis = Beitragsrechnung.Rechnen(40, 10, 12, Kategorien(unterkunft, verpflegung), [1200m]);

        Assert.Equal(4200m, Beitragsrechnung.Kosten(verpflegung, 40, 10, 12));
        Assert.Equal(8000m, Beitragsrechnung.Kosten(unterkunft, 40, 10, 12));
        Assert.Equal(new Beitragsergebnis(12200m, 1200m, 275.00m), ergebnis);
    }

    [Fact]
    public void Rundung_1000_Euro_bei_3_TN_ergibt_333_33_und_ein_halber_Cent_rundet_kaufmaennisch_auf()
    {
        Assert.Equal(333.33m, Beitragsrechnung.Rechnen(3, 0, 1, Kategorien(new Kostenansatz(1000m, 0m), Null), []).Teilnehmerbeitrag);
        Assert.Equal(666.67m, Beitragsrechnung.Rechnen(3, 0, 1, Kategorien(new Kostenansatz(2000m, 0m), Null), []).Teilnehmerbeitrag);
        // 0,05 ÷ 2 = 0,025 → 0,03 (nicht 0,02 wie beim Runden auf gerade).
        Assert.Equal(0.03m, Beitragsrechnung.Rechnen(2, 0, 1, Kategorien(new Kostenansatz(0.05m, 0m), Null), []).Teilnehmerbeitrag);
    }

    [Fact]
    public void Ohne_Teilnehmende_gibt_es_keinen_Beitrag()
    {
        var ergebnis = Beitragsrechnung.Rechnen(0, 10, 12, Kategorien(new Kostenansatz(2000m, 10m), Null), [100m]);

        Assert.Equal(new Beitragsergebnis(3200m, 100m, null), ergebnis);
    }

    [Fact]
    public void Einnahmen_ueber_den_Kosten_ergeben_0_Euro()
    {
        var ergebnis = Beitragsrechnung.Rechnen(10, 2, 3, Kategorien(new Kostenansatz(100m, 0m), Null), [80m, 50m]);

        Assert.Equal(new Beitragsergebnis(100m, 130m, 0m), ergebnis);
    }

    [Fact]
    public void Mitarbeitende_zaehlen_als_Personen_und_ihre_Kosten_tragen_die_Teilnehmenden()
    {
        // 2 TN + 2 MA, 1 Tag, 10 € pro Person und Tag: 40 €, je TN 20 €.
        Assert.Equal(20m, Beitragsrechnung.Rechnen(2, 2, 1, Kategorien(Null, new Kostenansatz(0m, 10m)), []).Teilnehmerbeitrag);
    }

    [Fact]
    public void Tage_zaehlen_ersten_und_letzten_Tag_mit()
    {
        Assert.Equal(12, Beitragsrechnung.Tage(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13)));
        Assert.Equal(1, Beitragsrechnung.Tage(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 2)));
    }

    [Fact]
    public void Kategorien_in_fester_Reihenfolge_mit_Namen_und_Lesen_aus_der_Route()
    {
        Assert.Equal(
            ["Unterkunft", "Verpflegung", "Fahrt", "Programm und Material", "Mitarbeitende", "Sonstiges"],
            Kostenkategorien.Alle.Select(Kostenkategorien.Text));
        Assert.Equal(Kostenkategorie.ProgrammUndMaterial, Kostenkategorien.Lesen("programmundmaterial"));
        Assert.Equal(Kostenkategorie.Verpflegung, Kostenkategorien.Lesen("Verpflegung"));
        Assert.Null(Kostenkategorien.Lesen("xyz"));
        Assert.Null(Kostenkategorien.Lesen("1"));
        Assert.Null(Kostenkategorien.Lesen(null));
    }

    // ---------- Werte ----------

    [Theory]
    [InlineData("7,5", "7,5")]
    [InlineData("7.50", "7,5")]
    [InlineData(" 2000 ", "2000")]
    [InlineData("0", "0")]
    [InlineData("0,00", "0")]
    [InlineData("1000000", "1000000")]
    [InlineData("0.125", null)]
    [InlineData("1.250", null)]
    [InlineData("12.500", null)]
    [InlineData("-3", null)]
    [InlineData("abc", null)]
    [InlineData("1,234", null)]
    [InlineData("1000001", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Betrag_lesen_mit_Komma_oder_Punkt_ohne_Tausenderpunkte_hoechstens_zwei_Nachkommastellen(string? text, string? erwartet)
    {
        var betrag = FinanzenWerte.BetragLesen(text);

        Assert.Equal(erwartet, betrag is null ? null : FinanzenWerte.BetragText(betrag.Value));
        Assert.Equal(erwartet is null, FinanzenWerte.BetragFehler(text) is not null);
    }

    [Theory]
    [InlineData("40", 40)]
    [InlineData(" 0 ", 0)]
    [InlineData("1000", 1000)]
    [InlineData("1001", null)]
    [InlineData("-1", null)]
    [InlineData("1,5", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void Planzahl_lesen_ganze_Zahl_von_0_bis_1000(string text, int? erwartet)
    {
        Assert.Equal(erwartet, FinanzenWerte.PlanzahlLesen(text));
        Assert.Equal(erwartet is null, FinanzenWerte.PlanzahlFehler(text) is not null);
    }

    // ---------- Entitäten ----------

    [Fact]
    public void Kalkulation_FeldAendern_mit_passendem_alten_Wert_speichert_ein_anderer_alter_Wert_ist_ein_Konflikt()
    {
        var kalkulation = Kalkulation.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Equal(("0", "0"), (kalkulation.Wert(KalkulationFeld.Teilnehmende), kalkulation.Wert(KalkulationFeld.Mitarbeitende)));
        Assert.True(kalkulation.FeldAendern(KalkulationFeld.Teilnehmende, " 0 ", " 40 "));
        Assert.Equal((40, "40"), (kalkulation.Teilnehmende, kalkulation.Wert(KalkulationFeld.Teilnehmende)));
        Assert.False(kalkulation.FeldAendern(KalkulationFeld.Teilnehmende, "0", "50"));
        Assert.False(kalkulation.FeldAendern(KalkulationFeld.Mitarbeitende, null, "5"));
        Assert.True(kalkulation.FeldAendern(KalkulationFeld.Mitarbeitende, "0", "10"));
        Assert.Equal((40, 10), (kalkulation.Teilnehmende, kalkulation.Mitarbeitende));
    }

    [Fact]
    public void Kategorie_FeldAendern_normalisiert_Betraege_und_vergleicht_den_alten_Wert_als_Betrag()
    {
        var posten = KategoriePosten.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Kostenkategorie.Verpflegung);

        Assert.Equal(("0", "0"), (posten.Wert(KategorieFeld.Fixkosten), posten.Wert(KategorieFeld.ProPersonUndTag)));
        Assert.True(posten.FeldAendern(KategorieFeld.ProPersonUndTag, "0,00", "7.50"));
        Assert.Equal((7.5m, "7,5"), (posten.ProPersonUndTag, posten.Wert(KategorieFeld.ProPersonUndTag)));
        Assert.True(posten.FeldAendern(KategorieFeld.ProPersonUndTag, "7,50", "8"));
        Assert.False(posten.FeldAendern(KategorieFeld.ProPersonUndTag, "7,5", "9"));
        Assert.True(posten.FeldAendern(KategorieFeld.Fixkosten, "0", "2000"));
        Assert.Equal((2000m, 8m), (posten.Fixkosten, posten.ProPersonUndTag));
    }

    [Fact]
    public void Einnahmeposten_Anlegen_trimmt_und_FeldAendern_folgt_der_Konfliktregel()
    {
        var posten = Einnahmeposten.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "  Zuschuss Landesjugendplan ", 0m);

        Assert.Equal(("Zuschuss Landesjugendplan", "0"), (posten.Wert(EinnahmepostenFeld.Bezeichnung), posten.Wert(EinnahmepostenFeld.Betrag)));
        Assert.True(posten.FeldAendern(EinnahmepostenFeld.Betrag, "0", "1200"));
        Assert.True(posten.FeldAendern(EinnahmepostenFeld.Bezeichnung, " Zuschuss Landesjugendplan", " Zuschuss "));
        Assert.False(posten.FeldAendern(EinnahmepostenFeld.Bezeichnung, "Zuschuss Landesjugendplan", "Spende"));
        Assert.Equal(("Zuschuss", 1200m), (posten.Bezeichnung, posten.Betrag));
    }

    [Fact]
    public void Neue_Kennung_einer_Einnahme_liegt_auch_in_derselben_Millisekunde_nach_der_letzten()
    {
        var jetzt = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        var letzte = Guid.CreateVersion7(jetzt.AddMilliseconds(5));

        Assert.True(EinnahmeAnlegenHandler.NeueKennung(jetzt, letzte).CompareTo(letzte) > 0);
        Assert.True(EinnahmeAnlegenHandler.NeueKennung(jetzt, null) != Guid.Empty);
    }

    // ---------- Formprüfung ----------

    [Fact]
    public void Validatoren_melden_Fehler_am_JSON_Feld()
    {
        Assert.Equal(
            ["teilnehmende"],
            new KalkulationFeldAendernValidator().Validate(new KalkulationFeldAendernCommand(Guid.CreateVersion7(), KalkulationFeld.Teilnehmende, 0, "0", "1001"))
                .Errors.Select(e => e.PropertyName));
        Assert.Equal(
            ["proPersonUndTag"],
            new KategorieFeldAendernValidator().Validate(
                    new KategorieFeldAendernCommand(Guid.CreateVersion7(), Kostenkategorie.Verpflegung, KategorieFeld.ProPersonUndTag, 0, "0", "1.250"))
                .Errors.Select(e => e.PropertyName));
        Assert.Equal(
            ["bezeichnung", "betrag"],
            new EinnahmeAnlegenValidator().Validate(new EinnahmeAnlegenCommand(Guid.CreateVersion7(), " ", "-3")).Errors.Select(e => e.PropertyName));
        Assert.True(new EinnahmeAnlegenValidator().Validate(new EinnahmeAnlegenCommand(Guid.CreateVersion7(), "Zuschuss", null)).IsValid);
        Assert.Equal(
            ["bezeichnung"],
            new EinnahmeFeldAendernValidator().Validate(
                    new EinnahmeFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), EinnahmepostenFeld.Bezeichnung, 0, "a", new string('x', 201)))
                .Errors.Select(e => e.PropertyName));
        Assert.Equal(
            ["betrag"],
            new EinnahmeFeldAendernValidator().Validate(
                    new EinnahmeFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), EinnahmepostenFeld.Betrag, 0, "0", ""))
                .Errors.Select(e => e.PropertyName));
    }

    // ---------- Merkmale der Commands ----------

    [Fact]
    public void Jeder_Benutzer_Command_gehoert_zu_Finanzen_und_hat_keine_Ausnahme_nach_dem_Abschluss()
    {
        var commands = typeof(KalkulationFeldAendernCommand).Assembly.GetTypes().Where(t => typeof(IBenutzerCommand).IsAssignableFrom(t) && !t.IsInterface).ToList();

        Assert.Equal(
            ["EinnahmeAnlegenCommand", "EinnahmeFeldAendernCommand", "EinnahmeLoeschenCommand", "KalkulationFeldAendernCommand", "KategorieFeldAendernCommand"],
            commands.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(commands, t =>
        {
            Assert.True(typeof(ISchalterCommand).IsAssignableFrom(t), t.Name);
            Assert.True(typeof(IModulCommand<FinanzenDbContext>).IsAssignableFrom(t), t.Name);
            Assert.Empty(t.GetCustomAttributes(typeof(ErlaubtNachAbschlussAttribute), false));
        });
        Assert.Equal(FunktionsSchalter.Finanzen, new EinnahmeLoeschenCommand(Guid.CreateVersion7(), Guid.CreateVersion7()).Schalter);
        Assert.Null(typeof(Kalkulation).GetProperty("PersonId"));
        Assert.Null(typeof(KategoriePosten).GetProperty("PersonId"));
        Assert.Null(typeof(Einnahmeposten).GetProperty("PersonId"));
    }

    // ---------- Datenbank: Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigene_Kalkulation()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueMigrierteDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await KalkulationAsync(factory, orgA);
        var b = await KalkulationAsync(factory, orgB);

        foreach (var (org, erwartet) in new[] { (orgA, a), (orgB, b) })
        {
            Assert.Equal([erwartet.Kalkulation], await LesenAsync(factory, org, db => db.Kalkulationen.Select(k => k.Id).ToListAsync(Abbruch)));
            Assert.Equal([erwartet.Kategorie], await LesenAsync(factory, org, db => db.KategoriePosten.Select(k => k.Id).ToListAsync(Abbruch)));
            Assert.Equal([erwartet.Einnahme], await LesenAsync(factory, org, db => db.Einnahmeposten.Select(e => e.Id).ToListAsync(Abbruch)));
        }

        Assert.Empty(await LesenAsync(factory, null, db => db.Kalkulationen.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.KategoriePosten.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Einnahmeposten.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreiben = scope.ServiceProvider.GetRequiredService<FinanzenDbContext>();
        Assert.Equal([b.Kalkulation], await schreiben.Kalkulationen.Select(k => k.Id).ToListAsync(Abbruch));
        var posten = Assert.Single(await schreiben.KategoriePosten.ToListAsync(Abbruch));
        Assert.Equal((Kostenkategorie.Verpflegung, 7.5m), (posten.Kategorie, posten.ProPersonUndTag));
    }

    /// <summary>Kalkulation (40 TN), Verpflegung 7,50 € und ein Zuschuss in einer neuen Freizeit des Mandanten.</summary>
    private static async Task<(Guid Kalkulation, Guid Kategorie, Guid Einnahme)> KalkulationAsync(ZeltlotseApiFactory factory, Guid organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<FinanzenDbContext>();
        var freizeit = Guid.CreateVersion7();
        var kalkulation = Kalkulation.Anlegen(Guid.CreateVersion7(), organisation, freizeit);
        kalkulation.FeldAendern(KalkulationFeld.Teilnehmende, "0", "40");
        var kategorie = KategoriePosten.Anlegen(Guid.CreateVersion7(), organisation, freizeit, Kostenkategorie.Verpflegung);
        kategorie.FeldAendern(KategorieFeld.ProPersonUndTag, "0", "7,5");
        var einnahme = Einnahmeposten.Anlegen(Guid.CreateVersion7(), organisation, freizeit, "Zuschuss", 1200m);
        db.Kalkulationen.Add(kalkulation);
        db.KategoriePosten.Add(kategorie);
        db.Einnahmeposten.Add(einnahme);
        await db.SaveChangesAsync(Abbruch);
        return (kalkulation.Id, kategorie.Id, einnahme.Id);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<FinanzenLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<FinanzenLeseDbContext>());
    }
}
