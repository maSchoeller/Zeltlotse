using System.Security.Claims;

namespace Zeltlotse.SharedKernel.Anmeldung;

/// <summary>
/// Gemeinsame Angaben der Cookie-Sitzung (AD-12, AD-25): Name des Schemas und der Claims. Die Sitzung trägt nur
/// <c>KontoId</c>, <c>PersonId</c> und den <c>SitzungsStempel</c>, nie Rollen. Konten stellt sie aus, der API-Host prüft sie.
/// </summary>
public static class Sitzung
{
    /// <summary>Name des Authentifizierungs-Schemas der Cookie-Sitzung.</summary>
    public const string Schema = "Zeltlotse";

    /// <summary>Name des Sitzungs-Cookies (Präfix <c>__Host-</c>, AD-25).</summary>
    public const string CookieName = "__Host-Zeltlotse";

    /// <summary>Gleitende Gültigkeit der Sitzung.</summary>
    public static readonly TimeSpan Dauer = TimeSpan.FromDays(30);

    /// <summary>Claim der Konto-Kennung; als NameIdentifier bindet auch das Antiforgery-Token daran.</summary>
    public const string KontoIdClaim = ClaimTypes.NameIdentifier;

    public const string PersonIdClaim = "zeltlotse:person";

    public const string StempelClaim = "zeltlotse:stempel";

    /// <summary>Die Identität einer Sitzung mit genau den drei Claims.</summary>
    public static ClaimsPrincipal Prinzipal(Guid kontoId, Guid personId, Guid sitzungsStempel) =>
        new(new ClaimsIdentity(
            [
                new Claim(KontoIdClaim, kontoId.ToString("D")),
                new Claim(PersonIdClaim, personId.ToString("D")),
                new Claim(StempelClaim, sitzungsStempel.ToString("D")),
            ],
            Schema));

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
}
