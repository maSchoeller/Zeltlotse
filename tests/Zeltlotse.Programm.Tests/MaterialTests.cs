using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Inventar.Contracts.Events;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Datenschutz;
using Zeltlotse.Programm.Materialien;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using Material = Zeltlotse.Programm.Materialien.Material;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Tests;

/// <summary>
/// Story 3-2, Material am Programmpunkt (FR-36, FR-69, AD-6, AD-16, AD-19, AD-28): Anlegen mit Vorgabe „gebraucht“, Statuswechsel in jeden Zustand,
/// „Ich bringe das mit“ und Zurücknehmen nur für sich selbst (Platzhalter gilt als frei), Feldänderung mit Konfliktregel und Trimmen, die
/// Formprüfung, der Mandantenfilter, das Mitlöschen mit dem Programmpunkt und der Empfänger von <c>PersonGeloescht</c>.
/// </summary>
public sealed class MaterialTests(PostgresContainer postgres)
{
    private static readonly DateOnly Tag = new(2027, 8, 3);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Material Neu(string bezeichnung = "Seile", string? menge = "4 × 10 m") =>
        Material.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), bezeichnung, menge);

    // ---------- Entität: Anlegen und Status ----------

    [Fact]
    public void Anlegen_trimmt_Bezeichnung_und_Menge_und_setzt_den_Status_gebraucht()
    {
        var material = Neu("  Seile ", " 4 × 10 m ");

        Assert.Equal(("Seile", "4 × 10 m", MaterialStatus.Gebraucht), (material.Bezeichnung, material.Menge, material.Status));
        Assert.Null(material.PersonId);
        Assert.Null(material.BringtMit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anlegen_ohne_Menge_heisst_keine_Menge(string? menge)
    {
        Assert.Null(Neu(menge: menge).Menge);
    }

    [Fact]
    public void StatusSetzen_erlaubt_jeden_Zustand_und_derselbe_Zustand_bleibt()
    {
        var material = Neu();

        foreach (var status in new[] { MaterialStatus.Eingepackt, MaterialStatus.Gebraucht, MaterialStatus.Besorgt, MaterialStatus.Vorhanden, MaterialStatus.Vorhanden })
        {
            material.StatusSetzen(status);
            Assert.Equal(status, material.Status);
        }
    }

    [Fact]
    public void Mitbringen_aendert_den_Status_nicht()
    {
        var material = Neu();

        material.Mitbringen(Guid.CreateVersion7());

        Assert.Equal(MaterialStatus.Gebraucht, material.Status);
    }

    // ---------- Entität: Ich bringe das mit ----------

    [Fact]
    public void Mitbringen_setzt_die_Person_und_ist_fuer_dieselbe_Person_wiederholbar_eine_andere_ist_ein_Konflikt()
    {
        var material = Neu();
        var (miriam, jonas) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.True(material.Mitbringen(miriam));
        Assert.True(material.Mitbringen(miriam));
        Assert.False(material.Mitbringen(jonas));

        Assert.Equal(miriam, material.BringtMit);
    }

    [Fact]
    public void NichtMitbringen_macht_frei_frei_bleibt_frei_eine_andere_Person_darf_nicht()
    {
        var material = Neu();
        var (miriam, jonas) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.True(material.NichtMitbringen(miriam));

        material.Mitbringen(miriam);
        Assert.False(material.NichtMitbringen(jonas));
        Assert.Equal(miriam, material.PersonId);

        Assert.True(material.NichtMitbringen(miriam));
        Assert.Null(material.PersonId);
    }

    [Fact]
    public void Der_Platzhalter_einer_geloeschten_Person_gilt_als_frei()
    {
        var material = Neu();
        var miriam = Guid.CreateVersion7();
        material.Mitbringen(Guid.CreateVersion7());
        typeof(Material).GetProperty(nameof(Material.PersonId))!.SetValue(material, GeloeschtePerson.Id);
        Assert.Null(material.BringtMit);

        Assert.True(material.Mitbringen(miriam));
        Assert.Equal(miriam, material.BringtMit);

        typeof(Material).GetProperty(nameof(Material.PersonId))!.SetValue(material, GeloeschtePerson.Id);
        Assert.True(material.NichtMitbringen(miriam));
        Assert.Null(material.PersonId);
    }

    // ---------- Entität: Feldänderung ----------

    [Fact]
    public void FeldAendern_setzt_getrimmt_wenn_der_alte_Wert_stimmt_leere_Menge_heisst_keine_Menge()
    {
        var material = Neu();

        Assert.True(material.FeldAendern(MaterialFeld.Bezeichnung, " Seile ", "  Kletterseile "));
        Assert.True(material.FeldAendern(MaterialFeld.Menge, "4 × 10 m", " 6 × 10 m "));
        Assert.Equal(("Kletterseile", "6 × 10 m"), (material.Bezeichnung, material.Menge));

        Assert.True(material.FeldAendern(MaterialFeld.Menge, "6 × 10 m", "  "));
        Assert.Null(material.Menge);
        Assert.Null(material.Wert(MaterialFeld.Menge));
        Assert.True(material.FeldAendern(MaterialFeld.Menge, null, "2"));
        Assert.Equal("2", material.Wert(MaterialFeld.Menge));
    }

    [Fact]
    public void FeldAendern_mit_abweichendem_alten_Wert_desselben_Felds_ist_ein_Konflikt_ein_anderes_Feld_nicht()
    {
        var material = Neu();
        Assert.True(material.FeldAendern(MaterialFeld.Menge, "4 × 10 m", "5 × 10 m"));

        Assert.False(material.FeldAendern(MaterialFeld.Menge, "4 × 10 m", "6 × 10 m"));
        Assert.Equal("5 × 10 m", material.Menge);

        Assert.True(material.FeldAendern(MaterialFeld.Bezeichnung, "Seile", "Kletterseile"));
        Assert.Equal("Kletterseile", material.Bezeichnung);
    }

    // ---------- Entität: Reservierung aus dem Inventar (Story 3-3) ----------

    [Fact]
    public void ReservierungZuordnen_merkt_die_Kennung_und_setzt_vorhanden_Loesen_nur_mit_derselben_Kennung()
    {
        var material = Neu();
        material.StatusSetzen(MaterialStatus.Besorgt);
        var (reservierung, andere) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        material.ReservierungZuordnen(reservierung);
        Assert.Equal((reservierung, MaterialStatus.Vorhanden), (material.ReservierungId, material.Status));

        material.ReservierungLoesen(andere);
        Assert.Equal((reservierung, MaterialStatus.Vorhanden), (material.ReservierungId, material.Status));

        material.ReservierungLoesen(reservierung);
        Assert.Equal(((Guid?)null, MaterialStatus.Gebraucht), (material.ReservierungId, material.Status));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("x", true)]
    public void Validatoren_verlangen_eine_Bezeichnung(string? bezeichnung, bool gueltig)
    {
        var anlegen = new MaterialAnlegenValidator().Validate(new MaterialAnlegenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), bezeichnung, null));

        Assert.Equal(gueltig, anlegen.IsValid);
        Assert.Equal(gueltig, AendernPruefen(MaterialFeld.Bezeichnung, bezeichnung).IsValid);
        if (!gueltig)
        {
            var fehler = Assert.Single(anlegen.Errors);
            Assert.Equal(("bezeichnung", "Bezeichnung fehlt."), (fehler.PropertyName, fehler.ErrorMessage));
        }
    }

    [Fact]
    public void Validatoren_erlauben_hoechstens_120_Zeichen_Bezeichnung_und_40_Zeichen_Menge_nach_dem_Trimmen()
    {
        static FluentValidation.Results.ValidationResult Anlegen(string bezeichnung, string? menge) =>
            new MaterialAnlegenValidator().Validate(new MaterialAnlegenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), bezeichnung, menge));

        Assert.True(Anlegen($" {new string('a', 120)} ", $" {new string('m', 40)} ").IsValid);
        var zuLang = Anlegen(new string('a', 121), null);
        Assert.Equal(("bezeichnung", "Höchstens 120 Zeichen."), (Assert.Single(zuLang.Errors).PropertyName, zuLang.Errors[0].ErrorMessage));
        var mengeZuLang = Anlegen("Seile", new string('m', 41));
        Assert.Equal(("menge", "Höchstens 40 Zeichen."), (Assert.Single(mengeZuLang.Errors).PropertyName, mengeZuLang.Errors[0].ErrorMessage));
        Assert.True(Anlegen("Seile", "").IsValid);

        Assert.False(AendernPruefen(MaterialFeld.Bezeichnung, new string('a', 121)).IsValid);
        Assert.False(AendernPruefen(MaterialFeld.Menge, new string('m', 41)).IsValid);
        Assert.True(AendernPruefen(MaterialFeld.Menge, "").IsValid);
        Assert.True(AendernPruefen(MaterialFeld.Menge, null).IsValid);
        Assert.Equal("feld", Assert.Single(AendernPruefen((MaterialFeld)99, "x").Errors).PropertyName);
    }

    [Fact]
    public void Der_Status_muss_angegeben_sein()
    {
        var ohne = new MaterialStatusSetzenValidator().Validate(new MaterialStatusSetzenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), null));

        Assert.Equal("status", Assert.Single(ohne.Errors).PropertyName);
        Assert.True(new MaterialStatusSetzenValidator().Validate(new MaterialStatusSetzenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), MaterialStatus.Eingepackt)).IsValid);
        Assert.False(new MaterialStatusSetzenValidator().Validate(new MaterialStatusSetzenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), (MaterialStatus)9)).IsValid);
    }

    [Fact]
    public void Das_Mitbringen_gibt_keine_Personenkennung_aus()
    {
        var person = Guid.CreateVersion7();

        var text = new MaterialMitbringenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), person, Mitbringen: true).ToString();

        Assert.DoesNotContain(person.ToString(), text, StringComparison.Ordinal);
    }

    private static FluentValidation.Results.ValidationResult AendernPruefen(MaterialFeld feld, string? wert) =>
        new MaterialFeldAendernValidator().Validate(new MaterialFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, null, wert));

    // ---------- Datenbank: Mandantenfilter und Mitlöschen ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_das_eigene_Material()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var inA = await MaterialAsync(factory, orgA, "Fahnen");
        var inB = await MaterialAsync(factory, orgB, "Seile");

        Assert.Equal([inA.Id], await LesenAsync(factory, orgA, db => db.Material.Select(m => m.Id).ToListAsync(Abbruch)));
        Assert.Equal([inB.Id], await LesenAsync(factory, orgB, db => db.Material.Select(m => m.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Material.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        Assert.Equal([inB.Id], await scope.ServiceProvider.GetRequiredService<ProgrammDbContext>().Material.Select(m => m.Id).ToListAsync(Abbruch));
    }

    [Fact]
    public async Task Ein_geloeschter_Programmpunkt_nimmt_sein_Material_mit()
    {
        await using var factory = await FactoryAsync();
        var org = Guid.CreateVersion7();
        var material = await MaterialAsync(factory, org, "Fahnen");
        var bleibt = await MaterialAsync(factory, org, "Seile");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(org);
            var db = scope.ServiceProvider.GetRequiredService<ProgrammDbContext>();
            await db.Programmpunkte.Where(p => p.Id == material.ProgrammpunktId).ExecuteDeleteAsync(Abbruch);
        }

        Assert.Equal([bleibt.Id], await LesenAsync(factory, org, db => db.Material.Select(m => m.Id).ToListAsync(Abbruch)));
    }

    // ---------- Empfänger PersonGeloescht ----------

    [Fact]
    public async Task PersonGeloescht_ersetzt_die_mitbringende_Person_in_allen_Organisationen_durch_den_Platzhalter()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (miriam, jonas) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (var (org, person) in new[] { (orgA, miriam), (orgB, miriam), (orgA, jonas) })
        {
            await MaterialAsync(factory, org, "Taschenlampen", person);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                ProgrammPersonLoeschen.Aus(new Umschlag(Guid.CreateVersion7(), factory.Uhr.GetUtcNow(), null, miriam, 1), new PersonGeloescht(miriam)), Abbruch);
        }

        var personen = new List<Guid?>();
        foreach (var org in new[] { orgA, orgB })
        {
            personen.AddRange(await LesenAsync(factory, org, db => db.Material.Select(m => m.PersonId).ToListAsync(Abbruch)));
        }

        Assert.Equal(2, personen.Count(p => p == GeloeschtePerson.Id));
        Assert.Single(personen, p => p == jonas);
        Assert.DoesNotContain(miriam, personen);
    }

    // ---------- Empfänger ReservierungGeaendert (Story 3-3) ----------

    [Fact]
    public async Task ReservierungGeaendert_setzt_vorhanden_mit_Kennung_und_aufgehoben_wieder_gebraucht_fremde_Freizeit_aendert_nichts()
    {
        await using var factory = await FactoryAsync();
        var org = Guid.CreateVersion7();
        var material = await MaterialAsync(factory, org, "Fahnen");
        var reservierung = Guid.CreateVersion7();

        await EmpfangenAsync(factory, org, new ReservierungGeaendert(reservierung, Guid.CreateVersion7(), Guid.CreateVersion7(), material.Id, ReservierungArt.Reserviert));
        Assert.Equal(((Guid?)null, MaterialStatus.Gebraucht), await StandAsync(factory, org, material.Id));

        await EmpfangenAsync(factory, org, new ReservierungGeaendert(reservierung, material.FreizeitId, Guid.CreateVersion7(), material.Id, ReservierungArt.Reserviert));
        Assert.Equal(((Guid?)reservierung, MaterialStatus.Vorhanden), await StandAsync(factory, org, material.Id));

        await EmpfangenAsync(factory, org, new ReservierungGeaendert(Guid.CreateVersion7(), material.FreizeitId, Guid.CreateVersion7(), material.Id, ReservierungArt.Aufgehoben));
        Assert.Equal(((Guid?)reservierung, MaterialStatus.Vorhanden), await StandAsync(factory, org, material.Id));

        await EmpfangenAsync(factory, org, new ReservierungGeaendert(reservierung, material.FreizeitId, Guid.CreateVersion7(), material.Id, ReservierungArt.Aufgehoben));
        Assert.Equal(((Guid?)null, MaterialStatus.Gebraucht), await StandAsync(factory, org, material.Id));

        // Unbekanntes Material und ein anderer Mandant: nichts geschieht, kein Fehler.
        await EmpfangenAsync(factory, org, new ReservierungGeaendert(reservierung, material.FreizeitId, Guid.CreateVersion7(), Guid.CreateVersion7(), ReservierungArt.Reserviert));
        await EmpfangenAsync(factory, Guid.CreateVersion7(), new ReservierungGeaendert(reservierung, material.FreizeitId, Guid.CreateVersion7(), material.Id, ReservierungArt.Reserviert));
        Assert.Equal(((Guid?)null, MaterialStatus.Gebraucht), await StandAsync(factory, org, material.Id));
    }

    private static async Task EmpfangenAsync(ZeltlotseApiFactory factory, Guid organisation, ReservierungGeaendert ereignis)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            ProgrammReservierungUebernehmen.Aus(new Umschlag(Guid.CreateVersion7(), factory.Uhr.GetUtcNow(), organisation, Guid.CreateVersion7(), 1), ereignis), Abbruch);
    }

    private static Task<(Guid?, MaterialStatus)> StandAsync(ZeltlotseApiFactory factory, Guid organisation, Guid material) =>
        LesenAsync(factory, organisation, async db =>
        {
            var m = await db.Material.SingleAsync(x => x.Id == material, Abbruch);
            return (m.ReservierungId, m.Status);
        });

    // ---------- Helfer ----------

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        return factory;
    }

    /// <summary>Ein Programmpunkt mit einem Material in einer neuen Freizeit des Mandanten.</summary>
    private static async Task<Material> MaterialAsync(ZeltlotseApiFactory factory, Guid organisation, string bezeichnung, Guid? mitbringen = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<ProgrammDbContext>();
        var freizeit = Guid.CreateVersion7();
        var punkt = Programmpunkt.Anlegen(Guid.CreateVersion7(), organisation, freizeit, Tag, null, "Geländespiel");
        var material = Material.Anlegen(Guid.CreateVersion7(), organisation, freizeit, punkt.Id, bezeichnung, null);
        if (mitbringen is { } person)
        {
            material.Mitbringen(person);
        }

        db.Programmpunkte.Add(punkt);
        db.Material.Add(material);
        await db.SaveChangesAsync(Abbruch);
        return material;
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<ProgrammLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<ProgrammLeseDbContext>());
    }
}
