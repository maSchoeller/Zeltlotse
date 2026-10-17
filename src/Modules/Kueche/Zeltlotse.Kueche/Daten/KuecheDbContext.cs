using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;
using Rezept = Zeltlotse.Kueche.Rezepte.Rezept;
using Zutat = Zeltlotse.Kueche.Rezepte.Zutat;

namespace Zeltlotse.Kueche.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class KuecheDbContext(DbContextOptions<KuecheDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    /// <summary>Story 4-1: die Rezepte aller Freizeiten.</summary>
    public DbSet<Rezept> Rezepte => Set<Rezept>();

    /// <summary>Story 4-1: die Zutaten der Rezepte.</summary>
    public DbSet<Zutat> Zutaten => Set<Zutat>();

    protected override string Schema => KuecheModul.Schema;

    /// <summary>
    /// Story 4-1: Lädt ein Rezept der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): Änderungen, Löschen und neue Zutaten desselben
    /// Rezepts laufen nacheinander. <c>null</c>, wenn es das Rezept in dieser Freizeit nicht gibt.
    /// </summary>
    public async Task<Rezept?> RezeptSperrenAsync(Guid freizeitId, Guid rezeptId, CancellationToken abbruch) =>
        (await Rezepte
            .FromSql($"SELECT *, xmin FROM kueche.rezept WHERE \"Id\" = {rezeptId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Story 4-1: Lädt eine Zutat des Rezepts der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE). <c>null</c>, wenn es sie in diesem
    /// Rezept dieser Freizeit nicht (mehr) gibt.
    /// </summary>
    public async Task<Zutat?> ZutatSperrenAsync(Guid freizeitId, Guid rezeptId, Guid zutatId, CancellationToken abbruch) =>
        (await Zutaten
            .FromSql($"SELECT *, xmin FROM kueche.zutat WHERE \"Id\" = {zutatId} AND \"RezeptId\" = {rezeptId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KuecheModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class KuecheLeseDbContext(DbContextOptions<KuecheLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Rezept> Rezepte => Set<Rezept>();

    public DbSet<Zutat> Zutaten => Set<Zutat>();

    protected override string Schema => KuecheModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KuecheModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class KuecheModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        // Story 4-1, FR-46: Rezepte je Freizeit (Arbeitsdaten der Freizeit, AD-16, ohne Personenbezug).
        modelBuilder.Entity<Rezept>(e =>
        {
            e.ToTable("rezept");
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Name).HasMaxLength(RezeptFelder.NameMaximaleLaenge);
            e.Property(r => r.Zubereitung).HasMaxLength(RezeptFelder.ZubereitungMaximaleLaenge);
            e.Property(r => r.Version).IsRowVersion();
            e.HasIndex(r => r.FreizeitId);
            e.HasIndex(r => r.OrganisationId);
        });

        // Story 4-1: Zutaten gehören zu genau einem Rezept und verschwinden mit ihm (Fremdschlüssel im eigenen Schema, AD-3).
        modelBuilder.Entity<Zutat>(e =>
        {
            e.ToTable("zutat");
            e.Property(z => z.Id).ValueGeneratedNever();
            e.Property(z => z.Bezeichnung).HasMaxLength(ZutatFelder.BezeichnungMaximaleLaenge);
            e.Property(z => z.Menge).HasPrecision(10, ZutatFelder.MengeNachkommastellen);
            e.Property(z => z.Einheit).HasMaxLength(ZutatFelder.EinheitMaximaleLaenge);
            e.Property(z => z.Allergene).HasMaxLength(200);
            e.Property(z => z.Version).IsRowVersion();
            e.HasOne<Rezept>().WithMany().HasForeignKey(z => z.RezeptId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(z => z.FreizeitId);
            e.HasIndex(z => z.RezeptId);
            e.HasIndex(z => z.OrganisationId);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class KuecheDbContextFabrik : IDesignTimeDbContextFactory<KuecheDbContext>
{
    public KuecheDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<KuecheDbContext>(KuecheModul.Schema), new MandantenKontext());
}
