using Microsoft.AspNetCore.CookiePolicy;

namespace Zeltlotse.Api.Sicherheit;

/// <summary>
/// Zentrale Cookie-Richtlinie (AD-25): Jedes Cookie, das der Host setzt oder löscht, hat das Präfix <c>__Host-</c>
/// und ist <c>Secure</c>, <c>HttpOnly</c>, <c>SameSite=Lax</c> mit <c>Path=/</c> und ohne <c>Domain</c>.
/// Ein Cookie ohne Präfix ist ein Programmierfehler und bricht die Anfrage ab.
/// </summary>
internal static class CookieRichtlinie
{
    public const string Praefix = "__Host-";

    public static IServiceCollection AddCookieRichtlinie(this IServiceCollection services) =>
        services.Configure<CookiePolicyOptions>(optionen =>
        {
            optionen.MinimumSameSitePolicy = SameSiteMode.Lax;
            optionen.HttpOnly = HttpOnlyPolicy.Always;
            optionen.Secure = CookieSecurePolicy.Always;
            optionen.OnAppendCookie = k => Erzwingen(k.CookieName, k.CookieOptions);
            optionen.OnDeleteCookie = k => Erzwingen(k.CookieName, k.CookieOptions);
        });

    private static void Erzwingen(string name, CookieOptions optionen)
    {
        if (!name.StartsWith(Praefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Das Cookie {name} hat nicht das Präfix {Praefix} (AD-25).");
        }

        optionen.Secure = true;
        optionen.HttpOnly = true;
        optionen.SameSite = SameSiteMode.Lax;
        optionen.Path = "/";
        optionen.Domain = null;
    }
}
