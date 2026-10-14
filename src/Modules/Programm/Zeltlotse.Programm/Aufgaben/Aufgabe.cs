using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5: Eine Aufgabe der Freizeit (FR-39, CAP-23): Titel, Phase, optional ein Programmpunkt, höchstens eine übernehmende Person, erledigt
/// oder offen und optional die Aufgaben-Vorlage, aus der sie stammt oder die aus ihr entstand. Titel und Phase ändern sich nach AD-19 über
/// <see cref="FeldAendern"/>, Übernahme und Erledigt nur über die Übergänge (AD-28). Die Leitung teilt nichts zu.
/// </summary>
/// <remarks>
/// Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16): Bei <c>PersonGeloescht</c> wird <see cref="PersonId"/> zum Platzhalter, nach der Frist für
/// Freizeit-Daten löscht der Lösch-Lauf die Zeile. Mit dem Programmpunkt verschwindet die Aufgabe (<c>ON DELETE CASCADE</c>); mit der Vorlage
/// nur die Verknüpfung (<c>ON DELETE SET NULL</c>).
/// </remarks>
internal sealed class Aufgabe : IOrganisationsEigen
{
    private Aufgabe()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="AufgabenFelder.TitelMaximaleLaenge"/> Zeichen.</summary>
    public string Titel { get; private set; } = "";

    public AufgabenPhase Phase { get; private set; }

    /// <summary>Der Programmpunkt derselben Freizeit, an dem die Aufgabe hängt, sonst <c>null</c>.</summary>
    public Guid? ProgrammpunktId { get; private set; }

    /// <summary>Die Person, die die Aufgabe übernommen hat (AD-7, „Ich übernehme“), sonst <c>null</c>.</summary>
    public Guid? PersonId { get; private set; }

    /// <summary>Die übernehmende Person; der Platzhalter einer gelöschten Person (<see cref="GeloeschtePerson.Id"/>) zählt als frei.</summary>
    public Guid? Uebernommen => PersonId == GeloeschtePerson.Id ? null : PersonId;

    /// <summary>Abgehakt; wer abgehakt hat, vermerkt Zeltlotse nicht.</summary>
    public bool Erledigt { get; private set; }

    /// <summary>Die verknüpfte Aufgaben-Vorlage der Organisation; wird sie gelöscht, wird die Verknüpfung leer.</summary>
    public Guid? VorlageId { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Aufgabe Anlegen(Guid id, Guid organisationId, Guid freizeitId, string titel, AufgabenPhase phase, Guid? programmpunktId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Titel = titel.Trim(),
        Phase = phase,
        ProgrammpunktId = programmpunktId,
    };

    /// <summary>Eine Aufgabe aus einer Vorlage der Organisation (Titel, Phase), verknüpft mit ihr.</summary>
    public static Aufgabe AusVorlage(Guid id, Guid freizeitId, AufgabenVorlage vorlage)
    {
        var aufgabe = Anlegen(id, vorlage.OrganisationId, freizeitId, vorlage.Titel, vorlage.Phase, null);
        aufgabe.VorlageId = vorlage.Id;
        return aufgabe;
    }

    /// <summary>
    /// „Ich übernehme“: Ist die Aufgabe frei, übernimmt sie die Person; hat sie sie schon, bleibt es dabei. Hat sie jemand anderes, bleibt alles,
    /// wie es ist, und das Ergebnis ist <c>false</c>.
    /// </summary>
    public bool Uebernehmen(Guid personId)
    {
        if (Uebernommen is { } bisher)
        {
            return bisher == personId;
        }

        PersonId = personId;
        return true;
    }

    /// <summary>„Abgeben“: nur die übernehmende Person; ist die Aufgabe schon frei, bleibt es dabei. Sonst <c>false</c>.</summary>
    public bool Abgeben(Guid personId)
    {
        if (Uebernommen is { } bisher && bisher != personId)
        {
            return false;
        }

        PersonId = null;
        return true;
    }

    /// <summary>„Erledigt“: abhaken; wiederholt unschädlich.</summary>
    public void Erledigen() => Erledigt = true;

    /// <summary>„Wieder offen“; wiederholt unschädlich.</summary>
    public void Oeffnen() => Erledigt = false;

    /// <summary>„Als Vorlage speichern“: die neu angelegte Vorlage merken.</summary>
    public void VorlageMerken(Guid vorlageId) => VorlageId = vorlageId;

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert (die Phase als Name).</summary>
    public string Wert(AufgabeFeld feld) => feld switch
    {
        AufgabeFeld.Titel => Titel,
        AufgabeFeld.Phase => AufgabenFelder.PhaseText(Phase),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es ist,
    /// und das Ergebnis ist <c>false</c>. Die Form prüft der Validator.
    /// </summary>
    public bool FeldAendern(AufgabeFeld feld, string? alterWert, string? neuerWert)
    {
        var phase = feld == AufgabeFeld.Phase;
        if (!AufgabenWerte.Gleich(phase, Wert(feld), alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case AufgabeFeld.Titel:
                Titel = (neuerWert ?? "").Trim();
                break;
            case AufgabeFeld.Phase:
                Phase = AufgabenWerte.Phase(neuerWert);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }
}

/// <summary>Vergleich und Lesen der Feldwerte, gleich für Aufgabe und Vorlage.</summary>
internal static class AufgabenWerte
{
    /// <summary>Gleiche Werte: Text getrimmt (leer = gleich leer), die Phase ohne Rücksicht auf Groß- und Kleinschreibung.</summary>
    public static bool Gleich(bool phase, string? aktuell, string? alt)
    {
        if (phase && AufgabenFelder.PhaseLesen(alt, out var altePhase) && AufgabenFelder.PhaseLesen(aktuell, out var aktuellePhase))
        {
            return altePhase == aktuellePhase;
        }

        return Normal(aktuell) == Normal(alt);
    }

    public static AufgabenPhase Phase(string? text) =>
        AufgabenFelder.PhaseLesen(text, out var phase) ? phase : throw new ArgumentException("Keine Phase.", nameof(text));

    private static string? Normal(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
