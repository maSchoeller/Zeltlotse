using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.SharedKernel.Daten;

/// <summary>
/// Gemeinsame Grundlage des schreibenden und des lesenden DbContexts eines Moduls:
/// eigenes Schema (AD-3) und globaler Mandantenfilter für organisationseigene Entitäten (AD-6).
/// </summary>
public abstract class ModulDbContextBasis(DbContextOptions optionen, IMandantenKontext mandantenKontext) : DbContext(optionen)
{
    private static readonly MethodInfo FilterMethode =
        typeof(ModulDbContextBasis).GetMethod(nameof(MandantenFilterSetzen), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Das Schema des Moduls; jede Tabelle des Modells liegt darin.</summary>
    protected abstract string Schema { get; }

    /// <summary>Wird bei jeder Abfrage neu ausgewertet; ohne Kontext liefert der Filter nichts.</summary>
    protected Guid? AktuelleOrganisationId => mandantenKontext.OrganisationId;

    /// <summary>Entitäten des Moduls; schreibender und lesender Kontext rufen dieselbe Konfiguration auf.</summary>
    protected abstract void ModellKonfigurieren(ModelBuilder modelBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ModellKonfigurieren(modelBuilder);
        ZusatzModellKonfigurieren(modelBuilder);

        foreach (var entitaet in modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType is null).ToList())
        {
            if (typeof(Aggregat).IsAssignableFrom(entitaet.ClrType))
            {
                modelBuilder.Entity(entitaet.ClrType).Property(nameof(Aggregat.Folgenummer)).IsConcurrencyToken();
            }

            if (typeof(IOrganisationsEigen).IsAssignableFrom(entitaet.ClrType))
            {
                FilterMethode.MakeGenericMethod(entitaet.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    /// <summary>Platz für Tabellen der Infrastruktur (Outbox, Inbox) im schreibenden Kontext.</summary>
    private protected virtual void ZusatzModellKonfigurieren(ModelBuilder modelBuilder)
    {
    }

    private void MandantenFilterSetzen<TEntitaet>(ModelBuilder modelBuilder)
        where TEntitaet : class, IOrganisationsEigen =>
        modelBuilder.Entity<TEntitaet>().HasQueryFilter(
            e => AktuelleOrganisationId != null && e.OrganisationId == AktuelleOrganisationId);
}
