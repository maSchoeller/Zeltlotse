using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.Testing;

namespace Zeltlotse.Api.Tests;

public sealed class MigrationsReihenfolgeTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Host_wendet_Migrationen_in_fester_Modulreihenfolge_an()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        _ = factory.CreateClient();

        var reihenfolge = factory.Services.GetRequiredService<ModulMigrationen>().AngewendeteReihenfolge;

        Assert.Equal(
            ["Konten", "Personen", "Organisationen", "Freizeiten", "Inventar", "Sicherheit", "Programm", "Kueche", "Finanzen", "Kanaele"],
            reihenfolge);
    }

    [Fact]
    public void Jedes_Modul_meldet_eigenes_Schema_an()
    {
        var module = ModulMigrationen.Ordnen(
            new ServiceCollection().AddZeltlotseModule().BuildServiceProvider().GetServices<ModulBeschreibung>());

        Assert.Equal(
            ["konten", "personen", "organisationen", "freizeiten", "inventar", "sicherheit", "programm", "kueche", "finanzen", "kanaele"],
            module.Select(m => m.Schema));
    }
}
