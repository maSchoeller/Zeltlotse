using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;

namespace Zeltlotse.ArchitectureTests;

/// <summary>AD-3/AD-24: Jedes DbContext-Modell berührt nur das eigene Schema, auch die Migrations-Historie.</summary>
public sealed class SchemaTests
{
    public static TheoryData<string> ModulNamen => [.. Loesung.Module.Select(m => m.Name)];

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void DbContexts_des_Moduls_bleiben_im_eigenen_Schema(string name)
    {
        var modul = Loesung.Module.Single(m => m.Name == name);
        var assembly = Loesung.AssemblyVon(modul.Projekt)!;
        var registrierung = assembly.GetType($"{modul.Projekt}.{modul.Name}Modul", throwOnError: true)!
            .GetMethod($"Add{modul.Name}Modul", BindingFlags.Public | BindingFlags.Static)!;

        var services = Grunddienste();
        registrierung.Invoke(null, [services]);

        PruefeSchemas(services, assembly, modul.Schema);
    }

    [Fact]
    public void Jedes_Modul_meldet_sich_mit_seinem_Schema_an()
    {
        foreach (var modul in Loesung.Module)
        {
            var assembly = Loesung.AssemblyVon(modul.Projekt)!;
            var services = Grunddienste();
            assembly.GetType($"{modul.Projekt}.{modul.Name}Modul")!
                .GetMethod($"Add{modul.Name}Modul")!
                .Invoke(null, [services]);

            using var dienste = services.BuildServiceProvider();
            var beschreibung = Assert.Single(dienste.GetServices<ModulBeschreibung>());
            Assert.Equal(modul.Name, beschreibung.Name);
            Assert.Equal(modul.Schema, beschreibung.Schema);
            Assert.Equal(assembly, beschreibung.DbContextTyp.Assembly);
        }
    }

    private static ServiceCollection Grunddienste()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:zeltlotse"] = "Host=nirgends;Database=keine" })
            .Build());
        return services;
    }

    private static void PruefeSchemas(ServiceCollection services, Assembly assembly, string schema)
    {
        var kontextTypen = services
            .Select(d => d.ServiceType)
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && t.Assembly == assembly)
            .Distinct()
            .ToList();
        Assert.Equal(2, kontextTypen.Count);

        using var dienste = services.BuildServiceProvider();
        using var scope = dienste.CreateScope();
        foreach (var kontextTyp in kontextTypen)
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(kontextTyp);
            Assert.Empty(DatenschutzRegeln.FremdeSchemas(db, schema));
        }
    }
}
