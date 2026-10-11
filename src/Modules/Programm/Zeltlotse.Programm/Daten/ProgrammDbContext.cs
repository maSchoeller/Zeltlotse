using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class ProgrammDbContext(DbContextOptions<ProgrammDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => ProgrammModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => ProgrammModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class ProgrammLeseDbContext(DbContextOptions<ProgrammLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => ProgrammModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => ProgrammModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class ProgrammModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class ProgrammDbContextFabrik : IDesignTimeDbContextFactory<ProgrammDbContext>
{
    public ProgrammDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<ProgrammDbContext>(ProgrammModul.Schema), new MandantenKontext());
}
