using System.Reflection;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.Programm.Aufgaben;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Datenschutz;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using Aufgabe = Zeltlotse.Programm.Aufgaben.Aufgabe;
using AufgabenVorlage = Zeltlotse.Programm.Aufgaben.AufgabenVorlage;

namespace Zeltlotse.Programm.Tests;

/// <summary>
/// Story 3-5, Aufgaben und Aufgaben-Vorlagen (CAP-23, FR-39, FR-40, AD-6, AD-16, AD-19, AD-28): Anlegen mit Trimmen, aus einer Vorlage,
/// Feldänderung mit Konfliktregel, „Ich übernehme“ und „Abgeben“ mit Platzhalter, Erledigen und Wieder öffnen, die Verknüpfung zur Vorlage,
/// die Formprüfung, der Mandantenfilter beider Entitäten, der Empfänger von <c>PersonGeloescht</c> und die Gleichheit der Rechtelisten mit
/// Organisationen und Freizeiten.
/// </summary>
public sealed class AufgabeTests(PostgresContainer postgres)
{
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Aufgabe Neu(string titel = "Andacht vorbereiten", AufgabenPhase phase = AufgabenPhase.Vorbereitung, Guid? punkt = null) =>
        Aufgabe.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), titel, phase, punkt);

    // ---------- Entität: Anlegen ----------

    [Fact]
    public void Anlegen_trimmt_den_Titel_und_die_Aufgabe_ist_offen_und_unbesetzt()
    {
        var punkt = Guid.CreateVersion7();

        var aufgabe = Neu("  Andacht vorbereiten ", AufgabenPhase.Waehrend, punkt);

        Assert.Equal(("Andacht vorbereiten", AufgabenPhase.Waehrend, punkt), (aufgabe.Titel, aufgabe.Phase, aufgabe.ProgrammpunktId));
        Assert.Null(aufgabe.PersonId);
        Assert.Null(aufgabe.Uebernommen);
        Assert.False(aufgabe.Erledigt);
        Assert.Null(aufgabe.VorlageId);
    }

    [Fact]
    public void AusVorlage_uebernimmt_Titel_Phase_Organisation_und_merkt_die_Vorlage()
    {
        var org = Guid.CreateVersion7();
        var vorlage = AufgabenVorlage.Anlegen(Guid.CreateVersion7(), org, " Zeltplatz übergeben ", AufgabenPhase.Nachbereitung);
        var freizeit = Guid.CreateVersion7();

        var aufgabe = Aufgabe.AusVorlage(Guid.CreateVersion7(), freizeit, vorlage);

        Assert.Equal(("Zeltplatz übergeben", AufgabenPhase.Nachbereitung), (vorlage.Titel, vorlage.Phase));
        Assert.Equal((org, freizeit, "Zeltplatz übergeben", AufgabenPhase.Nachbereitung, vorlage.Id), (aufgabe.OrganisationId, aufgabe.FreizeitId, aufgabe.Titel, aufgabe.Phase, aufgabe.VorlageId));
        Assert.Null(aufgabe.ProgrammpunktId);
    }

    // ---------- Entität: Übergänge ----------

    [Fact]
    public void Uebernehmen_ist_fuer_dieselbe_Person_wiederholbar_fuer_eine_andere_ein_Konflikt()
    {
        var aufgabe = Neu();
        var (lea, tobias) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.True(aufgabe.Uebernehmen(lea));
        Assert.True(aufgabe.Uebernehmen(lea));
        Assert.False(aufgabe.Uebernehmen(tobias));
        Assert.False(aufgabe.Abgeben(tobias));

        Assert.Equal(lea, aufgabe.Uebernommen);
        Assert.True(aufgabe.Abgeben(lea));
        Assert.Null(aufgabe.PersonId);
        Assert.True(aufgabe.Abgeben(lea));
    }

    [Fact]
    public void Der_Platzhalter_einer_geloeschten_Person_gilt_als_frei()
    {
        var aufgabe = Neu();
        var lea = Guid.CreateVersion7();
        typeof(Aufgabe).GetProperty(nameof(Aufgabe.PersonId))!.SetValue(aufgabe, GeloeschtePerson.Id);
        Assert.Null(aufgabe.Uebernommen);

        Assert.True(aufgabe.Uebernehmen(lea));
        Assert.Equal(lea, aufgabe.Uebernommen);

        typeof(Aufgabe).GetProperty(nameof(Aufgabe.PersonId))!.SetValue(aufgabe, GeloeschtePerson.Id);
        Assert.True(aufgabe.Abgeben(lea));
        Assert.Null(aufgabe.PersonId);
    }

    [Fact]
    public void Erledigen_und_Oeffnen_sind_wiederholbar_und_lassen_die_Uebernahme_stehen()
    {
        var aufgabe = Neu();
        var lea = Guid.CreateVersion7();
        aufgabe.Uebernehmen(lea);

        aufgabe.Erledigen();
        aufgabe.Erledigen();
        Assert.True(aufgabe.Erledigt);

        aufgabe.Oeffnen();
        aufgabe.Oeffnen();
        Assert.False(aufgabe.Erledigt);
        Assert.Equal(lea, aufgabe.Uebernommen);
    }

    [Fact]
    public void VorlageMerken_setzt_die_Kennung_der_Vorlage()
    {
        var aufgabe = Neu();
        var vorlage = Guid.CreateVersion7();

        aufgabe.VorlageMerken(vorlage);

        Assert.Equal(vorlage, aufgabe.VorlageId);
    }

    // ---------- Entität: Feldänderung ----------

    [Fact]
    public void FeldAendern_setzt_Titel_getrimmt_und_Phase_wenn_der_alte_Wert_stimmt()
    {
        var aufgabe = Neu();

        Assert.True(aufgabe.FeldAendern(AufgabeFeld.Titel, " Andacht vorbereiten ", "  Andacht am Lagerfeuer "));
        Assert.True(aufgabe.FeldAendern(AufgabeFeld.Phase, "vorbereitung", "Waehrend"));

        Assert.Equal(("Andacht am Lagerfeuer", AufgabenPhase.Waehrend), (aufgabe.Titel, aufgabe.Phase));
        Assert.Equal("Waehrend", aufgabe.Wert(AufgabeFeld.Phase));
        Assert.Equal("Andacht am Lagerfeuer", aufgabe.Wert(AufgabeFeld.Titel));
    }

    [Fact]
    public void FeldAendern_mit_abweichendem_alten_Wert_desselben_Felds_ist_ein_Konflikt_ein_anderes_Feld_nicht()
    {
        var aufgabe = Neu();
        Assert.True(aufgabe.FeldAendern(AufgabeFeld.Phase, "Vorbereitung", "Nachbereitung"));

        Assert.False(aufgabe.FeldAendern(AufgabeFeld.Phase, "Vorbereitung", "Waehrend"));
        Assert.Equal(AufgabenPhase.Nachbereitung, aufgabe.Phase);

        Assert.True(aufgabe.FeldAendern(AufgabeFeld.Titel, "Andacht vorbereiten", "Andacht"));
        Assert.Equal("Andacht", aufgabe.Titel);
    }

    [Fact]
    public void Vorlage_FeldAendern_folgt_derselben_Konfliktregel()
    {
        var vorlage = AufgabenVorlage.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), "Erste-Hilfe-Kasten auffüllen", AufgabenPhase.Vorbereitung);

        Assert.True(vorlage.FeldAendern(AufgabenVorlageFeld.Titel, "Erste-Hilfe-Kasten auffüllen", " Erste-Hilfe-Kasten prüfen "));
        Assert.False(vorlage.FeldAendern(AufgabenVorlageFeld.Titel, "Erste-Hilfe-Kasten auffüllen", "Anders"));
        Assert.True(vorlage.FeldAendern(AufgabenVorlageFeld.Phase, "Vorbereitung", "Waehrend"));

        Assert.Equal(("Erste-Hilfe-Kasten prüfen", AufgabenPhase.Waehrend), (vorlage.Titel, vorlage.Phase));
        Assert.Equal("Waehrend", vorlage.Wert(AufgabenVorlageFeld.Phase));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("x", true)]
    public void Validatoren_verlangen_einen_Titel(string? titel, bool gueltig)
    {
        var anlegen = new AufgabeAnlegenValidator().Validate(new AufgabeAnlegenCommand(Guid.CreateVersion7(), titel, null, null));
        var vorlage = new VorlageAnlegenValidator().Validate(new VorlageAnlegenCommand(titel, null));

        Assert.Equal(gueltig, anlegen.IsValid);
        Assert.Equal(gueltig, vorlage.IsValid);
        Assert.Equal(gueltig, AendernPruefen(AufgabeFeld.Titel, titel).IsValid);
        Assert.Equal(gueltig, VorlageAendernPruefen(AufgabenVorlageFeld.Titel, titel).IsValid);
        if (!gueltig)
        {
            var fehler = Assert.Single(anlegen.Errors);
            Assert.Equal(("titel", "Titel fehlt."), (fehler.PropertyName, fehler.ErrorMessage));
        }
    }

    [Fact]
    public void Validatoren_erlauben_hoechstens_120_Zeichen_im_Titel_nach_dem_Trimmen()
    {
        Assert.True(new AufgabeAnlegenValidator().Validate(new AufgabeAnlegenCommand(Guid.CreateVersion7(), $" {new string('a', 120)} ", null, null)).IsValid);
        var zuLang = new AufgabeAnlegenValidator().Validate(new AufgabeAnlegenCommand(Guid.CreateVersion7(), new string('a', 121), null, null));
        Assert.Equal(("titel", "Höchstens 120 Zeichen."), (Assert.Single(zuLang.Errors).PropertyName, zuLang.Errors[0].ErrorMessage));
        Assert.False(new VorlageAnlegenValidator().Validate(new VorlageAnlegenCommand(new string('a', 121), null)).IsValid);
        Assert.False(AendernPruefen(AufgabeFeld.Titel, new string('a', 121)).IsValid);
        Assert.False(VorlageAendernPruefen(AufgabenVorlageFeld.Titel, new string('a', 121)).IsValid);
    }

    [Theory]
    [InlineData("Vorbereitung", true)]
    [InlineData("waehrend", true)]
    [InlineData("NACHBEREITUNG", true)]
    [InlineData("1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Während", false)]
    public void Validatoren_pruefen_die_Phase(string? phase, bool gueltig)
    {
        var aendern = AendernPruefen(AufgabeFeld.Phase, phase);
        var vorlage = VorlageAendernPruefen(AufgabenVorlageFeld.Phase, phase);

        Assert.Equal(gueltig, aendern.IsValid);
        Assert.Equal(gueltig, vorlage.IsValid);
        if (!gueltig)
        {
            Assert.Equal("phase", Assert.Single(aendern.Errors).PropertyName);
            Assert.Equal("phase", Assert.Single(vorlage.Errors).PropertyName);
        }
    }

    [Fact]
    public void Anlegen_mit_unbekannter_Phase_ist_ein_Fehler_am_Feld_ohne_Phase_gueltig()
    {
        var unbekannt = new AufgabeAnlegenValidator().Validate(new AufgabeAnlegenCommand(Guid.CreateVersion7(), "Andacht", (AufgabenPhase)9, null));
        var vorlage = new VorlageAnlegenValidator().Validate(new VorlageAnlegenCommand("Andacht", (AufgabenPhase)0));

        Assert.Equal("phase", Assert.Single(unbekannt.Errors).PropertyName);
        Assert.Equal("phase", Assert.Single(vorlage.Errors).PropertyName);
        Assert.True(new AufgabeAnlegenValidator().Validate(new AufgabeAnlegenCommand(Guid.CreateVersion7(), "Andacht", null, null)).IsValid);
        Assert.Equal("feld", Assert.Single(AendernPruefen((AufgabeFeld)99, "x").Errors).PropertyName);
        Assert.Equal("feld", Assert.Single(VorlageAendernPruefen((AufgabenVorlageFeld)99, "x").Errors).PropertyName);
    }

    [Fact]
    public void Der_Uebergang_gibt_keine_Personenkennung_aus()
    {
        var person = Guid.CreateVersion7();

        var text = new AufgabeUebergangCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), person, AufgabeUebergang.Uebernehmen).ToString();

        Assert.DoesNotContain(person.ToString(), text, StringComparison.Ordinal);
    }

    private static FluentValidation.Results.ValidationResult AendernPruefen(AufgabeFeld feld, string? wert) =>
        new AufgabeFeldAendernValidator().Validate(new AufgabeFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, null, wert));

    private static FluentValidation.Results.ValidationResult VorlageAendernPruefen(AufgabenVorlageFeld feld, string? wert) =>
        new VorlageFeldAendernValidator().Validate(new VorlageFeldAendernCommand(Guid.CreateVersion7(), feld, 0, null, wert));

    // ---------- Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen_Aufgaben_und_Vorlagen()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var vorlageA = AufgabenVorlage.Anlegen(Guid.CreateVersion7(), orgA, "Zeltplatz-Vertrag prüfen", AufgabenPhase.Vorbereitung);
        var vorlageB = AufgabenVorlage.Anlegen(Guid.CreateVersion7(), orgB, "Zeltplatz-Vertrag prüfen", AufgabenPhase.Vorbereitung);
        var aufgabeA = Aufgabe.AusVorlage(Guid.CreateVersion7(), Guid.CreateVersion7(), vorlageA);
        var aufgabeB = Aufgabe.Anlegen(Guid.CreateVersion7(), orgB, Guid.CreateVersion7(), "Fotos sammeln", AufgabenPhase.Nachbereitung, null);
        await SpeichernAsync(factory, orgA, db => db.AufgabenVorlagen.Add(vorlageA), db => db.Aufgaben.Add(aufgabeA));
        await SpeichernAsync(factory, orgB, db => db.AufgabenVorlagen.Add(vorlageB), db => db.Aufgaben.Add(aufgabeB));

        Assert.Equal([aufgabeA.Id], await LesenAsync(factory, orgA, db => db.Aufgaben.Select(a => a.Id).ToListAsync(Abbruch)));
        Assert.Equal([vorlageA.Id], await LesenAsync(factory, orgA, db => db.AufgabenVorlagen.Select(v => v.Id).ToListAsync(Abbruch)));
        Assert.Equal([aufgabeB.Id], await LesenAsync(factory, orgB, db => db.Aufgaben.Select(a => a.Id).ToListAsync(Abbruch)));
        Assert.Equal([vorlageB.Id], await LesenAsync(factory, orgB, db => db.AufgabenVorlagen.Select(v => v.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Aufgaben.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.AufgabenVorlagen.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreibend = scope.ServiceProvider.GetRequiredService<ProgrammDbContext>();
        Assert.Equal([aufgabeB.Id], await schreibend.Aufgaben.Select(a => a.Id).ToListAsync(Abbruch));
        Assert.Null(await schreibend.AufgabeSperrenAsync(aufgabeA.FreizeitId, aufgabeA.Id, Abbruch));
        Assert.NotNull(await schreibend.AufgabeSperrenAsync(aufgabeB.FreizeitId, aufgabeB.Id, Abbruch));
        Assert.Null(await schreibend.VorlageSperrenAsync(vorlageA.Id, Abbruch));
        Assert.NotNull(await schreibend.VorlageSperrenAsync(vorlageB.Id, Abbruch));
    }

    [Fact]
    public async Task Wird_die_Vorlage_geloescht_bleibt_die_Aufgabe_ohne_Verknuepfung()
    {
        await using var factory = await FactoryAsync();
        var org = Guid.CreateVersion7();
        var vorlage = AufgabenVorlage.Anlegen(Guid.CreateVersion7(), org, "Zeltplatz-Vertrag prüfen", AufgabenPhase.Vorbereitung);
        var aufgabe = Aufgabe.AusVorlage(Guid.CreateVersion7(), Guid.CreateVersion7(), vorlage);
        await SpeichernAsync(factory, org, db => db.AufgabenVorlagen.Add(vorlage), db => db.Aufgaben.Add(aufgabe));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(org);
            await scope.ServiceProvider.GetRequiredService<ProgrammDbContext>().AufgabenVorlagen.ExecuteDeleteAsync(Abbruch);
        }

        var danach = Assert.Single(await LesenAsync(factory, org, db => db.Aufgaben.ToListAsync(Abbruch)));
        Assert.Equal(("Zeltplatz-Vertrag prüfen", null), (danach.Titel, danach.VorlageId));
    }

    // ---------- Empfänger PersonGeloescht ----------

    [Fact]
    public async Task PersonGeloescht_ersetzt_die_Uebernahme_einer_Aufgabe_in_allen_Organisationen_durch_den_Platzhalter()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (lea, jonas) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (var (org, person) in new[] { (orgA, lea), (orgB, lea), (orgA, jonas) })
        {
            var aufgabe = Aufgabe.Anlegen(Guid.CreateVersion7(), org, Guid.CreateVersion7(), "Andacht vorbereiten", AufgabenPhase.Waehrend, null);
            aufgabe.Uebernehmen(person);
            await SpeichernAsync(factory, org, db => db.Aufgaben.Add(aufgabe));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                ProgrammPersonLoeschen.Aus(new Umschlag(Guid.CreateVersion7(), factory.Uhr.GetUtcNow(), null, lea, 1), new PersonGeloescht(lea)), Abbruch);
        }

        var personen = new List<Guid?>();
        foreach (var org in new[] { orgA, orgB })
        {
            personen.AddRange(await LesenAsync(factory, org, db => db.Aufgaben.Select(a => a.PersonId).ToListAsync(Abbruch)));
        }

        Assert.Equal(2, personen.Count(p => p == GeloeschtePerson.Id));
        Assert.Single(personen, p => p == jonas);
        Assert.DoesNotContain(lea, personen);
    }

    // ---------- Rechte ----------

    [Fact]
    public void Vorlagen_ist_gleich_den_Aufgaben_Vorlagen_in_Organisationen_Mitarbeit_gleich_Freizeiten()
    {
        Assert.Equal(Liste("Zeltlotse.Organisationen", "Zeltlotse.Organisationen.Rollen.OrgRechte", "AufgabenVorlagen"), ProgrammRechte.Vorlagen.Cast<object>().Order());
        Assert.Equal(Liste("Zeltlotse.Freizeiten", "Zeltlotse.Freizeiten.Rechte.FreizeitRechte", "Mitarbeit"), ProgrammRechte.Mitarbeit.Cast<object>().Order());
        Assert.Single(ProgrammRechte.Vorlagen);
    }

    private static IEnumerable<object> Liste(string assembly, string typ, string feld)
    {
        var werte = (Array)Assembly.Load(assembly).GetType(typ, throwOnError: true)!.GetField(feld, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return werte.Cast<object>().Order();
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
