using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Demo;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Matrix-Zeilen „Demo an/aus“ und „Demo-Katalog“ (NFR-20, Konvention „Anwender-Doku“): Der Schalter
/// <c>Zeltlotse:DemoDaten:Aktiv</c> ist nur in <c>Development</c> und <c>Testing</c> erlaubt; ist er an, spielt der Host
/// nach den Migrationen die Demo-Daten ein, auch wiederholt ohne Schaden.
/// </summary>
public sealed class DemoDatenTests(PostgresContainer postgres, S3Container s3)
{
    private const string Schalter = "Zeltlotse:DemoDaten:Aktiv";

    /// <summary>Story 1-5: Die Demo-Daten laden Nachweis-Dateien hoch und brauchen den Dateispeicher.</summary>
    private ZeltlotseApiFactory MitS3(ZeltlotseApiFactory factory)
    {
        foreach (var (schluessel, wert) in s3.Einstellungen(S3Container.NeuerBucket()))
        {
            factory.Einstellungen[schluessel] = wert;
        }

        return factory;
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public async Task Demo_an_in_Testing_oder_Development_startet_und_spielt_die_Demo_Daten_ein(string umgebung)
    {
        await using var factory = MitS3(new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung });
        factory.Einstellungen[Schalter] = "true";
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        var demo = factory.Services.GetRequiredService<DemoDaten>();
        Assert.True(demo.Aktiv);
        Assert.Equal(1, demo.Einspielungen);
        Assert.Contains(ModulMigrationen.Betrieb, factory.Services.GetRequiredService<ModulMigrationen>().AngewendeteReihenfolge);
    }

    [Fact]
    public async Task Nach_Ablauf_der_Freigabe_gibt_Jonas_beim_erneuten_Einspielen_wieder_frei()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var bucket = S3Container.NeuerBucket();
        await using (var erster = new ZeltlotseApiFactory(verbindung))
        {
            erster.Einstellungen[Schalter] = "true";
            foreach (var (schluessel, wert) in s3.Einstellungen(bucket))
            {
                erster.Einstellungen[schluessel] = wert;
            }

            using var client = erster.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        }

        await using var spaeter = new ZeltlotseApiFactory(verbindung);
        spaeter.Uhr.Advance(TimeSpan.FromDays(20));
        spaeter.Einstellungen[Schalter] = "true";
        foreach (var (schluessel, wert) in s3.Einstellungen(bucket))
        {
            spaeter.Einstellungen[schluessel] = wert;
        }

