using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Programm.Bibliothek;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using Aufgabe = Zeltlotse.Programm.Aufgaben.Aufgabe;
using BibliotheksEintrag = Zeltlotse.Programm.Bibliothek.BibliotheksEintrag;
using Material = Zeltlotse.Programm.Materialien.Material;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Tests;

/// <summary>
/// Story 3-6, Programmbibliothek (CAP-24, FR-41, AD-6, AD-16): Ablegen als Momentaufnahme (Titel, Material nach Bezeichnung, Aufgaben nach Phase
/// und Titel, ohne Status und Personen), keine Mahlzeit, die Verknüpfung am Programmpunkt, die Formprüfung der Übernahme, die JSON-Spalten, der
/// Mandantenfilter, das Leeren der Verknüpfung beim Entfernen und die Gleichheit der Rechteliste mit Organisationen.
/// </summary>
public sealed class BibliothekTests(PostgresContainer postgres)
{
    private static readonly DateTimeOffset Jetzt = new(2027, 8, 14, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    /// <summary>„Geländespiel“ mit Seilen (eingepackt, Menge) und Fahnen (von Jonas mitgebracht), der Aufgabe „Fahnen verstecken“ (Während, übernommen)
    /// und „Spielfeld abstecken“ (Vorbereitung, erledigt) sowie Material und Aufgabe eines anderen Punkts.</summary>
    private static (Programmpunkt Punkt, List<Material> Material, List<Aufgabe> Aufgaben) Gelaendespiel(Guid org)
    {
        var freizeit = Guid.CreateVersion7();
        var punkt = Programmpunkt.Anlegen(Guid.CreateVersion7(), org, freizeit, new DateOnly(2027, 8, 4), new TimeOnly(14, 0), "Geländespiel");
        var anderer = Guid.CreateVersion7();
        var seile = Material.Anlegen(Guid.CreateVersion7(), org, freizeit, punkt.Id, "Seile", "4 × 10 m");
        seile.StatusSetzen(MaterialStatus.Eingepackt);
        var fahnen = Material.Anlegen(Guid.CreateVersion7(), org, freizeit, punkt.Id, "fahnen", null);
        fahnen.Mitbringen(Guid.CreateVersion7());
        var fremdesMaterial = Material.Anlegen(Guid.CreateVersion7(), org, freizeit, anderer, "Gitarre", null);
        var verstecken = Aufgabe.Anlegen(Guid.CreateVersion7(), org, freizeit, "Fahnen verstecken", AufgabenPhase.Waehrend, punkt.Id);
        verstecken.Uebernehmen(Guid.CreateVersion7());
        var abstecken = Aufgabe.Anlegen(Guid.CreateVersion7(), org, freizeit, "Spielfeld abstecken", AufgabenPhase.Vorbereitung, punkt.Id);
        abstecken.Erledigen();
        var fremdeAufgabe = Aufgabe.Anlegen(Guid.CreateVersion7(), org, freizeit, "Liedblatt drucken", AufgabenPhase.Vorbereitung, anderer);
        return (punkt, [seile, fahnen, fremdesMaterial], [verstecken, abstecken, fremdeAufgabe]);
    }

    // ---------- Entität ----------

    [Fact]
    public void Ablegen_nimmt_Titel_Material_nach_Bezeichnung_und_Aufgaben_nach_Phase_ohne_Status_Personen_und_fremde_Punkte()
    {
        var org = Guid.CreateVersion7();
        var (punkt, material, aufgaben) = Gelaendespiel(org);
        var id = Guid.CreateVersion7();

        var eintrag = BibliotheksEintrag.Ablegen(id, org, punkt, material, aufgaben, Jetzt);

        Assert.Equal((id, org, "Geländespiel", Jetzt), (eintrag.Id, eintrag.OrganisationId, eintrag.Titel, eintrag.AbgelegtAm));
        Assert.Equal([("fahnen", (string?)null), ("Seile", "4 × 10 m")], eintrag.Material.Select(m => (m.Bezeichnung, m.Menge)));
        Assert.Equal(
            [("Spielfeld abstecken", AufgabenPhase.Vorbereitung), ("Fahnen verstecken", AufgabenPhase.Waehrend)],
            eintrag.Aufgaben.Select(a => (a.Titel, a.Phase)));
    }

    [Fact]
    public void Ablegen_einer_Mahlzeit_wirft()
    {
        var org = Guid.CreateVersion7();
        var fruehstueck = Programmpunkt.MahlzeitAnlegen(Guid.CreateVersion7(), org, Guid.CreateVersion7(), new DateOnly(2027, 8, 4), Mahlzeit.Fruehstueck);

        Assert.Throws<InvalidOperationException>(() => BibliotheksEintrag.Ablegen(Guid.CreateVersion7(), org, fruehstueck, [], [], Jetzt));
    }

    [Fact]
    public void BibliothekMerken_verknuepft_den_Programmpunkt()
    {
        var punkt = Programmpunkt.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2027, 8, 4), null, "Lagerfeuer");
        var eintrag = Guid.CreateVersion7();
        Assert.Null(punkt.BibliotheksEintragId);
        Assert.False(ProgrammLesen.AlsApi(punkt, null).InBibliothek);

        punkt.BibliothekMerken(eintrag);

