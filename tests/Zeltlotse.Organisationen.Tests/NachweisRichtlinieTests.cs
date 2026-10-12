using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Nachweise;
using Zeltlotse.Organisationen.Verwaltung;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using EreignisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Organisationen.Tests;

/// <summary>
/// Nachweis-Richtlinie und -Bestätigung in Organisationen (FR-9, FR-12, AD-9, NFR-5): Vorgabewerte beim Anlegen der
/// Organisation, Feldregeln nach AD-19, Empfänger von <see cref="NachweisEingesehen"/> mit „gültig bis“ aus der Richtlinie
/// zum Zeitpunkt des Empfangs, jüngste Einsicht gewinnt, jedes Ereignis nur einmal.
/// </summary>
public sealed class NachweisRichtlinieTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid Gerd = Guid.CreateVersion7();
    private static readonly Guid Jonas = Guid.CreateVersion7();
    private static readonly Guid Miriam = Guid.CreateVersion7();
    private static readonly Guid Tobias = Guid.CreateVersion7();

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = new ZeltlotseApiFactory(_verbindung);
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Neue_Organisation_bekommt_die_vier_Vorgabezeilen()
    {
        var org = await OrganisationAsync();

        var zeilen = await LesenAsync(db => db.Richtlinien.OrderBy(r => r.Art).ToListAsync(Abbruch), org);

        Assert.Equal(
            [(EreignisArt.Fuehrungszeugnis, true, 60), (EreignisArt.Juleica, false, 36), (EreignisArt.ErsteHilfe, false, 24), (EreignisArt.Praeventionsschulung, false, 60)],
            zeilen.Select(z => (z.Art, z.Pflicht, z.GueltigkeitMonate)));
        Assert.All(zeilen, z => Assert.Equal(org, z.OrganisationId));
        Assert.Empty(await LesenAsync(db => db.Richtlinien.ToListAsync(Abbruch), Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Feld_aendern_folgt_der_Konfliktregel()
    {
        var org = await OrganisationAsync();

        var gespeichert = Assert.IsType<RichtlinienAenderung.Gespeichert>(
            await SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.Juleica, RichtlinieFeld.GueltigkeitMonate, 1, "36", " 48 "), org));
        Assert.IsType<RichtlinienAenderung.Konflikt>(
            await SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.Juleica, RichtlinieFeld.GueltigkeitMonate, 1, "36", "12"), org));
        var pflicht = Assert.IsType<RichtlinienAenderung.Gespeichert>(
            await SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.Juleica, RichtlinieFeld.Pflicht, 1, "false", "true"), org));
        Assert.IsType<RichtlinienAenderung.Konflikt>(
            await SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.Juleica, RichtlinieFeld.Pflicht, 1, "false", "false"), org));

        Assert.Equal("48", gespeichert.Richtlinie.Wert(RichtlinieFeld.GueltigkeitMonate));
        Assert.Equal("true", pflicht.Richtlinie.Wert(RichtlinieFeld.Pflicht));
        var zeile = await LesenAsync(db => db.Richtlinien.SingleAsync(r => r.Art == EreignisArt.Juleica, Abbruch), org);
        Assert.Equal((true, 48), (zeile.Pflicht, zeile.GueltigkeitMonate));
    }

    [Theory]
    [InlineData(RichtlinieFeld.GueltigkeitMonate, "0", "gueltigkeitMonate", "Die Gültigkeit muss zwischen 1 und 120 Monaten liegen.")]
    [InlineData(RichtlinieFeld.GueltigkeitMonate, "121", "gueltigkeitMonate", "Die Gültigkeit muss zwischen 1 und 120 Monaten liegen.")]
    [InlineData(RichtlinieFeld.GueltigkeitMonate, "zwölf", "gueltigkeitMonate", "Die Gültigkeit muss zwischen 1 und 120 Monaten liegen.")]
    [InlineData(RichtlinieFeld.GueltigkeitMonate, null, "gueltigkeitMonate", "Die Gültigkeit muss zwischen 1 und 120 Monaten liegen.")]
    [InlineData(RichtlinieFeld.Pflicht, "ja", "pflicht", "Pflicht ist „true“ oder „false“.")]
    [InlineData(RichtlinieFeld.Pflicht, null, "pflicht", "Pflicht ist „true“ oder „false“.")]
    public async Task Feld_aendern_prueft_den_Wert(RichtlinieFeld feld, string? wert, string schluessel, string meldung)
    {
        var org = await OrganisationAsync();

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.Juleica, feld, 1, null, wert), org));

        Assert.Equal([meldung], fehler.Fehler[schluessel]);
    }

    [Fact]
    public async Task Feld_aendern_in_einer_fremden_Organisation_findet_nichts()
    {
        var org = await OrganisationAsync();

        Assert.IsType<RichtlinienAenderung.NichtGefunden>(
            await SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.Juleica, RichtlinieFeld.Pflicht, 1, "false", "true"), Guid.CreateVersion7()));
    }

    [Fact]
    public void Empfaenger_ist_angemeldet()
    {
        var eintrag = Assert.Single(_factory.Services.GetRequiredService<EreignisKatalog>().EmpfaengerFuer(typeof(NachweisEingesehen)));
        Assert.Equal(typeof(NachweisBestaetigungSpeichern), eintrag.CommandTyp);
    }

    [Fact]
    public async Task Bestaetigung_rechnet_gueltig_bis_aus_der_Richtlinie_beim_Empfang_und_speichert_kein_Ausstellungsdatum()
    {
        var org = await OrganisationAsync();
        var eingesehen = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

        await ZustellenAsync(Umschlag(org), new NachweisEingesehen(Jonas, Guid.CreateVersion7(), EreignisArt.ErsteHilfe, new DateOnly(2025, 4, 15), eingesehen, Miriam));
        await SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.Fuehrungszeugnis, RichtlinieFeld.GueltigkeitMonate, 1, "60", "12"), org);
        await ZustellenAsync(Umschlag(org), new NachweisEingesehen(Jonas, Guid.CreateVersion7(), EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 11, 3), eingesehen, Miriam));

        var zeilen = await LesenAsync(db => db.Bestaetigungen.OrderBy(b => b.Art).ToListAsync(Abbruch), org);
        Assert.Equal(
            [(EreignisArt.Fuehrungszeugnis, new DateOnly(2026, 11, 3)), (EreignisArt.ErsteHilfe, new DateOnly(2027, 4, 15))],
            zeilen.Select(z => (z.Art, z.GueltigBis)));
        Assert.All(zeilen, z => Assert.Equal((org, Jonas, eingesehen, Miriam), (z.OrganisationId, z.PersonId, z.EingesehenAm, z.GeprueftVon)));
        var spalten = await SpaltenAsync("nachweis_bestaetigung");
        Assert.DoesNotContain("Ausstellungsdatum", spalten);
        Assert.Equal(["Art", "EingesehenAm", "GeprueftVon", "GueltigBis", "OrganisationId", "PersonId"], spalten.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Die_juengste_Einsicht_gewinnt_und_dasselbe_Ereignis_zaehlt_einmal()
    {
        var org = await OrganisationAsync();
        var frueh = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var spaet = frueh.AddDays(30);
        var umschlag = Umschlag(org);

        await ZustellenAsync(umschlag, new NachweisEingesehen(Jonas, Guid.CreateVersion7(), EreignisArt.Juleica, new DateOnly(2024, 5, 20), spaet, Tobias));
        await ZustellenAsync(umschlag, new NachweisEingesehen(Jonas, Guid.CreateVersion7(), EreignisArt.Juleica, new DateOnly(2020, 1, 1), spaet, Miriam));
        await ZustellenAsync(Umschlag(org), new NachweisEingesehen(Jonas, Guid.CreateVersion7(), EreignisArt.Juleica, new DateOnly(2023, 1, 1), frueh, Miriam));

        var zeile = Assert.Single(await LesenAsync(db => db.Bestaetigungen.ToListAsync(Abbruch), org));
        Assert.Equal((spaet, new DateOnly(2027, 5, 20), Tobias), (zeile.EingesehenAm, zeile.GueltigBis, zeile.GeprueftVon));
        Assert.Empty(await LesenAsync(db => db.Bestaetigungen.ToListAsync(Abbruch), Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Ohne_Richtlinienzeile_gilt_der_Vorgabewert()
    {
        var org = await OrganisationAsync();
        await AusfuehrenAsync($"DELETE FROM organisationen.nachweis_richtlinie WHERE \"OrganisationId\" = '{org}'");

        await ZustellenAsync(Umschlag(org), new NachweisEingesehen(Jonas, Guid.CreateVersion7(), EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 11, 3), DateTimeOffset.UtcNow, Miriam));

        Assert.Equal(new DateOnly(2030, 11, 3), (await LesenAsync(db => db.Bestaetigungen.SingleAsync(Abbruch), org)).GueltigBis);
    }

    private async Task<Guid> OrganisationAsync() =>
        (await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk " + Guid.NewGuid().ToString("N")[..6], "andrea@example.org", Gerd))).OrganisationId;

    private static Umschlag Umschlag(Guid org) => new(Guid.CreateVersion7(), DateTimeOffset.UtcNow, org, Guid.CreateVersion7(), 1);

    /// <summary>Wie der Zustelldienst: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    private async Task ZustellenAsync(Umschlag umschlag, NachweisEingesehen ereignis)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(umschlag.OrganisationId);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(NachweisBestaetigungSpeichern.Aus(umschlag, ereignis), Abbruch);
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<OrganisationenLeseDbContext, Task<T>> abfrage, Guid? organisationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<OrganisationenLeseDbContext>());
    }

    private async Task AusfuehrenAsync(string sql)
    {
        await using var db = new NpgsqlConnection(_verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        await befehl.ExecuteNonQueryAsync(Abbruch);
    }

    private async Task<List<string>> SpaltenAsync(string tabelle)
    {
        await using var db = new NpgsqlConnection(_verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'organisationen' AND table_name = @t", db);
        befehl.Parameters.AddWithValue("t", tabelle);
        await using var leser = await befehl.ExecuteReaderAsync(Abbruch);
        var spalten = new List<string>();
        while (await leser.ReadAsync(Abbruch))
        {
            spalten.Add(leser.GetString(0));
        }

        return spalten;
    }
}

/// <summary>Die Migration legt die Vorgabezeilen für schon bestehende Organisationen an (FR-9, NFR-5).</summary>
public sealed class NachweisRichtlinieMigrationTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Bestehende_Organisationen_bekommen_die_vier_Vorgabezeilen()
    {
        var abbruch = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:zeltlotse"] = await postgres.NeueDatenbankAsync(),
                [Zeltlotse.SharedKernel.Datenschutz.Hauptschluessel.Konfigurationsschluessel] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            })
            .Build());
        services.AddOrganisationenModul();
        await using var dienste = services.BuildServiceProvider();
        await using var scope = dienste.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganisationenDbContext>();
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);

        await migrator.MigrateAsync("20261011170104_OrganisationRolleEinladung", abbruch);
        var org = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync($"INSERT INTO organisationen.organisation (\"Id\", \"Name\", \"AngelegtAm\") VALUES ({org}, 'Alt', now())", abbruch);
        await migrator.MigrateAsync(cancellationToken: abbruch);

        var zeilen = await db.Richtlinien.IgnoreQueryFilters().Where(r => r.OrganisationId == org).OrderBy(r => r.Art).ToListAsync(abbruch);
        Assert.Equal(
            [(EreignisArt.Fuehrungszeugnis, true, 60), (EreignisArt.Juleica, false, 36), (EreignisArt.ErsteHilfe, false, 24), (EreignisArt.Praeventionsschulung, false, 60)],
            zeilen.Select(z => (z.Art, z.Pflicht, z.GueltigkeitMonate)));
    }
}
