using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Zeltlotse.Api.Adapter;
using Zeltlotse.Api.Demo;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.SharedKernel.Web;

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
/// Hauptschlüssel (AD-14), VAPID-Schlüssel (AD-21), HMAC-Schlüssel der Logs (AD-22), Demo-Daten nur in <c>Development</c>/<c>Testing</c> (NFR-20),
/// Mail-Versand (Art, Ordner-Adapter nur in <c>Development</c>/<c>Testing</c>), öffentliche Adresse, gültige Mail-Adressen in
/// <c>Zeltlotse:GlobalAdmins</c> und – außerhalb der Umgebung <c>Testing</c> –
/// keine DbContexts mit <c>EnableSensitiveDataLogging</c>/<c>EnableDetailedErrors</c> und keine Verbindung
/// mit <c>Include Error Detail=true</c> (AD-22).
/// </summary>
internal static class Startpruefung
{
    public const string TestUmgebung = "Testing";

    public static void Ausfuehren(IServiceProvider dienste, IHostEnvironment umgebung)
    {
        _ = dienste.GetRequiredService<Hauptschluessel>();
        // Story 6-1: der VAPID-Schlüssel für Web-Push (AD-21).
        _ = dienste.GetRequiredService<VapidSchluessel>();
        ProtokollSchwaerzung.SchluesselPruefen(dienste.GetRequiredService<IConfiguration>());
        DemoDatenPruefen(dienste.GetRequiredService<IConfiguration>(), umgebung);
        MailRegistrierung.Pruefen(dienste.GetRequiredService<IConfiguration>(), umgebung, TestUmgebung);
        AdressePruefen(dienste.GetRequiredService<IConfiguration>(), umgebung);
        GlobalAdmins.Pruefen(dienste.GetRequiredService<IConfiguration>());

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

    /// <summary>
    /// Öffentliche Adresse: Pflicht und absolut; außerhalb von <c>Development</c> und <c>Testing</c> nur <c>https</c>,
    /// damit Login-Links nie unverschlüsselt verschickt werden (AD-11, AD-25).
    /// </summary>
    private static void AdressePruefen(IConfiguration konfiguration, IHostEnvironment umgebung)
    {
        var adresse = OeffentlicheAdresse.Lesen(konfiguration);
        if (!adresse.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !umgebung.IsDevelopment() && !umgebung.IsEnvironment(TestUmgebung))
        {
            throw new InvalidOperationException(
                $"{OeffentlicheAdresse.Schluessel} muss in {umgebung.EnvironmentName} eine https-Adresse sein.");
        }
    }

    /// <summary>Demo-Daten (NFR-20) sind nur in <c>Development</c> und <c>Testing</c> erlaubt.</summary>
    private static void DemoDatenPruefen(IConfiguration konfiguration, IHostEnvironment umgebung)
    {
        if (DemoDaten.IstAktiv(konfiguration) && !umgebung.IsDevelopment() && !umgebung.IsEnvironment(TestUmgebung))
        {
            throw new InvalidOperationException(
                $"{DemoDaten.Schalter} ist nur in den Umgebungen Development und {TestUmgebung} erlaubt, nicht in {umgebung.EnvironmentName}.");
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
