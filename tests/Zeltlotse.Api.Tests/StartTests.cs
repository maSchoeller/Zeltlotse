using System.Net;
using Npgsql;
using Zeltlotse.Testing;

namespace Zeltlotse.Api.Tests;

/// <summary>Matrix-Zeilen 1 und 2: Start gegen leere Datenbank und zweiter Start.</summary>
public sealed class StartTests(PostgresContainer postgres)
{
    private static readonly string[] Schemas =
        ["konten", "personen", "organisationen", "freizeiten", "sicherheit", "programm", "inventar", "kueche", "finanzen", "kanaele"];

    private static readonly string[] TabellenJeSchema =
        ["outbox", "outbox_zustellung", "inbox", "inbox_stand", "__EFMigrationsHistory"];

    [Fact]
    public async Task Start_gegen_leere_Datenbank_legt_alle_Schemas_an_und_health_antwortet_200()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        using var client = factory.CreateClient();

        var antwort = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var tabellen = await TabellenAsync(verbindung);
        foreach (var schema in Schemas)
        {
            foreach (var tabelle in TabellenJeSchema)
            {
                Assert.Contains($"{schema}.{tabelle}", tabellen);
            }
        }

        // Schema betrieb des API-Hosts (AD-22): Sicherheitsprotokoll mit eigener Migrations-Historie.
        Assert.Contains("betrieb.sicherheitsprotokoll", tabellen);
        Assert.Contains("betrieb.__EFMigrationsHistory", tabellen);
        Assert.Contains("personen.personen_schluessel", tabellen);
    }

    [Fact]
    public async Task Alive_antwortet_200()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();

        var antwort = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    [Fact]
    public async Task Zweiter_Start_laeuft_ohne_Fehler_und_aendert_nichts()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using (var erster = new ZeltlotseApiFactory(verbindung))
        {
            using var client = erster.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);
        }

        var vorher = await MigrationsStandAsync(verbindung);

        await using var zweiter = new ZeltlotseApiFactory(verbindung);
        using var zweiterClient = zweiter.CreateClient();
        var antwort = await zweiterClient.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(vorher, await MigrationsStandAsync(verbindung));
    }

    private static async Task<HashSet<string>> TabellenAsync(string verbindungsZeichenfolge)
    {
        await using var verbindung = new NpgsqlConnection(verbindungsZeichenfolge);
        await verbindung.OpenAsync();
        await using var befehl = new NpgsqlCommand(
            "SELECT table_schema || '.' || table_name FROM information_schema.tables", verbindung);
        await using var leser = await befehl.ExecuteReaderAsync();
        var ergebnis = new HashSet<string>();
        while (await leser.ReadAsync())
        {
            ergebnis.Add(leser.GetString(0));
        }

        return ergebnis;
    }

    private static async Task<string> MigrationsStandAsync(string verbindungsZeichenfolge)
    {
        var teile = new List<string>();
        await using var verbindung = new NpgsqlConnection(verbindungsZeichenfolge);
        await verbindung.OpenAsync();
        foreach (var schema in Schemas.Append("betrieb"))
        {
            await using var befehl = new NpgsqlCommand(
                $"SELECT string_agg(\"MigrationId\", ',' ORDER BY \"MigrationId\") FROM \"{schema}\".\"__EFMigrationsHistory\"", verbindung);
            teile.Add($"{schema}:{await befehl.ExecuteScalarAsync()}");
        }

        return string.Join(";", teile);
    }
}
