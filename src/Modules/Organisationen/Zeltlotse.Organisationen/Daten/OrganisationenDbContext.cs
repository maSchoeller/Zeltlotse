using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Organisationen.Domaene;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Organisationen.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class OrganisationenDbContext(DbContextOptions<OrganisationenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    public DbSet<Organisation> Organisationen => Set<Organisation>();

    public DbSet<OrgRolle> Rollen => Set<OrgRolle>();

    public DbSet<OrgEinladung> Einladungen => Set<OrgEinladung>();

    protected override string Schema => OrganisationenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => OrganisationenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class OrganisationenLeseDbContext(DbContextOptions<OrganisationenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Organisation> Organisationen => Set<Organisation>();

    public DbSet<OrgRolle> Rollen => Set<OrgRolle>();

    public DbSet<OrgEinladung> Einladungen => Set<OrgEinladung>();

    protected override string Schema => OrganisationenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => OrganisationenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class OrganisationenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organisation>(e =>
        {
            e.ToTable("organisation");
            e.Property(o => o.Id).ValueGeneratedNever();
            e.Property(o => o.Name).HasMaxLength(Organisation.MaximaleNamensLaenge);
        });

        modelBuilder.Entity<OrgRolle>(e =>
        {
            e.ToTable("org_rolle");
            e.HasKey(r => new { r.OrganisationId, r.PersonId, r.Rolle });
            e.Property(r => r.Rolle).HasConversion<short>();
            e.HasIndex(r => r.PersonId);
        });

        modelBuilder.Entity<OrgEinladung>(e =>
        {
            e.ToTable("org_einladung");
            e.Property(i => i.Id).ValueGeneratedNever();
            e.Property(i => i.Mail).HasMaxLength(MailAdresse.MaximaleLaenge);
            e.HasIndex(i => new { i.OrganisationId, i.Mail });
            e.Property(i => i.Rolle).HasConversion<short>();
            e.Property(i => i.TokenHash).HasColumnType("bytea");
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.Property(i => i.Status).HasConversion<short>();
            e.Property(i => i.Version).IsRowVersion();
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class OrganisationenDbContextFabrik : IDesignTimeDbContextFactory<OrganisationenDbContext>
{
    public OrganisationenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<OrganisationenDbContext>(OrganisationenModul.Schema), new MandantenKontext());
}
