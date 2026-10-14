using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;
using InventarArtikel = Zeltlotse.Inventar.Artikel.InventarArtikel;
using Reservierung = Zeltlotse.Inventar.Reservierungen.Reservierung;
using SchadenVerlust = Zeltlotse.Inventar.Schaeden.SchadenVerlust;

namespace Zeltlotse.Inventar.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class InventarDbContext(DbContextOptions<InventarDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    /// <summary>Story 3-3: die Artikel im Inventar der Organisationen.</summary>
    public DbSet<InventarArtikel> Artikel => Set<InventarArtikel>();

    /// <summary>Story 3-3: die Reservierungen der Artikel für Freizeiten.</summary>
    public DbSet<Reservierung> Reservierungen => Set<Reservierung>();

    /// <summary>Story 3-4: die Schadens- und Verlustlisten der Freizeiten.</summary>
    public DbSet<SchadenVerlust> Schaeden => Set<SchadenVerlust>();

    protected override string Schema => InventarModul.Schema;

    /// <summary>
    /// Story 3-3: Lädt einen Artikel unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): Änderungen, Löschen und Reservieren desselben
    /// Artikels laufen nacheinander. <c>null</c>, wenn es den Artikel in dieser Organisation nicht gibt.
    /// </summary>
    public async Task<InventarArtikel?> ArtikelSperrenAsync(Guid artikelId, CancellationToken abbruch) =>
        (await Artikel
            .FromSql($"SELECT *, xmin FROM inventar.inventar_artikel WHERE \"Id\" = {artikelId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Story 3-3: Lädt eine Reservierung der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): ein gleichzeitiges zweites Aufheben
    /// wartet und findet danach nichts mehr. <c>null</c>, wenn es sie in dieser Freizeit nicht gibt.
    /// </summary>
    public async Task<Reservierung?> ReservierungSperrenAsync(Guid freizeitId, Guid reservierungId, CancellationToken abbruch) =>
        (await Reservierungen
            .FromSql($"SELECT * FROM inventar.reservierung WHERE \"Id\" = {reservierungId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Story 3-4: Lädt einen Eintrag der Schadens- und Verlustliste der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): Änderungen
    /// und Entfernen desselben Eintrags laufen nacheinander. <c>null</c>, wenn es ihn in dieser Freizeit nicht gibt.
    /// </summary>
    public async Task<SchadenVerlust?> SchadenSperrenAsync(Guid freizeitId, Guid eintragId, CancellationToken abbruch) =>
        (await Schaeden
            .FromSql($"SELECT *, xmin FROM inventar.schaden_verlust WHERE \"Id\" = {eintragId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Story 3-4: Wie die Sperre oben, aber ohne Freizeit: für „erledigt“ auf der Seite der Organisation (die Freizeit prüft der
    /// Handler danach über ihre Phase).
    /// </summary>
    public async Task<SchadenVerlust?> SchadenSperrenAsync(Guid eintragId, CancellationToken abbruch) =>
        (await Schaeden
            .FromSql($"SELECT *, xmin FROM inventar.schaden_verlust WHERE \"Id\" = {eintragId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => InventarModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class InventarLeseDbContext(DbContextOptions<InventarLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<InventarArtikel> Artikel => Set<InventarArtikel>();

    public DbSet<Reservierung> Reservierungen => Set<Reservierung>();

    public DbSet<SchadenVerlust> Schaeden => Set<SchadenVerlust>();

    protected override string Schema => InventarModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => InventarModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class InventarModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        // Story 3-3, FR-37: das Inventar der Organisation.
        modelBuilder.Entity<InventarArtikel>(e =>
        {
            e.ToTable("inventar_artikel");
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.Bezeichnung).HasMaxLength(InventarFelder.BezeichnungMaximaleLaenge);
            e.Property(a => a.Menge).HasMaxLength(InventarFelder.MengeMaximaleLaenge);
            e.Property(a => a.Version).IsRowVersion();
            e.HasIndex(a => a.OrganisationId);
        });

        // Story 3-3, FR-37: Reservierungen hängen am Artikel (Fremdschlüssel im eigenen Schema, AD-3). Ohne Cascade: Einen Artikel mit
        // Reservierungen lehnt das Löschen ab, statt sie still mitzunehmen.
        modelBuilder.Entity<Reservierung>(e =>
        {
            e.ToTable("reservierung");
            e.Property(r => r.Id).ValueGeneratedNever();
            e.HasOne<InventarArtikel>().WithMany().HasForeignKey(r => r.ArtikelId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => r.OrganisationId);
            e.HasIndex(r => r.ArtikelId);
            e.HasIndex(r => r.FreizeitId);
        });

        // Story 3-4, FR-32/FR-38: die Schadens- und Verlustliste der Freizeiten; je Vorfall-Eintrag höchstens ein Eintrag (Empfänger idempotent).
        modelBuilder.Entity<SchadenVerlust>(e =>
        {
            e.ToTable("schaden_verlust");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.Art).HasConversion<short>();
            e.Property(s => s.Bezeichnung).HasMaxLength(SchadenFelder.BezeichnungMaximaleLaenge);
            e.Property(s => s.Version).IsRowVersion();
            e.HasIndex(s => s.OrganisationId);
            e.HasIndex(s => s.FreizeitId);
            e.HasIndex(s => s.VorfallEintragId).IsUnique().HasFilter("\"VorfallEintragId\" IS NOT NULL");
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class InventarDbContextFabrik : IDesignTimeDbContextFactory<InventarDbContext>
{
    public InventarDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<InventarDbContext>(InventarModul.Schema), new MandantenKontext());
}
