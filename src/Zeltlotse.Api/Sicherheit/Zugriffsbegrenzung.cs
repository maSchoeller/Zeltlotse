using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Api.Sicherheit;

/// <summary>Grenzen der Zugriffsbegrenzung, Abschnitt <c>Zeltlotse:Zugriffsbegrenzung</c>.</summary>
internal sealed class ZugriffsbegrenzungOptionen
{
    public const string Abschnitt = "Zeltlotse:Zugriffsbegrenzung";

    public int AnfragenJeMinute { get; set; } = 20;
}

/// <summary>
/// Eine benannte Richtlinie (AD-11, AD-25), an Endpunkten mit <c>.Zugriffsbegrenzt()</c>; Partition je Besucher-IP:
/// die Verbindungs-IP (nach den weitergeleiteten Headern konfigurierter Proxys) oder, mit gültiger Dienst-Kennung,
/// die durchgereichte IP aus <c>X-Zeltlotse-Besucher-Ip</c>. Überschreitung: <c>429</c> mit <c>Retry-After</c>.
/// </summary>
internal static class ZugriffsbegrenzungRegistrierung
{
    private static readonly TimeSpan Fenster = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddZugriffsbegrenzung(this IServiceCollection services)
    {
        services.AddOptions<ZugriffsbegrenzungOptionen>()
            .BindConfiguration(ZugriffsbegrenzungOptionen.Abschnitt)
            .Validate(o => o.AnfragenJeMinute > 0, $"{ZugriffsbegrenzungOptionen.Abschnitt}:AnfragenJeMinute muss größer als 0 sein.")
            .ValidateOnStart();

        services.AddRateLimiter(optionen =>
        {
            optionen.AddPolicy(Zugriffsbegrenzung.Richtlinie, kontext =>
            {
                var grenze = kontext.RequestServices.GetRequiredService<IOptions<ZugriffsbegrenzungOptionen>>().Value.AnfragenJeMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    Partition(kontext),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = grenze, Window = Fenster, QueueLimit = 0 });
            });
            optionen.OnRejected = AbgelehntAsync;
        });
        return services;
    }

    internal static string Partition(HttpContext kontext)
    {
        var kennung = kontext.RequestServices.GetRequiredService<PublicSeitenDienstKennung>();
        if (kontext.Request.Headers.TryGetValue(Kopfzeilen.Dienst, out var dienst)
            && kennung.Pruefen(dienst.ToString())
            && IPAddress.TryParse(kontext.Request.Headers[Kopfzeilen.BesucherIp].ToString(), out var besucher))
        {
            return Normalisieren(besucher);
        }

        return kontext.Connection.RemoteIpAddress is { } ip ? Normalisieren(ip) : "unbekannt";
    }

    private static string Normalisieren(IPAddress ip) => (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();

    private static async ValueTask AbgelehntAsync(OnRejectedContext abgelehnt, CancellationToken abbruch)
    {
        var kontext = abgelehnt.HttpContext;
        var warten = abgelehnt.Lease.TryGetMetadata(MetadataName.RetryAfter, out var dauer) ? dauer : Fenster;
        kontext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        kontext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(warten.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await kontext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = kontext,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Zu viele Anfragen. Bitte etwas warten.",
                Extensions = { [Problemcodes.Feld] = Problemcodes.Zugriffsbegrenzt },
            },
        });
    }
}
