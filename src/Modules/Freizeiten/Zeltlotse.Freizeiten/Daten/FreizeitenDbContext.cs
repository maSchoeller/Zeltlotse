using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Freizeiten.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class FreizeitenDbContext(DbContextOptions<FreizeitenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => FreizeitenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => FreizeitenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class FreizeitenLeseDbContext(DbContextOptions<FreizeitenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => FreizeitenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => FreizeitenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class FreizeitenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class FreizeitenDbContextFabrik : IDesignTimeDbContextFactory<FreizeitenDbContext>
{
    public FreizeitenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<FreizeitenDbContext>(FreizeitenModul.Schema), new MandantenKontext());
}
