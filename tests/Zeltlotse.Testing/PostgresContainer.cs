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
    // Jede Test-Factory öffnet eigene Verbindungs-Pools; parallel laufende Testklassen brauchen mehr als die Vorgabe 100.
    // Docker gibt Containern nur 64 MB Shared Memory; bei vielen parallelen Abfragen scheitert PostgreSQL sonst mit
    // „could not resize shared memory segment … No space left on device“.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6")
        .WithCommand("-c", "max_connections=500")
        .WithCreateParameterModifier(parameter => parameter.HostConfig!.ShmSize = 1024L * 1024 * 1024)
        .Build();

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

        // Jede Test-Datenbank hat eigene Pools; ungenutzte Verbindungen schließen nach Sekunden statt nach 5 Minuten, damit
        // viele parallel laufende Tests die Grenze max_connections nicht erreichen.
        return new NpgsqlConnectionStringBuilder(VerbindungsZeichenfolge)
        {
            Database = name,
            ConnectionIdleLifetime = 10,
            ConnectionPruningInterval = 2,
        }.ConnectionString;
    }
}
