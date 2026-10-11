using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Api.Sicherheit;

/// <summary>Verdrahtung von AD-25 im API-Host: Header, Cookies, Antiforgery-Zwang, Dienst-Kennung, Zugriffsbegrenzung, Reverse Proxy.</summary>
internal static class WebSicherheit
{
    /// <summary>Vertrauenswürdige Reverse-Proxy-Adressen; Vorgabe: keine.</summary>
    public const string ProxyAbschnitt = "Zeltlotse:ReverseProxy:Adressen";

    public static IServiceCollection AddWebSicherheit(this IServiceCollection services)
    {
        services.AddSingleton<PublicSeitenDienstKennung>();
        services.AddCookieRichtlinie();
        services.AddAntiforgery(optionen =>
        {
            optionen.HeaderName = Kopfzeilen.Antiforgery;
            optionen.Cookie.Name = AntiforgeryZwang.CookieName;
            optionen.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            optionen.Cookie.SameSite = SameSiteMode.Lax;
            optionen.Cookie.HttpOnly = true;
            optionen.Cookie.Path = "/";
        });
        services.AddZugriffsbegrenzung();

        // Weitergeleitete Header nur von konfigurierten Proxy-Adressen; die eingebauten Loopback-Vorgaben fallen weg.
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IConfiguration>((optionen, konfiguration) =>
            {
                optionen.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                optionen.KnownIPNetworks.Clear();
                optionen.KnownProxies.Clear();
                foreach (var adresse in konfiguration.GetSection(ProxyAbschnitt).Get<string[]>() ?? [])
                {
                    optionen.KnownProxies.Add(IPAddress.Parse(adresse));
                }
            })
            .ValidateOnStart();
        return services;
    }

    /// <summary>Vor allem anderen: Header (gelten für jede Antwort) und die Absender-IP aus dem Reverse Proxy.</summary>
    public static IApplicationBuilder UseWebSicherheitAmAnfang(this IApplicationBuilder app)
    {
        app.UseSicherheitsHeader();

        // Achtung: Sind KnownProxies und KnownIPNetworks leer, vertraut die Middleware JEDEM Absender.
        // Ohne konfigurierte Proxy-Adressen bleibt sie deshalb ganz weg.
        var weitergeleitet = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        if (weitergeleitet.KnownProxies.Count > 0)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    /// <summary>Nach dem Routing: Cookie-Richtlinie, Zugriffsbegrenzung, dann der Antiforgery-Zwang.</summary>
    public static IApplicationBuilder UseWebSicherheitNachRouting(this IApplicationBuilder app) =>
        app.UseCookiePolicy().UseRateLimiter().UseMiddleware<AntiforgeryZwang>();
}
