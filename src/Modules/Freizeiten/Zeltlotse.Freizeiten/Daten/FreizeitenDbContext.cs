using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using FreizeitseiteFelder = Zeltlotse.Api.Contracts.Freizeiten.FreizeitseiteFelder;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
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

    public DbSet<Anmeldelink> Anmeldelinks => Set<Anmeldelink>();

    public DbSet<Anmeldung> Anmeldungen => Set<Anmeldung>();

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

    public DbSet<Anmeldelink> Anmeldelinks => Set<Anmeldelink>();

    public DbSet<Anmeldung> Anmeldungen => Set<Anmeldung>();

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
            e.Property(f => f.Beschreibung).HasMaxLength(FreizeitseiteFelder.BeschreibungMaximaleLaenge);
            e.Property(f => f.Ort).HasMaxLength(FreizeitseiteFelder.OrtMaximaleLaenge);
            e.Property(f => f.Teilnahmebedingungen).HasMaxLength(FreizeitseiteFelder.TeilnahmebedingungenMaximaleLaenge);
            e.Property(f => f.KontaktMail).HasMaxLength(FreizeitseiteFelder.KontaktMailMaximaleLaenge);
            e.Property(f => f.KontaktTelefon).HasMaxLength(FreizeitseiteFelder.KontaktTelefonMaximaleLaenge);
            // FR-22: gewählte Fragen-Kennungen je Zielgruppe; eine neue Freizeit fragt nichts ab.
            e.Property(f => f.FragenTeilnehmende).HasColumnType("text[]");
            e.Property(f => f.FragenMitarbeitende).HasColumnType("text[]");
            e.Property(f => f.Version).IsRowVersion();
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

        modelBuilder.Entity<Anmeldelink>(e =>
        {
            e.ToTable("anmeldelink");
            e.Property(l => l.Id).ValueGeneratedNever();
            e.Property(l => l.Art).HasConversion<short>();
            e.HasIndex(l => new { l.FreizeitId, l.Art }).IsUnique();
            e.Property(l => l.TokenHash).HasColumnType("bytea");
            e.HasIndex(l => l.TokenHash).IsUnique();
            e.Property(l => l.TokenGeschuetzt).HasColumnType("bytea");
            e.HasOne<Freizeit>().WithMany().HasForeignKey(l => l.FreizeitId).OnDelete(DeleteBehavior.Cascade);
        });

        // FR-16: je Freizeit und Person höchstens eine Anmeldung; der Platzhalter gelöschter Personen ist ausgenommen, damit zwei
        // gelöschte Personen derselben Freizeit nicht kollidieren (AD-16).
        modelBuilder.Entity<Anmeldung>(e =>
        {
            e.ToTable("anmeldung");
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.Art).HasConversion<short>();
            e.Property(a => a.Status).HasConversion<short>();
            e.HasIndex(a => new { a.FreizeitId, a.PersonId })
                .IsUnique()
                .HasFilter($"\"PersonId\" <> '{GeloeschtePerson.Id}'")
                .HasDatabaseName("IX_anmeldung_je_person");
            e.HasIndex(a => a.PersonId);
            e.HasIndex(a => a.OrganisationId);
            e.HasOne<Freizeit>().WithMany().HasForeignKey(a => a.FreizeitId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class FreizeitenDbContextFabrik : IDesignTimeDbContextFactory<FreizeitenDbContext>
{
    public FreizeitenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<FreizeitenDbContext>(FreizeitenModul.Schema), new MandantenKontext());
}
