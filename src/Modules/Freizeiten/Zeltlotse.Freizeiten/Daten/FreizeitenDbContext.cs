using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class FreizeitenDbContext(DbContextOptions<FreizeitenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    public DbSet<Freizeit> Freizeiten => Set<Freizeit>();

    public DbSet<FreizeitRolle> Rollen => Set<FreizeitRolle>();

    public DbSet<LeitungsEinladung> Einladungen => Set<LeitungsEinladung>();

    protected override string Schema => FreizeitenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => FreizeitenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class FreizeitenLeseDbContext(DbContextOptions<FreizeitenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Freizeit> Freizeiten => Set<Freizeit>();

    public DbSet<FreizeitRolle> Rollen => Set<FreizeitRolle>();

    public DbSet<LeitungsEinladung> Einladungen => Set<LeitungsEinladung>();

    protected override string Schema => FreizeitenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => FreizeitenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class FreizeitenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Freizeit>(e =>
        {
            e.ToTable("freizeit");
            e.Property(f => f.Id).ValueGeneratedNever();
            e.Property(f => f.Name).HasMaxLength(Freizeit.MaximaleNamensLaenge);
            e.HasIndex(f => f.OrganisationId);
            e.HasMany(f => f.Rollen).WithOne().HasForeignKey(r => r.FreizeitId);
        });

        modelBuilder.Entity<FreizeitRolle>(e =>
        {
            e.ToTable("freizeit_rolle");
            e.HasKey(r => new { r.FreizeitId, r.PersonId });
            e.Property(r => r.Rolle).HasConversion<short>();
            e.HasIndex(r => r.PersonId);
        });

        modelBuilder.Entity<LeitungsEinladung>(e =>
        {
            e.ToTable("leitungs_einladung");
            e.Property(i => i.Id).ValueGeneratedNever();
            e.Property(i => i.Mail).HasMaxLength(MailAdresse.MaximaleLaenge);
            e.HasIndex(i => new { i.FreizeitId, i.Mail });
            e.Property(i => i.TokenHash).HasColumnType("bytea");
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.Property(i => i.Status).HasConversion<short>();
            e.Property(i => i.Version).IsRowVersion();
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class FreizeitenDbContextFabrik : IDesignTimeDbContextFactory<FreizeitenDbContext>
{
    public FreizeitenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<FreizeitenDbContext>(FreizeitenModul.Schema), new MandantenKontext());
}
