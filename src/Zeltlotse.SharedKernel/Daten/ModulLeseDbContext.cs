using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.SharedKernel.Daten;

/// <summary>
/// Lesender DbContext eines Moduls für Endpunkte (AD-27): ohne Änderungsverfolgung, Speichern wirft;
/// die Verbindung ist schreibgeschützt (siehe <see cref="ModulRegistrierung"/>).
/// </summary>
public abstract class ModulLeseDbContext(DbContextOptions optionen, IMandantenKontext mandantenKontext)
    : ModulDbContextBasis(optionen, mandantenKontext)
{
    private const string Meldung = "Der Lese-DbContext speichert nicht; Änderungen laufen über Commands.";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        base.OnConfiguring(optionsBuilder);
    }

    public sealed override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException(Meldung);

    public sealed override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Meldung);
}
