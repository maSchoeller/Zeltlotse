using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Server.Integration.Tests;

/// <summary>
/// Ein echtes SQL Server je Testlauf. Row-Level-Security und Migrationen lassen
/// sich gegen keine In-Memory-Datenbank prüfen — genau dort läge sonst die
/// Lücke, die niemand bemerkt.
/// </summary>
public sealed class DatenbankFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

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

        await using (var verbindung = new SqlConnection(Verbindungszeichenfolge))
        {
            await verbindung.OpenAsync();

            await using var befehl = verbindung.CreateCommand();
            befehl.CommandText = $"CREATE DATABASE [{name}]";
            await befehl.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(Verbindungszeichenfolge)
        {
            InitialCatalog = name,
        }.ConnectionString;
    }

    /// <summary>Kontext mit dem Mandantenkontext, den der Test vorgibt.</summary>
    public ZeltlotseDbContext Kontext(MandantKontext mandant, string? verbindung = null)
    {
        var optionen = new DbContextOptionsBuilder<ZeltlotseDbContext>()
            .UseSqlServer(verbindung ?? Verbindungszeichenfolge)
            .AddInterceptors(new MandantInterceptor(mandant))
            .Options;

        return new ZeltlotseDbContext(optionen, mandant);
    }
}

[CollectionDefinition(nameof(DatenbankSammlung))]
public sealed class DatenbankSammlung : ICollectionFixture<DatenbankFixture>;
