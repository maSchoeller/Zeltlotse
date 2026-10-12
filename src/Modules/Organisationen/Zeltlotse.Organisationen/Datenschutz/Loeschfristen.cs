using System.Globalization;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Organisationen.Datenschutz;

/// <summary>
/// Löschfristen der Organisation (NFR-5, AD-16): nach wie vielen Monaten Zeltlotse die Arbeitsdaten einer Freizeit (ab
/// Freizeitende) und abgelaufene Nachweis-Bestätigungen (ab „gültig bis“) endgültig löscht. Eine Zeile je Organisation, angelegt
/// mit der Organisation (<see cref="VorgabenFuer"/>). Bearbeiten nach AD-19.
/// </summary>
internal sealed class Loeschfristen : IOrganisationsEigen
{
    /// <summary>Vorgabe beider Fristen in Monaten.</summary>
    public const int VorgabeMonate = 12;

    private Loeschfristen()
    {
    }

    public Guid OrganisationId { get; private init; }

    public int FreizeitDatenMonate { get; private set; }

    public int NachweisBestaetigungMonate { get; private set; }

    /// <summary>Versionsprüfung (xmin, AD-19).</summary>
    public uint Version { get; private set; }

    public static Loeschfristen VorgabenFuer(Guid organisationId) => new()
    {
        OrganisationId = organisationId,
        FreizeitDatenMonate = VorgabeMonate,
        NachweisBestaetigungMonate = VorgabeMonate,
    };

    /// <summary>Wert eines Felds als Text (Zahl der Monate).</summary>
    public string Wert(LoeschfristFeld feld) => feld switch
    {
        LoeschfristFeld.FreizeitDatenMonate => FreizeitDatenMonate.ToString(CultureInfo.InvariantCulture),
        LoeschfristFeld.NachweisBestaetigungMonate => NachweisBestaetigungMonate.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19 (alter Wert desselben Felds muss passen, sonst <c>false</c>). Die Formprüfung
    /// hat der Validator erledigt.
    /// </summary>
    public bool FeldAendern(LoeschfristFeld feld, string? alterWert, string neuerWert)
    {
        if (!string.Equals(Wert(feld), alterWert?.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var monate = int.Parse(neuerWert.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
        switch (feld)
        {
            case LoeschfristFeld.FreizeitDatenMonate:
                FreizeitDatenMonate = monate;
                break;
            case LoeschfristFeld.NachweisBestaetigungMonate:
                NachweisBestaetigungMonate = monate;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }
}
