using System.Security.Cryptography;
using System.Text;

namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Zentrale Verschlüsselung der <c>[Besonders]</c>-Felder und -Dateien (AD-14): AES-256-GCM mit dem Schlüssel der Person.
/// Handler rufen sie ausdrücklich auf, bevor sie speichern; einen Klartext-Rückfall gibt es nicht.
/// </summary>
public interface IVerschluesselung
{
    Task<Geschuetzt> VerschluesselnAsync(Guid personId, string klartext, CancellationToken abbruch = default);

    Task<Geschuetzt> VerschluesselnAsync(Guid personId, byte[] klartext, CancellationToken abbruch = default);

    /// <exception cref="PersonenSchluesselFehltAusnahme">Der Schlüssel der Person ist vernichtet.</exception>
    /// <exception cref="CryptographicException">Chiffretext verändert oder einer anderen Person untergeschoben.</exception>
    Task<string> EntschluesselnAsync(Geschuetzt geschuetzt, CancellationToken abbruch = default);

    /// <inheritdoc cref="EntschluesselnAsync"/>
    Task<byte[]> EntschluesselnBytesAsync(Geschuetzt geschuetzt, CancellationToken abbruch = default);
}

/// <summary>
/// Liest den umhüllten Personen-Schlüssel. Personen besitzt die Tabelle und setzt die Schnittstelle um;
/// so verschlüsseln alle Module über den SharedKernel, ohne Personen direkt aufzurufen.
/// </summary>
public interface IPersonenSchluesselQuelle
{
    /// <summary>Der umhüllte Schlüssel oder <c>null</c>, wenn es keinen (mehr) gibt.</summary>
    Task<byte[]?> UmhuelltenSchluesselLesenAsync(Guid personId, CancellationToken abbruch = default);
}

/// <summary>Der Personen-Schlüssel fehlt: Die Person ist gelöscht (Crypto-Shredding), ihre Daten sind nicht mehr lesbar.</summary>
public sealed class PersonenSchluesselFehltAusnahme(Guid personId)
    : Exception($"Für die Person {personId} gibt es keinen Schlüssel (mehr).")
{
    public Guid PersonId { get; } = personId;
}

internal sealed class Verschluesselung(Hauptschluessel hauptschluessel, IPersonenSchluesselQuelle quelle) : IVerschluesselung
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public Task<Geschuetzt> VerschluesselnAsync(Guid personId, string klartext, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(klartext);
        return VerschluesselnAsync(personId, Inhaltsart.Text, Utf8.GetBytes(klartext), abbruch);
    }

    public Task<Geschuetzt> VerschluesselnAsync(Guid personId, byte[] klartext, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(klartext);
        return VerschluesselnAsync(personId, Inhaltsart.Bytes, klartext, abbruch);
    }

    public async Task<string> EntschluesselnAsync(Geschuetzt geschuetzt, CancellationToken abbruch = default) =>
        Utf8.GetString(await EntschluesselnAsync(geschuetzt, Inhaltsart.Text, abbruch));

    public Task<byte[]> EntschluesselnBytesAsync(Geschuetzt geschuetzt, CancellationToken abbruch = default) =>
        EntschluesselnAsync(geschuetzt, Inhaltsart.Bytes, abbruch);

    private async Task<Geschuetzt> VerschluesselnAsync(Guid personId, Inhaltsart art, byte[] klartext, CancellationToken abbruch)
    {
        var schluessel = await PersonenSchluesselAsync(personId, abbruch);
        try
        {
            Span<byte> kopf = stackalloc byte[Geschuetzt.KopfLaenge];
            Geschuetzt.KopfSchreiben(kopf, art, personId);
            Span<byte> nonce = stackalloc byte[Geschuetzt.NonceLaenge];
            RandomNumberGenerator.Fill(nonce);
            Span<byte> tag = stackalloc byte[Geschuetzt.TagLaenge];
            var chiffre = new byte[klartext.Length];

            using var aes = new AesGcm(schluessel, Geschuetzt.TagLaenge);
            aes.Encrypt(nonce, klartext, chiffre, tag, kopf);
            return Geschuetzt.Zusammensetzen(art, personId, nonce, tag, chiffre);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(schluessel);
        }
    }

    private async Task<byte[]> EntschluesselnAsync(Geschuetzt geschuetzt, Inhaltsart erwartet, CancellationToken abbruch)
    {
        ArgumentNullException.ThrowIfNull(geschuetzt);
        if (geschuetzt.Art != erwartet)
        {
            throw new InvalidOperationException($"Der Chiffretext enthält {geschuetzt.Art}, erwartet war {erwartet}.");
        }

        var schluessel = await PersonenSchluesselAsync(geschuetzt.PersonId, abbruch);
        try
        {
            var klartext = new byte[geschuetzt.Chiffre.Length];
            using var aes = new AesGcm(schluessel, Geschuetzt.TagLaenge);
            aes.Decrypt(geschuetzt.Nonce, geschuetzt.Chiffre, geschuetzt.Tag, klartext, geschuetzt.Kopf);
            return klartext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(schluessel);
        }
    }

    private async Task<byte[]> PersonenSchluesselAsync(Guid personId, CancellationToken abbruch)
    {
        var umhuellt = await quelle.UmhuelltenSchluesselLesenAsync(personId, abbruch)
                       ?? throw new PersonenSchluesselFehltAusnahme(personId);
        return hauptschluessel.Auspacken(personId, umhuellt);
    }
}
