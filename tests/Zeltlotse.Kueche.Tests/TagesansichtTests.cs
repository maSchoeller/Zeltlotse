using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Einkauf;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Testing;

namespace Zeltlotse.Kueche.Tests;

/// <summary>
/// Story 4-4, Küchen-Tagesansicht und Restetag (CAP-31, FR-52, FR-53, AD-6, AD-19): der Restetag am Stand einer Mahlzeit (Konflikt nach dem alten Wert),
/// die Formprüfung, die Merkmale des Commands, die Sonderkost-Zählung, „heute oder erster Tag“, die Kopie der Sonderkost in den API-Verträgen und der
/// Mandantenfilter samt eindeutigem Index.
/// </summary>
public sealed class TagesansichtTests(PostgresContainer postgres)
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Freizeit = Guid.CreateVersion7();

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    // ---------- Restetag ----------

    [Fact]
    public void MahlzeitStand_beginnt_ohne_Restetag_ohne_Zeile_gilt_false_und_kennt_den_Konflikt()
    {
        Assert.Equal("false", MahlzeitStand.Wert(null, MahlzeitFeld.Restetag));
        var stand = MahlzeitStand.Anlegen(Guid.CreateVersion7(), Org, Freizeit, Guid.CreateVersion7());
        Assert.False(stand.Restetag);
        Assert.Equal("false", MahlzeitStand.Wert(stand, MahlzeitFeld.Restetag));

        // Anderer alter Wert (auch keiner) ist ein Konflikt, nichts ändert sich.
        Assert.False(stand.FeldAendern(MahlzeitFeld.Restetag, "true", "true"));
        Assert.False(stand.FeldAendern(MahlzeitFeld.Restetag, null, "true"));
        Assert.False(stand.Restetag);

        Assert.True(stand.FeldAendern(MahlzeitFeld.Restetag, " false ", "true"));
        Assert.True(stand.Restetag);
        Assert.Equal("true", MahlzeitStand.Wert(stand, MahlzeitFeld.Restetag));
        Assert.False(stand.FeldAendern(MahlzeitFeld.Restetag, "false", "false"));

        // Rückgängig ist ein Ändern mit dem vorigen Wert.
        Assert.True(stand.FeldAendern(MahlzeitFeld.Restetag, "true", "false"));
        Assert.False(stand.Restetag);
    }

    [Fact]
    public void Kueche_Entitaet_MahlzeitStand_hat_keine_Eigenschaft_PersonId()
    {
        Assert.Null(typeof(MahlzeitStand).GetProperty("PersonId"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", true)]
    [InlineData("ja", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("1", false)]
    public void Restetag_nur_true_oder_false_sonst_Feldfehler_restetag(string? wert, bool gueltig)
    {
        var ergebnis = new RestetagAendernValidator().Validate(new RestetagAendernCommand(Freizeit, Guid.CreateVersion7(), MahlzeitFeld.Restetag, 0, "false", wert));

        Assert.Equal(gueltig, ergebnis.IsValid);
        if (!gueltig)
        {
            Assert.Equal(MahlzeitFelder.Restetag, Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    [Fact]
    public void Restetag_ist_ein_Benutzer_Command_des_Schalters_Verpflegung_im_Kontext_der_Kueche()
    {
        var command = new RestetagAendernCommand(Freizeit, Guid.CreateVersion7(), MahlzeitFeld.Restetag, 0, "false", "true");

        Assert.IsAssignableFrom<IBenutzerCommand>(command);
        Assert.IsAssignableFrom<IModulCommand<KuecheDbContext>>(command);
        Assert.Equal(FunktionsSchalter.Verpflegung, Assert.IsAssignableFrom<ISchalterCommand>(command).Schalter);
        Assert.Equal("restetag", MahlzeitFelder.JsonName(MahlzeitFeld.Restetag));
    }

    // ---------- Sonderkost ----------

    [Fact]
    public void Sonderkost_Uebersicht_zaehlt_je_Option_in_Katalogreihenfolge_nur_Anzahlen_groesser_0()
    {
        IReadOnlyList<IReadOnlyList<string>> profile = [["vegan"], ["kein-schweinefleisch", "vegetarisch"], ["vegetarisch"]];

        var uebersicht = EinkaufRechnen.SonderkostUebersicht(profile);

        Assert.Equal([("vegetarisch", 2), ("vegan", 1), ("kein-schweinefleisch", 1)], uebersicht.Select(s => (s.Sonderkost, s.Anzahl)));
        Assert.Equal([("vegetarisch", 1)], EinkaufRechnen.SonderkostUebersicht([["vegetarisch"]]).Select(s => (s.Sonderkost, s.Anzahl)));
        Assert.Empty(EinkaufRechnen.SonderkostUebersicht([]));
    }

    [Fact]
    public void Die_Sonderkost_der_API_Vertraege_gleicht_der_Auswahl_der_Frage_Ernaehrung_in_Schluessel_Text_und_Reihenfolge()
    {
        var frage = Fragenkatalog.Finden(Fragenkatalog.Ernaehrung)!;

        Assert.Equal(frage.Optionen.Select(o => (o.Wert, o.Text)), Sonderkost.Alle.Select(s => (s.Wert, s.Text)));
        Assert.Equal("Vegetarisch", Sonderkost.Text("vegetarisch"));
        Assert.Equal("unbekannt", Sonderkost.Text("unbekannt"));
    }

    // ---------- Tag ohne Wahl ----------

    [Fact]
    public void Ohne_Wahl_heute_in_Berlin_wenn_heute_ein_Freizeittag_ist_sonst_der_erste_Tag()
    {
        var tage = new FreizeitTage(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13));

        Assert.Equal(new DateOnly(2027, 8, 5), KuecheWerte.TagOhneWahl(tage, new DateTimeOffset(2027, 8, 5, 10, 0, 0, TimeSpan.Zero)));
        // 22:30 UTC am 04.08. ist in Berlin (MESZ) schon der 05.08.
        Assert.Equal(new DateOnly(2027, 8, 5), KuecheWerte.TagOhneWahl(tage, new DateTimeOffset(2027, 8, 4, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 8, 13), KuecheWerte.TagOhneWahl(tage, new DateTimeOffset(2027, 8, 13, 21, 59, 0, TimeSpan.Zero)));
        // Davor und danach: der erste Tag.
        Assert.Equal(new DateOnly(2027, 8, 2), KuecheWerte.TagOhneWahl(tage, new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 8, 2), KuecheWerte.TagOhneWahl(tage, new DateTimeOffset(2027, 8, 13, 22, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 8, 2), KuecheWerte.TagOhneWahl(tage, new DateTimeOffset(2027, 8, 1, 21, 59, 0, TimeSpan.Zero)));
    }

    // ---------- Datenbank: Mandantenfilter und eindeutiger Index ----------

    [Fact]
    public async Task Mandantenfilter_MahlzeitStand_nur_im_eigenen_Mandanten_eindeutig_je_Freizeit_und_Programmpunkt()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await DatenAsync(factory, orgA);
        var b = await DatenAsync(factory, orgB);

        Assert.Equal([a.Stand], await LesenAsync(factory, orgA, db => db.MahlzeitStaende.Select(s => s.Id).ToListAsync(Abbruch)));
        Assert.Equal([b.Stand], await LesenAsync(factory, orgB, db => db.MahlzeitStaende.Select(s => s.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.MahlzeitStaende.ToListAsync(Abbruch)));

        // Eine zweite Zeile für dieselbe (Freizeit, Mahlzeit) verbietet der eindeutige Index.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgA);
            var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
            db.MahlzeitStaende.Add(MahlzeitStand.Anlegen(Guid.CreateVersion7(), orgA, a.Freizeit, a.Mahlzeit));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Abbruch));
        }

        // Sicherstellen und sperren legt keine zweite Zeile an und liefert die vorhandene; im fremden Mandanten nichts.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgA);
            var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
            await using var transaktion = await db.Database.BeginTransactionAsync(Abbruch);
            var stand = await db.MahlzeitStandSperrenAsync(Guid.CreateVersion7(), orgA, a.Freizeit, a.Mahlzeit, Abbruch);
            Assert.Equal((a.Stand, true), (stand.Id, stand.Restetag));
            await transaktion.CommitAsync(Abbruch);
        }

        Assert.Single(await LesenAsync(factory, orgA, db => db.MahlzeitStaende.ToListAsync(Abbruch)));
    }

    private static async Task<(Guid Freizeit, Guid Mahlzeit, Guid Stand)> DatenAsync(ZeltlotseApiFactory factory, Guid organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<KuecheDbContext>();
        var (freizeit, mahlzeit) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var stand = MahlzeitStand.Anlegen(Guid.CreateVersion7(), organisation, freizeit, mahlzeit);
        Assert.True(stand.FeldAendern(MahlzeitFeld.Restetag, "false", "true"));
        db.MahlzeitStaende.Add(stand);
        await db.SaveChangesAsync(Abbruch);
        return (freizeit, mahlzeit, stand.Id);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<KuecheLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<KuecheLeseDbContext>());
    }
}
