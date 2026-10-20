using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Belege;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Datenschutz;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Testing;

namespace Zeltlotse.Finanzen.Tests;

using Beleg = Zeltlotse.Finanzen.Belege.Beleg;

/// <summary>
/// Story 5-2, Belege, Soll-Ist und Erstattung (CAP-33, FR-58, FR-59, AD-6, AD-16, AD-17, AD-19, AD-22): die Statusgrenzen des Soll-Ist-Vergleichs, die
/// Rechnung je Kategorie und Gesamt, die Erstattungen je Person mit Sortierung, die Feldänderung eines Belegs nach der Konfliktregel, die Formprüfung, die
/// Merkmale der Commands (nur der Erstattungsvermerk nach dem Abschluss) und der Mandantenfilter.
/// </summary>
public sealed class BelegeTests(PostgresContainer postgres)
{
    private static readonly Guid Lea = Guid.Parse("0199f800-0000-7000-8000-00000000000a");
    private static readonly Guid Jonas = Guid.Parse("0199f800-0000-7000-8000-00000000000b");
    private static readonly Guid Miriam = Guid.Parse("0199f800-0000-7000-8000-00000000000c");

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    // ---------- Status ----------

    [Theory]
    [InlineData("0", "0", SollIstStatus.ImPlan)]
    [InlineData("0", "0.01", SollIstStatus.Ueberschritten)]
    [InlineData("100", "0", SollIstStatus.ImPlan)]
    [InlineData("100", "90", SollIstStatus.ImPlan)]
    [InlineData("100", "90.01", SollIstStatus.Knapp)]
    [InlineData("100", "100", SollIstStatus.Knapp)]
    [InlineData("100", "100.01", SollIstStatus.Ueberschritten)]
    [InlineData("4200", "3900", SollIstStatus.Knapp)]
    [InlineData("1800", "1950", SollIstStatus.Ueberschritten)]
    public void Status_Im_Plan_bis_90_Prozent_Knapp_bis_100_Prozent_darueber_Ueberschritten(string soll, string ist, SollIstStatus erwartet) =>
        Assert.Equal(erwartet, SollIstRechnung.Status(decimal.Parse(soll, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(ist, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Rechnen_je_Kategorie_in_fester_Reihenfolge_fehlendes_Soll_ist_0_dazu_Gesamt()
    {
        var soll = new Dictionary<Kostenkategorie, decimal> { [Kostenkategorie.Verpflegung] = 4200m, [Kostenkategorie.Fahrt] = 1800m, [Kostenkategorie.Sonstiges] = 200m };

        var ergebnis = SollIstRechnung.Rechnen(
            soll,
            [
                new BelegBetrag(Kostenkategorie.Verpflegung, 3854.20m, Jonas, true),
                new BelegBetrag(Kostenkategorie.Verpflegung, 45.80m, Lea, false),
                new BelegBetrag(Kostenkategorie.Fahrt, 1950m, Miriam, true),
                new BelegBetrag(Kostenkategorie.Unterkunft, 10m, Lea, false),
            ]);

        Assert.Equal(Kostenkategorien.Alle, ergebnis.Zeilen.Select(z => z.Kategorie!.Value));
        Assert.Equal(
            [
                "Unterkunft 0 10 Ueberschritten", "Verpflegung 4200 3900 Knapp", "Fahrt 1800 1950 Ueberschritten", "ProgrammUndMaterial 0 0 ImPlan",
                "Mitarbeitende 0 0 ImPlan", "Sonstiges 200 0 ImPlan",
            ],
            ergebnis.Zeilen.Select(z => FormattableString.Invariant($"{z.Kategorie} {z.Soll} {z.Ist} {z.Status}")));
        Assert.Equal(new SollIstZeile(null, 6200m, 5860m, SollIstStatus.Knapp), ergebnis.Gesamt);
    }

    [Fact]
    public void Erstattungen_je_Person_summiert_nach_Offen_absteigend_bei_Gleichstand_nach_Erstattet()
    {
        var erstattungen = SollIstRechnung.Erstattungen(
        [
            new BelegBetrag(Kostenkategorie.Fahrt, 100m, Jonas, true),
            new BelegBetrag(Kostenkategorie.Verpflegung, 23.40m, Lea, false),
            new BelegBetrag(Kostenkategorie.Verpflegung, 10m, Miriam, false),
            new BelegBetrag(Kostenkategorie.Sonstiges, 22.40m, Lea, false),
            new BelegBetrag(Kostenkategorie.Sonstiges, 5m, Miriam, true),
            new BelegBetrag(Kostenkategorie.Sonstiges, 20m, Jonas, true),
        ]);

        Assert.Equal([new Erstattung(Lea, 45.80m, 0m), new Erstattung(Miriam, 10m, 5m), new Erstattung(Jonas, 0m, 120m)], erstattungen);
        Assert.Empty(SollIstRechnung.Erstattungen([]));
    }

    // ---------- Beleg ----------

    private static Beleg Neu(string? bezeichnung = "Großeinkauf") =>
        Beleg.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, Kostenkategorie.Verpflegung, 23.40m, bezeichnung, Lea);

    [Fact]
    public void Ein_neuer_Beleg_ist_nicht_erstattet_ohne_Datei_und_liefert_seine_Werte_als_Text()
    {
        var beleg = Neu("  Brötchen ");

        Assert.Equal(("Brötchen", "23,4", "Verpflegung", Lea.ToString("D"), false, (DateiArt?)null), (
            beleg.Wert(BelegFeld.Bezeichnung), beleg.Wert(BelegFeld.Betrag), beleg.Wert(BelegFeld.Kategorie), beleg.Wert(BelegFeld.BezahltVon), beleg.Erstattet,
            beleg.DateiArt));
        Assert.Null(Neu(" ").Bezeichnung);
    }

    [Fact]
    public void Feld_aendern_nach_der_Konfliktregel_mit_normalisiertem_alten_Wert()
    {
        var beleg = Neu();

        Assert.True(beleg.FeldAendern(BelegFeld.Betrag, "23,40", "1234,5"));
        Assert.Equal(1234.5m, beleg.Betrag);
        Assert.False(beleg.FeldAendern(BelegFeld.Betrag, "23,4", "1"));
        Assert.True(beleg.FeldAendern(BelegFeld.Kategorie, "verpflegung", "Fahrt"));
        Assert.Equal(Kostenkategorie.Fahrt, beleg.Kategorie);
        Assert.False(beleg.FeldAendern(BelegFeld.Kategorie, "Verpflegung", "Sonstiges"));
        Assert.True(beleg.FeldAendern(BelegFeld.BezahltVon, Lea.ToString("N").ToUpperInvariant(), Jonas.ToString()));
        Assert.Equal(Jonas, beleg.PersonId);
        Assert.True(beleg.FeldAendern(BelegFeld.Bezeichnung, "Großeinkauf", " "));
        Assert.Null(beleg.Bezeichnung);
        Assert.True(beleg.FeldAendern(BelegFeld.Bezeichnung, null, "Busfahrt"));
    }

    [Fact]
    public void Erstattet_aendern_nach_der_Konfliktregel()
    {
        var beleg = Neu();

        Assert.False(beleg.ErstattetAendern("true", "false"));
        Assert.True(beleg.ErstattetAendern("false", "TRUE"));
        Assert.Equal((true, "true"), (beleg.Erstattet, beleg.ErstattetText));
        Assert.True(beleg.ErstattetAendern("True", "false"));
        Assert.False(beleg.Erstattet);
    }

    [Fact]
    public void Datei_setzen_ersetzt_und_entfernen_leert()
    {
        var beleg = Neu();

        beleg.DateiSetzen(DateiArt.Png, "Kassenbon.png");
        beleg.DateiSetzen(DateiArt.Pdf, "Rechnung.pdf");
        Assert.Equal((DateiArt.Pdf, "Rechnung.pdf"), (beleg.DateiArt!.Value, beleg.Dateiname));
        beleg.DateiEntfernen();
        Assert.Equal(((DateiArt?)null, (string?)null), (beleg.DateiArt, beleg.Dateiname));
    }

    // ---------- Formprüfung ----------

    private static List<string> Fehler(BelegAnlegenCommand command) =>
        [.. new BelegAnlegenValidator().Validate(command).Errors.Select(e => e.PropertyName)];

    [Fact]
    public void Anlegen_Pflicht_sind_Kategorie_und_Betrag_Bezeichnung_hoechstens_200_bezahlt_von_eine_Kennung()
    {
        var freizeit = Guid.CreateVersion7();

        Assert.Empty(Fehler(new BelegAnlegenCommand(freizeit, Lea, "Verpflegung", "23,40", null, null)));
        Assert.Empty(Fehler(new BelegAnlegenCommand(freizeit, Lea, "fahrt", "0", new string('x', 200), Jonas.ToString())));
        Assert.Equal(["kategorie", "betrag"], Fehler(new BelegAnlegenCommand(freizeit, Lea, null, " ", null, null)));
        Assert.Equal(["kategorie", "betrag", "bezeichnung", "bezahltVon"], Fehler(new BelegAnlegenCommand(freizeit, Lea, "xyz", "1.250", new string('x', 201), "Lea")));
        Assert.Equal(
            ["Kategorie fehlt.", "Betrag fehlt."],
            new BelegAnlegenValidator().Validate(new BelegAnlegenCommand(freizeit, Lea, "", null, null, null)).Errors.Select(e => e.ErrorMessage));
        Assert.Equal(
            "Unbekannte Kategorie.",
            Assert.Single(new BelegAnlegenValidator().Validate(new BelegAnlegenCommand(freizeit, Lea, "7", "1", null, null)).Errors).ErrorMessage);
    }

    [Fact]
    public void Feld_aendern_und_Erstattet_pruefen_den_neuen_Wert_am_Feld()
    {
        var (freizeit, beleg) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        List<string> Fehler(BelegFeld feld, string? wert) =>
            [.. new BelegFeldAendernValidator().Validate(new BelegFeldAendernCommand(freizeit, beleg, feld, 0, null, wert)).Errors.Select(e => e.PropertyName)];

        Assert.Equal(["betrag"], Fehler(BelegFeld.Betrag, ""));
        Assert.Equal(["kategorie"], Fehler(BelegFeld.Kategorie, "Kosten"));
        Assert.Equal(["bezahltVon"], Fehler(BelegFeld.BezahltVon, null));
        Assert.Equal(["bezeichnung"], Fehler(BelegFeld.Bezeichnung, new string('x', 201)));
        Assert.Empty(Fehler(BelegFeld.Bezeichnung, null));
        Assert.Equal(["feld"], Fehler((BelegFeld)17, "x"));
        Assert.Equal(
            ["erstattet"],
            new BelegErstattetValidator().Validate(new BelegErstattetCommand(freizeit, beleg, 0, "false", "ja")).Errors.Select(e => e.PropertyName));
        Assert.True(new BelegErstattetValidator().Validate(new BelegErstattetCommand(freizeit, beleg, 0, "false", "true")).IsValid);
    }

    // ---------- Merkmale der Commands ----------

    [Fact]
    public void Jeder_Benutzer_Command_der_Belege_gehoert_zu_Finanzen_und_nur_der_Erstattungsvermerk_gilt_nach_dem_Abschluss()
    {
        var commands = typeof(BelegAnlegenCommand).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(BelegAnlegenCommand).Namespace && typeof(IBenutzerCommand).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();

        Assert.Equal(
            ["BelegAnlegenCommand", "BelegDateiEntfernenCommand", "BelegDateiHochladenCommand", "BelegErstattetCommand", "BelegFeldAendernCommand", "BelegLoeschenCommand"],
            commands.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(commands, t =>
        {
            Assert.True(typeof(ISchalterCommand).IsAssignableFrom(t), t.Name);
            Assert.True(typeof(IModulCommand<FinanzenDbContext>).IsAssignableFrom(t), t.Name);
        });
        Assert.Equal(
            ["BelegErstattetCommand"],
            typeof(BelegAnlegenCommand).Assembly.GetTypes().Where(t => t.IsDefined(typeof(ErlaubtNachAbschlussAttribute), false)).Select(t => t.Name));
        Assert.Equal(FunktionsSchalter.Finanzen, new BelegErstattetCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, "false", "true").Schalter);
    }

    [Fact]
    public void Commands_mit_Dateiinhalt_oder_Personenkennung_geben_beides_nicht_aus()
    {
        var texte = new object[]
        {
            new BelegAnlegenCommand(Guid.CreateVersion7(), Lea, "Verpflegung", "23,40", "Brötchen", Jonas.ToString()),
            new BelegFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), BelegFeld.BezahltVon, 0, Lea.ToString(), Jonas.ToString()),
            new BelegDateiHochladenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), [0x89, 0x50, 0x4E, 0x47], DateiArt.Png, "Kassenbon Lea.png"),
            new FinanzenPersonLoeschen(new Umschlag(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, null, Lea, 1), Lea),
        }.Select(c => c.ToString()!).ToList();