        Assert.Equal(eintrag, punkt.BibliotheksEintragId);
        Assert.True(ProgrammLesen.AlsApi(punkt, null).InBibliothek);
    }

    // ---------- Formprüfung der Übernahme ----------

    [Theory]
    [InlineData("2027-08-05", "10:00", null)]
    [InlineData("2027-08-05", null, null)]
    [InlineData("2027-08-05", "", null)]
    [InlineData(null, "10:00", "tag")]
    [InlineData("2027-08-05", "25:99", "beginn")]
    public void Uebernahme_braucht_einen_Tag_und_eine_Uhrzeit_HH_mm_oder_keine(string? tag, string? beginn, string? fehlerFeld)
    {
        var ergebnis = new BibliothekUebernehmenValidator().Validate(new BibliothekUebernehmenCommand(
            Guid.CreateVersion7(), Guid.CreateVersion7(), tag is null ? null : DateOnly.Parse(tag, System.Globalization.CultureInfo.InvariantCulture), beginn));

        Assert.Equal(fehlerFeld is null ? [] : [fehlerFeld], ergebnis.Errors.Select(e => e.PropertyName));
    }

    // ---------- Speichern, Mandantenfilter, Verknüpfung ----------

    [Fact]
    public async Task Material_und_Aufgaben_kommen_aus_den_JSON_Spalten_zurueck_und_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (punktA, materialA, aufgabenA) = Gelaendespiel(orgA);
        var eintragA = BibliotheksEintrag.Ablegen(Guid.CreateVersion7(), orgA, punktA, materialA, aufgabenA, Jetzt);
        var punktB = Programmpunkt.Anlegen(Guid.CreateVersion7(), orgB, Guid.CreateVersion7(), new DateOnly(2027, 9, 1), null, "Lagerfeuer");
        var eintragB = BibliotheksEintrag.Ablegen(Guid.CreateVersion7(), orgB, punktB, [], [], Jetzt);
        await SpeichernAsync(factory, orgA, db => db.Bibliothek.Add(eintragA));
        await SpeichernAsync(factory, orgB, db => db.Bibliothek.Add(eintragB));

        var gelesen = Assert.Single(await LesenAsync(factory, orgA, db => db.Bibliothek.ToListAsync(Abbruch)));
        Assert.Equal((eintragA.Id, "Geländespiel", Jetzt), (gelesen.Id, gelesen.Titel, gelesen.AbgelegtAm));
        Assert.Equal([("fahnen", (string?)null), ("Seile", "4 × 10 m")], gelesen.Material.Select(m => (m.Bezeichnung, m.Menge)));
        Assert.Equal(
            [("Spielfeld abstecken", AufgabenPhase.Vorbereitung), ("Fahnen verstecken", AufgabenPhase.Waehrend)],
            gelesen.Aufgaben.Select(a => (a.Titel, a.Phase)));
        var leer = Assert.Single(await LesenAsync(factory, orgB, db => db.Bibliothek.ToListAsync(Abbruch)));
        Assert.Equal((eintragB.Id, 0, 0), (leer.Id, leer.Material.Count, leer.Aufgaben.Count));
        Assert.Empty(await LesenAsync(factory, null, db => db.Bibliothek.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreibend = scope.ServiceProvider.GetRequiredService<ProgrammDbContext>();
        Assert.Equal([eintragB.Id], await schreibend.Bibliothek.Select(e => e.Id).ToListAsync(Abbruch));
        Assert.Null(await schreibend.EintragSperrenAsync(eintragA.Id, Abbruch));
        Assert.NotNull(await schreibend.EintragSperrenAsync(eintragB.Id, Abbruch));
    }

    [Fact]
    public async Task Wird_der_Eintrag_entfernt_bleibt_der_Programmpunkt_ohne_Verknuepfung()
    {
        await using var factory = await FactoryAsync();
        var org = Guid.CreateVersion7();
        var punkt = Programmpunkt.Anlegen(Guid.CreateVersion7(), org, Guid.CreateVersion7(), new DateOnly(2027, 8, 4), null, "Lagerfeuer");
        var eintrag = BibliotheksEintrag.Ablegen(Guid.CreateVersion7(), org, punkt, [], [], Jetzt);
        punkt.BibliothekMerken(eintrag.Id);
        await SpeichernAsync(factory, org, db => db.Bibliothek.Add(eintrag), db => db.Programmpunkte.Add(punkt));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(org);
            await scope.ServiceProvider.GetRequiredService<ProgrammDbContext>().Bibliothek.ExecuteDeleteAsync(Abbruch);
        }

        var danach = Assert.Single(await LesenAsync(factory, org, db => db.Programmpunkte.ToListAsync(Abbruch)));
        Assert.Equal(("Lagerfeuer", null), (danach.Titel, danach.BibliotheksEintragId));
    }

    // ---------- Rechte ----------

    [Fact]
    public void Bibliothek_ist_gleich_der_Programmbibliothek_in_Organisationen_nur_Org_Admin()
    {
        var werte = (Array)Assembly.Load("Zeltlotse.Organisationen").GetType("Zeltlotse.Organisationen.Rollen.OrgRechte", throwOnError: true)!
            .GetField("Programmbibliothek", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        Assert.Equal(werte.Cast<object>().Order(), ProgrammRechte.Bibliothek.Cast<object>().Order());
        Assert.Equal([SharedKernel.Rechte.OrgRolle.OrgAdmin], ProgrammRechte.Bibliothek);
    }

    // ---------- Helfer ----------

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        return factory;
    }

    private static async Task SpeichernAsync(ZeltlotseApiFactory factory, Guid organisation, params Action<ProgrammDbContext>[] schritte)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<ProgrammDbContext>();
        foreach (var schritt in schritte)
        {
            schritt(db);
            await db.SaveChangesAsync(Abbruch);
        }
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<ProgrammLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<ProgrammLeseDbContext>());
    }
}
