namespace Zeltlotse.Testing;

/// <summary>Test-Datenbanken mit allen Migrationen des API-Hosts.</summary>
public static class MigrierteDatenbank
{
    private const string Vorlage = "api-migriert";

    /// <summary>
    /// Legt eine neue Datenbank mit allen Migrationen an: eine Kopie der Vorlage, die ein einmaliger Start des API-Hosts je
    /// Testlauf migriert hat. Der Host einer Test-Factory findet beim Start nur noch angewendete Migrationen vor; das spart je
    /// Test die Migrationen auf einer leeren Datenbank. Tests der Migrationen selbst nehmen <see cref="PostgresContainer.NeueDatenbankAsync()"/>.
    /// </summary>
    public static async Task<string> NeueMigrierteDatenbankAsync(this PostgresContainer postgres) =>
        await postgres.NeueDatenbankAsync(await postgres.VorlageAsync(Vorlage, MigrierenAsync));

    private static async Task MigrierenAsync(string verbindung)
    {
        await using var factory = new ZeltlotseApiFactory(verbindung);
        factory.CreateClient().Dispose();
    }
}
