using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Organisationen.Datenschutz;
using Zeltlotse.Organisationen.Domaene;
using Zeltlotse.Organisationen.Nachweise;
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

    public DbSet<NachweisRichtlinie> Richtlinien => Set<NachweisRichtlinie>();

    public DbSet<NachweisBestaetigung> Bestaetigungen => Set<NachweisBestaetigung>();

    public DbSet<Loeschfristen> Loeschfristen => Set<Loeschfristen>();

    public DbSet<Mitgliedschaft> Mitgliedschaften => Set<Mitgliedschaft>();

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

    public DbSet<NachweisRichtlinie> Richtlinien => Set<NachweisRichtlinie>();

    public DbSet<NachweisBestaetigung> Bestaetigungen => Set<NachweisBestaetigung>();

    public DbSet<Loeschfristen> Loeschfristen => Set<Loeschfristen>();

    public DbSet<Mitgliedschaft> Mitgliedschaften => Set<Mitgliedschaft>();

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

        // FR-9, NFR-5: je Organisation und Art eine Zeile mit Pflicht und Gültigkeit.
        modelBuilder.Entity<NachweisRichtlinie>(e =>
        {
            e.ToTable("nachweis_richtlinie");
            e.HasKey(r => new { r.OrganisationId, r.Art });
            e.Property(r => r.Art).HasConversion<short>();
            e.Property(r => r.Version).IsRowVersion();
        });

        // AD-9: nur die Bestätigung, je Organisation, Person und Art eine Zeile.
        modelBuilder.Entity<NachweisBestaetigung>(e =>
        {
            e.ToTable("nachweis_bestaetigung");
            e.HasKey(b => new { b.OrganisationId, b.PersonId, b.Art });
            e.Property(b => b.Art).HasConversion<short>();
            e.HasIndex(b => b.PersonId);
        });

        // NFR-5, AD-16: Löschfristen, eine Zeile je Organisation.
        modelBuilder.Entity<Loeschfristen>(e =>
        {
            e.ToTable("loeschfristen");
            e.HasKey(f => f.OrganisationId);
            e.Property(f => f.OrganisationId).ValueGeneratedNever();
            e.Property(f => f.Version).IsRowVersion();
        });

        // FR-7, Story 2-3: Mitarbeitende der Organisation, je Organisation und Person eine Zeile.
        modelBuilder.Entity<Mitgliedschaft>(e =>
        {
            e.ToTable("mitgliedschaft");
            e.HasKey(m => new { m.OrganisationId, m.PersonId });
            e.HasIndex(m => m.PersonId);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class OrganisationenDbContextFabrik : IDesignTimeDbContextFactory<OrganisationenDbContext>
{
    public OrganisationenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<OrganisationenDbContext>(OrganisationenModul.Schema), new MandantenKontext());
}
