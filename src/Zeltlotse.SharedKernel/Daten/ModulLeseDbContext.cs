using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.SharedKernel.Daten;

/// <summary>Lesender DbContext eines Moduls für Endpunkte (AD-27): ohne Änderungsverfolgung, Speichern wirft.</summary>
public abstract class ModulLeseDbContext(DbContextOptions optionen, IMandantenKontext mandantenKontext)
    : ModulDbContextBasis(optionen, mandantenKontext)
{
    private const string Meldung = "Der Lese-DbContext speichert nicht; Änderungen laufen über Commands.";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        optionsBuilder.AddInterceptors(NurLesenInterceptor.Instanz);
        base.OnConfiguring(optionsBuilder);
    }

    public sealed override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException(Meldung);

    public sealed override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Meldung);
}

/// <summary>
/// Lässt im Lese-Kontext nur lesende SQL-Befehle durch; ExecuteUpdate/ExecuteDelete und rohes SQL,
/// das schreibt, werden abgelehnt.
/// </summary>
internal sealed class NurLesenInterceptor : DbCommandInterceptor
{
    public static readonly NurLesenInterceptor Instanz = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Pruefen(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Pruefen(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Pruefen(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Pruefen(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Pruefen(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Pruefen(command);
        return ValueTask.FromResult(result);
    }

    private static void Pruefen(DbCommand command)
    {
        var text = command.CommandText.TrimStart();
        if (!text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Der Lese-DbContext führt nur lesende Befehle aus; Änderungen laufen über Commands.");
        }
    }
}
