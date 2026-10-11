using System.Security.Cryptography;

namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Unveränderlicher Chiffretext eines <c>[Besonders]</c>-Felds (AD-14). Erzeugt wird er nur von <see cref="IVerschluesselung"/>
/// (und beim Lesen aus der Datenbank); außerhalb des SharedKernel gibt es keinen Weg, ihn aus eigenen Bytes zu bauen.
/// <para>
/// Aufbau: Formatversion (1 Byte), Inhaltsart (1 Byte), PersonId (16 Bytes), Nonce (12 Bytes), Tag (16 Bytes), Chiffre.
/// Die ersten 18 Bytes sind die zusätzlichen Authentifizierungsdaten von AES-256-GCM: Wer Version, Art oder PersonId ändert,
/// bricht die Entschlüsselung.
/// </para>
/// </summary>
public sealed class Geschuetzt : IEquatable<Geschuetzt>
{
    internal const byte Version1 = 1;
    internal const int KopfLaenge = 2 + 16;
    internal const int NonceLaenge = 12;
    internal const int TagLaenge = 16;
    internal const int MindestLaenge = KopfLaenge + NonceLaenge + TagLaenge;

    private readonly byte[] _daten;

    private Geschuetzt(byte[] daten)
    {
        _daten = daten;
        PersonId = new Guid(daten.AsSpan(2, 16));
    }

    /// <summary>Die Person, mit deren Schlüssel verschlüsselt wurde; Teil der Authentifizierungsdaten.</summary>
    public Guid PersonId { get; }

    internal byte Formatversion => _daten[0];

    internal Inhaltsart Art => (Inhaltsart)_daten[1];

    internal ReadOnlySpan<byte> Kopf => _daten.AsSpan(0, KopfLaenge);

    internal ReadOnlySpan<byte> Nonce => _daten.AsSpan(KopfLaenge, NonceLaenge);

    internal ReadOnlySpan<byte> Tag => _daten.AsSpan(KopfLaenge + NonceLaenge, TagLaenge);

    internal ReadOnlySpan<byte> Chiffre => _daten.AsSpan(MindestLaenge);

    /// <summary>Kopie der gespeicherten Bytes (für Datenbank und Dateispeicher).</summary>
    public byte[] AlsBytes() => [.. _daten];

    /// <summary>Nur für die Datenbank-Abbildung und den SharedKernel: prüft Länge und Formatversion.</summary>
    internal static Geschuetzt AusBytes(byte[] daten)
    {
        ArgumentNullException.ThrowIfNull(daten);
        if (daten.Length < MindestLaenge)
        {
            throw new CryptographicException("Der Chiffretext ist zu kurz.");
        }

        if (daten[0] != Version1)
        {
            throw new CryptographicException($"Unbekannte Formatversion {daten[0]} des Chiffretexts.");
        }

        return new Geschuetzt([.. daten]);
    }

    internal static Geschuetzt Zusammensetzen(Inhaltsart art, Guid personId, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> chiffre)
    {
        var daten = new byte[MindestLaenge + chiffre.Length];
        KopfSchreiben(daten, art, personId);
        nonce.CopyTo(daten.AsSpan(KopfLaenge));
        tag.CopyTo(daten.AsSpan(KopfLaenge + NonceLaenge));
        chiffre.CopyTo(daten.AsSpan(MindestLaenge));
        return new Geschuetzt(daten);
    }

    internal static void KopfSchreiben(Span<byte> ziel, Inhaltsart art, Guid personId)
    {
        ziel[0] = Version1;
        ziel[1] = (byte)art;
        personId.TryWriteBytes(ziel.Slice(2, 16));
    }

    public bool Equals(Geschuetzt? other) => other is not null && _daten.AsSpan().SequenceEqual(other._daten);

    public override bool Equals(object? obj) => obj is Geschuetzt other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_daten);
        return hash.ToHashCode();
    }

    /// <summary>Nie den Inhalt ausgeben, auch nicht als Chiffretext.</summary>
    public override string ToString() => $"Geschuetzt(Person {PersonId})";
}

/// <summary>Was verschlüsselt wurde; Teil der Authentifizierungsdaten, damit Text nicht als Bytes gelesen wird und umgekehrt.</summary>
internal enum Inhaltsart : byte
{
    Text = 1,
    Bytes = 2,
}
