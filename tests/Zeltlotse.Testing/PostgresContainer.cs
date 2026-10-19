using System.Collections.Concurrent;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Zeltlotse.Testing;

/// <summary>
/// Ein PostgreSQL-18.6-Container (wie im AppHost) je Testassembly (als <c>AssemblyFixture</c> anmelden).
/// Jeder Test, der eine leere Datenbank braucht, legt sich mit <see cref="NeueDatenbankAsync()"/> eine eigene an; wer eine
/// vorbereitete Datenbank braucht (z. B. mit allen Migrationen), kopiert sie mit <see cref="NeueDatenbankAsync(string)"/> aus
/// einer Vorlage (<see cref="VorlageAsync"/>).
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

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _vorlagen = new();

    public string VerbindungsZeichenfolge => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Legt eine neue, leere Datenbank an und liefert deren Verbindungszeichenfolge.</summary>
    public Task<string> NeueDatenbankAsync() => AnlegenAsync(vorlage: null);

    /// <summary>Legt eine neue Datenbank als Kopie der Vorlage <paramref name="vorlage"/> an (Name aus <see cref="VorlageAsync"/>).</summary>
    public Task<string> NeueDatenbankAsync(string vorlage) => AnlegenAsync(vorlage);

    /// <summary>
    /// Liefert den Namen der Vorlage <paramref name="schluessel"/>. Beim ersten Aufruf je Schlüssel legt die Methode eine leere
    /// Datenbank an, lässt <paramref name="befuellen"/> sie über die Verbindungszeichenfolge vorbereiten und sperrt sie danach für
    /// Verbindungen, damit PostgreSQL sie mit <c>CREATE DATABASE … TEMPLATE</c> kopieren kann (das verlangt eine Vorlage ohne
    /// offene Verbindungen). Gleichzeitige Aufrufe warten auf dieselbe Vorlage.
    /// </summary>
    public Task<string> VorlageAsync(string schluessel, Func<string, Task> befuellen) =>
        _vorlagen.GetOrAdd(schluessel, _ => new Lazy<Task<string>>(() => VorlageAnlegenAsync(befuellen))).Value;

    private async Task<string> VorlageAnlegenAsync(Func<string, Task> befuellen)
    {
        var verbindung = await AnlegenAsync(vorlage: null);
        await befuellen(verbindung);
        var name = new NpgsqlConnectionStringBuilder(verbindung).Database!;
        await AusfuehrenAsync($"ALTER DATABASE \"{name}\" WITH ALLOW_CONNECTIONS false");
        await AusfuehrenAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}'");
        return name;
    }

    private async Task<string> AnlegenAsync(string? vorlage)
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await AusfuehrenAsync(vorlage is null ? $"CREATE DATABASE \"{name}\"" : $"CREATE DATABASE \"{name}\" TEMPLATE \"{vorlage}\"");

        // Jede Test-Datenbank hat eigene Pools; ungenutzte Verbindungen schließen nach Sekunden statt nach 5 Minuten, damit
        // viele parallel laufende Tests die Grenze max_connections nicht erreichen.
        return new NpgsqlConnectionStringBuilder(VerbindungsZeichenfolge)
        {
            Database = name,
            ConnectionIdleLifetime = 10,
            ConnectionPruningInterval = 2,
        }.ConnectionString;
    }

    private async Task AusfuehrenAsync(string sql)
    {
        await using var verbindung = new NpgsqlConnection(VerbindungsZeichenfolge);
        await verbindung.OpenAsync();
        await using var befehl = new NpgsqlCommand(sql, verbindung);
        await befehl.ExecuteNonQueryAsync();
    }
}
