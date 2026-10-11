using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Konten.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class KontenDbContext(DbContextOptions<KontenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    public DbSet<Konto> Konten => Set<Konto>();

    public DbSet<LoginAnfrage> LoginAnfragen => Set<LoginAnfrage>();

    protected override string Schema => KontenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KontenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class KontenLeseDbContext(DbContextOptions<KontenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Konto> Konten => Set<Konto>();

    public DbSet<LoginAnfrage> LoginAnfragen => Set<LoginAnfrage>();

    protected override string Schema => KontenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KontenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class KontenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Konto>(e =>
        {
            e.ToTable("konto");
            e.Property(k => k.Id).ValueGeneratedNever();
            e.Property(k => k.Mail).HasMaxLength(MailAdresse.MaximaleLaenge);
            e.HasIndex(k => k.Mail).IsUnique();
            e.HasIndex(k => k.PersonId).IsUnique();
            e.Property(k => k.PasswortHash).HasMaxLength(500);
        });

        modelBuilder.Entity<LoginAnfrage>(e =>
        {
            e.ToTable("login_anfrage");
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.Mail).HasMaxLength(MailAdresse.MaximaleLaenge);
            e.HasIndex(a => a.Mail);
            e.Property(a => a.TokenHash).HasColumnType("bytea");
            e.HasIndex(a => a.TokenHash).IsUnique();
            e.Property(a => a.CodeHash).HasColumnType("bytea");
            e.Property(a => a.Status).HasConversion<short>();
            e.Property(a => a.Version).IsRowVersion();
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class KontenDbContextFabrik : IDesignTimeDbContextFactory<KontenDbContext>
{
    public KontenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<KontenDbContext>(KontenModul.Schema), new MandantenKontext());
}
