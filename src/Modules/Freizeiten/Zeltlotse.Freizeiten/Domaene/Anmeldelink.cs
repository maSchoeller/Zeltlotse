using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>Art eines <see cref="Anmeldelink"/>; gespeichert als Zahl, nur anhängen.</summary>
internal enum LinkArt : short
{
    Teilnehmende = 1,
    Mitarbeitende = 2,
}

/// <summary>
/// Anmeldelink einer Freizeit (AD-11, FR-16): mehrfach nutzbar und teilbar, an die Freizeit gebunden, gültig bis Freizeitende.
/// Je Freizeit und Art genau einer. Gespeichert sind der Hash des Tokens mit dem Zweck der Art und das Token verschlüsselt
/// (<c>ILinkSchutz</c>), damit die Leitung den Link wieder anzeigen kann. <see cref="Erneuern"/> ersetzt beides; der alte Link
/// ist danach sofort ungültig. Der Aufruf zeigt nur Seiten, eingelöst wird erst mit dem Absenden einer Anmeldung (Story 2-2).
/// </summary>
internal sealed class Anmeldelink : IOrganisationsEigen
{
    /// <summary>Zweck im Token-Hash für Teilnehmende.</summary>
    public const string ZweckTeilnehmende = "anmeldelink-teilnehmende";

    /// <summary>Zweck im Token-Hash für Mitarbeitende.</summary>
    public const string ZweckMitarbeitende = "anmeldelink-mitarbeitende";

    private Anmeldelink()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public LinkArt Art { get; private init; }

    /// <summary>SHA-256 über Zweck und Token (<c>Zugangslink.TokenHash</c>).</summary>
    public byte[] TokenHash { get; private set; } = [];

    /// <summary>Das Token, verschlüsselt mit Freizeit und Zweck in den Zusatzdaten.</summary>
    public byte[] TokenGeschuetzt { get; private set; } = [];

    public DateTimeOffset ErzeugtAm { get; private set; }

    /// <summary>Der Zweck einer Art; ein Token eines anderen Zwecks (andere Art, Leitungs-Einladung, Login) passt nie.</summary>
    public static string Zweck(LinkArt art) => art switch
    {
        LinkArt.Teilnehmende => ZweckTeilnehmende,
        LinkArt.Mitarbeitende => ZweckMitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    public static Anmeldelink Anlegen(
        Guid id, Guid organisationId, Guid freizeitId, LinkArt art, byte[] tokenHash, byte[] tokenGeschuetzt, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Art = art,
        TokenHash = tokenHash,
        TokenGeschuetzt = tokenGeschuetzt,
        ErzeugtAm = jetzt,
    };

    /// <summary>Ersetzt das Token; der bisherige Link ist sofort ungültig.</summary>
    public void Erneuern(byte[] tokenHash, byte[] tokenGeschuetzt, DateTimeOffset jetzt)
    {
        TokenHash = tokenHash;
        TokenGeschuetzt = tokenGeschuetzt;
        ErzeugtAm = jetzt;
    }
}

/// <summary>
/// Platzlage einer Freizeit (FR-67): ausgebucht, sobald die bestätigten Teilnehmenden die Höchstzahl erreichen. Ohne Höchstzahl
/// nie; über den Link für Mitarbeitende nie, weil Mitarbeitende immer dazukommen können (FR-16). Anmelden bleibt möglich.
/// </summary>
internal static class Platzlage
{
    public static bool Ausgebucht(int? hoechstzahlTeilnehmende, int bestaetigteTeilnehmende, LinkArt art) =>
        art == LinkArt.Teilnehmende && hoechstzahlTeilnehmende is { } hoechstzahl && bestaetigteTeilnehmende >= hoechstzahl;
}
