using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Zeltlotse.Konten.Contracts.Sitzungen;
using Zeltlotse.SharedKernel.Anmeldung;

namespace Zeltlotse.Api.Sicherheit;

/// <summary>
/// Cookie-Sitzung (AD-12, AD-25): Cookie-Authentifizierung des Frameworks mit <c>__Host-Zeltlotse</c>, gleitend 30 Tage.
/// Bei jeder Anfrage unter <c>/api</c> prüft der Host über <see cref="IKontoPruefung"/>, dass das Konto existiert und der
/// Sitzungsstempel passt; sonst gilt die Anfrage als nicht angemeldet und das Cookie wird gelöscht. Außerhalb von
/// <c>/api</c> (Statikdateien, öffentliche Seiten) zählt die Sitzung nicht und kostet keine Datenbankabfrage.
/// Ohne Sitzung antworten angemeldete Endpunkte mit <c>401</c> (keine Weiterleitung), ohne Recht mit <c>403</c>.
/// </summary>
internal static class SitzungsRegistrierung
{
    public static IServiceCollection AddSitzung(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAngemeldet, HttpAngemeldet>();
        services.AddAuthorization();
        services.AddAuthentication(Sitzung.Schema)
            .AddCookie(Sitzung.Schema, optionen =>
            {
                optionen.Cookie.Name = Sitzung.CookieName;
                optionen.Cookie.HttpOnly = true;
                optionen.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                optionen.Cookie.SameSite = SameSiteMode.Lax;
                optionen.Cookie.Path = "/";
                optionen.ExpireTimeSpan = Sitzung.Dauer;
                optionen.SlidingExpiration = true;
                optionen.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = PruefenAsync,
                    OnRedirectToLogin = kontext => StatusSetzen(kontext, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = kontext => StatusSetzen(kontext, StatusCodes.Status403Forbidden),
                };
            });
        return services;
    }

    private static async Task PruefenAsync(CookieValidatePrincipalContext kontext)
    {
        if (!ApiPfad.Ist(kontext.Request.Path))
        {
            kontext.RejectPrincipal();
            return;
        }

        var sitzung = Sitzung.Lesen(kontext.Principal);
        var gueltig = sitzung is { } s && await kontext.HttpContext.RequestServices.GetRequiredService<IKontoPruefung>()
            .SitzungGueltigAsync(s.KontoId, s.PersonId, s.SitzungsStempel, kontext.HttpContext.RequestAborted);
        if (!gueltig)
        {
            kontext.RejectPrincipal();
            await kontext.HttpContext.SignOutAsync(Sitzung.Schema);
        }
    }

    private static Task StatusSetzen(RedirectContext<CookieAuthenticationOptions> kontext, int status)
    {
        kontext.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}

/// <summary><see cref="IAngemeldet"/> aus der Sitzung der laufenden Anfrage; ohne gültige Sitzung beide Werte <c>null</c>.</summary>
internal sealed class HttpAngemeldet(IHttpContextAccessor anfrage) : IAngemeldet
{
    public Guid? KontoId => Sitzung.Lesen(anfrage.HttpContext?.User)?.KontoId;

    public Guid? PersonId => Sitzung.Lesen(anfrage.HttpContext?.User)?.PersonId;
}
