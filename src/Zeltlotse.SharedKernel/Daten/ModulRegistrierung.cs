using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Zeltlotse.SharedKernel.Daten;

public static class ModulRegistrierung
{
    /// <summary>Name der Verbindungszeichenfolge der gemeinsamen PostgreSQL-Datenbank.</summary>
    public const string Verbindungsname = "zeltlotse";

    /// <summary>Name der Historientabelle der Migrationen; sie liegt im Schema des Moduls.</summary>
    public const string Historientabelle = "__EFMigrationsHistory";

    /// <summary>
    /// Registriert schreibenden und lesenden DbContext eines Moduls mit eigenem Schema und eigener Migrations-Historie,
    /// die Validatoren der Modul-Assembly und die Modul-Beschreibung.
    /// </summary>
    public static IServiceCollection AddModul<TSchreib, TLese>(this IServiceCollection services, string name, string schema)
        where TSchreib : ModulDbContext
        where TLese : ModulLeseDbContext
    {
        services.AddSharedKernel();
        services.AddDbContext<TSchreib>((sp, optionen) =>
            Konfigurieren(optionen, Verbindung(sp), schema));
        services.AddDbContext<TLese>((sp, optionen) =>
            Konfigurieren(optionen, Verbindung(sp), schema));
        services.AddValidatorsFromAssembly(typeof(TSchreib).Assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        services.AddSingleton(new ModulBeschreibung(name, schema, typeof(TSchreib)));
        return services;
    }

    /// <summary>Gemeinsame Npgsql-Einstellungen; auch für die Design-Zeit-Fabriken der Migrationen.</summary>
    public static DbContextOptionsBuilder Konfigurieren(DbContextOptionsBuilder optionen, string verbindung, string schema) =>
        optionen.UseNpgsql(verbindung, npgsql => npgsql.MigrationsHistoryTable(Historientabelle, schema));

    /// <summary>Optionen für <c>dotnet ef</c> (Design-Zeit); verbindet sich beim Erzeugen von Migrationen nicht.</summary>
    public static DbContextOptions<TKontext> DesignZeitOptionen<TKontext>(string schema)
        where TKontext : DbContext =>
        (DbContextOptions<TKontext>)Konfigurieren(new DbContextOptionsBuilder<TKontext>(), "Host=localhost;Database=zeltlotse", schema).Options;

    private static string Verbindung(IServiceProvider sp) =>
        sp.GetRequiredService<IConfiguration>().GetConnectionString(Verbindungsname)
        ?? throw new InvalidOperationException($"Verbindungszeichenfolge '{Verbindungsname}' fehlt.");
}
