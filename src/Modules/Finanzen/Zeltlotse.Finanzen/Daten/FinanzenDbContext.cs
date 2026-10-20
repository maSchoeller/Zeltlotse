using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Finanzen.Daten;

using Einnahmeposten = Zeltlotse.Finanzen.Kalkulation.Einnahmeposten;
using Kalkulation = Zeltlotse.Finanzen.Kalkulation.Kalkulation;
using KategoriePosten = Zeltlotse.Finanzen.Kalkulation.KategoriePosten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class FinanzenDbContext(DbContextOptions<FinanzenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    /// <summary>Story 5-1: die Planzahlen der Kalkulationen, höchstens eine je Freizeit.</summary>
    public DbSet<Kalkulation> Kalkulationen => Set<Kalkulation>();

    /// <summary>Story 5-1: die Beträge der Kostenkategorien, höchstens eine Zeile je (Freizeit, Kategorie).</summary>
    public DbSet<KategoriePosten> KategoriePosten => Set<KategoriePosten>();

    /// <summary>Story 5-1: die Einnahmeposten der Kalkulationen.</summary>
    public DbSet<Einnahmeposten> Einnahmeposten => Set<Einnahmeposten>();

    protected override string Schema => FinanzenModul.Schema;

    /// <summary>
    /// Story 5-1: Stellt die Kalkulation der Freizeit sicher (<c>INSERT … ON CONFLICT DO NOTHING</c> auf den eindeutigen Index der Freizeit) und lädt sie
    /// unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE): Zwei gleichzeitige erste Änderungen legen genau eine Zeile an und laufen danach
    /// nacheinander (keine 500). Ob es die Freizeit gibt, prüft der Aufrufer vorher.
    /// </summary>
    public async Task<Kalkulation> KalkulationSperrenAsync(Guid id, Guid organisationId, Guid freizeitId, CancellationToken abbruch)
    {
        await Database.ExecuteSqlAsync(
            $"INSERT INTO finanzen.kalkulation (\"Id\", \"OrganisationId\", \"FreizeitId\", \"Teilnehmende\", \"Mitarbeitende\") VALUES ({id}, {organisationId}, {freizeitId}, 0, 0) ON CONFLICT (\"FreizeitId\") DO NOTHING",
            abbruch);
        return (await Kalkulationen
                .FromSql($"SELECT *, xmin FROM finanzen.kalkulation WHERE \"FreizeitId\" = {freizeitId} FOR UPDATE")
                .ToListAsync(abbruch))
            .Single();
    }

    /// <summary>Story 5-1: Wie <see cref="KalkulationSperrenAsync"/> für eine Kategorie der Freizeit (eindeutig je Freizeit und Kategorie).</summary>
    public async Task<KategoriePosten> KategorieSperrenAsync(Guid id, Guid organisationId, Guid freizeitId, Kostenkategorie kategorie, CancellationToken abbruch)
    {
        var wert = (int)kategorie;
        await Database.ExecuteSqlAsync(
            $"INSERT INTO finanzen.kalkulation_kategorie (\"Id\", \"OrganisationId\", \"FreizeitId\", \"Kategorie\", \"Fixkosten\", \"ProPersonUndTag\") VALUES ({id}, {organisationId}, {freizeitId}, {wert}, 0, 0) ON CONFLICT (\"FreizeitId\", \"Kategorie\") DO NOTHING",
            abbruch);
        return (await KategoriePosten
                .FromSql($"SELECT *, xmin FROM finanzen.kalkulation_kategorie WHERE \"FreizeitId\" = {freizeitId} AND \"Kategorie\" = {wert} FOR UPDATE")
                .ToListAsync(abbruch))
            .Single();
    }

    /// <summary>Story 5-1: Lädt einen Einnahmeposten der Freizeit unter dem Mandantenfilter mit Zeilensperre (FOR UPDATE); <c>null</c>, wenn es ihn nicht (mehr) gibt.</summary>
    public async Task<Einnahmeposten?> EinnahmeSperrenAsync(Guid freizeitId, Guid einnahmeId, CancellationToken abbruch) =>
        (await Einnahmeposten
            .FromSql($"SELECT *, xmin FROM finanzen.einnahmeposten WHERE \"Id\" = {einnahmeId} AND \"FreizeitId\" = {freizeitId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => FinanzenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class FinanzenLeseDbContext(DbContextOptions<FinanzenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Kalkulation> Kalkulationen => Set<Kalkulation>();

    public DbSet<KategoriePosten> KategoriePosten => Set<KategoriePosten>();

    public DbSet<Einnahmeposten> Einnahmeposten => Set<Einnahmeposten>();

    protected override string Schema => FinanzenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => FinanzenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class FinanzenModell
{
    /// <summary>Beträge in Euro mit Cent (bis 9.999.999.999,99).</summary>
    private const int BetragStellen = 12;

    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        // Story 5-1, FR-54: Planzahlen je Freizeit höchstens einmal (Arbeitsdaten der Freizeit, AD-16, ohne Personenbezug).
        modelBuilder.Entity<Kalkulation>(e =>
        {
            e.ToTable("kalkulation");
            e.Property(k => k.Id).ValueGeneratedNever();
            e.Property(k => k.Version).IsRowVersion();
            e.HasIndex(k => k.FreizeitId).IsUnique();
            e.HasIndex(k => k.OrganisationId);
        });

        // Story 5-1, FR-54, FR-55: Beträge je (Freizeit, Kategorie) höchstens einmal.
        modelBuilder.Entity<KategoriePosten>(e =>
        {
            e.ToTable("kalkulation_kategorie");
            e.Property(k => k.Id).ValueGeneratedNever();
            e.Property(k => k.Fixkosten).HasPrecision(BetragStellen, KategorieFelder.BetragNachkommastellen);
            e.Property(k => k.ProPersonUndTag).HasPrecision(BetragStellen, KategorieFelder.BetragNachkommastellen);
            e.Property(k => k.Version).IsRowVersion();
            e.HasIndex(k => new { k.FreizeitId, k.Kategorie }).IsUnique();
            e.HasIndex(k => k.OrganisationId);
        });

        // Story 5-1, FR-54: freie Einnahmeposten der Kalkulation.
        modelBuilder.Entity<Einnahmeposten>(e =>
        {
            e.ToTable("einnahmeposten");
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Bezeichnung).HasMaxLength(EinnahmepostenFelder.BezeichnungMaximaleLaenge);
            e.Property(p => p.Betrag).HasPrecision(BetragStellen, KategorieFelder.BetragNachkommastellen);
            e.Property(p => p.Version).IsRowVersion();
            e.HasIndex(p => p.FreizeitId);
            e.HasIndex(p => p.OrganisationId);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class FinanzenDbContextFabrik : IDesignTimeDbContextFactory<FinanzenDbContext>
{
    public FinanzenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<FinanzenDbContext>(FinanzenModul.Schema), new MandantenKontext());
}
