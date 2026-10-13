using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Zeltlotse.Api.Datenschutz;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Täglicher Lösch-Lauf (AD-16, NFR-5, Story 1-7) mit künstlicher Uhr: feste technische Fristen für Login-Anfragen (1 Tag) und
/// Einladungen/Übergaben (30 Tage nach Ablauf), Org-Fristen für die Arbeitsdaten einer Freizeit (Nachweis-Erinnerungen) und für
/// abgelaufene Nachweis-Bestätigungen; ein werfendes Modul hält die anderen nicht auf. Die Daten liegen per SQL bereit, der Lauf
/// wird direkt aufgerufen.
/// </summary>
public sealed class LoeschlaufTests(PostgresContainer postgres)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task Login_Anfragen_mit_Ablauf_vor_mehr_als_einem_Tag_werden_geloescht()
    {
        var (factory, verbindung) = await StartenAsync();
        await using var _ = factory;
        var jetzt = factory.Uhr.GetUtcNow();
        var alt = await LoginAnfrageAsync(verbindung, jetzt - TimeSpan.FromHours(25));
        var jung = await LoginAnfrageAsync(verbindung, jetzt - TimeSpan.FromHours(23));

        await factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);

        Assert.Equal([jung], await KennungenAsync(verbindung, "SELECT \"Id\" FROM konten.login_anfrage"));
        Assert.DoesNotContain(alt, await KennungenAsync(verbindung, "SELECT \"Id\" FROM konten.login_anfrage"));
    }

    [Fact]
    public async Task Einladungen_und_Uebergaben_mit_Ablauf_vor_31_Tagen_werden_geloescht_vor_29_Tagen_bleiben_sie()
    {
        var (factory, verbindung) = await StartenAsync();
        await using var _ = factory;
        var jetzt = factory.Uhr.GetUtcNow();
        var org = await OrganisationAsync(verbindung, 12, 12);
        var freizeit = await FreizeitAsync(verbindung, org, Heute(jetzt).AddMonths(1));
        var familie = Guid.CreateVersion7();
        await AusfuehrenAsync(verbindung, "INSERT INTO personen.familie (\"Id\", \"AngelegtAm\") VALUES (@id, now())", ("id", familie));
        var bleiben = new List<Guid>();
        var weg = new List<Guid>();
        foreach (var (tage, liste) in new[] { (31, weg), (29, bleiben) })
        {
            var ablauf = jetzt - TimeSpan.FromDays(tage);
            liste.Add(await EinladungAsync(verbindung, "organisationen.org_einladung", ablauf, ("\"OrganisationId\", \"Rolle\"", "@org, 1"), ("org", org)));
            liste.Add(await EinladungAsync(verbindung, "freizeiten.leitungs_einladung", ablauf, ("\"OrganisationId\", \"FreizeitId\"", "@org, @fz"), ("org", org), ("fz", freizeit)));
            liste.Add(await EinladungAsync(verbindung, "personen.familien_einladung", ablauf, ("\"FamilieId\"", "@fam"), ("fam", familie)));
            liste.Add(await EinladungAsync(verbindung, "personen.profil_uebergabe", ablauf, ("\"FamilieId\", \"PersonId\", \"Folgenummer\"", "@fam, @p, 0"), ("fam", familie), ("p", Guid.CreateVersion7())));
        }

        await factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);

        var uebrig = new List<Guid>();
        foreach (var tabelle in new[] { "organisationen.org_einladung", "freizeiten.leitungs_einladung", "personen.familien_einladung", "personen.profil_uebergabe" })
        {
            uebrig.AddRange(await KennungenAsync(verbindung, $"SELECT \"Id\" FROM {tabelle}"));
        }

        Assert.Equal(bleiben.Order(), uebrig.Order());
    }

    [Fact]
    public async Task Nachweis_Erinnerungen_einer_Freizeit_werden_nach_Freizeitende_plus_Frist_der_Organisation_geloescht()
    {
        var (factory, verbindung) = await StartenAsync();
        await using var _ = factory;
        var heute = Heute(factory.Uhr.GetUtcNow());
        var orgA = await OrganisationAsync(verbindung, 12, 12);
        var orgB = await OrganisationAsync(verbindung, 1, 12);
        // Letzter Tag so, dass das Freizeitende (24:00 Uhr) genau 13, 2 bzw. 11 Monate zurückliegt.
        var a13 = await FreizeitAsync(verbindung, orgA, heute.AddMonths(-13).AddDays(-1));
        var b2 = await FreizeitAsync(verbindung, orgB, heute.AddMonths(-2).AddDays(-1));
        var a11 = await FreizeitAsync(verbindung, orgA, heute.AddMonths(-11).AddDays(-1));
        foreach (var (org, freizeit) in new[] { (orgA, a13), (orgB, b2), (orgA, a11) })
        {
            await AusfuehrenAsync(
                verbindung,
                "INSERT INTO personen.nachweis_erinnerung (\"Id\", \"OrganisationId\", \"FreizeitId\", \"PersonId\", \"ErinnertVon\", \"ErinnertAm\", \"Folgenummer\") VALUES (@id, @org, @fz, @p, @v, now(), 1)",
                ("id", Guid.CreateVersion7()), ("org", org), ("fz", freizeit), ("p", Guid.CreateVersion7()), ("v", Guid.CreateVersion7()));
            // Anmeldungen sind Arbeitsdaten der Freizeit (Story 2-2) und fallen mit derselben Frist.
            var anmeldung = Guid.CreateVersion7();
            var person = Guid.CreateVersion7();
            await AusfuehrenAsync(
                verbindung,
                "INSERT INTO freizeiten.anmeldung (\"Id\", \"OrganisationId\", \"FreizeitId\", \"PersonId\", \"Art\", \"Status\", \"EingegangenAm\", \"TeilnahmebedingungenBestaetigtAm\", \"Folgenummer\") VALUES (@id, @org, @fz, @p, 1, 1, now(), now(), 0)",
                ("id", anmeldung), ("org", org), ("fz", freizeit), ("p", person));
            // Story 2-5: Team-Notizen fallen mit ihren Anmeldungen.
            await AusfuehrenAsync(
                verbindung,
                "INSERT INTO freizeiten.team_notiz (\"Id\", \"OrganisationId\", \"FreizeitId\", \"AnmeldungId\", \"PersonId\", \"VerfasstVon\", \"Text\", \"AngelegtAm\") VALUES (@id, @org, @fz, @a, @p, @v, '\\x00'::bytea, now())",
                ("id", Guid.CreateVersion7()), ("org", org), ("fz", freizeit), ("a", anmeldung), ("p", person), ("v", Guid.CreateVersion7()));
        }

        var auftrag = await factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);

        Assert.Equal(new[] { a13, b2 }.Order(), auftrag.FreizeitenMitAbgelaufenenDaten.Order());
        Assert.Equal([a11], await KennungenAsync(verbindung, "SELECT \"FreizeitId\" FROM personen.nachweis_erinnerung"));
        Assert.Equal([a11], await KennungenAsync(verbindung, "SELECT \"FreizeitId\" FROM freizeiten.anmeldung"));
        Assert.Equal([a11], await KennungenAsync(verbindung, "SELECT \"FreizeitId\" FROM freizeiten.team_notiz"));
        // Freizeit-Rollen und Freizeiten selbst löscht keine Frist.
        Assert.Equal(3L, await WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit"));
    }

    [Fact]
    public async Task Nachweis_Bestaetigungen_werden_nach_gueltig_bis_plus_Frist_ihrer_Organisation_geloescht()
    {
        var (factory, verbindung) = await StartenAsync();
        await using var _ = factory;
        var heute = Heute(factory.Uhr.GetUtcNow());
        // Org A: Bestätigungen 6 Monate (Freizeitdaten 12), Org B umgekehrt; so fällt eine Verwechslung der Fristen auf.
        var orgA = await OrganisationAsync(verbindung, 12, 6);
        var orgB = await OrganisationAsync(verbindung, 6, 12);
        var a7 = Guid.CreateVersion7();
        var a5 = Guid.CreateVersion7();
        var b13 = Guid.CreateVersion7();
        var b7 = Guid.CreateVersion7();
        foreach (var (org, person, gueltigBis) in new[]
                 {
                     (orgA, a7, heute.AddMonths(-7)), (orgA, a5, heute.AddMonths(-5)),
                     (orgB, b13, heute.AddMonths(-13)), (orgB, b7, heute.AddMonths(-7)),
                 })
        {
            await AusfuehrenAsync(
                verbindung,
                "INSERT INTO organisationen.nachweis_bestaetigung (\"OrganisationId\", \"PersonId\", \"Art\", \"EingesehenAm\", \"GueltigBis\", \"GeprueftVon\") VALUES (@org, @p, 1, now(), @bis, @v)",
                ("org", org), ("p", person), ("bis", gueltigBis), ("v", Guid.CreateVersion7()));
        }

        await factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);

        Assert.Equal(new[] { a5, b7 }.Order(), (await KennungenAsync(verbindung, "SELECT \"PersonId\" FROM organisationen.nachweis_bestaetigung")).Order());
    }

    [Fact]
    public async Task Ein_werfendes_Modul_wird_geloggt_und_haelt_die_anderen_nicht_auf()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        // Als erstes registriert, damit alle Module der Lösung danach laufen müssen.
        factory.ZusatzDienste.Add(s => s.Insert(0, ServiceDescriptor.Scoped<ILoeschlauf, WerfenderLoeschlauf>()));
        _ = factory.CreateClient();
        var alt = await LoginAnfrageAsync(verbindung, factory.Uhr.GetUtcNow() - TimeSpan.FromDays(2));
        var logger = new FakeLogger<Loeschlauf>();
        var lauf = new Loeschlauf(factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Uhr, logger);

        await lauf.LaufAsync(Abbruch);

        Assert.DoesNotContain(alt, await KennungenAsync(verbindung, "SELECT \"Id\" FROM konten.login_anfrage"));
        var fehler = Assert.Single(logger.Collector.GetSnapshot(), e => e.Level == LogLevel.Error);
        Assert.Contains(nameof(WerfenderLoeschlauf), fehler.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), fehler.Message, StringComparison.Ordinal);
        // Nur Kennungen und Typen, nie die Meldung der Ausnahme (AD-22).
        Assert.DoesNotContain("Erika", fehler.Message, StringComparison.Ordinal);
    }

    private async Task<(ZeltlotseApiFactory Factory, string Verbindung)> StartenAsync()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        return (factory, verbindung);
    }

    private static DateOnly Heute(DateTimeOffset jetzt) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(jetzt, Berlin).DateTime);

    private static async Task<Guid> LoginAnfrageAsync(string verbindung, DateTimeOffset gueltigBis)
    {
        var id = Guid.CreateVersion7();
        await AusfuehrenAsync(
            verbindung,
            "INSERT INTO konten.login_anfrage (\"Id\", \"Mail\", \"TokenHash\", \"CodeHash\", \"AngelegtAm\", \"GueltigBis\", \"Fehlversuche\", \"Status\") VALUES (@id, 'x@example.org', @t, @c, @an, @bis, 0, 1)",
            ("id", id), ("t", RandomNumberGenerator.GetBytes(32)), ("c", RandomNumberGenerator.GetBytes(32)), ("an", gueltigBis - TimeSpan.FromMinutes(15)), ("bis", gueltigBis));
        return id;
    }

    private static async Task<Guid> OrganisationAsync(string verbindung, int freizeitMonate, int bestaetigungMonate)
    {
        var id = Guid.CreateVersion7();
        await AusfuehrenAsync(verbindung, "INSERT INTO organisationen.organisation (\"Id\", \"Name\", \"AngelegtAm\") VALUES (@id, @name, now())", ("id", id), ("name", "Org " + id));
        await AusfuehrenAsync(
            verbindung,
            "INSERT INTO organisationen.loeschfristen (\"OrganisationId\", \"FreizeitDatenMonate\", \"NachweisBestaetigungMonate\") VALUES (@id, @f, @b)",
            ("id", id), ("f", freizeitMonate), ("b", bestaetigungMonate));
        return id;
    }

    private static async Task<Guid> FreizeitAsync(string verbindung, Guid org, DateOnly letzterTag)
    {
        var id = Guid.CreateVersion7();
        await AusfuehrenAsync(
            verbindung,
            "INSERT INTO freizeiten.freizeit (\"Id\", \"OrganisationId\", \"Name\", \"ErsterTag\", \"LetzterTag\", \"FreigabeIntervallTage\", \"AngelegtAm\", \"AngelegtVon\", \"Folgenummer\") " +
            "VALUES (@id, @org, 'Zeltlager', @erster, @letzter, 30, now(), @von, 0)",
            ("id", id), ("org", org), ("erster", letzterTag.AddDays(-7)), ("letzter", letzterTag), ("von", Guid.CreateVersion7()));
        return id;
    }

    private static async Task<Guid> EinladungAsync(
        string verbindung, string tabelle, DateTimeOffset gueltigBis, (string Spalten, string Werte) zusatz, params (string Name, object Wert)[] parameter)
    {
        var id = Guid.CreateVersion7();
        await AusfuehrenAsync(
            verbindung,
            $"INSERT INTO {tabelle} (\"Id\", {zusatz.Spalten}, \"Mail\", \"TokenHash\", \"AngelegtAm\", \"GueltigBis\", \"EingeladenVon\", \"Status\") " +
            $"VALUES (@id, {zusatz.Werte}, 'x@example.org', @t, @an, @bis, @von, 1)",
            [("id", id), ("t", RandomNumberGenerator.GetBytes(32)), ("an", gueltigBis - TimeSpan.FromDays(7)), ("bis", gueltigBis), ("von", Guid.CreateVersion7()), .. parameter]);
        return id;
    }

    private static async Task<List<Guid>> KennungenAsync(string verbindung, string sql)
    {
        await using var db = new Npgsql.NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(sql, db);
        await using var leser = await befehl.ExecuteReaderAsync(Abbruch);
        var kennungen = new List<Guid>();
        while (await leser.ReadAsync(Abbruch))
        {
            kennungen.Add(leser.GetGuid(0));
        }

        return kennungen;
    }

    /// <summary>Ein Modul, dessen Lauf immer scheitert; die Meldung enthält absichtlich einen Namen.</summary>
    private sealed class WerfenderLoeschlauf : ILoeschlauf
    {
        public Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch) =>
            throw new InvalidOperationException("Erika Muster ist kaputt.");
    }
}
