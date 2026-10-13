using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Programm.Materialien;

/// <summary>
/// Story 3-2: Ein Material an genau einem Programmpunkt (FR-36, AD-3): Bezeichnung, optionale Menge als Freitext, Status und höchstens eine
/// mitbringende Person. Bezeichnung und Menge ändern sich nach AD-19 über <see cref="FeldAendern"/>, der Status nur über
/// <see cref="StatusSetzen"/>, das Mitbringen nur über <see cref="Mitbringen"/> und <see cref="NichtMitbringen"/> (AD-28). Mitbringen und
/// Status sind unabhängig voneinander.
/// </summary>
/// <remarks>
/// Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16): Bei <c>PersonGeloescht</c> wird <see cref="PersonId"/> zum Platzhalter, nach der Frist für
/// Freizeit-Daten löscht der Lösch-Lauf die Zeile. Wird der Programmpunkt gelöscht, verschwindet sein Material mit (Fremdschlüssel im eigenen
/// Schema, <c>ON DELETE CASCADE</c>).
/// </remarks>
internal sealed class Material : IOrganisationsEigen
{
    private Material()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public Guid ProgrammpunktId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="MaterialFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public string Bezeichnung { get; private set; } = "";

    /// <summary>Freitext, getrimmt, höchstens <see cref="MaterialFelder.MengeMaximaleLaenge"/> Zeichen; ohne Menge <c>null</c>.</summary>
    public string? Menge { get; private set; }

    public MaterialStatus Status { get; private set; }

    /// <summary>Die Person, die das Material mitbringt (AD-7, „Ich bringe das mit“), sonst <c>null</c>.</summary>
    public Guid? PersonId { get; private set; }

    /// <summary>
    /// Die mitbringende Person; der Platzhalter einer gelöschten Person (<see cref="GeloeschtePerson.Id"/>) zählt als frei, damit „Ich bringe das
    /// mit“ wieder möglich ist.
    /// </summary>
    public Guid? BringtMit => PersonId == GeloeschtePerson.Id ? null : PersonId;

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Material Anlegen(Guid id, Guid organisationId, Guid freizeitId, Guid programmpunktId, string bezeichnung, string? menge) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        ProgrammpunktId = programmpunktId,
        Bezeichnung = bezeichnung.Trim(),
        Menge = TextOderNull(menge),
        Status = MaterialStatus.Gebraucht,
    };

    /// <summary>Setzt den Status; jeder Zustand ist von jedem aus erlaubt, derselbe Zustand lässt alles, wie es ist.</summary>
    public void StatusSetzen(MaterialStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        Status = status;
    }

    /// <summary>
    /// „Ich bringe das mit“: Bringt es niemand mit, übernimmt es die Person; bringt sie es schon mit, bleibt es dabei. Bringt es jemand anderes
    /// mit, bleibt alles, wie es ist, und das Ergebnis ist <c>false</c>.
    /// </summary>
    public bool Mitbringen(Guid personId)
    {
        if (BringtMit is { } bisher)
        {
            return bisher == personId;
        }

        PersonId = personId;
        return true;
    }

    /// <summary>
    /// „Doch nicht mitbringen“: Bringt die Person es mit, ist es danach frei; ist es schon frei, bleibt es dabei. Bringt es jemand anderes mit,
    /// bleibt alles, wie es ist, und das Ergebnis ist <c>false</c>.
    /// </summary>
    public bool NichtMitbringen(Guid personId)
    {
        if (BringtMit is { } bisher && bisher != personId)
        {
            return false;
        }

        PersonId = null;
        return true;
    }

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert.</summary>
    public string? Wert(MaterialFeld feld) => feld switch
    {
        MaterialFeld.Bezeichnung => Bezeichnung,
        MaterialFeld.Menge => Menge,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es ist,
    /// und das Ergebnis ist <c>false</c>. Beide Felder werden getrimmt; eine leere Menge heißt ohne Menge. Die Form prüft der Validator.
    /// </summary>
    public bool FeldAendern(MaterialFeld feld, string? alterWert, string? neuerWert)
    {
        if (TextOderNull(Wert(feld)) != TextOderNull(alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case MaterialFeld.Bezeichnung:
                Bezeichnung = (neuerWert ?? "").Trim();
                break;
            case MaterialFeld.Menge:
                Menge = TextOderNull(neuerWert);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Getrimmter Text; leer oder nur Leerzeichen heißt <c>null</c>.</summary>
    private static string? TextOderNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
