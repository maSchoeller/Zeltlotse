using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Inventar.Artikel;
using Zeltlotse.Inventar.Contracts.Events;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.Inventar.Reservierungen;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using InventarArtikel = Zeltlotse.Inventar.Artikel.InventarArtikel;
using Reservierung = Zeltlotse.Inventar.Reservierungen.Reservierung;

namespace Zeltlotse.Inventar.Tests;

/// <summary>
/// Story 3-3, Inventar und Reservierung (FR-37, AD-6, AD-19, AD-28): Artikel anlegen und Felder ändern mit Konfliktregel und Trimmen, Reservierung
/// mit Ereignis nur bei verknüpftem Material, die Überschneidungsregel mit Grenztagen, die Formprüfung, der Mandantenfilter beider Entitäten und
/// die Rechtelisten.
/// </summary>
public sealed class InventarTests(PostgresContainer postgres)
{
    private static readonly DateOnly Erster = new(2027, 8, 2);
    private static readonly DateOnly Letzter = new(2027, 8, 13);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static InventarArtikel Neu(string bezeichnung = "Großzelt", string? menge = "1 Stück") =>
        InventarArtikel.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), bezeichnung, menge);

    private static Reservierung Reservieren(Guid? material = null, Guid? artikel = null, Guid? freizeit = null, DateOnly? von = null, DateOnly? bis = null) =>
        Reservierung.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), artikel ?? Guid.CreateVersion7(), freizeit ?? Guid.CreateVersion7(), von ?? Erster, bis ?? Letzter, material);

    // ---------- Artikel ----------

    [Fact]
    public void Anlegen_trimmt_Bezeichnung_und_Menge_leere_Menge_heisst_keine_Menge()
    {
        var artikel = Neu("  Fahnen ", " 8 Stück ");

        Assert.Equal(("Fahnen", "8 Stück"), (artikel.Bezeichnung, artikel.Menge));
        Assert.Null(Neu(menge: "  ").Menge);
        Assert.Null(Neu(menge: null).Menge);
    }

    [Fact]
    public void FeldAendern_setzt_getrimmt_wenn_der_alte_Wert_stimmt_sonst_Konflikt_nur_fuer_dasselbe_Feld()
    {
        var artikel = Neu();

        Assert.True(artikel.FeldAendern(InventarArtikelFeld.Menge, "1 Stück", " 2 Stück "));
        Assert.Equal("2 Stück", artikel.Wert(InventarArtikelFeld.Menge));
        Assert.False(artikel.FeldAendern(InventarArtikelFeld.Menge, "1 Stück", "3 Stück"));
        Assert.Equal("2 Stück", artikel.Menge);

        Assert.True(artikel.FeldAendern(InventarArtikelFeld.Bezeichnung, " Großzelt ", " Großes Zelt "));
        Assert.Equal("Großes Zelt", artikel.Bezeichnung);
        Assert.True(artikel.FeldAendern(InventarArtikelFeld.Menge, "2 Stück", ""));
        Assert.Null(artikel.Menge);
        Assert.True(artikel.FeldAendern(InventarArtikelFeld.Menge, null, "1"));
        Assert.Equal("1", artikel.Menge);
    }

    // ---------- Reservierung und Ereignis ----------

    [Fact]
    public void Reservieren_mit_Material_legt_ReservierungGeaendert_Reserviert_ab_ohne_Material_nichts()
    {
        var material = Guid.CreateVersion7();
        var mit = Reservieren(material);
        var ohne = Reservieren();

        var ereignis = Assert.IsType<ReservierungGeaendert>(Assert.Single(mit.Ereignisse));
        Assert.Equal(new ReservierungGeaendert(mit.Id, mit.FreizeitId, mit.ArtikelId, material, ReservierungArt.Reserviert), ereignis);
        Assert.Equal(1, mit.Folgenummer);
        Assert.Empty(ohne.Ereignisse);
        Assert.Equal((Erster, Letzter, (Guid?)material), (mit.Von, mit.Bis, mit.MaterialId));
    }

    [Fact]
    public void Aufheben_mit_Material_legt_ReservierungGeaendert_Aufgehoben_ab_ohne_Material_nichts()
    {
        var material = Guid.CreateVersion7();
        var mit = Reservieren(material);
        var ohne = Reservieren();

        mit.Aufheben();
        ohne.Aufheben();

        Assert.Equal(
            new ReservierungGeaendert(mit.Id, mit.FreizeitId, mit.ArtikelId, material, ReservierungArt.Aufgehoben),
            Assert.IsType<ReservierungGeaendert>(mit.Ereignisse[^1]));
        Assert.Equal(2, mit.Folgenummer);
        Assert.Empty(ohne.Ereignisse);
    }

    [Theory]
    [InlineData("2027-08-10", "2027-08-12", true)]
    [InlineData("2027-08-13", "2027-08-15", true)]
    [InlineData("2027-07-30", "2027-08-02", true)]
    [InlineData("2027-07-01", "2027-09-01", true)]
    [InlineData("2027-08-14", "2027-08-15", false)]
    [InlineData("2027-07-30", "2027-08-01", false)]
    public void Ueberschneiden_zaehlt_die_Tage_einschliesslich(string von, string bis, bool erwartet)
    {
        var (b, e) = (DateOnly.Parse(von, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(bis, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(erwartet, Reservierung.Ueberschneiden(Erster, Letzter, b, e));
        Assert.Equal(erwartet, Reservierung.Ueberschneiden(b, e, Erster, Letzter));
    }

    [Fact]
    public void Ein_Konflikt_braucht_denselben_Artikel_und_eine_andere_Freizeit()
    {
        var (artikel, freizeit) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var zeltlager = Reservieren(artikel: artikel, freizeit: freizeit);

        Assert.True(zeltlager.KonfliktMit(Reservieren(artikel: artikel, von: new DateOnly(2027, 8, 10), bis: new DateOnly(2027, 8, 12))));
        Assert.False(zeltlager.KonfliktMit(Reservieren(artikel: artikel, freizeit: freizeit)));
        Assert.False(zeltlager.KonfliktMit(Reservieren()));
        Assert.False(zeltlager.KonfliktMit(Reservieren(artikel: artikel, von: new DateOnly(2027, 8, 14), bis: new DateOnly(2027, 8, 15))));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData("", "bezeichnung", "Bezeichnung fehlt.")]
    [InlineData("   ", "bezeichnung", "Bezeichnung fehlt.")]
    [InlineData(null, "bezeichnung", "Bezeichnung fehlt.")]
    public void Anlegen_verlangt_eine_Bezeichnung(string? bezeichnung, string feld, string text)
    {
        var ergebnis = new ArtikelAnlegenValidator().Validate(new ArtikelAnlegenCommand(Guid.CreateVersion7(), bezeichnung, null));

        var fehler = Assert.Single(ergebnis.Errors);
        Assert.Equal((feld, text), (fehler.PropertyName, fehler.ErrorMessage));
        Assert.False(AendernPruefen(InventarArtikelFeld.Bezeichnung, bezeichnung).IsValid);
    }

    [Fact]
    public void Validatoren_erlauben_hoechstens_120_Zeichen_Bezeichnung_und_40_Zeichen_Menge_nach_dem_Trimmen()
    {
        static FluentValidation.Results.ValidationResult Anlegen(string bezeichnung, string? menge) =>
            new ArtikelAnlegenValidator().Validate(new ArtikelAnlegenCommand(Guid.CreateVersion7(), bezeichnung, menge));

        Assert.True(Anlegen($" {new string('a', 120)} ", $" {new string('m', 40)} ").IsValid);
        var zuLang = Anlegen(new string('a', 121), null);
        Assert.Equal(("bezeichnung", "Höchstens 120 Zeichen."), (Assert.Single(zuLang.Errors).PropertyName, zuLang.Errors[0].ErrorMessage));
        var mengeZuLang = Anlegen("Fahnen", new string('m', 41));
        Assert.Equal(("menge", "Höchstens 40 Zeichen."), (Assert.Single(mengeZuLang.Errors).PropertyName, mengeZuLang.Errors[0].ErrorMessage));
        Assert.True(Anlegen("Fahnen", "").IsValid);

        Assert.False(AendernPruefen(InventarArtikelFeld.Bezeichnung, new string('a', 121)).IsValid);
        Assert.False(AendernPruefen(InventarArtikelFeld.Menge, new string('m', 41)).IsValid);
        Assert.True(AendernPruefen(InventarArtikelFeld.Menge, null).IsValid);
        Assert.Equal("feld", Assert.Single(AendernPruefen((InventarArtikelFeld)99, "x").Errors).PropertyName);
    }

    [Theory]
    [InlineData("2027-08-02", "2027-08-13", true)]
    [InlineData("2027-08-05", "2027-08-05", true)]
    [InlineData(null, null, true)]
    [InlineData("2027-08-02", null, false)]
    [InlineData(null, "2027-08-13", false)]
    [InlineData("2027-08-13", "2027-08-02", false)]
    public void Reservieren_braucht_beide_Tage_oder_keinen_und_Von_nicht_nach_Bis(string? von, string? bis, bool gueltig)
    {
        static DateOnly? Tag(string? text) => text is null ? null : DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var ergebnis = new ReservierenValidator().Validate(new ReservierenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), Tag(von), Tag(bis), null));

        Assert.Equal(gueltig, ergebnis.IsValid);
        if (!gueltig)
        {
            Assert.Equal("bis", Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    private static FluentValidation.Results.ValidationResult AendernPruefen(InventarArtikelFeld feld, string? wert) =>
        new ArtikelFeldAendernValidator().Validate(new ArtikelFeldAendernCommand(Guid.CreateVersion7(), feld, 0, null, wert));

    // ---------- Rechte ----------

    [Fact]
    public void Rechtelisten_sind_gleich_denen_in_Organisationen_und_Freizeiten()
    {
        Assert.Equal(Liste("Zeltlotse.Organisationen", "Zeltlotse.Organisationen.Rollen.OrgRechte", "InventarSehen"), InventarRechte.OrgLesen.Cast<object>().Order());
        Assert.Equal(Liste("Zeltlotse.Organisationen", "Zeltlotse.Organisationen.Rollen.OrgRechte", "InventarVerwalten"), InventarRechte.OrgVerwalten.Cast<object>().Order());
        Assert.Equal(Liste("Zeltlotse.Freizeiten", "Zeltlotse.Freizeiten.Rechte.FreizeitRechte", "Mitarbeit"), InventarRechte.Mitarbeit.Cast<object>().Order());
    }

    private static IEnumerable<object> Liste(string assembly, string typ, string feld)
    {
        var werte = (Array)Assembly.Load(assembly).GetType(typ, throwOnError: true)!.GetField(feld, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return werte.Cast<object>().Order();
    }

    // ---------- Datenbank: Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen_Artikel_und_Reservierungen()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (artikelA, reservierungA) = await AnlegenAsync(factory, orgA, "Fahnen");
        var (artikelB, reservierungB) = await AnlegenAsync(factory, orgB, "Kanus");

        Assert.Equal([artikelA], await LesenAsync(factory, orgA, db => db.Artikel.Select(a => a.Id).ToListAsync(Abbruch)));
        Assert.Equal([reservierungA], await LesenAsync(factory, orgA, db => db.Reservierungen.Select(r => r.Id).ToListAsync(Abbruch)));
        Assert.Equal([artikelB], await LesenAsync(factory, orgB, db => db.Artikel.Select(a => a.Id).ToListAsync(Abbruch)));
        Assert.Equal([reservierungB], await LesenAsync(factory, orgB, db => db.Reservierungen.Select(r => r.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Artikel.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Reservierungen.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreibend = scope.ServiceProvider.GetRequiredService<InventarDbContext>();
        Assert.Equal([artikelB], await schreibend.Artikel.Select(a => a.Id).ToListAsync(Abbruch));
        Assert.Null(await schreibend.ArtikelSperrenAsync(artikelA, Abbruch));
        Assert.NotNull(await schreibend.ArtikelSperrenAsync(artikelB, Abbruch));
    }

    // ---------- Helfer ----------

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        return factory;
    }

    /// <summary>Ein Artikel mit einer Reservierung (ohne Material) im Mandanten.</summary>
    private static async Task<(Guid Artikel, Guid Reservierung)> AnlegenAsync(ZeltlotseApiFactory factory, Guid organisation, string bezeichnung)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<InventarDbContext>();
        var artikel = InventarArtikel.Anlegen(Guid.CreateVersion7(), organisation, bezeichnung, null);
        var reservierung = Reservierung.Anlegen(Guid.CreateVersion7(), organisation, artikel.Id, Guid.CreateVersion7(), Erster, Letzter, null);
        db.Artikel.Add(artikel);
        db.Reservierungen.Add(reservierung);
        await db.SaveChangesAsync(Abbruch);
        return (artikel.Id, reservierung.Id);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<InventarLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<InventarLeseDbContext>());
    }
}
