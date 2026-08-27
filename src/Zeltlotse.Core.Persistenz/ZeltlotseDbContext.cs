using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Zeltlotse.Core.Persistenz;

/// <summary>
/// Der Mandantenkontext einer Anfrage. Zwei verschiedene Dinge, bewusst
/// getrennt:
/// <list type="bullet">
/// <item><see cref="AktuellerMandant"/> — die Organisation aus der Adresse
/// /o/{slug}. Steuert den EF-Query-Filter.</item>
/// <item><see cref="SichtbareOrganisationen"/> — alle Organisationen, denen
/// der angemeldete Nutzer angehört. Steuert die Row-Level-Security in
/// PostgreSQL.</item>
/// </list>
/// Ein vergessener Filter im Anwendungscode wird dadurch von der Datenbank
/// aufgefangen und umgekehrt.
/// </summary>
public interface IMandantKontext
{
    Guid? AktuellerMandant { get; }

    IReadOnlyCollection<Guid> SichtbareOrganisationen { get; }

    /// <summary>Migrationen und Aufräumdienst arbeiten ohne Mandantenschranke.</summary>
    bool Wartung { get; }

    /// <summary>
    /// Der Betreiber gehört zu keiner Organisation, muss aber Einladungen für
    /// jede schreiben können. Nur die Einladungstabelle kennt dieses Recht —
    /// Freizeiten und Zuordnungen bleiben ihm auch in der Datenbank verwehrt.
    /// </summary>
    bool IstBetreiber { get; }
}

public sealed class MandantKontext : IMandantKontext
{
    public Guid? AktuellerMandant { get; set; }

    public IReadOnlyCollection<Guid> SichtbareOrganisationen { get; set; } = [];

    public bool Wartung { get; set; }

    public bool IstBetreiber { get; set; }
}

public sealed class ZeltlotseDbContext(
    DbContextOptions<ZeltlotseDbContext> optionen,
    IMandantKontext mandant)
    : IdentityDbContext<Nutzer, IdentityRole<Guid>, Guid>(optionen)
{
    private readonly IMandantKontext _mandant = mandant;

    public DbSet<Organisation> Organisationen => Set<Organisation>();

    public DbSet<OrgMitgliedschaft> OrgMitgliedschaften => Set<OrgMitgliedschaft>();

    public DbSet<Freizeit> Freizeiten => Set<Freizeit>();

    public DbSet<FreizeitZuordnung> FreizeitZuordnungen => Set<FreizeitZuordnung>();

    public DbSet<Einladung> Einladungen => Set<Einladung>();

    public DbSet<Erneuerungstoken> Erneuerungstoken => Set<Erneuerungstoken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Nutzer>(e =>
        {
            e.ToTable("nutzer");
            e.HasIndex(n => n.NormalizedEmail).IsUnique();
            e.Property(n => n.Name).HasMaxLength(120);
        });

        builder.Entity<Organisation>(e =>
        {
            e.ToTable("organisation");
            e.HasIndex(o => o.Slug).IsUnique();
            e.Property(o => o.Name).HasMaxLength(200);
            e.Property(o => o.Slug).HasMaxLength(60);
        });

        builder.Entity<OrgMitgliedschaft>(e =>
        {
            e.ToTable("org_mitgliedschaft");
            e.HasKey(m => new { m.NutzerId, m.OrganisationId });
            e.HasOne(m => m.Nutzer).WithMany().HasForeignKey(m => m.NutzerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Organisation).WithMany(o => o.Mitgliedschaften)
                .HasForeignKey(m => m.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Freizeit>(e =>
        {
            e.ToTable("freizeit");
            e.Property(f => f.Name).HasMaxLength(120);
            e.Property(f => f.Ort).HasMaxLength(200);
            e.HasIndex(f => f.TenantId);
            e.HasOne(f => f.Organisation).WithMany(o => o.Freizeiten)
                .HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(f => _mandant.AktuellerMandant == null
                || f.TenantId == _mandant.AktuellerMandant);
        });

        builder.Entity<FreizeitZuordnung>(e =>
        {
            e.ToTable("freizeit_zuordnung");
            e.HasKey(z => new { z.NutzerId, z.FreizeitId });
            e.HasIndex(z => z.TenantId);
            e.HasOne(z => z.Nutzer).WithMany().HasForeignKey(z => z.NutzerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(z => z.Freizeit).WithMany(f => f.Zuordnungen)
                .HasForeignKey(z => z.FreizeitId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(z => _mandant.AktuellerMandant == null
                || z.TenantId == _mandant.AktuellerMandant);
        });

        builder.Entity<Einladung>(e =>
        {
            e.ToTable("einladung");
            e.Property(i => i.Name).HasMaxLength(120);
            e.Property(i => i.EMail).HasMaxLength(256);
            e.Property(i => i.TokenHash).HasMaxLength(64);
            e.HasIndex(i => i.TokenHash);
            e.HasIndex(i => i.TenantId);
            e.HasOne(i => i.Organisation).WithMany()
                .HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(i => _mandant.AktuellerMandant == null
                || i.TenantId == _mandant.AktuellerMandant);
        });

        builder.Entity<Erneuerungstoken>(e =>
        {
            e.ToTable("erneuerungstoken");
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.Nutzer).WithMany().HasForeignKey(t => t.NutzerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Nur nicht gelöschte Zeilen. Der Papierkorb lässt sie bewusst weg.</summary>
public static class AbfrageErweiterungen
{
    public static IQueryable<Freizeit> Aktiv(this IQueryable<Freizeit> abfrage)
        => abfrage.Where(f => f.GeloeschtAm == null);

    public static IQueryable<Organisation> Aktiv(this IQueryable<Organisation> abfrage)
        => abfrage.Where(o => o.GeloeschtAm == null);
}
