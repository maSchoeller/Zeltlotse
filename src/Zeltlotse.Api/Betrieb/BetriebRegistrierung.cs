using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Api.Betrieb;

internal static class BetriebRegistrierung
{
    /// <summary>Schema <c>betrieb</c>: Sicherheitsprotokoll mit täglichem Fristdienst und die Betriebs-Endpunkte.</summary>
    public static IServiceCollection AddBetrieb(this IServiceCollection services)
    {
        services.AddDbContextFactory<BetriebDbContext>((sp, optionen) => BetriebDbContext.Konfigurieren(
            optionen,
            sp.GetRequiredService<IConfiguration>().GetConnectionString(ModulRegistrierung.Verbindungsname)
            ?? throw new InvalidOperationException($"Verbindungszeichenfolge '{ModulRegistrierung.Verbindungsname}' fehlt.")));
        services.AddSingleton<ISicherheitsprotokoll, BetriebSicherheitsprotokoll>();
        services.AddSingleton(sp => new SicherheitsprotokollAufraeumen(
            sp.GetRequiredService<IDbContextFactory<BetriebDbContext>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<SicherheitsprotokollFristDienst>();
        services.AddSingleton<IValidator<BrowserFehler>, BrowserFehlerValidator>();
        return services;
    }
}
