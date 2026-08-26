using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Server.Integration.Tests;

/// <summary>
/// Eine echte PostgreSQL je Testlauf. Row-Level-Security und Migrationen lassen
/// sich gegen keine In-Memory-Datenbank prüfen — genau dort läge sonst die
/// Lücke, die niemand bemerkt.
/// </summary>
public sealed class DatenbankFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("zeltlotse")
        .Build();

    public string Verbindungszeichenfolge => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var wartung = Kontext(new MandantKontext { Wartung = true });
        await wartung.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Eine frische Datenbank im selben Container. Jeder Abnahmetest startet
    /// damit auf leerem Grund — nötig schon deshalb, weil die Einrichtungsseite
    /// sich nach dem ersten GlobalAdmin dauerhaft abschaltet.
    /// </summary>
    public async Task<string> NeueDatenbankAsync()
    {
        var name = $"zl_{Guid.NewGuid():N}";

        await using (var verbindung = new NpgsqlConnection(Verbindungszeichenfolge))
        {
            await verbindung.OpenAsync();

            await using var befehl = verbindung.CreateCommand();
            befehl.CommandText = $"CREATE DATABASE {name}";
            await befehl.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(Verbindungszeichenfolge)
        {
            Database = name,
        }.ConnectionString;
    }

    /// <summary>Kontext mit dem Mandantenkontext, den der Test vorgibt.</summary>
    public ZeltlotseDbContext Kontext(MandantKontext mandant, string? verbindung = null)
    {
        var optionen = new DbContextOptionsBuilder<ZeltlotseDbContext>()
            .UseNpgsql(verbindung ?? Verbindungszeichenfolge)
            .AddInterceptors(new MandantInterceptor(mandant))
            .Options;

        return new ZeltlotseDbContext(optionen, mandant);
    }
}

[CollectionDefinition(nameof(DatenbankSammlung))]
public sealed class DatenbankSammlung : ICollectionFixture<DatenbankFixture>;
