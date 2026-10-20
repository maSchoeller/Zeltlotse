using System.Reflection;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Datenschutz;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Tests;

/// <summary>
/// Story 3-1, Programmpunkte (FR-34, FR-35, AD-6, AD-16, AD-19, AD-28): Übergänge „Ich übernehme“ und „Abgeben“, Feldänderung mit Konfliktregel
/// und Trimmen, Mahlzeiten, die Formprüfung, der Mandantenfilter der Entität und der Tage aus Freizeiten, der Empfänger von
/// <c>PersonGeloescht</c> und die Gleichheit der Rechteliste mit Freizeiten.
/// </summary>
public sealed class ProgrammpunktTests(PostgresContainer postgres)
{
    private static readonly DateOnly Tag = new(2027, 8, 2);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Programmpunkt Neu(string titel = "Geländespiel", TimeOnly? beginn = null) =>
        Programmpunkt.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Tag, beginn, titel);

    // ---------- Entität: Übergänge ----------

    [Fact]
    public void Uebernehmen_setzt_die_Person_und_ist_fuer_dieselbe_Person_wiederholbar()
    {
        var punkt = Neu();
        var lea = Guid.CreateVersion7();

        Assert.True(punkt.Uebernehmen(lea));
        Assert.True(punkt.Uebernehmen(lea));

        Assert.Equal(lea, punkt.PersonId);
    }

    [Fact]
    public void Uebernehmen_durch_eine_andere_Person_ist_ein_Konflikt_und_aendert_nichts()
    {
        var punkt = Neu();
        var (lea, jonas) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        punkt.Uebernehmen(lea);

        Assert.False(punkt.Uebernehmen(jonas));

        Assert.Equal(lea, punkt.PersonId);
    }

    [Fact]
    public void Abgeben_macht_den_Punkt_frei_ein_freier_Punkt_bleibt_frei_eine_andere_Person_darf_nicht()
    {
        var punkt = Neu();
        var (lea, jonas) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.True(punkt.Abgeben(lea));
        Assert.Null(punkt.PersonId);

        punkt.Uebernehmen(lea);
        Assert.False(punkt.Abgeben(jonas));
        Assert.Equal(lea, punkt.PersonId);

        Assert.True(punkt.Abgeben(lea));
        Assert.Null(punkt.PersonId);
    }

    [Fact]
    public void Der_Platzhalter_einer_geloeschten_Person_gilt_als_frei()
    {
        var punkt = Neu();
        var (alt, lea) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        punkt.Uebernehmen(alt);
        typeof(Programmpunkt).GetProperty(nameof(Programmpunkt.PersonId))!.SetValue(punkt, GeloeschtePerson.Id);
        Assert.Null(punkt.Uebernommen);

        Assert.True(punkt.Uebernehmen(lea));
        Assert.Equal(lea, punkt.Uebernommen);

        typeof(Programmpunkt).GetProperty(nameof(Programmpunkt.PersonId))!.SetValue(punkt, GeloeschtePerson.Id);
        Assert.True(punkt.Abgeben(lea));
        Assert.Null(punkt.PersonId);
    }

    // ---------- Entität: Feldänderung ----------

    [Fact]
    public void Anlegen_trimmt_den_Titel()
    {
        Assert.Equal("Lagerfeuer", Neu("  Lagerfeuer ").Titel);
    }

    [Fact]
    public void FeldAendern_setzt_Titel_getrimmt_und_Uhrzeit_wenn_der_alte_Wert_stimmt()
    {
        var punkt = Neu(beginn: new TimeOnly(14, 0));

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Titel, " Geländespiel ", "  Geländespiel Dienstag "));
        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "14:00", "14:30"));

        Assert.Equal(("Geländespiel Dienstag", new TimeOnly(14, 30)), (punkt.Titel, punkt.Beginn));
        Assert.Equal("14:30", punkt.Wert(ProgrammpunktFeld.Beginn));
    }

    [Fact]
    public void FeldAendern_mit_abweichendem_alten_Wert_desselben_Felds_ist_ein_Konflikt_ein_anderes_Feld_nicht()
    {
        var punkt = Neu(beginn: new TimeOnly(14, 0));
        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "14:00", "15:00"));

        Assert.False(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "14:00", "16:00"));
        Assert.Equal(new TimeOnly(15, 0), punkt.Beginn);

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Titel, "Geländespiel", "Schnitzeljagd"));
        Assert.Equal("Schnitzeljagd", punkt.Titel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Leere_Uhrzeit_heisst_ohne_Uhrzeit(string? leer)
    {
        var punkt = Neu(beginn: new TimeOnly(20, 0));

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "20:00", leer));

        Assert.Null(punkt.Beginn);
        Assert.Null(punkt.Wert(ProgrammpunktFeld.Beginn));
        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "", "20:15"));
    }

    // ---------- Story 6-2: „Geändert, noch nicht mitgeteilt“ ----------

    [Fact]
    public void Verschieben_merkt_sich_die_zuletzt_mitgeteilte_Uhrzeit()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));
        Assert.Null(punkt.MitgeteilterBeginn);

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", "13:30"));

        Assert.Equal(new TimeOnly(13, 0), punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Erneutes_Verschieben_behaelt_die_mitgeteilte_Uhrzeit()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));
        punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", "13:30");

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:30", "14:00"));

        Assert.Equal(new TimeOnly(13, 0), punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Zurueckschieben_auf_die_mitgeteilte_Uhrzeit_leert_den_Hinweis()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));
        punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", "14:00");

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "14:00", "13:00"));

        Assert.Null(punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Die_erste_Uhrzeit_ist_keine_Verschiebung()
    {
        var punkt = Neu();

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, null, "15:00"));

        Assert.Null(punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Uhrzeit_entfernen_merkt_sich_die_alte_Uhrzeit()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", ""));

        Assert.Equal(new TimeOnly(13, 0), punkt.MitgeteilterBeginn);
        // Wieder auf die alte Uhrzeit: nichts mehr mitzuteilen.
        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "", "13:00"));
        Assert.Null(punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Titel_aendern_und_ein_Konflikt_beruehren_den_Hinweis_nicht()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));
        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Titel, "Mittagessen", "Grillen"));
        Assert.Null(punkt.MitgeteilterBeginn);

        punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", "13:30");
        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Titel, "Grillen", "Mittagessen"));
        Assert.False(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "12:00", "13:00"));

        Assert.Equal(new TimeOnly(13, 0), punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Gleiche_Uhrzeit_erneut_speichern_setzt_nichts()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));

        Assert.True(punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", "13:00"));

        Assert.Null(punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Anlegen_und_Mahlzeiten_beginnen_ohne_Hinweis()
    {
        Assert.Null(Neu(beginn: new TimeOnly(9, 0)).MitgeteilterBeginn);
        Assert.Null(Programmpunkt.MahlzeitAnlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Tag, Mahlzeit.Mittagessen).MitgeteilterBeginn);
    }

    [Fact]
    public void Mitgeteilt_zur_aktuellen_Uhrzeit_leert_den_Hinweis_auch_wenn_keiner_offen_ist()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));
        punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", "13:30");

        Assert.True(punkt.Mitgeteilt(new TimeOnly(13, 30)));
        Assert.Null(punkt.MitgeteilterBeginn);

        Assert.True(punkt.Mitgeteilt(new TimeOnly(13, 30)));
        Assert.Null(punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Mitgeteilt_zu_einer_anderen_Uhrzeit_ist_ein_Konflikt_und_der_Hinweis_bleibt()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));
        punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", "13:30");
        punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:30", "14:00");

        Assert.False(punkt.Mitgeteilt(new TimeOnly(13, 30)));
        Assert.False(punkt.Mitgeteilt(null));

        Assert.Equal(new TimeOnly(13, 0), punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Mitgeteilt_ohne_offenen_Hinweis_ist_auch_mit_veralteter_Uhrzeit_erledigt()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));

        Assert.True(punkt.Mitgeteilt(new TimeOnly(9, 0)));
        Assert.True(punkt.Mitgeteilt(null));

        Assert.Null(punkt.MitgeteilterBeginn);
    }

    [Fact]
    public void Mitgeteilt_ohne_Uhrzeit_passt_zu_einem_Punkt_ohne_Uhrzeit()
    {
        var punkt = Neu("Mittagessen", new TimeOnly(13, 0));
        punkt.FeldAendern(ProgrammpunktFeld.Beginn, "13:00", null);

        Assert.True(punkt.Mitgeteilt(null));

        Assert.Null(punkt.MitgeteilterBeginn);
    }

    [Theory]
    [InlineData("13:30", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("25:99", false)]
    [InlineData("halb zwei", false)]
    public void Der_Validator_von_Mitgeteilt_prueft_die_Uhrzeit(string? beginn, bool gueltig)
    {
        var ergebnis = new ProgrammpunktMitgeteiltValidator().Validate(new ProgrammpunktMitgeteiltCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), beginn));

        Assert.Equal(gueltig, ergebnis.IsValid);
        if (!gueltig)
        {
            Assert.Equal("beginn", Assert.Single(ergebnis.Errors).PropertyName);
        }
    }

    [Fact]
    public void Der_Mitgeteilt_Command_ist_ein_Benutzer_und_Freizeit_Command()
    {
        var merkmale = typeof(ProgrammpunktMitgeteiltCommand).GetInterfaces();

        Assert.Contains(typeof(SharedKernel.Commands.IBenutzerCommand), merkmale);
        Assert.Contains(typeof(SharedKernel.Commands.IFreizeitCommand), merkmale);
    }

    [Fact]
    public void Mahlzeiten_haben_Titel_und_Uhrzeit_und_sind_nicht_loeschbar()
    {
        var (org, freizeit) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var mahlzeiten = Programmpunkt.Mahlzeiten.Select(m => Programmpunkt.MahlzeitAnlegen(Guid.CreateVersion7(), org, freizeit, Tag, m.Mahlzeit)).ToList();

        Assert.Equal(
            ["Frühstück 08:00 Fruehstueck", "Mittagessen 12:30 Mittagessen", "Abendessen 18:30 Abendessen"],
            mahlzeiten.Select(m => $"{m.Titel} {m.Wert(ProgrammpunktFeld.Beginn)} {m.Mahlzeit}"));
        Assert.All(mahlzeiten, m => Assert.False(m.Loeschbar));
        Assert.True(Neu().Loeschbar);
        // Titel und Uhrzeit einer Mahlzeit lassen sich ändern.
        Assert.True(mahlzeiten[1].FeldAendern(ProgrammpunktFeld.Beginn, "12:30", "13:30"));
        Assert.True(mahlzeiten[1].FeldAendern(ProgrammpunktFeld.Titel, "Mittagessen", "Grillen"));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData("14:30", true)]
    [InlineData("08:00", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("8:00", false)]
    [InlineData("24:00", false)]
    [InlineData("14.30", false)]
    [InlineData("halb drei", false)]
    public void Validatoren_pruefen_die_Uhrzeit_im_Format_HH_mm(string? beginn, bool gueltig)
    {
        var anlegen = new ProgrammpunktAnlegenValidator().Validate(new ProgrammpunktAnlegenCommand(Guid.CreateVersion7(), "Lagerfeuer", Tag, beginn));
        var aendern = AendernPruefen(ProgrammpunktFeld.Beginn, beginn);

        Assert.Equal(gueltig, anlegen.IsValid);
        Assert.Equal(gueltig, aendern.IsValid);
        if (!gueltig)
        {
            Assert.Equal("beginn", Assert.Single(anlegen.Errors).PropertyName);
            Assert.Equal("beginn", Assert.Single(aendern.Errors).PropertyName);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("x", true)]
    public void Validatoren_verlangen_einen_Titel(string? titel, bool gueltig)
    {
        var anlegen = new ProgrammpunktAnlegenValidator().Validate(new ProgrammpunktAnlegenCommand(Guid.CreateVersion7(), titel, Tag, null));

        Assert.Equal(gueltig, anlegen.IsValid);
        Assert.Equal(gueltig, AendernPruefen(ProgrammpunktFeld.Titel, titel).IsValid);
        if (!gueltig)
        {
            var fehler = Assert.Single(anlegen.Errors);
            Assert.Equal(("titel", "Titel fehlt."), (fehler.PropertyName, fehler.ErrorMessage));
        }
    }

    [Fact]
    public void Validatoren_erlauben_hoechstens_120_Zeichen_im_Titel_nach_dem_Trimmen_und_verlangen_den_Tag()
    {
        Assert.True(new ProgrammpunktAnlegenValidator().Validate(new ProgrammpunktAnlegenCommand(Guid.CreateVersion7(), $" {new string('a', 120)} ", Tag, null)).IsValid);
        var zuLang = new ProgrammpunktAnlegenValidator().Validate(new ProgrammpunktAnlegenCommand(Guid.CreateVersion7(), new string('a', 121), Tag, null));
        Assert.Equal(("titel", "Höchstens 120 Zeichen."), (Assert.Single(zuLang.Errors).PropertyName, zuLang.Errors[0].ErrorMessage));
        Assert.False(AendernPruefen(ProgrammpunktFeld.Titel, new string('a', 121)).IsValid);

        var ohneTag = new ProgrammpunktAnlegenValidator().Validate(new ProgrammpunktAnlegenCommand(Guid.CreateVersion7(), "Lagerfeuer", null, null));
        Assert.Equal("tag", Assert.Single(ohneTag.Errors).PropertyName);
        Assert.Equal("feld", Assert.Single(AendernPruefen((ProgrammpunktFeld)99, "x").Errors).PropertyName);
    }

    [Fact]
    public void Der_Uebergang_gibt_keine_Personenkennung_aus()
    {
        var person = Guid.CreateVersion7();

        var text = new ProgrammpunktUebergangCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), person, Uebergang.Uebernehmen).ToString();

        Assert.DoesNotContain(person.ToString(), text, StringComparison.Ordinal);
    }

    private static FluentValidation.Results.ValidationResult AendernPruefen(ProgrammpunktFeld feld, string? wert) =>
        new ProgrammpunktFeldAendernValidator().Validate(new ProgrammpunktFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, null, wert));

    // ---------- Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen_Programmpunkte()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (x, z) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await SpeichernAsync(factory, orgA, Programmpunkt.Anlegen(Guid.CreateVersion7(), orgA, x, Tag, null, "Lagerfeuer"));
        await SpeichernAsync(factory, orgB, Programmpunkt.Anlegen(Guid.CreateVersion7(), orgB, z, Tag, null, "Lagerfeuer"));

        Assert.Equal([x], await LesenAsync(factory, orgA, db => db.Programmpunkte.Select(p => p.FreizeitId).ToListAsync(Abbruch)));
        Assert.Equal([z], await LesenAsync(factory, orgB, db => db.Programmpunkte.Select(p => p.FreizeitId).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Programmpunkte.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        Assert.Equal([z], await scope.ServiceProvider.GetRequiredService<ProgrammDbContext>().Programmpunkte.Select(p => p.FreizeitId).ToListAsync(Abbruch));
    }

    [Fact]
    public async Task Die_Tage_aus_Freizeiten_liefern_nur_Freizeiten_des_eigenen_Mandanten()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var x = await FreizeitAsync(verbindung, orgA, new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13));
        var z = await FreizeitAsync(verbindung, orgB, new DateOnly(2027, 9, 1), new DateOnly(2027, 9, 3));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgA);
        var tage = await scope.ServiceProvider.GetRequiredService<IFreizeitTageLesen>().TageAsync([x, z, Guid.CreateVersion7()], Abbruch);

        Assert.Equal([x], tage.Keys);
        Assert.Equal(new FreizeitTage(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13)), tage[x]);
        Assert.Equal(12, tage[x].Alle().Count());
        Assert.True(tage[x].Enthaelt(new DateOnly(2027, 8, 13)));
        Assert.False(tage[x].Enthaelt(new DateOnly(2027, 8, 14)));
    }

    // ---------- Empfänger PersonGeloescht ----------

    [Fact]
    public async Task PersonGeloescht_ersetzt_die_Person_in_allen_Organisationen_durch_den_Platzhalter_andere_bleiben()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (lea, jonas) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (var (org, person) in new[] { (orgA, lea), (orgB, lea), (orgA, jonas) })
        {
            var punkt = Programmpunkt.Anlegen(Guid.CreateVersion7(), org, Guid.CreateVersion7(), Tag, null, "Lagerfeuer");
            punkt.Uebernehmen(person);
            await SpeichernAsync(factory, org, punkt);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                ProgrammPersonLoeschen.Aus(new Umschlag(Guid.CreateVersion7(), factory.Uhr.GetUtcNow(), null, lea, 1), new PersonGeloescht(lea)), Abbruch);
        }

        var personen = new List<Guid?>();
        foreach (var org in new[] { orgA, orgB })
        {
            personen.AddRange(await LesenAsync(factory, org, db => db.Programmpunkte.Select(p => p.PersonId).ToListAsync(Abbruch)));
        }

        Assert.Equal(2, personen.Count(p => p == GeloeschtePerson.Id));
        Assert.Single(personen, p => p == jonas);
        Assert.DoesNotContain(lea, personen);
    }

    [Fact]
    public async Task Das_Modul_meldet_ProgrammPersonLoeschen_als_Empfaenger_von_PersonGeloescht_an()
    {
        await using var factory = await FactoryAsync();

        Assert.Single(factory.Services.GetServices<EmpfaengerEintrag>(), e => e.EreignisTyp == typeof(PersonGeloescht) && e.CommandTyp == typeof(ProgrammPersonLoeschen));
    }

    // ---------- Rechte ----------

    [Fact]
    public void Mitarbeit_ist_gleich_der_Mitarbeit_in_Freizeiten()
    {
        var werte = (Array)Assembly.Load("Zeltlotse.Freizeiten").GetType("Zeltlotse.Freizeiten.Rechte.FreizeitRechte", throwOnError: true)!
            .GetField("Mitarbeit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        Assert.Equal(werte.Cast<object>().Order(), ProgrammRechte.Mitarbeit.Cast<object>().Order());
        Assert.Equal(2, ProgrammRechte.Mitarbeit.Length);
    }

    // ---------- Helfer ----------

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        return factory;
    }

    private static async Task SpeichernAsync(ZeltlotseApiFactory factory, Guid organisation, Programmpunkt punkt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<ProgrammDbContext>();
        db.Programmpunkte.Add(punkt);
        await db.SaveChangesAsync(Abbruch);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<ProgrammLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<ProgrammLeseDbContext>());
    }

    /// <summary>Eine Freizeit direkt in der Tabelle von Freizeiten (nur für die Tage; der Test prüft den Mandantenfilter).</summary>
    private static async Task<Guid> FreizeitAsync(string verbindungstext, Guid organisation, DateOnly erster, DateOnly letzter)
    {
        var id = Guid.CreateVersion7();
        await using var verbindung = new Npgsql.NpgsqlConnection(verbindungstext);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(
            "INSERT INTO freizeiten.freizeit (\"Id\", \"OrganisationId\", \"Name\", \"ErsterTag\", \"LetzterTag\", \"FreigabeIntervallTage\", \"AngelegtAm\", \"AngelegtVon\", \"Folgenummer\") " +
            "VALUES (@id, @org, 'Zeltlager', @erster, @letzter, 30, now(), @von, 0)",
            verbindung);
        befehl.Parameters.AddWithValue("id", id);
        befehl.Parameters.AddWithValue("org", organisation);
        befehl.Parameters.AddWithValue("erster", erster);
        befehl.Parameters.AddWithValue("letzter", letzter);
        befehl.Parameters.AddWithValue("von", Guid.CreateVersion7());
        await befehl.ExecuteNonQueryAsync(Abbruch);
        return id;
    }
}
