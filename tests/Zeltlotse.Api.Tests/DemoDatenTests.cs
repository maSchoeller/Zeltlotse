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
        // Story 2-3: Lea ist Team; ihre erste Mitarbeit fragt die Einsicht automatisch an. Story 3-3: ebenso Tims Leitung der Kinderbibeltage.
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht"));
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
        // Story 1-4: dazu die 3 Kinder im Familienkonto, je mit Personen-Schlüssel. Story 3-3: dazu Tim (Leitung der zweiten Freizeit).
        Assert.Equal(10L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto"));
        Assert.Equal(13L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person"));
        Assert.Equal(13L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.personen_schluessel"));

        // Story 1-2: Gerd ist Global Admin, eine Organisation, Andrea Org-Admin, eine offene Einladung; nichts doppelt.
        Assert.Equal(["gerd.betreiber@example.org"], await MailsAsync(verbindung, "SELECT \"Mail\" FROM konten.konto WHERE \"GlobalAdmin\""));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.organisation WHERE \"Name\" = 'Jugendwerk Musterstadt' AND \"EingerichtetAm\" IS NOT NULL"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.organisation"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_rolle"));
        Assert.Equal(await AnmeldeHelfer.PersonIdAsync(verbindung, "andrea.beispiel@example.org"), await AnmeldeHelfer.WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM organisationen.org_rolle WHERE \"Rolle\" = 1"));
        Assert.Equal(["vorstand@example.org"], await MailsAsync(verbindung, "SELECT \"Mail\" FROM organisationen.org_einladung WHERE \"Status\" = 1 AND \"Rolle\" = 2"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_einladung"));

        // Story 1-3: eine Freizeit „Zeltlager Sommer 2027“ in der Demo-Organisation, Jonas und Miriam als Leitung; nichts doppelt.
        // Story 3-3: dazu die zweite Freizeit „Kinderbibeltage 2027“ (unten).
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM freizeiten.freizeit WHERE \"Name\" = 'Zeltlager Sommer 2027' AND \"ErsterTag\" = '2027-08-02' AND \"LetzterTag\" = '2027-08-13' AND \"FreigabeIntervallTage\" = 30 AND \"OrganisationId\" = (SELECT \"Id\" FROM organisationen.organisation)"));
        var leitung = new[] { await AnmeldeHelfer.PersonIdAsync(verbindung, "jonas.huettner@example.org"), await AnmeldeHelfer.PersonIdAsync(verbindung, "miriam.zeltner@example.org") };
        // Story 2-3: dazu Lea als Team (bestätigte Mitarbeit). Story 3-3: dazu Tim als Leitung der Kinderbibeltage.
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"Rolle\" = 2 AND \"PersonId\" = @p", ("p", await AnmeldeHelfer.PersonIdAsync(verbindung, "lea.feuerstein@example.org"))));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"Rolle\" = 1 AND \"PersonId\" = ANY(@p)", ("p", leitung)));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung WHERE \"Status\" = 2"));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung"));
        // Zwei Rollenvergaben (Story 1-3), Emmas Anmeldung (Story 2-2) und Story 2-3: drei weitere Anmeldungen, zwei Bestätigungen mit je
        // einem Mail-Auftrag und Leas Rolle Team; Story 3-3: Tims Rolle Leitung.
        Assert.Equal(12L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.outbox"));
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
        // Story 2-3: dazu Leas automatische Einsicht (Team durch die bestätigte Mitarbeit); Story 3-3: Tims (Leitung der Kinderbibeltage).
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.nachweis_einsicht"));
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
        var freizeitId = await AnmeldeHelfer.WertAsync<Guid>(verbindung, "SELECT \"Id\" FROM freizeiten.freizeit WHERE \"Name\" = 'Zeltlager Sommer 2027'");
        using var warnungen = await miriamClient.GetAsync($"/api/freizeiten/{freizeitId}/nachweis-warnungen", Abbruch);
        Assert.Equal(HttpStatusCode.OK, warnungen.StatusCode);
        var personen = (await AnmeldeHelfer.JsonAsync(warnungen)).GetProperty("personen");
        // Story 2-3: Lea ist Team und hat noch keine Nachweise.
        Assert.Equal(2, personen.GetArrayLength());
        var lea = await AnmeldeHelfer.PersonIdAsync(verbindung, "lea.feuerstein@example.org");
        Assert.Equal(
            ["fuehrungszeugnis fehlt", "ersteHilfe fehlt"],
            personen.EnumerateArray().Single(p => p.GetProperty("personId").GetGuid() == lea).GetProperty("warnungen").EnumerateArray()
                .Select(w => $"{w.GetProperty("art").GetString()} {w.GetProperty("grund").GetString()}"));
        var jonasWarnung = personen.EnumerateArray().Single(p => p.GetProperty("personId").GetGuid() == jonas);
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
        // Story 3-3: die zweite Freizeit hat ihre eigenen zwei Anmeldelinks.
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.anmeldelink"));
        var einstellungen = await AnmeldeHelfer.JsonAsync(await miriamClient.GetAsync($"/api/freizeiten/{freizeitId}/freizeitseite", Abbruch));
        var teilnehmendenLink = einstellungen.GetProperty("anmeldelinks")[0].GetProperty("adresse").GetString()!;
        var seite = await AnmeldeHelfer.JsonAsync(await zweiterClient.GetAsync($"/api/freizeiten/oeffentlich/{teilnehmendenLink[(teilnehmendenLink.LastIndexOf('/') + 1)..]}", Abbruch));
        Assert.Equal(("Jugendwerk Musterstadt", 40), (seite.GetProperty("organisation").GetString(), einstellungen.GetProperty("hoechstzahlTeilnehmende").GetInt32()));
        Assert.Equal(
            ["Jonas Leitung Musterstadt", "Miriam Leitung Musterstadt", "Lea Team "],
            seite.GetProperty("team").EnumerateArray().Select(p => $"{p.GetProperty("vorname").GetString()} {p.GetProperty("rolle").GetString()} {p.GetProperty("herkunft").GetString()}"));

        // Story 2-2: Teilnahmebedingungen der Demo-Freizeit; Katrin hat Emma mit Konto angemeldet (genau eine Anmeldung, auch nach dem
        // zweiten Einspielen) und die Freigabe der Stufe Leitung ist zugestellt; die Familie zeigt Emmas Status.
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung, "SELECT count(*) FROM freizeiten.freizeit WHERE \"Teilnahmebedingungen\" = @t", ("t", DemoKatalog.Freizeit.Teilnahmebedingungen)));
        Assert.Equal(DemoKatalog.Freizeit.Teilnahmebedingungen, seite.GetProperty("teilnahmebedingungen").GetString());
        var emma = await AnmeldeHelfer.WertAsync<Guid>(verbindung, "SELECT \"Id\" FROM personen.person WHERE \"Vorname\" = 'Emma'");
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.anmeldung"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"PersonId\" = @e AND \"FreizeitId\" = @f AND \"Art\" = 1 AND \"Status\" = 1", ("e", emma), ("f", freizeitId)));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"PersonId\" = @e AND \"Stufe\" = 1", ("e", emma)));
        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.gast_zugang"));
        using var katrinClient = await AnmeldeHelfer.AngemeldeterClientAsync(zweiter, DemoKatalog.Anmeldungen[0].Mail);
        var anmeldungen = (await AnmeldeHelfer.JsonAsync(await katrinClient.GetAsync("/api/ich/anmeldungen", Abbruch))).EnumerateArray().ToList();
        Assert.Equal((emma, "Zeltlager Sommer 2027", "Eingegangen"), (
            Assert.Single(anmeldungen).GetProperty("personId").GetGuid(), anmeldungen[0].GetProperty("freizeitName").GetString(), anmeldungen[0].GetProperty("status").GetString()));

        // Story 2-3: Kontakt für Absagen; Mia und Lea bestätigt, Emma und Tobias eingegangen (auch nach dem zweiten Einspielen); Lea ist
        // Team und Mitarbeitende der Organisation; Miriam sieht den Balken „1 von 40“.
        Assert.Equal((DemoKatalog.KontaktFuerAbsagen.Mail, DemoKatalog.KontaktFuerAbsagen.Telefon), (
            einstellungen.GetProperty("kontaktMail").GetString(), einstellungen.GetProperty("kontaktTelefon").GetString()));
        Assert.Equal(DemoKatalog.KontaktFuerAbsagen.Mail, anmeldungen[0].GetProperty("kontaktMail").GetString());
        var liste = await AnmeldeHelfer.JsonAsync(await miriamClient.GetAsync($"/api/freizeiten/{freizeitId}/anmeldungen", Abbruch));
        Assert.Equal((40, 1), (liste.GetProperty("hoechstzahlTeilnehmende").GetInt32(), liste.GetProperty("bestaetigteTeilnehmende").GetInt32()));
        Assert.Equal(
            ["Emma Teilnehmende Eingegangen", "Lea Mitarbeitende Bestaetigt", "Mia Teilnehmende Bestaetigt", "Tobias Mitarbeitende Eingegangen"],
            liste.GetProperty("anmeldungen").EnumerateArray().Select(a =>
                $"{a.GetProperty("vorname").GetString()} {a.GetProperty("art").GetString()} {a.GetProperty("status").GetString()}").Order(StringComparer.Ordinal));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.mitgliedschaft WHERE \"PersonId\" = @p", ("p", lea)));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.mitgliedschaft"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"Stufe\" = 2"));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(
            verbindung, "SELECT count(*) FROM freizeiten.outbox_zustellung WHERE \"Empfaenger\" = 'Zeltlotse.Mail' AND \"ErledigtAm\" IS NOT NULL"));

        // Story 2-4: Miriam hat die Fragen je Zielgruppe gewählt; Katrin hat für Emma drei Antworten gespeichert (genau drei Zeilen,
        // auch nach dem zweiten Einspielen, nur verschlüsselt); Emmas Anmeldung trägt die Fragen für Teilnehmende.
        var fragen = await AnmeldeHelfer.JsonAsync(await miriamClient.GetAsync($"/api/freizeiten/{freizeitId}/fragen", Abbruch));
        Assert.Equal((DemoKatalog.Fragen.Teilnehmende, DemoKatalog.Fragen.Mitarbeitende), (fragen.GetProperty("teilnehmende").GetString(), fragen.GetProperty("mitarbeitende").GetString()));
        Assert.Equal(DemoKatalog.Fragen.Teilnehmende, string.Join(',', anmeldungen[0].GetProperty("fragen").EnumerateArray().Select(f => f.GetString())));
        Assert.Equal(7L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort WHERE \"PersonId\" = @e", ("e", emma)));

        // Story 2-5: Die Fragen-Auswahl kam vor den Anmeldungen: Emmas Freigabe (eingegangen) und Mias (bestätigt) tragen die Fragen für
        // Teilnehmende, Leas die für Mitarbeitende. Daniel hat für Mia drei Antworten gespeichert, Lea eine Team-Notiz zu Mia geschrieben
        // (genau eine, auch nach dem zweiten Einspielen, nur verschlüsselt). Emmas Anmeldung nennt „Einsicht bis“.
        var mia = await AnmeldeHelfer.WertAsync<Guid>(verbindung, "SELECT \"Id\" FROM personen.person WHERE \"Vorname\" = 'Mia'");
        var teilnehmende = DemoKatalog.Fragen.Teilnehmende.Split(',');
        foreach (var person in new[] { emma, mia })
        {
            Assert.Equal(teilnehmende, await AnmeldeHelfer.WertAsync<string[]>(verbindung, "SELECT \"Fragen\" FROM personen.freigabe WHERE \"PersonId\" = @p", ("p", person)));
        }

        Assert.Equal(
            DemoKatalog.Fragen.Mitarbeitende.Split(','), await AnmeldeHelfer.WertAsync<string[]>(verbindung, "SELECT \"Fragen\" FROM personen.freigabe WHERE \"PersonId\" = @p", ("p", lea)));
        // Story 2-11: dazu Mias Medikamente für den Notfallordner, also vier Antworten.
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort WHERE \"PersonId\" = @m", ("m", mia)));
        Assert.Contains(("medikamente", "Asthmaspray bei Bedarf, liegt im Kulturbeutel"), DemoKatalog.MiasFreizeitpass);
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.team_notiz"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung, "SELECT count(*) FROM freizeiten.team_notiz WHERE \"PersonId\" = @m AND \"VerfasstVon\" = @l AND position(convert_to('Heimweh', 'UTF8') in \"Text\") = 0",
            ("m", mia), ("l", lea)));
        Assert.Equal(new DateTimeOffset(2027, 9, 12, 22, 0, 0, TimeSpan.Zero), anmeldungen[0].GetProperty("einsichtBis").GetDateTimeOffset());
        var pass = (await AnmeldeHelfer.JsonAsync(await katrinClient.GetAsync($"/api/ich/freizeitpass/{emma}", Abbruch))).GetProperty("fragen").EnumerateArray()
            .Where(f => f.GetProperty("wert").ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(f => (f.GetProperty("kennung").GetString()!, f.GetProperty("wert").GetString()!));
        Assert.Equal(DemoKatalog.EmmasFreizeitpass, pass);

        // Story 2-6: Jonas sieht im Leitungs-Cockpit vier unvollständige Freizeitpässe; keine Demo-Erinnerung, also auch keine Mail.
        var stand = await AnmeldeHelfer.JsonAsync(await jonasClient.GetAsync($"/api/freizeiten/{freizeitId}/personen/freizeitpass-stand", Abbruch));
        var gefragtTeilnehmende = DemoKatalog.Fragen.Teilnehmende.Split(',').Length;
        var gefragtMitarbeitende = DemoKatalog.Fragen.Mitarbeitende.Split(',').Length;
        Assert.Equal(
            [
                $"Lea Feuerstein {gefragtMitarbeitende}/{gefragtMitarbeitende}",
                $"Emma Muster {gefragtTeilnehmende}/{gefragtTeilnehmende - 3}",
                $"Mia Probst {gefragtTeilnehmende}/{gefragtTeilnehmende - 4}",
                $"Tobias Wanderer {gefragtMitarbeitende}/{gefragtMitarbeitende}",
            ],
            stand.GetProperty("personen").EnumerateArray().Select(p =>
                $"{p.GetProperty("vorname").GetString()} {p.GetProperty("nachname").GetString()} {p.GetProperty("gefragt").GetInt32()}/{p.GetProperty("offen").GetInt32()}"));
        Assert.Equal((8, 5), (gefragtTeilnehmende, gefragtTeilnehmende - 3));
        Assert.All(stand.GetProperty("personen").EnumerateArray(), p => Assert.Equal(System.Text.Json.JsonValueKind.Null, p.GetProperty("erinnertAm").ValueKind));
        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeitpass_erinnerung"));

        // Story 2-11: Im Notfallordner steht Mia (bestätigt) mit Notfallkontakt und Medikamenten; Emma (eingegangen) fehlt.
        var ordner = (await AnmeldeHelfer.JsonAsync(await jonasClient.GetAsync(
            $"/api/freizeiten/{freizeitId}/personen/notfallordner?person={mia}&person={emma}", Abbruch))).EnumerateArray().ToList();
        var miaImOrdner = Assert.Single(ordner);
        Assert.Equal("Probst", miaImOrdner.GetProperty("nachname").GetString());
        var miaWerte = miaImOrdner.GetProperty("fragen").EnumerateArray().ToDictionary(f => f.GetProperty("kennung").GetString()!, f => f.GetProperty("wert").GetString());
        Assert.Equal(("Daniel Probst (Vater), 0123 456781", "Asthmaspray bei Bedarf, liegt im Kulturbeutel"), (miaWerte["notfallkontakte"], miaWerte["medikamente"]));
        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM personen.outbox_zustellung WHERE \"Empfaenger\" = 'Zeltlotse.Mail'"));

        // Story 2-7: Lea (Team) sieht sechs Dokumente, darunter die eingebundene Hausordnung; einbindbar ist noch der Leitfaden. Katrin
        // sieht in der Elterninfo zu Emma die vier Dokumente für Eltern. Nichts doppelt nach dem zweiten Einspielen.
        using var leaClient = await AnmeldeHelfer.AngemeldeterClientAsync(zweiter, "lea.feuerstein@example.org");
        var dokumente = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/dokumente", Abbruch))).GetProperty("dokumente")
            .EnumerateArray().Select(d => $"{d.GetProperty("titel").GetString()} | {d.GetProperty("sichtbarkeit").GetString()} | {d.GetProperty("ausOrganisation").GetBoolean()}");
        Assert.Equal(
            [
                "Anfahrt zum Zeltplatz | Oeffentlich | False", "Hausordnung Zeltplatz | ElternUndTeilnehmende | True",
                "Infobrief Zeltlager Sommer 2027 | ElternUndTeilnehmende | False", "Notfallnummern | ElternUndTeilnehmende | False",
                "Packliste | ElternUndTeilnehmende | False", "Programmideen | Team | False",
                // Story 2-9: das eingebundene Schutzkonzept (Sichtbarkeit Team).
                "Schutzkonzept Jugendwerk Musterstadt | Team | True",
            ],
            dokumente);
        var einbindbar = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/dokumente/org", Abbruch))).GetProperty("dokumente");
        Assert.Equal(["Leitfaden Aufsichtspflicht"], einbindbar.EnumerateArray().Select(d => d.GetProperty("titel").GetString()));
        var elterninfo = await AnmeldeHelfer.JsonAsync(
            await katrinClient.GetAsync($"/api/ich/anmeldungen/{anmeldungen[0].GetProperty("anmeldungId").GetGuid()}/elterninfo", Abbruch));
        Assert.Equal(
            ["Hausordnung Zeltplatz", "Infobrief Zeltlager Sommer 2027", "Notfallnummern", "Packliste"],
            elterninfo.GetProperty("dokumente").EnumerateArray().Select(d => d.GetProperty("titel").GetString()));
        Assert.Equal(7L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.dokument"));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.dokument"));

        // Story 2-8: Die Demo-Kette ist eingerichtet; Lea (Team) liest sie, genau eine Zeile auch nach dem zweiten Einspielen.
        var kette = await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/sicherheit/sicherheitskette", Abbruch));
        Assert.True(kette.GetProperty("eingerichtet").GetBoolean());
        Assert.Equal(string.Join('\n', DemoKatalog.Sicherheitskette.WasTun), kette.GetProperty("wasTun").GetString());
        Assert.Equal(
            ("0123 456782", "Petra Heimann (Gemeindebüro)", "0123 456783"),
            (kette.GetProperty("leitungTelefon").GetString(), kette.GetProperty("heimatName").GetString(), kette.GetProperty("heimatTelefon").GetString()));
        Assert.Equal(
            [
                "Ruhe bewahren und bei der verletzten Person bleiben.", "Bei Lebensgefahr sofort den Notruf 112 wählen.",
                "Leitung anrufen und Bescheid geben.", "Erste Hilfe leisten.",
            ],
            DemoKatalog.Sicherheitskette.WasTun);
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM sicherheit.sicherheitskette"));

        // Story 2-9: Das eingebundene Org-Dokument ist das Schutzkonzept; Jonas hat es bestätigt, Lea und Miriam noch nicht. Je genau eine
        // Zeile auch nach dem zweiten Einspielen.
        var schutzkonzept = await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/sicherheit/schutzkonzept", Abbruch));
        Assert.Equal(
            (DemoKatalog.Schutzkonzept, "PDF"),
            (schutzkonzept.GetProperty("titel").GetString(), schutzkonzept.GetProperty("dateiart").GetString()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, schutzkonzept.GetProperty("selbstGelesenAm").ValueKind);
        Assert.Equal(
            ["Lea Feuerstein Team False", "Jonas Hüttner Leitung True", "Miriam Zeltner Leitung False"],
            schutzkonzept.GetProperty("team").EnumerateArray().Select(l =>
                $"{l.GetProperty("vorname").GetString()} {l.GetProperty("nachname").GetString()} {(l.GetProperty("leitung").GetBoolean() ? "Leitung" : "Team")} "
                + $"{l.GetProperty("gelesenAm").ValueKind != System.Text.Json.JsonValueKind.Null}"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM sicherheit.schutzkonzept"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM sicherheit.schutzkonzept_bestaetigung"));

        // Story 2-10: drei Einträge im Vorfall-Protokoll zu Mia (Lea: Geschehen und Entnahme, Jonas: Medikament), neueste oben, nichts archiviert;
        // je genau eine Zeile auch nach dem zweiten Einspielen.
        var vorfaelle = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/sicherheit/vorfaelle", Abbruch))).EnumerateArray().ToList();
        Assert.Equal(
            [
                "Mia Probst Medikament Kühlpack, kein Medikament Jonas",
                "Mia Probst Entnahme 2 Pflaster, 1 sterile Kompresse Lea",
                "Mia Probst Geschehen Beim Geländespiel am linken Knie geschnitten. Wunde gereinigt und verbunden. Lea",
            ],
            vorfaelle.Select(v =>
                $"{v.GetProperty("vorname").GetString()} {v.GetProperty("nachname").GetString()} {v.GetProperty("art").GetString()} {v.GetProperty("text").GetString()} "
                + v.GetProperty("verfasserVorname").GetString()));
        Assert.Equal(DemoKatalog.Vorfaelle.Select(v => v.Id).Reverse(), vorfaelle.Select(v => v.GetProperty("id").GetGuid()));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM sicherheit.vorfall_eintrag"));
        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM sicherheit.vorfall_eintrag WHERE \"ArchiviertAm\" IS NOT NULL"));

        // Story 3-1: 36 Mahlzeiten (12 Tage) und die drei Programmpunkte aus dem Katalog; Lea hat das Lagerfeuer übernommen, Jonas das
        // Geländespiel. Nichts doppelt, auch nach dem zweiten Einspielen.
        var punkte = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/programm", Abbruch))).GetProperty("punkte").EnumerateArray().ToList();
        Assert.Equal(36, punkte.Count(p => p.GetProperty("mahlzeit").ValueKind != System.Text.Json.JsonValueKind.Null));
        Assert.Equal(
            ["2027-08-02 15:00 Anreise und Zeltaufbau offen False", "2027-08-02 20:00 Lagerfeuer Lea True", "2027-08-03 14:30 Geländespiel Jonas False"],
            punkte.Where(p => p.GetProperty("mahlzeit").ValueKind == System.Text.Json.JsonValueKind.Null).Select(p =>
            {
                var von = p.GetProperty("uebernommenVon").ValueKind == System.Text.Json.JsonValueKind.Null ? null : (Guid?)p.GetProperty("uebernommenVon").GetGuid();
                var wer = von == lea ? "Lea" : von == jonas ? "Jonas" : von is null ? "offen" : "?";
                return $"{p.GetProperty("tag").GetString()} {p.GetProperty("beginn").GetString()} {p.GetProperty("titel").GetString()} {wer} {p.GetProperty("meins").GetBoolean()}";
            }));
        Assert.Equal(39L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM programm.programmpunkt WHERE \"FreizeitId\" = @f", ("f", freizeitId)));

        // Story 3-2: fünf Materialien an Geländespiel und Lagerfeuer mit Menge und Status; Miriam bringt die Taschenlampen mit (für Lea nicht
        // „meins“). Nichts doppelt, auch nach dem zweiten Einspielen.
        var material = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/programm/material", Abbruch))).GetProperty("material").EnumerateArray().ToList();
        Assert.Equal(
            [
                "Lagerfeuer Feuerholz 2 Kisten Besorgt -", "Lagerfeuer Stockbrot-Teig 3 kg Eingepackt -", "Geländespiel Fahnen 8 Stück Vorhanden -",
                "Geländespiel Seile 4 × 10 m Gebraucht -", "Geländespiel Taschenlampen 10 Stück Gebraucht Miriam",
            ],
            material.Select(m =>
            {
                var von = m.GetProperty("bringtMit").ValueKind == System.Text.Json.JsonValueKind.Null ? null : (Guid?)m.GetProperty("bringtMit").GetGuid();
                var wer = von == miriam ? "Miriam" : von is null ? "-" : "?";
                return $"{m.GetProperty("programmpunkt").GetString()} {m.GetProperty("bezeichnung").GetString()} {m.GetProperty("menge").GetString()} "
                       + $"{m.GetProperty("status").GetString()} {wer}";
            }));
        Assert.All(material, m => Assert.False(m.GetProperty("meins").GetBoolean()));
        Assert.Equal(5L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM programm.material WHERE \"FreizeitId\" = @f", ("f", freizeitId)));

        // Story 3-3: vier Artikel im Inventar; das Zeltlager hat Fahnen (am Material „Fahnen“ des Geländespiels) und das Großzelt reserviert, die
        // Kinderbibeltage (Leitung Tim) das Großzelt zur gleichen Zeit: Konflikt. Nichts doppelt, auch nach dem zweiten Einspielen.
        var inventar = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/inventar", Abbruch))).GetProperty("artikel").EnumerateArray().ToList();
        Assert.Equal(["Fahnen 8 Stück", "Gaskocher 2 Stück", "Großzelt 1 Stück", "Kanus 6 Stück"], inventar.Select(a => $"{a.GetProperty("bezeichnung").GetString()} {a.GetProperty("menge").GetString()}"));
        string Reservierungen(System.Text.Json.JsonElement artikel) => string.Join(" | ", artikel.GetProperty("reservierungen").EnumerateArray().Select(r =>
            $"{r.GetProperty("von").GetString()} {r.GetProperty("bis").GetString()} {(r.GetProperty("materialId").ValueKind == System.Text.Json.JsonValueKind.Null ? "-" : "Material")} "
            + string.Join(", ", r.GetProperty("konflikte").EnumerateArray().Select(k => k.GetString()))));
        Assert.Equal(
            ["2027-08-02 2027-08-13 Material ", "", "2027-08-02 2027-08-13 - Kinderbibeltage 2027", ""],
            inventar.Select(Reservierungen));
        var fahnenMaterial = material.Single(m => m.GetProperty("bezeichnung").GetString() == "Fahnen").GetProperty("id").GetGuid();
        Assert.Equal(fahnenMaterial, inventar[0].GetProperty("reservierungen")[0].GetProperty("materialId").GetGuid());
        Assert.Equal(4L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM inventar.inventar_artikel"));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM inventar.reservierung"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM freizeiten.freizeit WHERE \"Name\" = 'Kinderbibeltage 2027' AND \"ErsterTag\" = '2027-08-10' AND \"LetzterTag\" = '2027-08-12'"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM freizeiten.freizeit_rolle r JOIN freizeiten.freizeit f ON f.\"Id\" = r.\"FreizeitId\" WHERE f.\"Name\" = 'Kinderbibeltage 2027' AND r.\"Rolle\" = 1 AND r.\"PersonId\" = @p",
            ("p", await AnmeldeHelfer.PersonIdAsync(verbindung, "tim.kroeger@example.org"))));

        // Story 3-4: zwei Einträge auf der Schadens- und Verlustliste der Demo-Freizeit; Leas Entnahme im Vorfall-Protokoll kommt nach der Zustellung
        // als „Erste-Hilfe-Kasten“ dazu (ohne Text). Nichts doppelt, auch nach dem zweiten Einspielen.
        await zweiter.OutboxJetztZustellenAsync(Abbruch);
        var schaeden = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/inventar/schaeden", Abbruch))).EnumerateArray().ToList();
        // Die Test-Uhr steht still: gleiche Zeitpunkte, darum ohne Reihenfolge verglichen.
        Assert.Equal(
            ["Entnahme Erste-Hilfe-Kasten Vorfall", "Schaden Großzelt: Stange verbogen -", "Verlust Taschenlampe -"],
            schaeden.Select(e => $"{e.GetProperty("art").GetString()} {e.GetProperty("bezeichnung").GetString()} "
                                 + (e.GetProperty("vorfallEintragId").ValueKind == System.Text.Json.JsonValueKind.Null ? "-" : "Vorfall")).Order(StringComparer.Ordinal));
        Assert.Equal(
            Guid.Parse("0199e8a0-0000-7000-8000-00000000f002"),
            schaeden.Single(e => e.GetProperty("art").GetString() == "Entnahme").GetProperty("vorfallEintragId").GetGuid());
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM inventar.schaden_verlust"));

        // Story 3-5: drei Aufgaben-Vorlagen der Organisation, beim Einspielen ins Zeltlager übernommen; dazu „Andacht am Lagerfeuer vorbereiten“ (am
        // Lagerfeuer, Lea) und „Fotos sammeln“ (offen). Lea hat den Erste-Hilfe-Kasten übernommen, Jonas den Zeltplatz-Vertrag und ihn abgehakt.
        // Nichts doppelt, auch nach dem zweiten Einspielen.
        var lagerfeuer = punkte.Single(p => p.GetProperty("titel").GetString() == "Lagerfeuer").GetProperty("id").GetGuid();
        var aufgaben = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/programm/aufgaben", Abbruch))).EnumerateArray().ToList();
        Assert.Equal(
            [
                "Vorbereitung Erste-Hilfe-Kasten auffüllen Lea offen Vorlage -", "Vorbereitung Zeltplatz-Vertrag prüfen Jonas erledigt Vorlage -",
                "Waehrend Andacht am Lagerfeuer vorbereiten Lea offen - Lagerfeuer", "Nachbereitung Fotos sammeln - offen - -",
                "Nachbereitung Zeltplatz besenrein übergeben - offen Vorlage -",
            ],
            aufgaben.Select(a =>
            {
                var von = a.GetProperty("uebernommenVon").ValueKind == System.Text.Json.JsonValueKind.Null ? null : (Guid?)a.GetProperty("uebernommenVon").GetGuid();
                var wer = von == lea ? "Lea" : von == jonas ? "Jonas" : von is null ? "-" : "?";
                var punkt = a.GetProperty("programmpunktId").ValueKind == System.Text.Json.JsonValueKind.Null ? "-" : a.GetProperty("programmpunktId").GetGuid() == lagerfeuer ? "Lagerfeuer" : "?";
                return $"{a.GetProperty("phase").GetString()} {a.GetProperty("titel").GetString()} {wer} {(a.GetProperty("erledigt").GetBoolean() ? "erledigt" : "offen")} "
                       + $"{(a.GetProperty("vorlage").GetBoolean() ? "Vorlage" : "-")} {punkt}";
            }));
        Assert.Equal(5L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM programm.aufgabe WHERE \"FreizeitId\" = @f", ("f", freizeitId)));
        Assert.Equal(3L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM programm.aufgaben_vorlage"));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM programm.aufgaben_vorlagen_uebernahme"));

        // Story 3-6: Das Team hat „Lagerfeuer“ und „Geländespiel“ des Zeltlagers in der Programmbibliothek abgelegt (Material und Aufgaben ohne Status
        // und Personen), Tim hat „Lagerfeuer“ in die Kinderbibeltage übernommen (11.08. um 19:30, Material „gebraucht“, Aufgabe frei). Nichts doppelt,
        // auch nach dem zweiten Einspielen.
        var bibliothek = (await AnmeldeHelfer.JsonAsync(await leaClient.GetAsync($"/api/freizeiten/{freizeitId}/programm/bibliothek", Abbruch))).EnumerateArray().ToList();
        Assert.Equal(
            [
                "Geländespiel: Fahnen 8 Stück, Seile 4 × 10 m, Taschenlampen 10 Stück | ",
                "Lagerfeuer: Feuerholz 2 Kisten, Stockbrot-Teig 3 kg | Waehrend Andacht am Lagerfeuer vorbereiten",
            ],
            bibliothek.Select(e =>
                $"{e.GetProperty("titel").GetString()}: "
                + string.Join(", ", e.GetProperty("material").EnumerateArray().Select(m => $"{m.GetProperty("bezeichnung").GetString()} {m.GetProperty("menge").GetString()}"))
                + " | "
                + string.Join(", ", e.GetProperty("aufgaben").EnumerateArray().Select(a => $"{a.GetProperty("phase").GetString()} {a.GetProperty("titel").GetString()}"))));
        Assert.Equal(2, punkte.Count(p => p.GetProperty("inBibliothek").GetBoolean()));
        Assert.Equal(2L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM programm.bibliotheks_eintrag"));
        var kinderbibeltage = await AnmeldeHelfer.WertAsync<Guid>(verbindung, "SELECT \"Id\" FROM freizeiten.freizeit WHERE \"Name\" = 'Kinderbibeltage 2027'");
        Assert.Equal(
            ["2027-08-11 19:30 Lagerfeuer frei"],
            await ZeilenAsync(verbindung, "SELECT \"Tag\"::text || ' ' || to_char(\"Beginn\", 'HH24:MI') || ' ' || \"Titel\" || ' ' || CASE WHEN \"PersonId\" IS NULL AND \"BibliotheksEintragId\" IS NULL THEN 'frei' ELSE '?' END FROM programm.programmpunkt WHERE \"FreizeitId\" = @f", kinderbibeltage));
        Assert.Equal(
            ["Feuerholz 2 Kisten 1 -", "Stockbrot-Teig 3 kg 1 -"],
            await ZeilenAsync(verbindung, "SELECT \"Bezeichnung\" || ' ' || \"Menge\" || ' ' || \"Status\" || ' ' || CASE WHEN \"PersonId\" IS NULL AND \"ReservierungId\" IS NULL THEN '-' ELSE '?' END FROM programm.material WHERE \"FreizeitId\" = @f ORDER BY 1", kinderbibeltage));
        Assert.Equal(
            ["Andacht am Lagerfeuer vorbereiten 2 frei offen"],
            await ZeilenAsync(verbindung, "SELECT \"Titel\" || ' ' || \"Phase\" || ' ' || CASE WHEN \"PersonId\" IS NULL THEN 'frei' ELSE '?' END || ' ' || CASE WHEN \"Erledigt\" THEN 'erledigt' ELSE 'offen' END FROM programm.aufgabe WHERE \"FreizeitId\" = @f AND \"ProgrammpunktId\" IS NOT NULL", kinderbibeltage));

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
        Assert.Equal(["Tim"], personen.Where(p => p.Rolle == DemoRolle.LeitungZweiteFreizeit).Select(p => p.Vorname));
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

    /// <summary>Story 3-6: die erste Spalte aller Zeilen einer Abfrage mit dem Parameter <c>@f</c> (rohes SQL nur für Prüfungen im Test).</summary>
    private static async Task<List<string>> ZeilenAsync(string verbindung, string sql, Guid freizeit)
    {
        await using var db = new Npgsql.NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(sql, db);
        befehl.Parameters.AddWithValue("f", freizeit);
        await using var leser = await befehl.ExecuteReaderAsync(Abbruch);
        var zeilen = new List<string>();
        while (await leser.ReadAsync(Abbruch))
        {
            zeilen.Add(leser.GetString(0));
        }

        return zeilen;
    }
}
