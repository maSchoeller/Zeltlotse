using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Organisationen.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class OrganisationenDbContext(DbContextOptions<OrganisationenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    protected override string Schema => OrganisationenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => OrganisationenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class OrganisationenLeseDbContext(DbContextOptions<OrganisationenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    protected override string Schema => OrganisationenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => OrganisationenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class OrganisationenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class OrganisationenDbContextFabrik : IDesignTimeDbContextFactory<OrganisationenDbContext>
{
    public OrganisationenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<OrganisationenDbContext>(OrganisationenModul.Schema), new MandantenKontext());
}
