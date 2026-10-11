using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Api.Start;

/// <summary>Alle registrierten DbContext-Typen; liest die Registrierungen erst beim Start (auch die aus Tests).</summary>
internal sealed class DbKontextVerzeichnis(IServiceCollection services)
{
    public IReadOnlyList<Type> Typen =>
    [
        .. services
            .Select(d => d.ServiceType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Select(t => t.GetGenericArguments()[0])
            .Distinct(),
    ];
}

/// <summary>
/// Prüfungen vor den Migrationen; jeder Verstoß bricht den Start ab, ohne Geheimnisse zu nennen:
/// Hauptschlüssel (AD-14), HMAC-Schlüssel der Logs (AD-22) und – außerhalb der Umgebung <c>Testing</c> –
/// keine DbContexts mit <c>EnableSensitiveDataLogging</c>/<c>EnableDetailedErrors</c> und keine Verbindung
/// mit <c>Include Error Detail=true</c> (AD-22).
/// </summary>
internal static class Startpruefung
{
    public const string TestUmgebung = "Testing";

    public static void Ausfuehren(IServiceProvider dienste, IHostEnvironment umgebung)
    {
        _ = dienste.GetRequiredService<Hauptschluessel>();
        ProtokollSchwaerzung.SchluesselPruefen(dienste.GetRequiredService<IConfiguration>());

        if (umgebung.IsEnvironment(TestUmgebung))
        {
            return;
        }

        var verstoesse = DatenbankVerstoesse(dienste, dienste.GetRequiredService<DbKontextVerzeichnis>().Typen).ToList();
        if (verstoesse.Count > 0)
        {
            throw new InvalidOperationException(
                "Außerhalb der Tests verboten (AD-22): " + string.Join("; ", verstoesse));
        }
    }

    internal static IEnumerable<string> DatenbankVerstoesse(IServiceProvider dienste, IEnumerable<Type> kontextTypen)
    {
        using var scope = dienste.CreateScope();
        foreach (var typ in kontextTypen)
        {
            var optionen = (DbContextOptions)scope.ServiceProvider.GetRequiredService(typeof(DbContextOptions<>).MakeGenericType(typ));
            var kern = optionen.FindExtension<CoreOptionsExtension>();
            if (kern?.IsSensitiveDataLoggingEnabled == true)
            {
                yield return $"{typ.Name} nutzt EnableSensitiveDataLogging";
            }

            if (kern?.DetailedErrorsEnabled == true)
            {
                yield return $"{typ.Name} nutzt EnableDetailedErrors";
            }

            var verbindung = optionen.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault()?.ConnectionString;
            if (verbindung is not null && MitFehlerdetails(verbindung))
            {
                yield return $"{typ.Name} verbindet mit Include Error Detail=true";
            }
        }
    }

    private static bool MitFehlerdetails(string verbindung)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(verbindung).IncludeErrorDetail;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
