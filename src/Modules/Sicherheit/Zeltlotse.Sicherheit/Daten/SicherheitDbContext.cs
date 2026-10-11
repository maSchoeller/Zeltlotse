using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Sicherheit.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class SicherheitDbContext(DbContextOptions<SicherheitDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => SicherheitModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => SicherheitModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class SicherheitLeseDbContext(DbContextOptions<SicherheitLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => SicherheitModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => SicherheitModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class SicherheitModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class SicherheitDbContextFabrik : IDesignTimeDbContextFactory<SicherheitDbContext>
{
    public SicherheitDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<SicherheitDbContext>(SicherheitModul.Schema), new MandantenKontext());
}
