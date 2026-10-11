using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Api.Betrieb;

/// <summary>
/// DbContext des API-Hosts im Schema <c>betrieb</c> (AD-22) mit eigener Migrations-Historie. Kein Modul:
/// Module erreichen das Schema nur über <see cref="ISicherheitsprotokoll"/>.
/// </summary>
internal sealed class BetriebDbContext(DbContextOptions<BetriebDbContext> optionen) : DbContext(optionen)
{
    public const string Schema = "betrieb";

    public DbSet<SicherheitsprotokollEintrag> Sicherheitsprotokoll => Set<SicherheitsprotokollEintrag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SicherheitsprotokollEintrag>(e =>
        {
            e.ToTable("sicherheitsprotokoll");
            e.HasKey(z => z.Id);
            e.Property(z => z.Id).ValueGeneratedNever();
            e.Property(z => z.Art).HasConversion<short>();
            e.HasIndex(z => z.Zeitpunkt);
        });
    }

    /// <summary>Gleiche Npgsql-Einstellungen wie die Module, Historie im Schema <c>betrieb</c>.</summary>
    public static DbContextOptionsBuilder Konfigurieren(DbContextOptionsBuilder optionen, string verbindung) =>
        ModulRegistrierung.Konfigurieren(optionen, verbindung, Schema);
}

/// <summary>Eine Zeile des Sicherheitsprotokolls: nur Kennungen, kein Freitext, keine IP.</summary>
internal sealed class SicherheitsprotokollEintrag
{
    public required Guid Id { get; init; }

    public required DateTimeOffset Zeitpunkt { get; init; }

    public required SicherheitsEreignisArt Art { get; init; }

    public Guid? PersonId { get; init; }

    public Guid? OrganisationId { get; init; }

    public Guid? BezugId { get; init; }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit) und die Schema-Prüfung der Architekturtests.</summary>
internal sealed class BetriebDbContextFabrik : IDesignTimeDbContextFactory<BetriebDbContext>
{
    public BetriebDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<BetriebDbContext>)BetriebDbContext
            .Konfigurieren(new DbContextOptionsBuilder<BetriebDbContext>(), "Host=localhost;Database=zeltlotse").Options);
}
