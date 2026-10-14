using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5: Eine Aufgaben-Vorlage der Organisation (FR-40): Titel und Phase. Neue Freizeiten übernehmen alle Vorlagen beim ersten Öffnen der
/// Aufgaben (<see cref="AufgabenVorlagenUebernehmen"/>). Beide Felder ändern sich nach AD-19 über <see cref="FeldAendern"/>. Kein Personenbezug.
/// </summary>
internal sealed class AufgabenVorlage : IOrganisationsEigen
{
    private AufgabenVorlage()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="AufgabenFelder.TitelMaximaleLaenge"/> Zeichen.</summary>
    public string Titel { get; private set; } = "";

    public AufgabenPhase Phase { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static AufgabenVorlage Anlegen(Guid id, Guid organisationId, string titel, AufgabenPhase phase) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        Titel = titel.Trim(),
        Phase = phase,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert (die Phase als Name).</summary>
    public string Wert(AufgabenVorlageFeld feld) => feld switch
    {
        AufgabenVorlageFeld.Titel => Titel,
        AufgabenVorlageFeld.Phase => AufgabenFelder.PhaseText(Phase),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Ändert ein Feld nach der Konfliktregel aus AD-19 (wie <see cref="Aufgabe.FeldAendern"/>).</summary>
    public bool FeldAendern(AufgabenVorlageFeld feld, string? alterWert, string? neuerWert)
    {
        if (!AufgabenWerte.Gleich(feld == AufgabenVorlageFeld.Phase, Wert(feld), alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case AufgabenVorlageFeld.Titel:
                Titel = (neuerWert ?? "").Trim();
                break;
            case AufgabenVorlageFeld.Phase:
                Phase = AufgabenWerte.Phase(neuerWert);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }
}

/// <summary>
/// Story 3-5: Merkt je Freizeit, dass ihre Aufgaben die Vorlagen der Organisation schon übernommen haben (Tabelle
/// <c>aufgaben_vorlagen_uebernahme</c>): so wird genau einmal kopiert, und gelöschte Aufgaben kommen nicht wieder.
/// </summary>
internal sealed class AufgabenVorlagenUebernahme : IOrganisationsEigen
{
    private AufgabenVorlagenUebernahme()
    {
    }

    public Guid FreizeitId { get; private set; }

    public Guid OrganisationId { get; private set; }

    public DateTimeOffset UebernommenAm { get; private set; }
}
