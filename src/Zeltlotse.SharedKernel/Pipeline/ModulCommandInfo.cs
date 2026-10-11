using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Daten;

namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>Ermittelt einmal je Command-Typ den DbContext aus <see cref="IModulCommand{TDbContext}"/>.</summary>
internal static class ModulCommandInfo<TMessage>
{
    private static readonly Type DbContextTyp =
        typeof(TMessage).GetInterfaces()
            .SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IModulCommand<>))
            ?.GetGenericArguments()[0]
        ?? throw new InvalidOperationException($"{typeof(TMessage).FullName} trägt kein IModulCommand<TDbContext>.");

    public static ModulDbContext DbContext(IServiceProvider dienste) =>
        (ModulDbContext)dienste.GetRequiredService(DbContextTyp);
}
