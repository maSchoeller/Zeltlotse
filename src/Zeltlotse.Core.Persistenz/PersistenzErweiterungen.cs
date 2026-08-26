using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Zeltlotse.Core.Persistenz;

public static class PersistenzErweiterungen
{
    public static IServiceCollection AddPersistenz(this IServiceCollection dienste)
    {
        dienste.AddScoped<MandantKontext>();
        dienste.AddScoped<IMandantKontext>(sp => sp.GetRequiredService<MandantKontext>());
        dienste.AddScoped<MandantInterceptor>();
        dienste.AddScoped<IBerechtigung, Berechtigung>();
        dienste.AddScoped<Organisationsaufloeser>();
        dienste.AddHttpContextAccessor();

        return dienste;
    }
}

/// <summary>
/// Nur für <c>dotnet ef</c>. Zur Entwurfszeit gibt es weder Anfrage noch
/// Mandant, deshalb ein Kontext im Wartungsmodus.
/// </summary>
public sealed class EntwurfszeitFabrik : IDesignTimeDbContextFactory<ZeltlotseDbContext>
{
    public ZeltlotseDbContext CreateDbContext(string[] argumente)
    {
        var optionen = new DbContextOptionsBuilder<ZeltlotseDbContext>()
            .UseNpgsql("Host=localhost;Database=zeltlotse;Username=postgres;Password=entwurfszeit")
            .Options;

        return new ZeltlotseDbContext(optionen, new MandantKontext { Wartung = true });
    }
}
