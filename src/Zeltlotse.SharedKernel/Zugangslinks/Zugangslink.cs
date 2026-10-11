using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Zeltlotse.SharedKernel.Zugangslinks;

/// <summary>
/// Gemeinsame Bausteine der Zugangslinks (AD-11): Token aus 32 Zufallsbytes (base64url, 43 Zeichen), Hash mit festem
/// Zweck und Formprüfung. Gespeichert werden nur die Hashes; der Zweck steckt im Hash, damit ein Token eines anderen
/// Zwecks nie passt.
/// </summary>
public static class Zugangslink
{
    /// <summary>Länge eines Tokens in Zeichen (32 Bytes base64url ohne Auffüllung).</summary>
    public const int TokenLaenge = 43;

    public static string NeuesToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 über „&lt;zweck&gt;:&lt;token&gt;“.</summary>
    public static byte[] TokenHash(string zweck, string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{zweck}:{token}"));

    /// <summary>Nur Zeichen von base64url in der Länge eines Tokens; alles andere kann kein Token sein.</summary>
    public static bool KannTokenSein(string? token) =>
        token is { Length: TokenLaenge } && token.All(z => char.IsAsciiLetterOrDigit(z) || z is '-' or '_');
}