        using var spaeterClient = spaeter.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await spaeterClient.GetAsync("/health", Abbruch)).StatusCode);
        var jonas = await AnmeldeHelfer.PersonIdAsync(verbindung, "jonas.huettner@example.org");
        Assert.Equal(
            (spaeter.Uhr.GetUtcNow() + TimeSpan.FromDays(14)).UtcDateTime,
            await AnmeldeHelfer.WertAsync<DateTime>(verbindung, "SELECT \"FreigegebenBis\" FROM personen.nachweis_einsicht WHERE \"PersonId\" = @p AND \"Status\" = 2", ("p", jonas)));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht"));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht_posten"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht_posten WHERE \"Ergebnis\" = 1 AND \"Art\" = 3"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung"));
    }

    [Fact]
    public async Task Zweiter_Start_mit_Demo_Daten_gegen_dieselbe_Datenbank_startet_ebenfalls()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var bucket = S3Container.NeuerBucket();
        string? hauptschluessel;
        await using (var erster = new ZeltlotseApiFactory(verbindung))
        {
            erster.Einstellungen[Schalter] = "true";
            hauptschluessel = erster.Einstellungen[Zeltlotse.SharedKernel.Datenschutz.Hauptschluessel.Konfigurationsschluessel];
            foreach (var (schluessel, wert) in s3.Einstellungen(bucket))
            {
                erster.Einstellungen[schluessel] = wert;
            }

            using var client = erster.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        }

        await using var zweiter = new ZeltlotseApiFactory(verbindung);
        // Echte Zeit, damit der Client das Sitzungs-Cookie annimmt (Story 1-6: Miriam ruft die Warnhinweise ab).
        zweiter.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        zweiter.Einstellungen[Schalter] = "true";
        // Derselbe Hauptschlüssel wie beim ersten Start (wie im Betrieb): Die Anmeldelinks liegen damit verschlüsselt (Story 2-1).
        zweiter.Einstellungen[Zeltlotse.SharedKernel.Datenschutz.Hauptschluessel.Konfigurationsschluessel] = hauptschluessel;
        foreach (var (schluessel, wert) in s3.Einstellungen(bucket))
        {
            zweiter.Einstellungen[schluessel] = wert;
        }

        using var zweiterClient = zweiter.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await zweiterClient.GetAsync("/health", Abbruch)).StatusCode);
        // Story 1-1: je Demo-Person mit Mail ein Konto und ein Profil mit Namen, nicht doppelt; Kinder ohne Mail bekommen kein Konto.
        // Story 1-4: dazu die 3 Kinder im Familienkonto, je mit Personen-Schlüssel.
        Assert.Equal(9L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto"));
        Assert.Equal(12L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person"));
        Assert.Equal(12L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.personen_schluessel"));

        // Story 1-2: Gerd ist Global Admin, eine Organisation, Andrea Org-Admin, eine offene Einladung; nichts doppelt.
        Assert.Equal(["gerd.betreiber@example.org"], await MailsAsync(verbindung, "SELECT \"Mail\" FROM konten.konto WHERE \"GlobalAdmin\""));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.organisation WHERE \"Name\" = 'Jugendwerk Musterstadt' AND \"EingerichtetAm\" IS NOT NULL"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.organisation"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_rolle"));
        Assert.Equal(await AnmeldeHelfer.PersonIdAsync(verbindung, "andrea.beispiel@example.org"), await AnmeldeHelfer.WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM organisationen.org_rolle WHERE \"Rolle\" = 1"));
        Assert.Equal(["vorstand@example.org"], await MailsAsync(verbindung, "SELECT \"Mail\" FROM organisationen.org_einladung WHERE \"Status\" = 1 AND \"Rolle\" = 2"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_einladung"));

        // Story 1-3: eine Freizeit „Zeltlager Sommer 2027“ in der Demo-Organisation, Jonas und Miriam als Leitung; nichts doppelt.
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM freizeiten.freizeit WHERE \"Name\" = 'Zeltlager Sommer 2027' AND \"ErsterTag\" = '2027-08-02' AND \"LetzterTag\" = '2027-08-13' AND \"FreigabeIntervallTage\" = 30 AND \"OrganisationId\" = (SELECT \"Id\" FROM organisationen.organisation)"));
        var leitung = new[] { await AnmeldeHelfer.PersonIdAsync(verbindung, "jonas.huettner@example.org"), await AnmeldeHelfer.PersonIdAsync(verbindung, "miriam.zeltner@example.org") };
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"Rolle\" = 1 AND \"PersonId\" = ANY(@p)", ("p", leitung)));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung WHERE \"Status\" = 2"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.outbox"));
        await zweiter.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeit_rolle WHERE \"Rolle\" = 1 AND \"PersonId\" = ANY(@p)", ("p", leitung)));

        // Story 1-4: zwei Familien mit je einem Elternteil (Katrin, Daniel) und drei Kinder; Paul ist volljährig; nichts doppelt.
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familie"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familien_elternteil"));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person WHERE \"FamilieId\" IS NOT NULL"));
        foreach (var kind in DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.Kind))
        {
            var eltern = DemoKatalog.Personen.Single(p => p.Rolle == DemoRolle.Eltern && p.Familie == kind.Familie);
            Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
                verbindung,
                "SELECT count(*) FROM personen.person p JOIN personen.familien_elternteil e ON e.\"FamilieId\" = p.\"FamilieId\" WHERE e.\"PersonId\" = @eltern AND p.\"Vorname\" = @vorname AND p.\"Nachname\" = @nachname AND p.\"Geburtsdatum\" = @geburtsdatum",
                ("eltern", await AnmeldeHelfer.PersonIdAsync(verbindung, eltern.Mail!)), ("vorname", kind.Vorname), ("nachname", kind.Nachname), ("geburtsdatum", kind.Geburtsdatum!.Value)));
        }

        Assert.Equal(new DateOnly(2008, 3, 14), DemoKatalog.Personen.Single(p => p.Vorname == "Paul").Geburtsdatum);

        // Story 1-5: Jonas hat drei Nachweise mit Datei; zwei Einsichten (Jonas freigegeben und an Miriam gebunden, Erste Hilfe
        // bestätigt; Miriam angefragt); die Organisation hat vier Richtlinien-Zeilen und nach der Zustellung eine Bestätigung.
        var jonas = leitung[0];
        var miriam = leitung[1];
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis"));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis WHERE \"PersonId\" = @p AND \"HochgeladenAm\" IS NOT NULL", ("p", jonas)));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung, "SELECT count(*) FROM personen.nachweis_einsicht WHERE \"PersonId\" = @p AND \"Status\" = 2 AND \"PruefendePersonId\" = @m", ("p", jonas), ("m", miriam)));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht WHERE \"PersonId\" = @m AND \"Status\" = 1", ("m", miriam)));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht_posten"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht_posten WHERE \"Ergebnis\" = 1 AND \"Art\" = 3"));
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.nachweis_richtlinie"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung WHERE \"Art\" = 3 AND \"GueltigBis\" = '2027-04-15' AND \"GeprueftVon\" = @m", ("m", miriam)));

        // Story 1-6: Erste Hilfe ist Pflicht (neben dem Führungszeugnis); Miriam sieht bei Jonas „fehlt“ und „läuft ab“. Keine
        // Demo-Erinnerung, also auch keine Mail.
        Assert.Equal(
            [(short)1, (short)3],
            await PflichtArtenAsync(verbindung));
        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_erinnerung"));
        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.outbox_zustellung WHERE \"Empfaenger\" = 'Zeltlotse.Mail'"));
        using var miriamClient = await AnmeldeHelfer.AngemeldeterClientAsync(zweiter, "miriam.zeltner@example.org");
        var freizeitId = await AnmeldeHelfer.WertAsync<Guid>(verbindung, "SELECT \"Id\" FROM freizeiten.freizeit");
        using var warnungen = await miriamClient.GetAsync($"/api/freizeiten/{freizeitId}/nachweis-warnungen", Abbruch);
        Assert.Equal(HttpStatusCode.OK, warnungen.StatusCode);
        var personen = (await AnmeldeHelfer.JsonAsync(warnungen)).GetProperty("personen");
        var jonasWarnung = Assert.Single(personen.EnumerateArray());
        Assert.Equal(jonas, jonasWarnung.GetProperty("personId").GetGuid());
        Assert.Equal(
            ["fuehrungszeugnis fehlt ", "ersteHilfe laeuftAb 2027-04-15"],
            jonasWarnung.GetProperty("warnungen").EnumerateArray().Select(w =>
                $"{w.GetProperty("art").GetString()} {w.GetProperty("grund").GetString()} {(w.TryGetProperty("gueltigBis", out var bis) ? bis.GetString() : "")}"));

        // Story 1-7: Miriam hat Jonas' Nachweise eingesehen (genau ein Eintrag, auch nach dem zweiten Einspielen); die
        // Demo-Organisation hat die Löschfristen mit den Vorgabewerten.
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.einsicht_protokoll"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @j AND \"LesendePersonId\" = @m AND \"FreizeitId\" = @f AND \"OrganisationId\" = (SELECT \"Id\" FROM organisationen.organisation) AND \"Zweck\" = 1",
            ("j", jonas), ("m", miriam), ("f", freizeitId)));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.loeschfristen WHERE \"FreizeitDatenMonate\" = 12 AND \"NachweisBestaetigungMonate\" = 12"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.loeschfristen"));
        using var jonasClient = await AnmeldeHelfer.AngemeldeterClientAsync(zweiter, "jonas.huettner@example.org");
        var protokoll = await AnmeldeHelfer.JsonAsync(await jonasClient.GetAsync("/api/ich/zugriffsprotokoll", Abbruch));
        Assert.Equal("Miriam Zeltner", Assert.Single(protokoll.EnumerateArray()).GetProperty("lesenderName").GetString());

        // Story 2-1: Jonas und Miriam mit Herkunft „Musterstadt“, die Demo-Freizeit mit Ort, Beschreibung, Höchstzahl 40 und genau
        // zwei Anmeldelinks (auch nach dem zweiten Einspielen); die Freizeitseite zeigt beide als „Leitung · aus Musterstadt“.
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person WHERE \"Herkunft\" = 'Musterstadt' AND \"Id\" = ANY(@p)", ("p", leitung)));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person WHERE \"Herkunft\" IS NOT NULL"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM freizeiten.freizeit WHERE \"Ort\" = @ort AND \"Beschreibung\" = @beschreibung AND \"HoechstzahlTeilnehmende\" = 40",
            ("ort", DemoKatalog.Freizeit.Ort), ("beschreibung", DemoKatalog.Freizeit.Beschreibung)));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.anmeldelink WHERE \"FreizeitId\" = @f", ("f", freizeitId)));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.anmeldelink"));
        var einstellungen = await AnmeldeHelfer.JsonAsync(await miriamClient.GetAsync($"/api/freizeiten/{freizeitId}/freizeitseite", Abbruch));
        var teilnehmendenLink = einstellungen.GetProperty("anmeldelinks")[0].GetProperty("adresse").GetString()!;
        var seite = await AnmeldeHelfer.JsonAsync(await zweiterClient.GetAsync($"/api/freizeiten/oeffentlich/{teilnehmendenLink[(teilnehmendenLink.LastIndexOf('/') + 1)..]}", Abbruch));
        Assert.Equal(("Jugendwerk Musterstadt", 40), (seite.GetProperty("organisation").GetString(), einstellungen.GetProperty("hoechstzahlTeilnehmende").GetInt32()));
        Assert.Equal(
            ["Jonas Leitung Musterstadt", "Miriam Leitung Musterstadt"],
            seite.GetProperty("team").EnumerateArray().Select(p => $"{p.GetProperty("vorname").GetString()} {p.GetProperty("rolle").GetString()} {p.GetProperty("herkunft").GetString()}"));

        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM konten.konto k LEFT JOIN personen.person p ON p.\"Id\" = k.\"PersonId\" WHERE p.\"Id\" IS NULL OR p.\"Vorname\" IS NULL OR p.\"Nachname\" IS NULL"));
        foreach (var person in DemoKatalog.Personen.Where(p => p.Mail is not null))
        {
            var personId = await AnmeldeHelfer.PersonIdAsync(verbindung, person.Mail!);
            Assert.Equal(person.Vorname, await AnmeldeHelfer.WertAsync<string>(verbindung, "SELECT \"Vorname\" FROM personen.person WHERE \"Id\" = @id", ("id", personId)));
            Assert.Equal(person.Nachname, await AnmeldeHelfer.WertAsync<string>(verbindung, "SELECT \"Nachname\" FROM personen.person WHERE \"Id\" = @id", ("id", personId)));
        }
    }

    [Fact]
    public async Task Demo_Person_meldet_sich_per_Login_Link_an_und_sieht_ihr_Profil()
    {
        await using var factory = MitS3(AnmeldeHelfer.Factory(await postgres.NeueDatenbankAsync()));
        factory.Einstellungen[Schalter] = "true";
        using var client = await AnmeldeHelfer.AngemeldeterClientAsync(factory, "jonas.huettner@example.org");

        using var profil = await AnmeldeHelfer.PostAsync(client, "/api/ich/profil");

        Assert.Equal(HttpStatusCode.OK, profil.StatusCode);
        Assert.Equal("Jonas", (await AnmeldeHelfer.JsonAsync(profil)).GetProperty("vorname").GetString());
    }

    [Fact]
    public async Task Gerd_ist_nur_mit_Demo_Daten_Global_Admin()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using (var mitDemo = MitS3(new ZeltlotseApiFactory(verbindung)))
        {
            mitDemo.Einstellungen[Schalter] = "true";
            _ = mitDemo.CreateClient();
        }

        await using var ohneDemo = new ZeltlotseApiFactory(verbindung);
        _ = ohneDemo.CreateClient();

        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto WHERE \"GlobalAdmin\""));
    }

    [Fact]
    public async Task Development_schaltet_die_Demo_Daten_ueber_appsettings_Development_ein()
    {
        await using var factory = MitS3(new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = "Development" });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        var demo = factory.Services.GetRequiredService<DemoDaten>();
        Assert.True(demo.Aktiv);
        Assert.Equal(1, demo.Einspielungen);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Demo_an_ausserhalb_von_Development_und_Testing_bricht_den_Start_ab_und_nennt_den_Schalter(string umgebung)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung };
        factory.Einstellungen[Schalter] = "true";

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains(Schalter, fehler, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Testing", null)]
    [InlineData("Testing", "false")]
    [InlineData("Production", null)]
    [InlineData("Production", "false")]
    public async Task Demo_aus_startet_ohne_Einspielen(string umgebung, string? wert)
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung };
        if (wert is not null)
        {
            factory.Einstellungen[Schalter] = wert;
        }

        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        var demo = factory.Services.GetRequiredService<DemoDaten>();
        Assert.False(demo.Aktiv);
        Assert.Equal(0, demo.Einspielungen);
    }

    [Fact]
    public void Demo_Katalog_hat_erfundene_Stammdaten_mit_Mail_Adressen_unter_example_org()
    {
        var personen = DemoKatalog.Personen;

        Assert.False(string.IsNullOrWhiteSpace(DemoKatalog.Organisation.Name));
        Assert.False(string.IsNullOrWhiteSpace(DemoKatalog.Freizeit.Name));
        Assert.False(string.IsNullOrWhiteSpace(DemoKatalog.Freizeit.Ort));
        Assert.True(DemoKatalog.Freizeit.Beginn < DemoKatalog.Freizeit.Ende);

        Assert.Equal(2, personen.Count(p => p.Rolle == DemoRolle.Leitung));
        Assert.Equal(3, personen.Count(p => p.Rolle == DemoRolle.Team));
        Assert.Equal(2, personen.Count(p => p.Rolle == DemoRolle.Eltern));
        Assert.Equal(3, personen.Count(p => p.Rolle == DemoRolle.Kind));
        Assert.Single(personen, p => p.Rolle == DemoRolle.OrgAdmin);
        Assert.Single(personen, p => p.Rolle == DemoRolle.GlobalAdmin);
        Assert.EndsWith("@example.org", DemoKatalog.UeberblickEinladung, StringComparison.Ordinal);

        // Alle Erwachsenen haben eine Mail-Adresse, Kinder unter 18 keine (sie haben kein eigenes Konto).
        Assert.All(personen.Where(p => p.Rolle != DemoRolle.Kind), p => Assert.NotNull(p.Mail));
        Assert.All(personen.Where(p => p.Rolle == DemoRolle.Kind), p => Assert.Null(p.Mail));
        var mails = personen.Select(p => p.Mail).OfType<string>().ToList();
        Assert.All(mails, m => Assert.EndsWith("@example.org", m, StringComparison.Ordinal));
        Assert.Equal(mails.Count, mails.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Jedes Kind gehört zur Familie eines Elternteils aus dem Katalog.
        var familien = personen.Where(p => p.Rolle == DemoRolle.Eltern).Select(p => p.Familie).ToHashSet();
        Assert.All(personen.Where(p => p.Rolle == DemoRolle.Kind), k => Assert.Contains(k.Familie, familien));
        Assert.All(personen, p => Assert.False(string.IsNullOrWhiteSpace(p.Vorname + p.Nachname)));
    }

    private static async Task<List<short>> PflichtArtenAsync(string verbindung)
    {
        await using var db = new Npgsql.NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand("SELECT \"Art\" FROM organisationen.nachweis_richtlinie WHERE \"Pflicht\" ORDER BY \"Art\"", db);
        await using var leser = await befehl.ExecuteReaderAsync(Abbruch);
        var arten = new List<short>();
        while (await leser.ReadAsync(Abbruch))
        {
            arten.Add(leser.GetInt16(0));
        }

        return arten;
    }

    private static async Task<List<string>> MailsAsync(string verbindung, string sql)
    {
        await using var db = new Npgsql.NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(sql, db);
        await using var leser = await befehl.ExecuteReaderAsync(Abbruch);
        var mails = new List<string>();
        while (await leser.ReadAsync(Abbruch))
        {
            mails.Add(leser.GetString(0));
        }

        return mails;
    }
}