        Assert.All(texte, t =>
        {
            Assert.DoesNotContain(Lea.ToString(), t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Jonas.ToString(), t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Kassenbon", t, StringComparison.Ordinal);
            Assert.DoesNotContain("Brötchen", t, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Byte", t, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Der_Platzhalter_der_API_ist_der_des_Servers() => Assert.Equal(GeloeschtePerson.Id, BelegFelder.GeloeschtePerson);

    // ---------- Datenbank: Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen_Belege()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueMigrierteDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await BelegAsync(factory, orgA);
        var b = await BelegAsync(factory, orgB);

        foreach (var (org, erwartet) in new[] { (orgA, a), (orgB, b) })
        {
            Assert.Equal([erwartet], await LesenAsync(factory, org, db => db.Belege.Select(x => x.Id).ToListAsync(Abbruch)));
        }

        Assert.Empty(await LesenAsync(factory, null, db => db.Belege.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreiben = scope.ServiceProvider.GetRequiredService<FinanzenDbContext>();
        var beleg = Assert.Single(await schreiben.Belege.ToListAsync(Abbruch));
        Assert.Equal((b, 23.4m, Lea, "Kassenbon.png"), (beleg.Id, beleg.Betrag, beleg.PersonId, beleg.Dateiname));
    }

    /// <summary>Ein Beleg (23,40 €, Verpflegung, bezahlt von Lea, mit Datei) in einer neuen Freizeit des Mandanten.</summary>
    private static async Task<Guid> BelegAsync(ZeltlotseApiFactory factory, Guid organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<FinanzenDbContext>();
        var beleg = Beleg.Anlegen(Guid.CreateVersion7(), organisation, Guid.CreateVersion7(), DateTimeOffset.UtcNow, Kostenkategorie.Verpflegung, 23.40m, null, Lea);
        beleg.DateiSetzen(DateiArt.Png, "Kassenbon.png");
        db.Belege.Add(beleg);
        await db.SaveChangesAsync(Abbruch);
        return beleg.Id;
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<FinanzenLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<FinanzenLeseDbContext>());
    }
}
