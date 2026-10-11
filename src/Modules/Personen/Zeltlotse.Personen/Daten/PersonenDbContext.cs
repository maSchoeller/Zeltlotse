using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Personen.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class PersonenDbContext(DbContextOptions<PersonenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    public DbSet<PersonenSchluessel> PersonenSchluessel => Set<PersonenSchluessel>();

    protected override string Schema => PersonenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => PersonenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class PersonenLeseDbContext(DbContextOptions<PersonenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<PersonenSchluessel> PersonenSchluessel => Set<PersonenSchluessel>();

    protected override string Schema => PersonenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => PersonenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class PersonenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonenSchluessel>(e =>
        {
            e.ToTable("personen_schluessel");
            e.HasKey(s => s.PersonId);
            e.Property(s => s.PersonId).ValueGeneratedNever();
            e.Property(s => s.Umhuellt).HasColumnType("bytea");
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class PersonenDbContextFabrik : IDesignTimeDbContextFactory<PersonenDbContext>
{
    public PersonenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<PersonenDbContext>(PersonenModul.Schema), new MandantenKontext());
}
