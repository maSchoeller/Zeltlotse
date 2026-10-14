using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Programm;
using Aufgabe = Zeltlotse.Programm.Aufgaben.Aufgabe;
using AufgabenVorlage = Zeltlotse.Programm.Aufgaben.AufgabenVorlage;
using AufgabenVorlagenUebernahme = Zeltlotse.Programm.Aufgaben.AufgabenVorlagenUebernahme;
using BibliotheksEintrag = Zeltlotse.Programm.Bibliothek.BibliotheksEintrag;
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

    /// <summary>Story 3-5: die Aufgaben aller Freizeiten.</summary>
    public DbSet<Aufgabe> Aufgaben => Set<Aufgabe>();

    /// <summary>Story 3-5: die Aufgaben-Vorlagen der Organisationen.</summary>
    public DbSet<AufgabenVorlage> AufgabenVorlagen => Set<AufgabenVorlage>();

    /// <summary>Story 3-5: je Freizeit, dass ihre Aufgaben die Vorlagen schon übernommen haben.</summary>
    public DbSet<AufgabenVorlagenUebernahme> VorlagenUebernahmen => Set<AufgabenVorlagenUebernahme>();

    /// <summary>Story 3-6: die Einträge der Programmbibliotheken der Organisationen.</summary>
    public DbSet<BibliotheksEintrag> Bibliothek => Set<BibliotheksEintrag>();

    protected override string Schema => ProgrammModul.Schema;

    /// <summary>
    /// Story 3-6: Lädt einen Eintrag der Programmbibliothek unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE). <c>null</c>, wenn es ihn in
    /// der Organisation des Mandanten nicht gibt.
    /// </summary>
    public async Task<BibliotheksEintrag?> EintragSperrenAsync(Guid eintragId, CancellationToken abbruch) =>
        (await Bibliothek
            .FromSql($"SELECT * FROM programm.bibliotheks_eintrag WHERE \"Id\" = {eintragId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Story 3-5: Lädt eine Aufgabe der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): Änderungen und Übergänge derselben
    /// Aufgabe laufen nacheinander. <c>null</c>, wenn es sie in dieser Freizeit nicht gibt.
    /// </summary>
    public async Task<Aufgabe?> AufgabeSperrenAsync(Guid freizeitId, Guid aufgabeId, CancellationToken abbruch) =>
        (await Aufgaben
            .FromSql($"SELECT *, xmin FROM programm.aufgabe WHERE \"Id\" = {aufgabeId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Story 3-5: Lädt eine Aufgaben-Vorlage unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE). <c>null</c>, wenn es sie in der
    /// Organisation des Mandanten nicht gibt.
    /// </summary>
    public async Task<AufgabenVorlage?> VorlageSperrenAsync(Guid vorlageId, CancellationToken abbruch) =>
        (await AufgabenVorlagen
            .FromSql($"SELECT *, xmin FROM programm.aufgaben_vorlage WHERE \"Id\" = {vorlageId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

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

    public DbSet<Aufgabe> Aufgaben => Set<Aufgabe>();

    public DbSet<AufgabenVorlage> AufgabenVorlagen => Set<AufgabenVorlage>();

    public DbSet<AufgabenVorlagenUebernahme> VorlagenUebernahmen => Set<AufgabenVorlagenUebernahme>();

    public DbSet<BibliotheksEintrag> Bibliothek => Set<BibliotheksEintrag>();

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
            // Story 3-6: Mit dem Eintrag der Bibliothek verschwindet nur die Verknüpfung (wie bei Aufgabe und Vorlage).
            e.HasOne<BibliotheksEintrag>().WithMany().HasForeignKey(p => p.BibliotheksEintragId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(p => p.BibliotheksEintragId);
        });

        // Story 3-6, FR-41: Programmbibliothek der Organisation (kein Personenbezug, unveränderlich). Material und Aufgaben sind eine Momentaufnahme
        // und werden nur als Ganzes gelesen, darum JSON-Spalten.
        modelBuilder.Entity<BibliotheksEintrag>(e =>
        {
            e.ToTable("bibliotheks_eintrag");
            e.Property(b => b.Id).ValueGeneratedNever();
            e.Property(b => b.Titel).HasMaxLength(ProgrammpunktFelder.TitelMaximaleLaenge);
            e.OwnsMany(b => b.Material, m => m.ToJson());
            e.OwnsMany(b => b.Aufgaben, a => a.ToJson());
            e.HasIndex(b => b.OrganisationId);
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

        // Story 3-5, FR-40: Aufgaben-Vorlagen der Organisation (kein Personenbezug).
        modelBuilder.Entity<AufgabenVorlage>(e =>
        {
            e.ToTable("aufgaben_vorlage");
            e.Property(v => v.Id).ValueGeneratedNever();
            e.Property(v => v.Titel).HasMaxLength(AufgabenFelder.TitelMaximaleLaenge);
            e.Property(v => v.Phase).HasConversion<short>();
            e.Property(v => v.Version).IsRowVersion();
            e.HasIndex(v => v.OrganisationId);
        });

        // Story 3-5, FR-39: Aufgaben einer Freizeit. Mit dem Programmpunkt verschwinden sie (wie Material), mit der Vorlage nur die Verknüpfung.
        modelBuilder.Entity<Aufgabe>(e =>
        {
            e.ToTable("aufgabe");
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.Titel).HasMaxLength(AufgabenFelder.TitelMaximaleLaenge);
            e.Property(a => a.Phase).HasConversion<short>();
            e.Property(a => a.Version).IsRowVersion();
            e.Ignore(a => a.Uebernommen);
            e.HasOne<Programmpunkt>().WithMany().HasForeignKey(a => a.ProgrammpunktId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AufgabenVorlage>().WithMany().HasForeignKey(a => a.VorlageId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(a => a.FreizeitId);
            e.HasIndex(a => a.ProgrammpunktId);
            e.HasIndex(a => a.PersonId);
            e.HasIndex(a => a.VorlageId);
            e.HasIndex(a => a.OrganisationId);
        });

        // Story 3-5: Je Freizeit höchstens eine Zeile; das Übernehmen der Vorlagen nutzt den Schlüssel (ON CONFLICT DO NOTHING).
        modelBuilder.Entity<AufgabenVorlagenUebernahme>(e =>
        {
            e.ToTable("aufgaben_vorlagen_uebernahme");
            e.HasKey(u => u.FreizeitId);
            e.Property(u => u.FreizeitId).ValueGeneratedNever();
            e.HasIndex(u => u.OrganisationId);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class ProgrammDbContextFabrik : IDesignTimeDbContextFactory<ProgrammDbContext>
{
    public ProgrammDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<ProgrammDbContext>(ProgrammModul.Schema), new MandantenKontext());
}
