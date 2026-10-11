using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zeltlotse.Konten.Domaene;

/// <summary>
/// Token und Code des Login-Links (AD-11): Token aus 32 Zufallsbytes (base64url, 43 Zeichen), Code aus 6 Ziffern.
/// Gespeichert werden nur Hashes; der Zweck steckt im Hash, damit ein Token eines anderen Zwecks nie passt.
/// </summary>
internal static class Geheimnisse
{
    /// <summary>Zweck des Login-Links im Token-Hash.</summary>
    public const string LoginZweck = "login";

    /// <summary>Länge eines Tokens in Zeichen (32 Bytes base64url ohne Auffüllung).</summary>
    public const int TokenLaenge = 43;

    public static string NeuesToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string NeuerCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>SHA-256 über „&lt;zweck&gt;:&lt;token&gt;“.</summary>
    public static byte[] TokenHash(string zweck, string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{zweck}:{token}"));

    /// <summary>SHA-256 über Zweck, Kennung der Anfrage und Code: Derselbe Code einer anderen Anfrage ergibt einen anderen Hash.</summary>
    public static byte[] CodeHash(Guid anfrageId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{LoginZweck}-code:{anfrageId:D}:{code}"));

    /// <summary>Vergleich in fester Zeit.</summary>
    public static bool CodePasst(LoginAnfrage anfrage, string? code) =>
        code is { Length: 6 } && code.All(char.IsAsciiDigit)
        && CryptographicOperations.FixedTimeEquals(CodeHash(anfrage.Id, code), anfrage.CodeHash);

    /// <summary>Nur Zeichen von base64url in der Länge eines Tokens; alles andere kann kein Token sein.</summary>
    public static bool KannTokenSein(string? token) =>
        token is { Length: TokenLaenge } && token.All(z => char.IsAsciiLetterOrDigit(z) || z is '-' or '_');
}
