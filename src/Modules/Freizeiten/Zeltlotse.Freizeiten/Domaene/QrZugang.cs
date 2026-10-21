using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>
/// Story 6-4 (AD-11, FR-64): Der QR-Link des Teilnehmer-Zugangs einer Freizeit, nach dem Muster <see cref="Anmeldelink"/>: mehrfach nutzbar und teilbar,
/// an die Freizeit gebunden, gültig bis Freizeitende. Je Freizeit höchstens einer. Gespeichert sind der Hash des Tokens mit dem Zweck
/// <see cref="Zweck"/> und das Token verschlüsselt (<c>ILinkSchutz</c>), damit die Leitung den Aushang wieder drucken kann. Der
/// <see cref="Stempel"/> steht im Gast-Cookie: <see cref="Erneuern"/> setzt Token und Stempel neu, damit verlieren der alte Link und alle damit
/// ausgestellten Gast-Sitzungen sofort ihre Wirkung.
/// </summary>
internal sealed class QrZugang : IOrganisationsEigen
{
    /// <summary>Zweck im Token-Hash; ein Token eines anderen Zwecks (Anmeldelink, Login) passt nie.</summary>
    public const string Zweck = "qr-zugang";

    private QrZugang()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    /// <summary>SHA-256 über Zweck und Token (<c>Zugangslink.TokenHash</c>).</summary>
    public byte[] TokenHash { get; private set; } = [];

    /// <summary>Das Token, verschlüsselt mit Freizeit und Zweck in den Zusatzdaten.</summary>
    public byte[] TokenGeschuetzt { get; private set; } = [];

    /// <summary>Kennung dieses Stands des Links; steht im Gast-Cookie und wechselt bei jedem Erneuern.</summary>
    public Guid Stempel { get; private set; }

    public DateTimeOffset ErzeugtAm { get; private set; }

    public static QrZugang Anlegen(
        Guid id, Guid organisationId, Guid freizeitId, byte[] tokenHash, byte[] tokenGeschuetzt, Guid stempel, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        TokenHash = tokenHash,
        TokenGeschuetzt = tokenGeschuetzt,
        Stempel = stempel,
        ErzeugtAm = jetzt,
    };

    /// <summary>Ersetzt Token und Stempel; der bisherige Link und die damit ausgestellten Gast-Sitzungen sind sofort ungültig.</summary>
    public void Erneuern(byte[] tokenHash, byte[] tokenGeschuetzt, Guid stempel, DateTimeOffset jetzt)
    {
        TokenHash = tokenHash;
        TokenGeschuetzt = tokenGeschuetzt;
        Stempel = stempel;
        ErzeugtAm = jetzt;
    }

    /// <summary>Nie Token oder Stempel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(QrZugang)}({Id})";
}
