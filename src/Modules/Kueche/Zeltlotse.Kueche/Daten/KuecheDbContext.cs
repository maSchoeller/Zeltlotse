using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kueche.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class KuecheDbContext(DbContextOptions<KuecheDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => KuecheModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KuecheModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class KuecheLeseDbContext(DbContextOptions<KuecheLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => KuecheModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KuecheModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class KuecheModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class KuecheDbContextFabrik : IDesignTimeDbContextFactory<KuecheDbContext>
{
    public KuecheDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<KuecheDbContext>(KuecheModul.Schema), new MandantenKontext());
}
