using System.Globalization;
using System.Security.Claims;

namespace Zeltlotse.SharedKernel.Anmeldung;

/// <summary>
/// Gemeinsame Angaben der Cookie-Sitzung (AD-12, AD-25): Name des Schemas und der Claims. Die Sitzung trägt nur
/// <c>KontoId</c>, <c>PersonId</c>, den <c>SitzungsStempel</c> und den Zeitpunkt der Anmeldung, nie Rollen. Konten stellt sie
/// aus, der API-Host prüft sie.
/// </summary>
public static class Sitzung
{
    /// <summary>Name des Authentifizierungs-Schemas der Cookie-Sitzung.</summary>
    public const string Schema = "Zeltlotse";

    /// <summary>Name des Sitzungs-Cookies (Präfix <c>__Host-</c>, AD-25).</summary>
    public const string CookieName = "__Host-Zeltlotse";

    /// <summary>Gleitende Gültigkeit der Sitzung.</summary>
    public static readonly TimeSpan Dauer = TimeSpan.FromDays(30);

    /// <summary>
    /// So lange gilt eine Anmeldung als frisch für Endpunkte mit erneuter Anmeldung (AD-25, <c>.ErneuteAnmeldung()</c>).
    /// </summary>
    public static readonly TimeSpan FrischeDauer = TimeSpan.FromMinutes(10);

    /// <summary>Claim der Konto-Kennung; als NameIdentifier bindet auch das Antiforgery-Token daran.</summary>
    public const string KontoIdClaim = ClaimTypes.NameIdentifier;

    public const string PersonIdClaim = "zeltlotse:person";

    public const string StempelClaim = "zeltlotse:stempel";

    /// <summary>Zeitpunkt der Anmeldung (Unix-Millisekunden, UTC); bleibt beim Neu-Ausstellen derselben Anmeldung erhalten.</summary>
    public const string AngemeldetClaim = "zeltlotse:angemeldet";

    /// <summary>Die Identität einer Sitzung mit den drei Kennungen und, falls bekannt, dem Zeitpunkt der Anmeldung.</summary>
    public static ClaimsPrincipal Prinzipal(Guid kontoId, Guid personId, Guid sitzungsStempel, DateTimeOffset? angemeldetAm = null)
    {
        var claims = new List<Claim>
        {
            new(KontoIdClaim, kontoId.ToString("D")),
            new(PersonIdClaim, personId.ToString("D")),
            new(StempelClaim, sitzungsStempel.ToString("D")),
        };
        if (angemeldetAm is { } am)
        {
            claims.Add(new Claim(AngemeldetClaim, am.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Schema));
    }

    /// <summary>Liest die drei Kennungen; <c>null</c>, wenn eine fehlt oder keine Kennung ist.</summary>
    public static (Guid KontoId, Guid PersonId, Guid SitzungsStempel)? Lesen(ClaimsPrincipal? prinzipal)
    {
        if (prinzipal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return Guid.TryParse(prinzipal.FindFirstValue(KontoIdClaim), out var konto)
               && Guid.TryParse(prinzipal.FindFirstValue(PersonIdClaim), out var person)
               && Guid.TryParse(prinzipal.FindFirstValue(StempelClaim), out var stempel)
            ? (konto, person, stempel)
            : null;
    }

    /// <summary>Der Zeitpunkt der Anmeldung; <c>null</c> ohne Sitzung oder bei einer Sitzung ohne diesen Claim.</summary>
    public static DateTimeOffset? AngemeldetAm(ClaimsPrincipal? prinzipal) =>
        Lesen(prinzipal) is not null
        && long.TryParse(prinzipal!.FindFirstValue(AngemeldetClaim), NumberStyles.Integer, CultureInfo.InvariantCulture, out var millisekunden)
            ? DateTimeOffset.FromUnixTimeMilliseconds(millisekunden)
            : null;
}
