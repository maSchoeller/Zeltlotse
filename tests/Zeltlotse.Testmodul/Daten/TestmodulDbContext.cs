using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testmodul.Notizen;

namespace Zeltlotse.Testmodul.Daten;

internal sealed class TestmodulDbContext(DbContextOptions<TestmodulDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    public DbSet<Notiz> Notizen => Set<Notiz>();

    public DbSet<Empfang.Empfang> Empfaenge => Set<Empfang.Empfang>();

    protected override string Schema => TestmodulModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => TestmodulModell.Konfigurieren(modelBuilder);
}

internal sealed class TestmodulLeseDbContext(DbContextOptions<TestmodulLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Notiz> Notizen => Set<Notiz>();

    protected override string Schema => TestmodulModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => TestmodulModell.Konfigurieren(modelBuilder);
}

internal static class TestmodulModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notiz>(e =>
        {
            e.ToTable("notiz");
            e.Property(n => n.Text).HasMaxLength(200);
        });
        modelBuilder.Entity<Empfang.Empfang>(e =>
        {
            e.ToTable("empfang");
            e.Property(x => x.Empfaenger).HasMaxLength(100);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c>.</summary>
internal sealed class TestmodulDbContextFabrik : IDesignTimeDbContextFactory<TestmodulDbContext>
{
    public TestmodulDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<TestmodulDbContext>(TestmodulModul.Schema), new MandantenKontext());
}
