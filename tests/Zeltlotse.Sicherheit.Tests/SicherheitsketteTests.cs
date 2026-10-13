using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Daten;
using Zeltlotse.Sicherheit.Sicherheitskette;
using Zeltlotse.Testing;
using Kette = Zeltlotse.Sicherheit.Sicherheitskette.Sicherheitskette;

namespace Zeltlotse.Sicherheit.Tests;

/// <summary>
/// Story 2-8, Sicherheitskette (FR-29, AD-3, AD-6, AD-19): Feldänderung mit Konfliktregel, Trimmen und Leeren, die Regel „eingerichtet“,
/// die Formprüfung (Längen, Telefon), der Mandantenfilter der Entität und die Gleichheit der Rechtelisten mit Freizeiten.
/// </summary>
public sealed class SicherheitsketteTests(PostgresContainer postgres)
{
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Kette Neu() => Kette.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    // ---------- Entität ----------

    [Fact]
    public void FeldAendern_setzt_den_Wert_getrimmt_wenn_der_alte_Wert_stimmt()
    {
        var kette = Neu();

        Assert.True(kette.FeldAendern(SicherheitsketteFeld.LeitungTelefon, null, "  0123 456782 "));

        Assert.Equal("0123 456782", kette.LeitungTelefon);
        Assert.Equal("0123 456782", kette.Wert(SicherheitsketteFeld.LeitungTelefon));
    }

    [Fact]
    public void FeldAendern_mit_abweichendem_alten_Wert_desselben_Felds_ist_ein_Konflikt_und_aendert_nichts()
    {
        var kette = Neu();
        Assert.True(kette.FeldAendern(SicherheitsketteFeld.HeimatName, null, "Petra Heimann"));

        Assert.False(kette.FeldAendern(SicherheitsketteFeld.HeimatName, "Jemand anders", "Neu"));
        Assert.False(kette.FeldAendern(SicherheitsketteFeld.HeimatName, null, "Neu"));

        Assert.Equal("Petra Heimann", kette.HeimatName);
        // Der alte Wert wird genauso normalisiert wie der neue.
        Assert.True(kette.FeldAendern(SicherheitsketteFeld.HeimatName, "  Petra Heimann ", "Neu"));
        Assert.Equal("Neu", kette.HeimatName);
    }

