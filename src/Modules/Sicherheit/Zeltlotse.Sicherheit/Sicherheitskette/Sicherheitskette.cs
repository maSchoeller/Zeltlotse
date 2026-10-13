using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Sicherheit.Sicherheitskette;

/// <summary>
/// Story 2-8: Sicherheitskette einer Freizeit (FR-29, AD-3): was im Notfall zu tun ist, das Telefon der Leitung im Notfall und die
/// Ansprechperson am Heimatort. Je Freizeit höchstens eine Zeile; bearbeitet nach AD-19 über <see cref="FeldAendern"/>. Keine
/// Zeltlotse-Person (keine <c>PersonId</c>): Name und Telefon am Heimatort sind Arbeitsdaten der Freizeit, die die Leitung pflegt.
/// </summary>
internal sealed class Sicherheitskette : IOrganisationsEigen
{
    private Sicherheitskette()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public string? WasTun { get; private set; }

    public string? LeitungTelefon { get; private set; }

    public string? HeimatName { get; private set; }

    public string? HeimatTelefon { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Sicherheitskette Anlegen(Guid id, Guid organisationId, Guid freizeitId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
    };

    /// <summary>
    /// Die einzige Stelle der Regel „eingerichtet“: was zu tun ist, das Telefon der Leitung und das Telefon am Heimatort sind gesetzt;
    /// der Name am Heimatort ist freiwillig.
    /// </summary>
    public bool Eingerichtet() => Eingerichtet(WasTun, LeitungTelefon, HeimatTelefon);

    /// <summary>Dieselbe Regel für Werte ohne Zeile (keine Zeile = nicht eingerichtet).</summary>
    public static bool Eingerichtet(string? wasTun, string? leitungTelefon, string? heimatTelefon) =>
        wasTun is not null && leitungTelefon is not null && heimatTelefon is not null;

    public string? Wert(SicherheitsketteFeld feld) => feld switch
    {
        SicherheitsketteFeld.WasTun => WasTun,
        SicherheitsketteFeld.LeitungTelefon => LeitungTelefon,
        SicherheitsketteFeld.HeimatName => HeimatName,
        SicherheitsketteFeld.HeimatTelefon => HeimatTelefon,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es
    /// ist, und das Ergebnis ist <c>false</c>. Leerer Text oder nur Leerzeichen setzt das Feld auf <c>null</c>, sonst wird getrimmt.
    /// </summary>
    public bool FeldAendern(SicherheitsketteFeld feld, string? alterWert, string? neuerWert)
    {
        if (Normalisieren(Wert(feld)) != Normalisieren(alterWert))
        {
            return false;
        }

        var wert = Normalisieren(neuerWert);
        switch (feld)
        {
            case SicherheitsketteFeld.WasTun:
                WasTun = wert;
                break;
            case SicherheitsketteFeld.LeitungTelefon:
                LeitungTelefon = wert;
                break;
            case SicherheitsketteFeld.HeimatName:
                HeimatName = wert;
                break;
            case SicherheitsketteFeld.HeimatTelefon:
                HeimatTelefon = wert;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Leerer Text oder nur Leerzeichen = <c>null</c>, sonst getrimmt.</summary>
    public static string? Normalisieren(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
