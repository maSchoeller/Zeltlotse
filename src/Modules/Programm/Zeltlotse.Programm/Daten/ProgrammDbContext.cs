using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Programm;
using Material = Zeltlotse.Programm.Materialien.Material;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class ProgrammDbContext(DbContextOptions<ProgrammDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    /// <summary>Story 3-1: die Programmpunkte aller Freizeiten, auch die festen Mahlzeiten.</summary>
    public DbSet<Programmpunkt> Programmpunkte => Set<Programmpunkt>();

    /// <summary>Story 3-2: das Material an den Programmpunkten.</summary>
    public DbSet<Material> Material => Set<Material>();

    protected override string Schema => ProgrammModul.Schema;

    /// <summary>
    /// Story 3-2: Lädt ein Material der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): Änderungen, Statuswechsel und
    /// „Ich bringe das mit“ desselben Materials laufen nacheinander. <c>null</c>, wenn es das Material in dieser Freizeit nicht gibt.
    /// </summary>
    public async Task<Material?> MaterialSperrenAsync(Guid freizeitId, Guid materialId, CancellationToken abbruch) =>
        (await Material
            .FromSql($"SELECT *, xmin FROM programm.material WHERE \"Id\" = {materialId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Story 3-1: Lädt einen Programmpunkt der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): Änderungen und Übernahmen
    /// desselben Punkts laufen nacheinander. <c>null</c>, wenn es ihn in dieser Freizeit nicht gibt.
    /// </summary>
    public async Task<Programmpunkt?> ProgrammpunktSperrenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch) =>
        (await Programmpunkte
            .FromSql($"SELECT *, xmin FROM programm.programmpunkt WHERE \"Id\" = {punktId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => ProgrammModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class ProgrammLeseDbContext(DbContextOptions<ProgrammLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Programmpunkt> Programmpunkte => Set<Programmpunkt>();

    public DbSet<Material> Material => Set<Material>();

    protected override string Schema => ProgrammModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => ProgrammModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class ProgrammModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        // Story 3-1, FR-34: Programmpunkte. Mahlzeiten gibt es je (Freizeit, Tag, Mahlzeit) genau einmal; das Sicherstellen nutzt diesen
        // Teilindex (ON CONFLICT).
        modelBuilder.Entity<Programmpunkt>(e =>
        {
            e.ToTable("programmpunkt");
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Titel).HasMaxLength(ProgrammpunktFelder.TitelMaximaleLaenge);
            e.Property(p => p.Mahlzeit).HasConversion<short>();
            e.Property(p => p.Version).IsRowVersion();
            e.Ignore(p => p.Loeschbar);
            e.Ignore(p => p.Uebernommen);
            e.HasIndex(p => new { p.FreizeitId, p.Tag });
            e.HasIndex(p => new { p.FreizeitId, p.Tag, p.Mahlzeit }).IsUnique().HasFilter("\"Mahlzeit\" IS NOT NULL");
            e.HasIndex(p => p.PersonId);
            e.HasIndex(p => p.OrganisationId);
        });

        // Story 3-2, FR-36: Material gehört zu genau einem Programmpunkt und verschwindet mit ihm (Fremdschlüssel im eigenen Schema, AD-3).
        modelBuilder.Entity<Material>(e =>
        {
            e.ToTable("material");
            e.Property(m => m.Id).ValueGeneratedNever();
            e.Property(m => m.Bezeichnung).HasMaxLength(MaterialFelder.BezeichnungMaximaleLaenge);
            e.Property(m => m.Menge).HasMaxLength(MaterialFelder.MengeMaximaleLaenge);
            e.Property(m => m.Status).HasConversion<short>();
            e.Property(m => m.Version).IsRowVersion();
            e.Ignore(m => m.BringtMit);
            e.HasOne<Programmpunkt>().WithMany().HasForeignKey(m => m.ProgrammpunktId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.FreizeitId);
            e.HasIndex(m => m.ProgrammpunktId);
            e.HasIndex(m => m.PersonId);
            e.HasIndex(m => m.OrganisationId);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class ProgrammDbContextFabrik : IDesignTimeDbContextFactory<ProgrammDbContext>
{
    public ProgrammDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<ProgrammDbContext>(ProgrammModul.Schema), new MandantenKontext());
}
