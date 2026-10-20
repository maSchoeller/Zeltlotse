using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1 (FR-54, FR-55): Die Beträge einer der sechs festen <see cref="Kostenkategorie"/>n in der Kalkulation einer Freizeit: Fixkosten und Kosten
/// pro Person und Tag (bei Verpflegung der Tagessatz). Höchstens eine Zeile je (Freizeit, Kategorie); ohne Zeile sind beide 0. Die Zeile entsteht beim
/// ersten Ändern. Keine Kopplung an Essensplan oder Küche.
/// </summary>
/// <remarks>Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug.</remarks>
internal sealed class KategoriePosten : IOrganisationsEigen
{
    private KategoriePosten()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public Kostenkategorie Kategorie { get; private set; }

    /// <summary>Feste Kosten in Euro.</summary>
    public decimal Fixkosten { get; private set; }

    /// <summary>Kosten pro Person und Tag in Euro.</summary>
    public decimal ProPersonUndTag { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Eine neue Kategorie mit 0 €.</summary>
    public static KategoriePosten Anlegen(Guid id, Guid organisationId, Guid freizeitId, Kostenkategorie kategorie) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Kategorie = kategorie,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert, z. B. „7,5“.</summary>
    public string Wert(KategorieFeld feld) => feld switch
    {
        KategorieFeld.Fixkosten => FinanzenWerte.BetragText(Fixkosten),
        KategorieFeld.ProPersonUndTag => FinanzenWerte.BetragText(ProPersonUndTag),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Ändert ein Feld nach der Konfliktregel aus AD-19 (alter Wert als Betrag verglichen); bei Abweichung <c>false</c> ohne Änderung.</summary>
    public bool FeldAendern(KategorieFeld feld, string? alterWert, string? neuerWert)
    {
        if (Wert(feld) != FinanzenWerte.BetragVergleichswert(alterWert))
        {
            return false;
        }

        var betrag = FinanzenWerte.BetragLesen(neuerWert) ?? throw new ArgumentException(FinanzenWerte.BetragFehlerText, nameof(neuerWert));
        switch (feld)
        {
            case KategorieFeld.Fixkosten:
                Fixkosten = betrag;
                break;
            case KategorieFeld.ProPersonUndTag:
                ProPersonUndTag = betrag;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }
}
