using Microsoft.AspNetCore.Identity;
using Zeltlotse.Konten.Domaene;

namespace Zeltlotse.Konten.Passwort;

/// <summary>
/// Passwörter (AD-12): 10 bis 200 Zeichen ohne Zeichenklassen-Zwang, gespeichert nur als Prüfwert des
/// <see cref="PasswordHasher{TUser}"/> von ASP.NET Core Identity (Hashformat v3).
/// </summary>
internal static class PasswortRegel
{
    public const int MindestLaenge = 10;

    public const int HoechstLaenge = 200;

    /// <summary>Hasher mit Hashformat v3 (PBKDF2 mit SHA-512).</summary>
    public static PasswordHasher<Konto> NeuerHasher() =>
        new(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3 }));
}
