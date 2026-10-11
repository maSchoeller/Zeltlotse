using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zeltlotse.Konten.Domaene;

/// <summary>
/// Login-spezifische Geheimnisse (AD-11): Zweck des Login-Links und der sechsstellige Login-Code. Token, Token-Hash und
/// Formprüfung kommen aus <see cref="SharedKernel.Zugangslinks.Zugangslink"/>.
/// </summary>
internal static class Geheimnisse
{
    /// <summary>Zweck des Login-Links im Token-Hash.</summary>
    public const string LoginZweck = "login";

    public static string NeuerCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>SHA-256 über Zweck, Kennung der Anfrage und Code: Derselbe Code einer anderen Anfrage ergibt einen anderen Hash.</summary>
    public static byte[] CodeHash(Guid anfrageId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{LoginZweck}-code:{anfrageId:D}:{code}"));

    /// <summary>Vergleich in fester Zeit.</summary>
    public static bool CodePasst(LoginAnfrage anfrage, string? code) =>
        code is { Length: 6 } && code.All(char.IsAsciiDigit)
        && CryptographicOperations.FixedTimeEquals(CodeHash(anfrage.Id, code), anfrage.CodeHash);
}
