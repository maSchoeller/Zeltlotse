using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.NachweisSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Konto löschen <c>POST /api/ich/profil/loeschen</c> (FR-14, AD-16, AD-25, Story 1-7) mit S3: sofort gelöscht in Personen, nach
/// der Outbox in Konten, Organisationen und Freizeiten; fremde Bezüge tragen den Platzhalter; Kinder einziger Eltern werden
/// mitgelöscht; ohne frische Anmeldung oder Antiforgery passiert nichts; danach gilt keine Sitzung mehr.
/// </summary>
public sealed class KontoLoeschenTests(PostgresContainer postgres, S3Container s3)
{
    private const string Pfad = "/api/ich/profil/loeschen";

    /// <summary>Tabellen der Infrastruktur, deren Inhalte Kennungen nur als Ereignis-Inhalt tragen (Outbox, Inbox).</summary>
    private static readonly string[] Infrastruktur = ["outbox", "outbox_zustellung", "inbox", "inbox_stand", "__EFMigrationsHistory"];

    [Fact]
    public async Task Jonas_loescht_sein_Konto_und_nach_der_Outbox_traegt_keine_Tabelle_mehr_seine_PersonId()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var jonas = await s.PersonIdAsync(Jonas);
        var miriam = await s.PersonIdAsync(Miriam);
        var tobias = await s.PersonIdAsync(Tobias);

        // Jonas' Nachweise mit Dateien, Miriam bestätigt Erste Hilfe (eigene Bestätigung bei der Organisation).
        var (jonasEinsicht, fz, eh, _) = await s.FreigegebenUndUebernommenAsync();
        (await BestaetigenAsync(s.MiriamClient, s.FreizeitPfad, jonasEinsicht, eh)).Dispose();
        // Jonas hat eine Org-Rolle (Org-Überblick) und ist Leitung.
        await OrgHelfer.EinladenAsync(s.AndreaClient, s.OrgId, Jonas, OrgRolle.OrgUeberblick);
        (await OrgHelfer.AnnehmenAsync(s.JonasClient, OrgHelfer.Token(s.Factory, Jonas))).Dispose();
        // Jonas prüft Miriam: Protokolleintrag als Leser und Bestätigung „geprüft von Jonas“.
        var miriamNachweis = await NachweisAsync(s.MiriamClient, "juleica", "2024-05-20", Pdf);
        var miriamEinsicht = await AnfrageAsync(s.MiriamClient);
        (await FreigebenAsync(s.MiriamClient, miriamEinsicht)).Dispose();
        (await PruefenAsync(s.JonasClient, s.FreizeitPfad, miriamEinsicht)).Dispose();
        await DetailAsync(s.JonasClient, s.FreizeitPfad, miriamEinsicht);
        (await BestaetigenAsync(s.JonasClient, s.FreizeitPfad, miriamEinsicht, miriamNachweis)).Dispose();
        // Jonas erinnert Tobias.
        (await PostAsync(s.JonasClient, $"{s.FreizeitPfad}/erinnerungen", new NachweisErinnerungSenden(tobias))).Dispose();
        await s.Factory.OutboxJetztZustellenAsync(Abbruch);
        await AnmeldenAsync(s.Factory, s.JonasClient, Jonas);
        using var zweiteSitzung = await s.AngemeldetAsync(Jonas);
        Assert.Equal(1L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.NotNull(await s.EntschluesseltAsync(fz));

        using var antwort = await PostAsync(s.JonasClient, Pfad);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Contains(antwort.Headers.GetValues("Set-Cookie"), c => c.StartsWith(SharedKernel.Anmeldung.Sitzung.CookieName + "=;", StringComparison.Ordinal));
        Assert.Equal(1L, await ProtokollAsync(s.Verbindung, SicherheitsEreignisArt.Loeschung, jonas));
        // Personen löscht sofort, samt Schlüssel und Dateien.
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", jonas)));
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM personen.personen_schluessel WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Null(await s.EntschluesseltAsync(fz));
        Assert.Null(await s.EntschluesseltAsync(eh));
        Assert.Equal(1L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM konten.konto WHERE \"PersonId\" = @p", ("p", jonas)));

