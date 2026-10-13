using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;
using MahlzeitArt = Zeltlotse.Api.Contracts.Programm.Mahlzeit;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 3-1: Ein Programmpunkt im Wochenraster einer Freizeit (FR-34, FR-35, AD-3): Tag, optionale Uhrzeit, Titel, bei den festen Mahlzeiten
/// die <see cref="Mahlzeit"/> und höchstens eine übernehmende Person. Titel und Uhrzeit ändern sich nach AD-19 über <see cref="FeldAendern"/>,
/// die Übernahme nur über <see cref="Uebernehmen"/> und <see cref="Abgeben"/> (AD-28). Der Tag ist fest: Verschieben heißt Uhrzeit ändern.
/// </summary>
/// <remarks>
/// Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16): Bei <c>PersonGeloescht</c> wird <see cref="PersonId"/> zum Platzhalter, nach der Frist für
/// Freizeit-Daten löscht der Lösch-Lauf die Zeile. Mahlzeiten gibt es je (Freizeit, Tag, Mahlzeit) genau einmal; sie lassen sich nicht löschen.
/// </remarks>
internal sealed class Programmpunkt : IOrganisationsEigen
{
    /// <summary>Anfangszeit und Titel der festen Mahlzeiten.</summary>
    public static readonly IReadOnlyList<(MahlzeitArt Mahlzeit, TimeOnly Beginn, string Titel)> Mahlzeiten =
    [
        (MahlzeitArt.Fruehstueck, new TimeOnly(8, 0), "Frühstück"),
        (MahlzeitArt.Mittagessen, new TimeOnly(12, 30), "Mittagessen"),
        (MahlzeitArt.Abendessen, new TimeOnly(18, 30), "Abendessen"),
    ];

    private Programmpunkt()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public DateOnly Tag { get; private set; }

    /// <summary>Uhrzeit; ohne steht der Punkt am Ende des Tages.</summary>
    public TimeOnly? Beginn { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="ProgrammpunktFelder.TitelMaximaleLaenge"/> Zeichen.</summary>
    public string Titel { get; private set; } = "";

    /// <summary>Gesetzt bei den festen Mahlzeiten.</summary>
    public MahlzeitArt? Mahlzeit { get; private set; }

    /// <summary>Die Person, die den Punkt übernommen hat (AD-7, „Ich übernehme“), sonst <c>null</c>.</summary>
    public Guid? PersonId { get; private set; }

    /// <summary>
    /// Die übernehmende Person; der Platzhalter einer gelöschten Person (<see cref="GeloeschtePerson.Id"/>) zählt als frei, damit der Punkt nicht
    /// dauerhaft blockiert bleibt.
    /// </summary>
    public Guid? Uebernommen => PersonId == GeloeschtePerson.Id ? null : PersonId;

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Mahlzeiten lassen sich nicht löschen, alle anderen Punkte schon.</summary>
    public bool Loeschbar => Mahlzeit is null;

    public static Programmpunkt Anlegen(Guid id, Guid organisationId, Guid freizeitId, DateOnly tag, TimeOnly? beginn, string titel) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Tag = tag,
        Beginn = beginn,
        Titel = titel.Trim(),
    };

    public static Programmpunkt MahlzeitAnlegen(Guid id, Guid organisationId, Guid freizeitId, DateOnly tag, MahlzeitArt mahlzeit)
    {
        var (_, beginn, titel) = Mahlzeiten.Single(m => m.Mahlzeit == mahlzeit);
        var punkt = Anlegen(id, organisationId, freizeitId, tag, beginn, titel);
        punkt.Mahlzeit = mahlzeit;
        return punkt;
    }

    /// <summary>
    /// „Ich übernehme“ (FR-35): Ist der Punkt frei, übernimmt ihn die Person; hat sie ihn schon, bleibt es dabei. Hat ihn jemand anderes,
    /// bleibt alles, wie es ist, und das Ergebnis ist <c>false</c>.
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

    /// <summary>
    /// „Abgeben“: Hat die Person den Punkt, ist er danach frei; ist er schon frei, bleibt es dabei. Hat ihn jemand anderes, bleibt alles, wie es
    /// ist, und das Ergebnis ist <c>false</c>.
    /// </summary>
    public bool Abgeben(Guid personId)
    {
        if (Uebernommen is { } bisher && bisher != personId)
        {
            return false;
        }

        PersonId = null;
        return true;
    }

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert (<c>beginn</c> als <c>HH:mm</c>).</summary>
    public string? Wert(ProgrammpunktFeld feld) => feld switch
    {
        ProgrammpunktFeld.Titel => Titel,
        ProgrammpunktFeld.Beginn => ProgrammpunktFelder.UhrzeitText(Beginn),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es ist,
    /// und das Ergebnis ist <c>false</c>. Der Titel wird getrimmt; eine leere Uhrzeit heißt ohne Uhrzeit. Die Form prüft der Validator.
    /// </summary>
    public bool FeldAendern(ProgrammpunktFeld feld, string? alterWert, string? neuerWert)
    {
        if (Normalisieren(feld, Wert(feld)) != Normalisieren(feld, alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case ProgrammpunktFeld.Titel:
                Titel = (neuerWert ?? "").Trim();
                break;
            case ProgrammpunktFeld.Beginn:
                if (!ProgrammpunktFelder.UhrzeitLesen(neuerWert, out var beginn))
                {
                    throw new ArgumentException("Keine Uhrzeit im Format HH:mm.", nameof(neuerWert));
                }

                Beginn = beginn;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Gleiche Werte gleich darstellen: Text getrimmt (leer = <c>null</c>), Uhrzeit als <c>HH:mm</c>.</summary>
    private static string? Normalisieren(ProgrammpunktFeld feld, string? wert)
    {
        if (string.IsNullOrWhiteSpace(wert))
        {
            return null;
        }

        return feld == ProgrammpunktFeld.Beginn && ProgrammpunktFelder.UhrzeitLesen(wert, out var uhrzeit)
            ? ProgrammpunktFelder.UhrzeitText(uhrzeit)
            : wert.Trim();
    }
}
