using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1 (CAP-32, FR-54): Die Planzahlen der Kalkulation einer Freizeit: geplante Teilnehmende und Mitarbeitende. Höchstens eine Zeile je Freizeit;
/// ohne Zeile sind beide 0. Die Zeile entsteht beim ersten Ändern (<c>INSERT … ON CONFLICT DO NOTHING</c>). Bewusst keine Kopplung an Anmeldungen
/// (Live-Kalkulation ist „bewusst nie“). Felder ändern sich nach AD-19 nur über <see cref="FeldAendern"/>.
/// </summary>
/// <remarks>Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug.</remarks>
internal sealed class Kalkulation : IOrganisationsEigen
{
    private Kalkulation()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Geplante Teilnehmende, 0 bis <see cref="KalkulationFelder.PlanzahlMaximum"/>.</summary>
    public int Teilnehmende { get; private set; }

    /// <summary>Geplante Mitarbeitende, 0 bis <see cref="KalkulationFelder.PlanzahlMaximum"/>.</summary>
    public int Mitarbeitende { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Eine neue Kalkulation mit Planzahlen 0.</summary>
    public static Kalkulation Anlegen(Guid id, Guid organisationId, Guid freizeitId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert, z. B. „40“.</summary>
    public string Wert(KalkulationFeld feld) => feld switch
    {
        KalkulationFeld.Teilnehmende => FinanzenWerte.PlanzahlText(Teilnehmende),
        KalkulationFeld.Mitarbeitende => FinanzenWerte.PlanzahlText(Mitarbeitende),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab (beide normalisiert), bleibt alles,
    /// wie es ist, und das Ergebnis ist <c>false</c>. Die Form des neuen Werts prüft der Validator.
    /// </summary>
    public bool FeldAendern(KalkulationFeld feld, string? alterWert, string? neuerWert)
    {
        if (Wert(feld) != FinanzenWerte.PlanzahlVergleichswert(alterWert))
        {
            return false;
        }

        var zahl = FinanzenWerte.PlanzahlLesen(neuerWert) ?? throw new ArgumentException(FinanzenWerte.PlanzahlFehlerText, nameof(neuerWert));
        switch (feld)
        {
            case KalkulationFeld.Teilnehmende:
                Teilnehmende = zahl;
                break;
            case KalkulationFeld.Mitarbeitende:
                Mitarbeitende = zahl;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }
}