        await s.Factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM konten.konto WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM konten.login_anfrage WHERE \"Mail\" = @m", ("m", Jonas)));
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM organisationen.org_rolle WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"PersonId\" = @p", ("p", jonas)));
        // Fremde Einträge zeigen den Platzhalter.
        var platzhalter = GeloeschtePerson.Id;
        Assert.Equal(platzhalter, await WertAsync<Guid>(s.Verbindung, "SELECT \"GeprueftVon\" FROM organisationen.nachweis_bestaetigung WHERE \"PersonId\" = @p", ("p", miriam)));
        Assert.Equal(platzhalter, await WertAsync<Guid>(s.Verbindung, "SELECT \"LesendePersonId\" FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @p", ("p", miriam)));
        Assert.Equal(platzhalter, await WertAsync<Guid>(s.Verbindung, "SELECT \"PruefendePersonId\" FROM personen.nachweis_einsicht WHERE \"Id\" = @e", ("e", miriamEinsicht)));
        Assert.Equal(platzhalter, await WertAsync<Guid>(s.Verbindung, "SELECT \"ErinnertVon\" FROM personen.nachweis_erinnerung WHERE \"PersonId\" = @p", ("p", tobias)));
        Assert.Empty(await SpaltenMitKennungAsync(s.Verbindung, jonas));
        // Miriam sieht im Zugriffsprotokoll den Eintrag ohne Namen.
        var protokoll = (await s.MiriamClient.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>("/api/ich/zugriffsprotokoll", Abbruch))!;
        Assert.Null(Assert.Single(protokoll).LesenderName);
        // Das Sicherheitsprotokoll bleibt (nur Kennungen, feste Frist).
        Assert.True(await ProtokollAsync(s.Verbindung, SicherheitsEreignisArt.Anmeldung, jonas) > 0);

        // Keine Sitzung des Kontos gilt mehr; eine neue Anmeldung mit derselben Mail-Adresse beginnt leer.
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.JonasClient.GetAsync("/api/ich/konto", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await zweiteSitzung.GetAsync("/api/ich/konto", Abbruch)).StatusCode);
        using var neu = await s.AngemeldetAsync(Jonas);
        using var profil = await PostAsync(neu, "/api/ich/profil");
        Assert.Equal(HttpStatusCode.OK, profil.StatusCode);
        Assert.Null((await profil.Content.ReadFromJsonAsync<ProfilAntwort>(Abbruch))!.Vorname);
        Assert.NotEqual(jonas, await s.PersonIdAsync(Jonas));
        Assert.Empty((await EigeneAsync(neu)).Nachweise);
    }

    [Fact]
    public async Task Org_Admin_loescht_sein_Konto_und_Freizeit_Leitungs_und_Org_Einladungen_tragen_den_Platzhalter()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var andrea = await s.PersonIdAsync(Andrea);
        // Andrea hat die Freizeit angelegt und die Leitung eingeladen (Szenario) und lädt dazu den Org-Überblick ein.
        await OrgHelfer.EinladenAsync(s.AndreaClient, s.OrgId, "vorstand@example.org", OrgRolle.OrgUeberblick);
        Assert.Equal(andrea, await WertAsync<Guid>(s.Verbindung, "SELECT \"AngelegtVon\" FROM freizeiten.freizeit WHERE \"Id\" = @f", ("f", s.FreizeitId)));

        using var antwort = await PostAsync(s.AndreaClient, Pfad);
        await s.Factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        var platzhalter = GeloeschtePerson.Id;
        Assert.Equal(platzhalter, await WertAsync<Guid>(s.Verbindung, "SELECT \"AngelegtVon\" FROM freizeiten.freizeit WHERE \"Id\" = @f", ("f", s.FreizeitId)));
        Assert.Equal(3L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung WHERE \"EingeladenVon\" = @p", ("p", platzhalter)));
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung WHERE \"EingeladenVon\" <> @p", ("p", platzhalter)));
        Assert.Equal(platzhalter, await WertAsync<Guid>(s.Verbindung, "SELECT \"EingeladenVon\" FROM organisationen.org_einladung WHERE \"Mail\" = 'vorstand@example.org'"));
        Assert.Empty(await SpaltenMitKennungAsync(s.Verbindung, andrea));
    }

    [Fact]
    public async Task Anmeldung_aelter_als_10_Minuten_ergibt_401_und_ohne_Antiforgery_400_und_nichts_ist_geloescht()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = await AngemeldeterClientAsync(factory, OrgHelfer.Jonas);
        (await PostAsync(client, "/api/ich/profil")).Dispose();
        var jonas = await PersonIdAsync(verbindung, OrgHelfer.Jonas);

        using (var ohneAntiforgery = await client.PostAsync(Pfad, content: null, Abbruch))
        {
            Assert.Equal(HttpStatusCode.BadRequest, ohneAntiforgery.StatusCode);
        }

        factory.Uhr.Advance(TimeSpan.FromMinutes(11));
        using var alt = await PostAsync(client, Pfad);

        Assert.Equal(HttpStatusCode.Unauthorized, alt.StatusCode);
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", jonas)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Equal(0L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.Loeschung));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.outbox"));
    }

    [Fact]
    public async Task Einziger_Elternteil_loescht_auch_die_Kinder_und_die_Familie()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var katrin = await AngemeldeterClientAsync(factory, FamilienHelfer.Katrin);
        (await PostAsync(katrin, "/api/ich/profil")).Dispose();
        var paul = await FamilienHelfer.VolljaehrigesKindAsync(katrin, "Paul");
        var emma = await FamilienHelfer.KindAsync(katrin, "Emma");
        (await FamilienHelfer.EinladenAsync(katrin, "oma@example.org")).Dispose();
        (await FamilienHelfer.UebergebenAsync(katrin, paul, FamilienHelfer.PaulMail)).Dispose();
        var katrinId = await PersonIdAsync(verbindung, FamilienHelfer.Katrin);

        using var antwort = await PostAsync(katrin, Pfad);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = ANY(@p)", ("p", new[] { katrinId, paul, emma })));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.personen_schluessel WHERE \"PersonId\" = ANY(@p)", ("p", new[] { katrinId, paul, emma })));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familie"));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familien_elternteil"));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familien_einladung"));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.profil_uebergabe"));
        Assert.Equal(3L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%.PersonGeloescht'"));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto"));
    }

    [Fact]
    public async Task Mit_zweitem_Elternteil_bleiben_Kinder_und_Familie_beim_anderen()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var katrin = await AngemeldeterClientAsync(factory, FamilienHelfer.Katrin);
        (await PostAsync(katrin, "/api/ich/profil")).Dispose();
        var emma = await FamilienHelfer.KindAsync(katrin, "Emma");
        using var thomas = await FamilienHelfer.ZweiterElternteilAsync(factory, katrin, FamilienHelfer.Thomas);
        var katrinId = await PersonIdAsync(verbindung, FamilienHelfer.Katrin);
        var thomasId = await PersonIdAsync(verbindung, FamilienHelfer.Thomas);

        using var antwort = await PostAsync(katrin, Pfad);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", katrinId)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familie"));
        var familie = await FamilienHelfer.FamilieAsync(thomas);
        Assert.Equal(thomasId, Assert.Single(familie.Eltern).PersonId);
        Assert.Equal(emma, Assert.Single(familie.Kinder).PersonId);
        // Die angenommene Einladung von Katrin trägt den Platzhalter.
        Assert.Equal(GeloeschtePerson.Id, await WertAsync<Guid>(verbindung, "SELECT \"EingeladenVon\" FROM personen.familien_einladung"));
        Assert.Empty(await SpaltenMitKennungAsync(verbindung, katrinId));
    }

    /// <summary>Alle Spalten vom Typ uuid in den Schemas der Module (außer Outbox und Inbox), die die Kennung noch enthalten.</summary>
    private static async Task<List<string>> SpaltenMitKennungAsync(string verbindung, Guid kennung)
    {
        var spalten = new List<(string Schema, string Tabelle, string Spalte)>();
        await using var db = new NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using (var befehl = new NpgsqlCommand(
                         "SELECT table_schema, table_name, column_name FROM information_schema.columns " +
                         "WHERE data_type = 'uuid' AND table_schema IN ('konten', 'personen', 'organisationen', 'freizeiten', 'sicherheit', 'programm', 'inventar', 'kueche', 'finanzen', 'kanaele')",
                         db))
        await using (var leser = await befehl.ExecuteReaderAsync(Abbruch))
        {
            while (await leser.ReadAsync(Abbruch))
            {
                spalten.Add((leser.GetString(0), leser.GetString(1), leser.GetString(2)));
            }
        }

        var treffer = new List<string>();
        foreach (var (schema, tabelle, spalte) in spalten.Where(s => !Infrastruktur.Contains(s.Tabelle)))
        {
            await using var zaehlen = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".\"{tabelle}\" WHERE \"{spalte}\" = @k", db);
            zaehlen.Parameters.AddWithValue("k", kennung);
            if ((long)(await zaehlen.ExecuteScalarAsync(Abbruch))! > 0)
            {
                treffer.Add($"{schema}.{tabelle}.{spalte}");
            }
        }

        return treffer;
    }
}
