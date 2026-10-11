using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Personen.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class PersonenDbContext(DbContextOptions<PersonenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => PersonenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => PersonenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class PersonenLeseDbContext(DbContextOptions<PersonenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => PersonenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => PersonenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class PersonenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class PersonenDbContextFabrik : IDesignTimeDbContextFactory<PersonenDbContext>
{
    public PersonenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<PersonenDbContext>(PersonenModul.Schema), new MandantenKontext());
}
