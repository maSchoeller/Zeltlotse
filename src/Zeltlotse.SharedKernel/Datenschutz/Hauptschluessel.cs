using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Der Hauptschlüssel (AD-14): 32 Bytes aus der Geheimnisverwaltung, getrennt von Datenbank und Dateispeicher.
/// Er umhüllt die Personen-Schlüssel mit AES-256-GCM; die PersonId ist Teil der Authentifizierungsdaten,
/// sodass ein umhüllter Schlüssel nur für seine Person auszupacken ist.
/// </summary>
public sealed class Hauptschluessel
{
    /// <summary>Konfigurationsschlüssel; der Wert ist Base64 mit genau 32 Bytes.</summary>
    public const string Konfigurationsschluessel = "Zeltlotse:Verschluesselung:Hauptschluessel";

    internal const int SchluesselLaenge = 32;

    private const byte UmhuellungsVersion = 1;
    private const int NonceLaenge = 12;
    private const int TagLaenge = 16;
    private const int UmhuelltLaenge = 1 + NonceLaenge + TagLaenge + SchluesselLaenge;

    private static readonly string Fehlermeldung =
        $"Der Hauptschlüssel ({Konfigurationsschluessel}) fehlt oder ist kein Base64-Wert mit genau {SchluesselLaenge} Bytes.";

    private readonly byte[] _schluessel;

    private Hauptschluessel(byte[] schluessel) => _schluessel = schluessel;

    /// <summary>Liest und prüft den Hauptschlüssel; die Meldung nennt den Wert nie.</summary>
    public static Hauptschluessel AusKonfiguration(IConfiguration konfiguration) =>
        AusBase64(konfiguration[Konfigurationsschluessel]);

    /// <summary>Prüft den Base64-Wert; die Meldung nennt den Wert nie.</summary>
    public static Hauptschluessel AusBase64(string? wert)
    {
        if (string.IsNullOrWhiteSpace(wert))
        {
            throw new InvalidOperationException(Fehlermeldung);
        }

        var puffer = new byte[(wert.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(wert.Trim(), puffer, out var laenge) || laenge != SchluesselLaenge)
        {
            CryptographicOperations.ZeroMemory(puffer);
            throw new InvalidOperationException(Fehlermeldung);
        }

        var schluessel = puffer[..SchluesselLaenge];
        CryptographicOperations.ZeroMemory(puffer);
        return new Hauptschluessel(schluessel);
    }

    /// <summary>
    /// Erzeugt einen neuen Personen-Schlüssel (32 Zufallsbytes) und liefert ihn umhüllt zum Speichern in
    /// <c>personen.personen_schluessel</c>. Der rohe Schlüssel verlässt diese Methode nicht.
    /// </summary>
    public byte[] NeuenPersonenSchluesselUmhuellen(Guid personId)
    {
        var schluessel = RandomNumberGenerator.GetBytes(SchluesselLaenge);
        try
        {
            return Umhuellen(personId, schluessel);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(schluessel);
        }
    }

    internal byte[] Umhuellen(Guid personId, ReadOnlySpan<byte> schluessel)
    {
        var umhuellt = new byte[UmhuelltLaenge];
        umhuellt[0] = UmhuellungsVersion;
        var nonce = umhuellt.AsSpan(1, NonceLaenge);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_schluessel, TagLaenge);
        aes.Encrypt(nonce, schluessel, umhuellt.AsSpan(1 + NonceLaenge + TagLaenge), umhuellt.AsSpan(1 + NonceLaenge, TagLaenge), Zusatzdaten(personId));
        return umhuellt;
    }

    /// <summary>Packt einen umhüllten Personen-Schlüssel aus; wirft bei Manipulation oder fremder PersonId.</summary>
    internal byte[] Auspacken(Guid personId, byte[] umhuellt)
    {
        if (umhuellt.Length != UmhuelltLaenge || umhuellt[0] != UmhuellungsVersion)
        {
            throw new CryptographicException("Der umhüllte Personen-Schlüssel hat ein unbekanntes Format.");
        }

        var schluessel = new byte[SchluesselLaenge];
        using var aes = new AesGcm(_schluessel, TagLaenge);
        aes.Decrypt(
            umhuellt.AsSpan(1, NonceLaenge),
            umhuellt.AsSpan(1 + NonceLaenge + TagLaenge),
            umhuellt.AsSpan(1 + NonceLaenge, TagLaenge),
            schluessel,
            Zusatzdaten(personId));
        return schluessel;
    }

    /// <summary>Nie den Schlüssel ausgeben.</summary>
    public override string ToString() => nameof(Hauptschluessel);

    private static byte[] Zusatzdaten(Guid personId) => [UmhuellungsVersion, .. personId.ToByteArray()];
}
