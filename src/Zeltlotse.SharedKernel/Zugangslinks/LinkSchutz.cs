using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.SharedKernel.Zugangslinks;

/// <summary>
/// Schutz dauerhaft geteilter Links (AD-11): Ihr Token wird zusätzlich zum Hash verschlüsselt gespeichert, damit es sich wieder
/// anzeigen und drucken lässt. Links gehören keiner Person, darum kein Personen-Schlüssel (AD-14), sondern ein aus dem
/// Hauptschlüssel abgeleiteter Schlüssel. Bezug (z. B. die Freizeit) und Zweck stehen in den Zusatzdaten: Ein Chiffretext lässt
/// sich nur für seinen Bezug und Zweck lesen.
/// </summary>
public interface ILinkSchutz
{
    byte[] Schuetzen(string token, Guid bezug, string zweck);

    /// <exception cref="CryptographicException">Verändert, falscher Bezug, falscher Zweck oder anderer Hauptschlüssel.</exception>
    string Lesen(byte[] geschuetzt, Guid bezug, string zweck);
}

/// <summary>AES-256-GCM; Aufbau: Version (1 Byte), Nonce (12), Tag (16), Chiffre.</summary>
internal sealed class LinkSchutz : ILinkSchutz
{
    /// <summary>HKDF-Zweck des abgeleiteten Schlüssels.</summary>
    public const string SchluesselZweck = "zugangslink";

    private const byte Version = 1;
    private const int NonceLaenge = 12;
    private const int TagLaenge = 16;
    private const int KopfLaenge = 1 + NonceLaenge + TagLaenge;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _schluessel;

    public LinkSchutz(Hauptschluessel hauptschluessel) => _schluessel = hauptschluessel.Ableiten(SchluesselZweck);

    public byte[] Schuetzen(string token, Guid bezug, string zweck)
    {
        ArgumentNullException.ThrowIfNull(token);
        var klartext = Utf8.GetBytes(token);
        var ergebnis = new byte[KopfLaenge + klartext.Length];
        ergebnis[0] = Version;
        var nonce = ergebnis.AsSpan(1, NonceLaenge);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_schluessel, TagLaenge);
        aes.Encrypt(nonce, klartext, ergebnis.AsSpan(KopfLaenge), ergebnis.AsSpan(1 + NonceLaenge, TagLaenge), Zusatzdaten(bezug, zweck));
        return ergebnis;
    }

    public string Lesen(byte[] geschuetzt, Guid bezug, string zweck)
    {
        ArgumentNullException.ThrowIfNull(geschuetzt);
        if (geschuetzt.Length < KopfLaenge || geschuetzt[0] != Version)
        {
            throw new CryptographicException("Der geschützte Link hat ein unbekanntes Format.");
        }

        var klartext = new byte[geschuetzt.Length - KopfLaenge];
        using var aes = new AesGcm(_schluessel, TagLaenge);
        aes.Decrypt(
            geschuetzt.AsSpan(1, NonceLaenge),
            geschuetzt.AsSpan(KopfLaenge),
            geschuetzt.AsSpan(1 + NonceLaenge, TagLaenge),
            klartext,
            Zusatzdaten(bezug, zweck));
        return Utf8.GetString(klartext);
    }

    /// <summary>Nie den Schlüssel ausgeben.</summary>
    public override string ToString() => nameof(LinkSchutz);

    private static byte[] Zusatzdaten(Guid bezug, string zweck) => [Version, .. bezug.ToByteArray(), .. Utf8.GetBytes(zweck)];
}

/// <summary>Registrierung des <see cref="ILinkSchutz"/> (im API-Host).</summary>
public static class LinkSchutzRegistrierung
{
    public static IServiceCollection AddLinkSchutz(this IServiceCollection services)
    {
        services.AddSharedKernel();
        services.TryAddSingleton<ILinkSchutz>(sp => new LinkSchutz(sp.GetRequiredService<Hauptschluessel>()));
        return services;
    }
}
