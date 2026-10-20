using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1 (FR-54): Ein freier Einnahmeposten der Kalkulation einer Freizeit, z. B. ein Zuschuss oder ein Rabatt der Organisation (positiver Betrag).
/// Felder ändern sich nach AD-19 nur über <see cref="FeldAendern"/>.
/// </summary>
/// <remarks>Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug; die Bezeichnung nennt keine Namen (Doku).</remarks>
internal sealed class Einnahmeposten : IOrganisationsEigen
{
    private Einnahmeposten()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="EinnahmepostenFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public string Bezeichnung { get; private set; } = "";

    /// <summary>Betrag in Euro, 0 bis <see cref="KategorieFelder.BetragMaximum"/>.</summary>
    public decimal Betrag { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Einnahmeposten Anlegen(Guid id, Guid organisationId, Guid freizeitId, string bezeichnung, decimal betrag) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Bezeichnung = bezeichnung.Trim(),
        Betrag = betrag,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert.</summary>
    public string Wert(EinnahmepostenFeld feld) => feld switch
    {
        EinnahmepostenFeld.Bezeichnung => Bezeichnung,
        EinnahmepostenFeld.Betrag => FinanzenWerte.BetragText(Betrag),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Ändert ein Feld nach der Konfliktregel aus AD-19; weicht der alte Wert ab, <c>false</c> ohne Änderung.</summary>
    public bool FeldAendern(EinnahmepostenFeld feld, string? alterWert, string? neuerWert)
    {
        var vergleich = feld == EinnahmepostenFeld.Betrag ? FinanzenWerte.BetragVergleichswert(alterWert) : FinanzenWerte.TextOderNull(alterWert);
        if (Wert(feld) != vergleich)
        {
            return false;
        }

        switch (feld)
        {
            case EinnahmepostenFeld.Bezeichnung:
                Bezeichnung = (neuerWert ?? "").Trim();
                break;
            case EinnahmepostenFeld.Betrag:
                Betrag = FinanzenWerte.BetragLesen(neuerWert) ?? throw new ArgumentException(FinanzenWerte.BetragFehlerText, nameof(neuerWert));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }
}