    [Fact]
    public void Ein_anderes_Feld_aendert_sich_ohne_Konflikt()
    {
        var kette = Neu();
        Assert.True(kette.FeldAendern(SicherheitsketteFeld.WasTun, null, "Ruhe bewahren."));

        Assert.True(kette.FeldAendern(SicherheitsketteFeld.HeimatTelefon, null, "0123 456783"));

        Assert.Equal(("Ruhe bewahren.", "0123 456783"), (kette.WasTun, kette.HeimatTelefon));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Leerer_Text_oder_nur_Leerzeichen_setzt_das_Feld_auf_null(string? leer)
    {
        var kette = Neu();
        Assert.True(kette.FeldAendern(SicherheitsketteFeld.WasTun, null, "Erste Hilfe leisten."));

        Assert.True(kette.FeldAendern(SicherheitsketteFeld.WasTun, "Erste Hilfe leisten.", leer));

        Assert.Null(kette.WasTun);
    }

    [Fact]
    public void Eingerichtet_heisst_was_tun_Telefon_der_Leitung_und_Telefon_am_Heimatort_der_Name_ist_freiwillig()
    {
        var kette = Neu();
        Assert.False(kette.Eingerichtet());

        kette.FeldAendern(SicherheitsketteFeld.WasTun, null, "Ruhe bewahren.");
        kette.FeldAendern(SicherheitsketteFeld.LeitungTelefon, null, "0123 456782");
        Assert.False(kette.Eingerichtet());

        kette.FeldAendern(SicherheitsketteFeld.HeimatTelefon, null, "0123 456783");
        Assert.True(kette.Eingerichtet());
        Assert.Null(kette.HeimatName);

        kette.FeldAendern(SicherheitsketteFeld.HeimatTelefon, "0123 456783", "  ");
        Assert.False(kette.Eingerichtet());
        Assert.False(Kette.Eingerichtet(null, null, null));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData(SicherheitsketteFeld.WasTun, 2000, true)]
    [InlineData(SicherheitsketteFeld.WasTun, 2001, false)]
    [InlineData(SicherheitsketteFeld.HeimatName, 200, true)]
    [InlineData(SicherheitsketteFeld.HeimatName, 201, false)]
    public void Validator_prueft_die_Hoechstlaenge_der_Texte(SicherheitsketteFeld feld, int laenge, bool gueltig)
    {
        var ergebnis = Pruefen(feld, new string('a', laenge));

        Assert.Equal(gueltig, ergebnis.IsValid);
        if (!gueltig)
        {
            Assert.Equal(SicherheitsketteFelder.JsonName(feld), Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    [Theory]
    [InlineData(SicherheitsketteFeld.LeitungTelefon)]
    [InlineData(SicherheitsketteFeld.HeimatTelefon)]
    public void Validator_verlangt_beim_Telefon_eine_Ziffer_und_hoechstens_40_Zeichen(SicherheitsketteFeld feld)
    {
        var name = SicherheitsketteFelder.JsonName(feld);

        var ohneZiffer = Assert.Single(Pruefen(feld, "abc").Errors);
        Assert.Equal((name, "Die Telefonnummer braucht mindestens eine Ziffer."), (ohneZiffer.PropertyName, ohneZiffer.ErrorMessage));
        Assert.Equal(name, Assert.Single(Pruefen(feld, new string('1', 41)).Errors).PropertyName);
        Assert.True(Pruefen(feld, "+49 123 456782").IsValid);
        Assert.True(Pruefen(feld, "   ").IsValid);
        Assert.True(Pruefen(feld, null).IsValid);
    }

    [Theory]
    [InlineData("0123 456 Durchwahl 5", false)]
    [InlineData("0123 456 782 +1", false)]
    [InlineData("0123.456782", false)]
    [InlineData("+49 (0) 7443 / 12-34", true)]
    [InlineData("(07443) 1234", true)]
    public void Validator_erlaubt_beim_Telefon_nur_Ziffern_Leerzeichen_und_Plus_vorn_Klammern_Schraegstrich_Strich(string wert, bool gueltig)
    {
        foreach (var feld in new[] { SicherheitsketteFeld.LeitungTelefon, SicherheitsketteFeld.HeimatTelefon })
        {
            var ergebnis = Pruefen(feld, wert);

            Assert.Equal(gueltig, ergebnis.IsValid);
            if (!gueltig)
            {
                var fehler = Assert.Single(ergebnis.Errors);
                Assert.Equal(
                    (SicherheitsketteFelder.JsonName(feld), "Die Telefonnummer darf nur Ziffern, Leerzeichen und + ( ) / - enthalten."),
                    (fehler.PropertyName, fehler.ErrorMessage));
            }
        }

        // Der Name am Heimatort ist Text, keine Nummer.
        Assert.True(Pruefen(SicherheitsketteFeld.HeimatName, wert).IsValid);
    }

    [Fact]
    public void Validator_lehnt_ein_unbekanntes_Feld_ab()
    {
        var ergebnis = Pruefen((SicherheitsketteFeld)99, "x");

        Assert.Equal("feld", Assert.Single(ergebnis.Errors).PropertyName);
    }

    private static FluentValidation.Results.ValidationResult Pruefen(SicherheitsketteFeld feld, string? wert) =>
        new SicherheitsketteFeldAendernValidator().Validate(new SicherheitsketteFeldAendernCommand(Guid.CreateVersion7(), feld, 0, null, wert));

    // ---------- Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigene_Kette()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (x, z) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await SpeichernAsync(factory, orgA, Kette.Anlegen(Guid.CreateVersion7(), orgA, x));
        await SpeichernAsync(factory, orgB, Kette.Anlegen(Guid.CreateVersion7(), orgB, z));

        Assert.Equal([x], await FreizeitenAsync(factory, orgA));
        Assert.Equal([z], await FreizeitenAsync(factory, orgB));
        Assert.Empty(await FreizeitenAsync(factory, null));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreiben = scope.ServiceProvider.GetRequiredService<SicherheitDbContext>();
        Assert.Equal([z], await schreiben.Sicherheitsketten.Select(k => k.FreizeitId).ToListAsync(Abbruch));
    }

    private static async Task SpeichernAsync(ZeltlotseApiFactory factory, Guid organisation, Kette kette)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<SicherheitDbContext>();
        db.Sicherheitsketten.Add(kette);
        await db.SaveChangesAsync(Abbruch);
    }

    private static async Task<List<Guid>> FreizeitenAsync(ZeltlotseApiFactory factory, Guid? organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await scope.ServiceProvider.GetRequiredService<SicherheitLeseDbContext>().Sicherheitsketten.Select(k => k.FreizeitId).ToListAsync(Abbruch);
    }

    // ---------- Rechte ----------

    [Fact]
    public void Lesen_ist_gleich_der_Mitarbeit_und_Einrichten_gleich_den_Einstellungen_in_Freizeiten()
    {
        Assert.Equal(Liste("Zeltlotse.Freizeiten", "Zeltlotse.Freizeiten.Rechte.FreizeitRechte", "Mitarbeit"), SicherheitRechte.Lesen.Cast<object>().Order());
        Assert.Equal(Liste("Zeltlotse.Freizeiten", "Zeltlotse.Freizeiten.Rechte.FreizeitRechte", "Einstellungen"), SicherheitRechte.Einrichten.Cast<object>().Order());
    }

    private static IEnumerable<object> Liste(string assembly, string typ, string feld)
    {
        var werte = (Array)Assembly.Load(assembly).GetType(typ, throwOnError: true)!.GetField(feld, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return werte.Cast<object>().Order();
    }
}
