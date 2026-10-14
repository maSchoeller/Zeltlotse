using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Rechte;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Rechte;

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
        services.AddSitzung();
        services.AddScoped<IKontextRollen, KontextRollen>();
        // Story 3-7: Schalter der Freizeit einmal pro Anfrage für Rechteprüfung und Pipeline (AD-17, AD-20).
        services.AddScoped<ISchalterStand, SchalterStand>();
        services.AddScoped<IAngemeldeteMail, AngemeldeteMail>();

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

    /// <summary>
    /// Nach dem Routing: Cookie-Richtlinie, Zugriffsbegrenzung, Sitzung (Authentifizierung und Autorisierung, AD-12), die
    /// zentrale Rechteprüfung mit dem Mandanten-Kontext aus der Route (AD-6, AD-12) und zuletzt der Antiforgery-Zwang. Er
    /// prüft das Token gegen die angemeldete Identität; eine Anfrage ohne Sitzung an einen angemeldeten Endpunkt endet schon
    /// vorher mit <c>401</c>, eine ohne Recht mit <c>403</c>/<c>404</c>. Alles läuft vor der Modellbindung der Endpunkte.
    /// </summary>
    public static IApplicationBuilder UseWebSicherheitNachRouting(this IApplicationBuilder app) =>
        app.UseCookiePolicy().UseRateLimiter().UseAuthentication().UseAuthorization()
            .UseMiddleware<RechtePruefung>().UseMiddleware<AntiforgeryZwang>();
}
