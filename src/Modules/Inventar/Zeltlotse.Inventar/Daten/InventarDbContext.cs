using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Inventar.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class InventarDbContext(DbContextOptions<InventarDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => InventarModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => InventarModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class InventarLeseDbContext(DbContextOptions<InventarLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => InventarModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => InventarModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class InventarModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class InventarDbContextFabrik : IDesignTimeDbContextFactory<InventarDbContext>
{
    public InventarDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<InventarDbContext>(InventarModul.Schema), new MandantenKontext());
}
