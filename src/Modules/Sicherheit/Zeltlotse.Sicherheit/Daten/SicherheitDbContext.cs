using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;
using Kette = Zeltlotse.Sicherheit.Sicherheitskette.Sicherheitskette;

namespace Zeltlotse.Sicherheit.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class SicherheitDbContext(DbContextOptions<SicherheitDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    /// <summary>Story 2-8: die Sicherheitsketten, je Freizeit höchstens eine.</summary>
    public DbSet<Kette> Sicherheitsketten => Set<Kette>();

    protected override string Schema => SicherheitModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => SicherheitModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class SicherheitLeseDbContext(DbContextOptions<SicherheitLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Kette> Sicherheitsketten => Set<Kette>();

    protected override string Schema => SicherheitModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => SicherheitModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class SicherheitModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Kette>(e =>
        {
            e.ToTable("sicherheitskette");
            e.Property(k => k.Id).ValueGeneratedNever();
            e.Property(k => k.WasTun).HasMaxLength(SicherheitsketteFelder.WasTunMaximaleLaenge);
            e.Property(k => k.LeitungTelefon).HasMaxLength(SicherheitsketteFelder.LeitungTelefonMaximaleLaenge);
            e.Property(k => k.HeimatName).HasMaxLength(SicherheitsketteFelder.HeimatNameMaximaleLaenge);
            e.Property(k => k.HeimatTelefon).HasMaxLength(SicherheitsketteFelder.HeimatTelefonMaximaleLaenge);
            e.Property(k => k.Version).IsRowVersion();
            // Je Freizeit höchstens eine Kette; die erste Anlage nutzt diesen Index (ON CONFLICT).
            e.HasIndex(k => k.FreizeitId).IsUnique();
            e.HasIndex(k => k.OrganisationId);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class SicherheitDbContextFabrik : IDesignTimeDbContextFactory<SicherheitDbContext>
{
    public SicherheitDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<SicherheitDbContext>(SicherheitModul.Schema), new MandantenKontext());
}
