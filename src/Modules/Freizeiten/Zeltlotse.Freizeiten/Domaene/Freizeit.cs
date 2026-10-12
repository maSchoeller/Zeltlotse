using System.Globalization;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>
/// Freizeit (FR-6): Name, Zeitraum und das beim Anlegen festgeschriebene Freigabe-Intervall (FR-24d), dazu die Angaben der
/// öffentlichen Freizeitseite (FR-15, FR-67), bearbeitet nach AD-19 über <see cref="FeldAendern"/>. Der Abschluss
/// (<see cref="AbgeschlossenAm"/>) ist nur ein Feld; diese Version setzt ihn nicht. Vergaben der Leitung legen das Ereignis
/// <see cref="FreizeitRolleGeaendert"/> ab (AD-28); die Freizeit ist der Datensatz im Umschlag.
/// </summary>
internal sealed class Freizeit : Aggregat, IOrganisationsEigen
{
    public const int MaximaleNamensLaenge = 100;

    public const int FruehestesJahr = 2000;

    public const int SpaetestesJahr = 2100;

    private readonly List<FreizeitRolle> _rollen = [];

    private Freizeit()
    {
    }

    public Guid OrganisationId { get; private init; }

    /// <summary>Getrimmt, 1 bis <see cref="MaximaleNamensLaenge"/> Zeichen.</summary>
    public string Name { get; private init; } = "";

    public DateOnly ErsterTag { get; private init; }

    /// <summary>Gleich oder nach <see cref="ErsterTag"/>.</summary>
    public DateOnly LetzterTag { get; private init; }

    /// <summary>Freigabe-Intervall in Tagen, beim Anlegen festgeschrieben; eine spätere Richtlinie ändert es nicht.</summary>
    public int FreigabeIntervallTage { get; private init; }

    public DateTimeOffset AngelegtAm { get; private init; }

    /// <summary>Die anlegende Person.</summary>
    public Guid AngelegtVon { get; private init; }

    /// <summary>Abschluss durch die Leitung; danach ist die Freizeit „gelaufen“ (AD-17).</summary>
    public DateTimeOffset? AbgeschlossenAm { get; private set; }

    /// <summary>Beschreibung auf der Freizeitseite, höchstens <see cref="FreizeitseiteFelder.BeschreibungMaximaleLaenge"/> Zeichen.</summary>
    public string? Beschreibung { get; private set; }

    /// <summary>Ort der Freizeit, höchstens <see cref="FreizeitseiteFelder.OrtMaximaleLaenge"/> Zeichen.</summary>
    public string? Ort { get; private set; }

    /// <summary>Höchstzahl Teilnehmende (1–1000); leer heißt: nie ausgebucht (FR-67). Mitarbeitende zählen nie.</summary>
    public int? HoechstzahlTeilnehmende { get; private set; }

    /// <summary>Versionsprüfung (xmin, AD-19).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<FreizeitRolle> Rollen => _rollen;

    public static Freizeit Anlegen(
        Guid id, Guid organisationId, string name, DateOnly ersterTag, DateOnly letzterTag, int freigabeIntervallTage, Guid angelegtVon, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        Name = name.Trim(),
        ErsterTag = ersterTag,
        LetzterTag = letzterTag,
        FreigabeIntervallTage = freigabeIntervallTage,
        AngelegtVon = angelegtVon,
        AngelegtAm = jetzt,
    };

    /// <summary>
    /// Setzt die Person als Leitung ein und legt das Ereignis ab; <c>false</c>, wenn sie schon Leitung ist (dann kein
    /// zweites Ereignis). Erwartet die geladenen <see cref="Rollen"/>.
    /// </summary>
    public bool LeitungEinsetzen(Guid personId, DateTimeOffset jetzt)
    {
        if (_rollen.Any(r => r.PersonId == personId && r.Rolle == RolleArt.Leitung))
        {
            return false;
        }

        _rollen.Add(FreizeitRolle.Vergeben(OrganisationId, Id, personId, RolleArt.Leitung, jetzt));
        EreignisAblegen(new FreizeitRolleGeaendert(Id, personId, FreizeitRolleArt.Leitung));
        return true;
    }

    /// <summary>Wert eines Felds der Freizeitseite als Text; die Höchstzahl als Zahl in Ziffern.</summary>
    public string? Wert(FreizeitseiteFeld feld) => feld switch
    {
        FreizeitseiteFeld.Beschreibung => Beschreibung,
        FreizeitseiteFeld.Ort => Ort,
        FreizeitseiteFeld.HoechstzahlTeilnehmende => HoechstzahlTeilnehmende?.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld der Freizeitseite nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert
    /// ab, bleibt alles, wie es ist, und das Ergebnis ist <c>false</c>. Leere Werte gelten als „nicht angegeben“. Die Formprüfung
    /// (Länge, Zahlenbereich) hat der Validator erledigt.
    /// </summary>
    public bool FeldAendern(FreizeitseiteFeld feld, string? alterWert, string? neuerWert)
    {
        if (Normalisieren(Wert(feld)) != Normalisieren(alterWert))
        {
            return false;
        }

        var wert = Normalisieren(neuerWert);
        switch (feld)
        {
            case FreizeitseiteFeld.Beschreibung:
                Beschreibung = wert;
                break;
            case FreizeitseiteFeld.Ort:
                Ort = wert;
                break;
            case FreizeitseiteFeld.HoechstzahlTeilnehmende:
                HoechstzahlTeilnehmende = wert is null ? null : int.Parse(wert, NumberStyles.None, CultureInfo.InvariantCulture);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    public static string? Normalisieren(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();

    public FreizeitPhase Phase(DateTimeOffset jetzt) => FreizeitZeiten.Phase(ErsterTag, LetzterTag, AbgeschlossenAm, jetzt);

    public FreizeitZeitpunkte Zeitpunkte(DateTimeOffset jetzt) => new(
        Phase(jetzt),
        FreizeitZeiten.Freizeitende(LetzterTag),
        FreizeitZeiten.FreigabeGueltigBis(LetzterTag, FreigabeIntervallTage));
}
