using System.Globalization;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;
using FunktionsSchalter = Zeltlotse.SharedKernel.Rechte.FunktionsSchalter;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>
/// Freizeit (FR-6): Name, Zeitraum und das beim Anlegen festgeschriebene Freigabe-Intervall (FR-24d), dazu die Angaben der
/// öffentlichen Freizeitseite (FR-15, FR-67), bearbeitet nach AD-19 über <see cref="FeldAendern"/>. Der Abschluss
/// (<see cref="AbgeschlossenAm"/>) ist der einzige gespeicherte Teil der Phase; ihn setzt nur <see cref="Abschliessen"/> (Story 3-8). Vergaben
/// der Leitung legen das Ereignis <see cref="FreizeitRolleGeaendert"/> ab, der Abschluss <see cref="FreizeitAbgeschlossen"/> (AD-28); die
/// Freizeit ist der Datensatz im Umschlag.
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

    /// <summary>
    /// Teilnahmebedingungen, die im Anmeldeformular vor dem Absenden stehen und digital bestätigt werden (FR-19), höchstens
    /// <see cref="FreizeitseiteFelder.TeilnahmebedingungenMaximaleLaenge"/> Zeichen.
    /// </summary>
    public string? Teilnahmebedingungen { get; private set; }

    /// <summary>
    /// Mail-Adresse, die Eltern und Mitarbeitende für eine Absage sehen (FR-21, Story 2-3); die Leitung trägt sie selbst ein. Höchstens
    /// <see cref="FreizeitseiteFelder.KontaktMailMaximaleLaenge"/> Zeichen, mit „@“.
    /// </summary>
    public string? KontaktMail { get; private set; }

    /// <summary>Telefon für Absagen (FR-21), höchstens <see cref="FreizeitseiteFelder.KontaktTelefonMaximaleLaenge"/> Zeichen.</summary>
    public string? KontaktTelefon { get; private set; }

    /// <summary>
    /// Kennungen der Fragen, die der Freizeitpass für Teilnehmende abfragt (FR-22, AD-10), in Katalogreihenfolge; eine neue Freizeit
    /// fragt nichts ab. Geändert nur über <see cref="FragenAendern"/>.
    /// </summary>
    public string[] FragenTeilnehmende { get; private set; } = [];

    /// <summary>Kennungen der Fragen für Mitarbeitende, Form wie <see cref="FragenTeilnehmende"/>.</summary>
    public string[] FragenMitarbeitende { get; private set; } = [];

    /// <summary>
    /// Story 3-7: Modul Verpflegung (Funktions-Schalter, AD-20); eine neue Freizeit nutzt kein Modul. Geändert nur über
    /// <see cref="ModulAendern"/>.
    /// </summary>
    public bool Verpflegung { get; private set; }

    /// <summary>Story 3-7: Modul Finanzen, Form wie <see cref="Verpflegung"/>.</summary>
    public bool Finanzen { get; private set; }

    /// <summary>Story 3-7: Modul Teilnehmer-Zugang, Form wie <see cref="Verpflegung"/>.</summary>
    public bool TeilnehmerZugang { get; private set; }

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

    /// <summary>
    /// Bestätigte Mitarbeitende werden Team (AD-11, Story 2-3) und das Ereignis wird abgelegt; <c>false</c>, wenn die Person schon
    /// eine Rolle hat (eine Leitung bleibt Leitung, kein Ereignis). Erwartet die geladenen <see cref="Rollen"/>.
    /// </summary>
    public bool TeamEinsetzen(Guid personId, DateTimeOffset jetzt)
    {
        if (_rollen.Any(r => r.PersonId == personId))
        {
            return false;
        }

        _rollen.Add(FreizeitRolle.Vergeben(OrganisationId, Id, personId, RolleArt.Team, jetzt));
        EreignisAblegen(new FreizeitRolleGeaendert(Id, personId, FreizeitRolleArt.Team));
        return true;
    }

    /// <summary>
    /// Entzieht nach einer Absage die Rolle Team und legt <see cref="FreizeitRolleArt.Entzogen"/> ab; eine Leitung oder keine Rolle
    /// bleibt unberührt (<c>false</c>). Erwartet die geladenen <see cref="Rollen"/>.
    /// </summary>
    public bool TeamEntziehen(Guid personId)
    {
        var team = _rollen.SingleOrDefault(r => r.PersonId == personId && r.Rolle == RolleArt.Team);
        if (team is null)
        {
            return false;
        }

        _rollen.Remove(team);
        EreignisAblegen(new FreizeitRolleGeaendert(Id, personId, FreizeitRolleArt.Entzogen));
        return true;
    }

    /// <summary>Wert eines Felds der Freizeitseite als Text; die Höchstzahl als Zahl in Ziffern.</summary>
    public string? Wert(FreizeitseiteFeld feld) => feld switch
    {
        FreizeitseiteFeld.Beschreibung => Beschreibung,
        FreizeitseiteFeld.Ort => Ort,
        FreizeitseiteFeld.HoechstzahlTeilnehmende => HoechstzahlTeilnehmende?.ToString(CultureInfo.InvariantCulture),
        FreizeitseiteFeld.Teilnahmebedingungen => Teilnahmebedingungen,
        FreizeitseiteFeld.KontaktMail => KontaktMail,
        FreizeitseiteFeld.KontaktTelefon => KontaktTelefon,
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
            case FreizeitseiteFeld.Teilnahmebedingungen:
                Teilnahmebedingungen = wert;
                break;
            case FreizeitseiteFeld.KontaktMail:
                KontaktMail = wert;
                break;
            case FreizeitseiteFeld.KontaktTelefon:
                KontaktTelefon = wert;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    public static string? Normalisieren(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();

    /// <summary>Die gewählten Fragen einer Zielgruppe als Komma-Liste in Katalogreihenfolge; keine Fragen = <c>null</c>.</summary>
    public string? Fragen(FragenFeld feld)
    {
        var kennungen = feld switch
        {
            FragenFeld.Teilnehmende => FragenTeilnehmende,
            FragenFeld.Mitarbeitende => FragenMitarbeitende,
            _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
        };
        return kennungen.Length == 0 ? null : string.Join(',', kennungen);
    }

    /// <summary>
    /// Ändert die Fragen-Auswahl einer Zielgruppe nach der Konfliktregel aus AD-19 (Vergleich in normalisierter Form): Weicht die
    /// aktuelle Auswahl vom mitgeschickten alten Wert ab, bleibt alles, wie es ist (<c>false</c>). Die Prüfung gegen den Katalog hat
    /// der Validator erledigt; gespeichert wird in Katalogreihenfolge ohne Doppelte.
    /// </summary>
    public bool FragenAendern(FragenFeld feld, string? alterWert, string? neuerWert)
    {
        var alt = Fragenkatalog.KennungenPruefen(alterWert);
        if (!string.Equals(Fragen(feld), alt.Gueltig ? alt.Wert : Normalisieren(alterWert), StringComparison.Ordinal))
        {
            return false;
        }

        string[] neu = [.. Fragenkatalog.Kennungen(neuerWert)];
        switch (feld)
        {
            case FragenFeld.Teilnehmende:
                FragenTeilnehmende = neu;
                break;
            case FragenFeld.Mitarbeitende:
                FragenMitarbeitende = neu;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Story 3-7: ob ein Modul eingeschaltet ist, als <c>"true"</c>/<c>"false"</c> (Form des Autosave-Ankreuzfelds).</summary>
    public string Modul(ModulFeld feld) => ModulAn(feld) ? "true" : "false";

    /// <summary>Story 3-7: die eingeschalteten Funktions-Schalter in Enum-Reihenfolge (Verpflegung, Finanzen, Teilnehmer-Zugang).</summary>
    public IReadOnlyList<FunktionsSchalter> Schalter() =>
    [
        .. new[] { (Verpflegung, FunktionsSchalter.Verpflegung), (Finanzen, FunktionsSchalter.Finanzen), (TeilnehmerZugang, FunktionsSchalter.TeilnehmerZugang) }
            .Where(s => s.Item1)
            .Select(s => s.Item2),
    ];

    /// <summary>
    /// Story 3-7: schaltet ein Modul nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt
    /// alles, wie es ist (<c>false</c>). Nur der Schalter ändert sich; Daten des Moduls bleiben erhalten (AD-20). Die Formprüfung
    /// (<c>"true"</c>/<c>"false"</c>) hat der Validator erledigt.
    /// </summary>
    public bool ModulAendern(ModulFeld feld, string? alterWert, string neuerWert)
    {
        if (!string.Equals(Modul(feld), alterWert?.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var an = neuerWert.Trim() == "true";
        switch (feld)
        {
            case ModulFeld.Verpflegung:
                Verpflegung = an;
                break;
            case ModulFeld.Finanzen:
                Finanzen = an;
                break;
            case ModulFeld.TeilnehmerZugang:
                TeilnehmerZugang = an;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    private bool ModulAn(ModulFeld feld) => feld switch
    {
        ModulFeld.Verpflegung => Verpflegung,
        ModulFeld.Finanzen => Finanzen,
        ModulFeld.TeilnehmerZugang => TeilnehmerZugang,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Story 2-5: die gewählten Kennungen der Zielgruppe einer Anmeldung, in Katalogreihenfolge.</summary>
    public IReadOnlyList<string> FragenFuer(AnmeldeArt art) => art switch
    {
        AnmeldeArt.Teilnehmende => FragenTeilnehmende,
        AnmeldeArt.Mitarbeitende => FragenMitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    /// <summary>
    /// Story 3-8: Die Leitung schließt die Freizeit ab (AD-17): nur in der Nachbereitung, danach ist sie gelaufen und das Ereignis
    /// <see cref="FreizeitAbgeschlossen"/> liegt bereit. <c>false</c> in jeder anderen Phase, dann ändert sich nichts.
    /// </summary>
    public bool Abschliessen(DateTimeOffset jetzt)
    {
        if (Phase(jetzt) != FreizeitPhase.Nachbereitung)
        {
            return false;
        }

        AbgeschlossenAm = jetzt;
        EreignisAblegen(new FreizeitAbgeschlossen(Id));
        return true;
    }

    public FreizeitPhase Phase(DateTimeOffset jetzt) => FreizeitZeiten.Phase(ErsterTag, LetzterTag, AbgeschlossenAm, jetzt);

    public FreizeitZeitpunkte Zeitpunkte(DateTimeOffset jetzt) => new(
        Phase(jetzt),
        FreizeitZeiten.Freizeitende(LetzterTag),
        FreizeitZeiten.FreigabeGueltigBis(LetzterTag, FreigabeIntervallTage));
}
