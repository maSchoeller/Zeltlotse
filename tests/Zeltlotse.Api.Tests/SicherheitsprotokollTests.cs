using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using Zeltlotse.Api.Betrieb;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Matrix-Zeile „Sicherheitsprotokoll“ (AD-22): nur Kennungen, eigene Transaktion, Frist 365 Tage.</summary>
public sealed class SicherheitsprotokollTests(PostgresContainer postgres)
{
    private static readonly Guid Person = Guid.Parse("0199b000-0000-7000-8000-00000000000a");
    private static readonly Guid Organisation = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid Bezug = Guid.Parse("0199c000-0000-7000-8000-00000000000c");

    [Fact]
    public async Task Schreiben_legt_eine_Zeile_mit_Kennungen_und_Zeitpunkt_an()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        var protokoll = factory.Services.GetRequiredService<ISicherheitsprotokoll>();

        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Rollenvergabe, Person, Organisation, Bezug, Abbruch);
        await protokoll.SchreibenAsync(SicherheitsEreignisArt.AnmeldungFehlgeschlagen, abbruch: Abbruch);

        var zeilen = await ZeilenAsync(verbindung);
        Assert.Equal(2, zeilen.Count);
        var rolle = zeilen.Single(z => z.Art == (int)SicherheitsEreignisArt.Rollenvergabe);
        Assert.Equal(Person, rolle.PersonId);
        Assert.Equal(Organisation, rolle.OrganisationId);
        Assert.Equal(Bezug, rolle.BezugId);
        Assert.Equal(factory.Uhr.GetUtcNow(), rolle.Zeitpunkt);
        var fehlgeschlagen = zeilen.Single(z => z.Art == (int)SicherheitsEreignisArt.AnmeldungFehlgeschlagen);
        Assert.Null(fehlgeschlagen.PersonId);
    }

    [Fact]
    public async Task Abbruch_des_Aufrufers_verhindert_den_Eintrag_nicht()
    {
        // Sonst entginge z. B. eine fehlgeschlagene Anmeldung dem Protokoll, wenn der Client die Verbindung abbricht.
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        using var abgebrochen = new CancellationTokenSource();
        await abgebrochen.CancelAsync();

        await factory.Services.GetRequiredService<ISicherheitsprotokoll>()
            .SchreibenAsync(SicherheitsEreignisArt.AnmeldungFehlgeschlagen, abbruch: abgebrochen.Token);

        Assert.Single(await ZeilenAsync(verbindung));
    }

    [Fact]
    public async Task Tabelle_hat_nur_Kennungen_und_keine_Freitext_oder_IP_Spalte()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();

        var spalten = await SpaltenAsync(verbindung);

        Assert.Equal(["Art", "BezugId", "Id", "OrganisationId", "PersonId", "Zeitpunkt"], spalten.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(spalten, s => s.Typ is "text" or "character varying" or "inet" or "cidr" or "jsonb");
    }

    [Fact]
    public void Es_gibt_die_geforderten_Arten()
    {
        Assert.Equal(
            ["Anmeldung", "AnmeldungFehlgeschlagen", "Export", "LinkEinloesungFehlgeschlagen", "Loeschung", "ProfilUebergabe", "Rollenvergabe"],
            Enum.GetNames<SicherheitsEreignisArt>().Order());
    }

    [Fact]
    public async Task Aufraeumlauf_loescht_Zeilen_aelter_als_365_Tage_und_behaelt_juengere()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        var protokoll = factory.Services.GetRequiredService<ISicherheitsprotokoll>();

        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Export, Person, abbruch: Abbruch);
        factory.Uhr.Advance(TimeSpan.FromDays(200));
        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Loeschung, Person, abbruch: Abbruch);
        factory.Uhr.Advance(TimeSpan.FromDays(165) + TimeSpan.FromSeconds(1));

        var geloescht = await factory.Services.GetRequiredService<SicherheitsprotokollAufraeumen>().AufraeumenAsync(Abbruch);

        Assert.Equal(1, geloescht);
        var zeilen = await ZeilenAsync(verbindung);
        Assert.Equal([(int)SicherheitsEreignisArt.Loeschung], zeilen.Select(z => z.Art));
        Assert.Equal(TimeSpan.FromDays(365), SicherheitsprotokollFrist.Dauer);
    }

    [Fact]
    public async Task Zeile_genau_an_der_Frist_bleibt()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        await factory.Services.GetRequiredService<ISicherheitsprotokoll>().SchreibenAsync(SicherheitsEreignisArt.Anmeldung, Person, abbruch: Abbruch);
        factory.Uhr.Advance(TimeSpan.FromDays(365));

        Assert.Equal(0, await factory.Services.GetRequiredService<SicherheitsprotokollAufraeumen>().AufraeumenAsync(Abbruch));
    }

    [Fact]
    public async Task Fristdienst_raeumt_beim_Start_und_danach_taeglich_auf()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        var protokoll = factory.Services.GetRequiredService<ISicherheitsprotokoll>();
        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Anmeldung, Person, abbruch: Abbruch);
        factory.Uhr.Advance(TimeSpan.FromDays(366));

        using var dienst = new SicherheitsprotokollFristDienst(
            factory.Services.GetRequiredService<SicherheitsprotokollAufraeumen>(),
            factory.Uhr,
            factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SicherheitsprotokollFristDienst>>());
        await dienst.StartAsync(Abbruch);
        await WartenBisAsync(async () => (await ZeilenAsync(verbindung)).Count == 0);

        // Der erste Lauf ist durch; die neue Zeile verschwindet nur durch einen weiteren, täglichen Lauf.
        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Anmeldung, Person, abbruch: Abbruch);
        Assert.Single(await ZeilenAsync(verbindung));
        factory.Uhr.Advance(TimeSpan.FromDays(366));
        await WartenBisAsync(async () => (await ZeilenAsync(verbindung)).Count == 0);
        await dienst.StopAsync(Abbruch);
    }

    [Fact]
    public async Task Fristdienst_laeuft_nach_einem_Fehler_weiter_und_loggt_nur_den_Typ()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();
        await factory.Services.GetRequiredService<ISicherheitsprotokoll>().SchreibenAsync(SicherheitsEreignisArt.Anmeldung, Person, abbruch: Abbruch);
        factory.Uhr.Advance(TimeSpan.FromDays(366));
        var logger = new FakeLogger<SicherheitsprotokollFristDienst>();

        using var dienst = new SicherheitsprotokollFristDienst(
            new SicherheitsprotokollAufraeumen(new ErstFehlerhafteFabrik(factory.Services.GetRequiredService<IDbContextFactory<BetriebDbContext>>()), factory.Uhr),
            factory.Uhr,
            logger);
        await dienst.StartAsync(Abbruch);
        await WartenBisAsync(() => Task.FromResult(logger.Collector.Count > 0));

        var fehler = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Error, fehler.Level);
        Assert.Contains(nameof(InvalidOperationException), fehler.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Erika", fehler.Message, StringComparison.Ordinal);
        Assert.Null(fehler.Exception);
        Assert.Single(await ZeilenAsync(verbindung));

        // Der nächste tägliche Lauf gelingt wieder.
        factory.Uhr.Advance(TimeSpan.FromDays(1));
        await WartenBisAsync(async () => (await ZeilenAsync(verbindung)).Count == 0);
        Assert.False(dienst.ExecuteTask!.IsCompleted);
        await dienst.StopAsync(Abbruch);
    }

    [Fact]
    public async Task Host_registriert_den_Fristdienst()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync(), mitZustellDienst: true);
        _ = factory.CreateClient();

        Assert.Contains(factory.Services.GetServices<IHostedService>(), d => d is SicherheitsprotokollFristDienst);
    }

    private static async Task WartenBisAsync(Func<Task<bool>> bedingung)
    {
        var bis = DateTime.UtcNow.AddSeconds(10);
        while (!await bedingung())
        {
            Assert.True(DateTime.UtcNow < bis, "Bedingung nicht rechtzeitig erfüllt.");
            await Task.Delay(20, Abbruch);
        }
    }

    /// <summary>Scheitert beim ersten Aufruf mit Personendaten in der Nachricht, danach wie die echte Fabrik.</summary>
    private sealed class ErstFehlerhafteFabrik(IDbContextFactory<BetriebDbContext> echt) : IDbContextFactory<BetriebDbContext>
    {
        private int _aufrufe;

        public BetriebDbContext CreateDbContext() =>
            Interlocked.Increment(ref _aufrufe) == 1 ? throw new InvalidOperationException("Erika Muster") : echt.CreateDbContext();
    }

    private sealed record Zeile(int Art, Guid? PersonId, Guid? OrganisationId, Guid? BezugId, DateTimeOffset Zeitpunkt);

    private static async Task<List<Zeile>> ZeilenAsync(string verbindungsZeichenfolge)
    {
        await using var verbindung = new NpgsqlConnection(verbindungsZeichenfolge);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(
            "SELECT \"Art\", \"PersonId\", \"OrganisationId\", \"BezugId\", \"Zeitpunkt\" FROM betrieb.sicherheitsprotokoll ORDER BY \"Zeitpunkt\"", verbindung);
        await using var leser = await befehl.ExecuteReaderAsync(Abbruch);
        var zeilen = new List<Zeile>();
        while (await leser.ReadAsync(Abbruch))
        {
            zeilen.Add(new Zeile(
                leser.GetInt16(0),
                leser.IsDBNull(1) ? null : leser.GetGuid(1),
                leser.IsDBNull(2) ? null : leser.GetGuid(2),
                leser.IsDBNull(3) ? null : leser.GetGuid(3),
                leser.GetFieldValue<DateTimeOffset>(4)));
        }

        return zeilen;
    }

    private static async Task<List<(string Name, string Typ)>> SpaltenAsync(string verbindungsZeichenfolge)
    {
        await using var verbindung = new NpgsqlConnection(verbindungsZeichenfolge);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'betrieb' AND table_name = 'sicherheitsprotokoll'", verbindung);
        await using var leser = await befehl.ExecuteReaderAsync(Abbruch);
        var spalten = new List<(string, string)>();
        while (await leser.ReadAsync(Abbruch))
        {
            spalten.Add((leser.GetString(0), leser.GetString(1)));
        }

        return spalten;
    }
}
