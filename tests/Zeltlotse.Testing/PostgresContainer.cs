using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Zeltlotse.Testing;

/// <summary>
/// Ein PostgreSQL-18.6-Container (wie im AppHost) je Testassembly (als <c>AssemblyFixture</c> anmelden).
/// Jeder Test, der eine leere Datenbank braucht, legt sich mit <see cref="NeueDatenbankAsync"/> eine eigene an.
/// </summary>
public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6").Build();

    public string VerbindungsZeichenfolge => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Legt eine neue, leere Datenbank an und liefert deren Verbindungszeichenfolge.</summary>
    public async Task<string> NeueDatenbankAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var verbindung = new NpgsqlConnection(VerbindungsZeichenfolge))
        {
            await verbindung.OpenAsync();
            await using var befehl = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", verbindung);
            await befehl.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(VerbindungsZeichenfolge) { Database = name }.ConnectionString;
    }
}
