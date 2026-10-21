using System.Security.Claims;

namespace Zeltlotse.SharedKernel.Anmeldung;

/// <summary>
/// Story 6-4 (AD-12, AD-25): Gemeinsame Angaben der Gast-Sitzung des QR-Teilnehmerzugangs. Ein eigenes Cookie-Schema neben der normalen Sitzung, nie
/// das Vorgabe-Schema: Für alle übrigen Endpunkte, für die Rollen und für die Antiforgery-Bindung bleibt ein Gast anonym. Das Cookie trägt nur die
/// Freizeit, eine Geräte-Kennung (neu je Einlösung) und den Stempel des QR-Links, nie das Token, einen Namen oder einen Schalter. Freizeiten stellt es
/// aus, der API-Host prüft es bei jeder Anfrage an einen Endpunkt mit <c>.NurQrGast()</c>.
/// </summary>
public static class GastSitzung
{
    /// <summary>Name des Authentifizierungs-Schemas der Gast-Sitzung.</summary>
    public const string Schema = "ZeltlotseGast";

    /// <summary>Name des Gast-Cookies (Präfix <c>__Host-</c>, AD-25).</summary>
    public const string CookieName = "__Host-Zeltlotse-Gast";

    public const string FreizeitClaim = "freizeit";

    public const string GeraetClaim = "geraet";

    public const string StempelClaim = "stempel";

    /// <summary>Die Identität einer Gast-Sitzung mit Freizeit, Geräte-Kennung und Stempel des QR-Links.</summary>
    public static ClaimsPrincipal Prinzipal(Guid freizeitId, Guid geraeteId, Guid stempel) =>
        new(new ClaimsIdentity(
            [
                new Claim(FreizeitClaim, freizeitId.ToString("D")),
                new Claim(GeraetClaim, geraeteId.ToString("D")),
                new Claim(StempelClaim, stempel.ToString("D")),
            ],
            Schema));

    /// <summary>Liest die drei Kennungen; <c>null</c>, wenn eine fehlt oder keine Kennung ist.</summary>
    public static (Guid FreizeitId, Guid GeraeteId, Guid Stempel)? Lesen(ClaimsPrincipal? prinzipal)
    {
        if (prinzipal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return Guid.TryParse(prinzipal.FindFirstValue(FreizeitClaim), out var freizeit)
               && Guid.TryParse(prinzipal.FindFirstValue(GeraetClaim), out var geraet)
               && Guid.TryParse(prinzipal.FindFirstValue(StempelClaim), out var stempel)
            ? (freizeit, geraet, stempel)
            : null;
    }
}
