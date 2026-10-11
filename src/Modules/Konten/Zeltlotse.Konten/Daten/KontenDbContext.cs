using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Konten.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class KontenDbContext(DbContextOptions<KontenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => KontenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KontenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class KontenLeseDbContext(DbContextOptions<KontenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => KontenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KontenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class KontenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class KontenDbContextFabrik : IDesignTimeDbContextFactory<KontenDbContext>
{
    public KontenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<KontenDbContext>(KontenModul.Schema), new MandantenKontext());
}
