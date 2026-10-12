using System.Globalization;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel;

namespace Zeltlotse.Personen.Profil;

/// <summary>
/// Person mit Profil (AD-7): Die Kennung ist die <c>PersonId</c>, die Konten beim Anlegen des Kontos vergibt; Kinderprofile
/// bekommen ihre Kennung von Personen. Nicht organisationseigen. <see cref="Version"/> ist <c>xmin</c> für das Bearbeiten nach
/// AD-19. Ein Kinderprofil gehört über <see cref="FamilieId"/> zu einer Familie, bis das volljährige Kind es übernimmt (FR-4).
/// Aggregat (AD-28): Das Löschen legt <see cref="PersonGeloescht"/> ab (Datensatz im Umschlag ist die Person).
/// </summary>
internal sealed class Person : Aggregat
{
    private Person()
    {
    }

    public string? Vorname { get; private set; }

    public string? Nachname { get; private set; }

    /// <summary>
    /// Freiwillig (FR-15): der Wohnort, z. B. „Musterstadt“. Erscheint auf der Freizeitseite jeder Freizeit, in der die Person mitarbeitet.
    /// </summary>
    public string? Herkunft { get; private set; }

    /// <summary>Pflicht bei Kinderprofilen (18-Grenze, FR-4); sonst bisher leer.</summary>
    public DateOnly? Geburtsdatum { get; private set; }

    /// <summary>Nur bei Kinderprofilen gesetzt: die Familie, deren Eltern das Profil verwalten.</summary>
    public Guid? FamilieId { get; private set; }

    /// <summary>Von PostgreSQL bei jeder Änderung weitergezählt (<c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public DateTimeOffset AngelegtAm { get; private init; }

    public static Person Anlegen(Guid personId, DateTimeOffset jetzt) => new() { Id = personId, AngelegtAm = jetzt };

    /// <summary>Ein Kinderprofil in einer Familie; Namen getrimmt, ein leerer Nachname gilt als nicht angegeben.</summary>
    public static Person Kind(Guid personId, Guid familieId, string vorname, string? nachname, DateOnly geburtsdatum, DateTimeOffset jetzt) => new()
    {
        Id = personId,
        FamilieId = familieId,
        Vorname = Normalisieren(vorname),
        Nachname = Normalisieren(nachname),
        Geburtsdatum = geburtsdatum,
        AngelegtAm = jetzt,
    };

    /// <summary>Ein eigenes Profil ohne Namen; ob es sonst leer ist (Freizeit-Rolle, Elternteil), prüft der Aufrufer.</summary>
    public bool OhneNamen => Vorname is null && Nachname is null;

    public string? Wert(ProfilFeld feld) => feld switch
    {
        ProfilFeld.Vorname => Vorname,
        ProfilFeld.Nachname => Nachname,
        ProfilFeld.Herkunft => Herkunft,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Wert eines Kinderfelds als Text; das Geburtsdatum im <see cref="KindFelder.Datumsformat"/>.</summary>
    public string? Wert(KindFeld feld) => feld switch
    {
        KindFeld.Vorname => Vorname,
        KindFeld.Nachname => Nachname,
        KindFeld.Geburtsdatum => Geburtsdatum?.ToString(KindFelder.Datumsformat, CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt
    /// alles, wie es ist, und das Ergebnis ist <c>false</c>. Leere Werte gelten als „nicht angegeben“.
    /// </summary>
    public bool FeldAendern(ProfilFeld feld, string? alterWert, string? neuerWert)
    {
        if (Normalisieren(Wert(feld)) != Normalisieren(alterWert))
        {
            return false;
        }

        var wert = Normalisieren(neuerWert);
        switch (feld)
        {
            case ProfilFeld.Vorname:
                Vorname = wert;
                break;
            case ProfilFeld.Nachname:
                Nachname = wert;
                break;
            case ProfilFeld.Herkunft:
                Herkunft = wert;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>
    /// Ändert ein Feld eines Kinderprofils nach der Konfliktregel aus AD-19. Die Formprüfung (Pflicht, Länge, Datum) hat der
    /// Validator schon erledigt; das Geburtsdatum kommt im <see cref="KindFelder.Datumsformat"/>.
    /// </summary>
    public bool KindFeldAendern(KindFeld feld, string? alterWert, string? neuerWert)
    {
        if (Normalisieren(Wert(feld)) != Normalisieren(alterWert))
        {
            return false;
        }

        var wert = Normalisieren(neuerWert);
        switch (feld)
        {
            case KindFeld.Vorname:
                Vorname = wert;
                break;
            case KindFeld.Nachname:
                Nachname = wert;
                break;
            case KindFeld.Geburtsdatum:
                Geburtsdatum = DateOnly.ParseExact(wert!, KindFelder.Datumsformat, CultureInfo.InvariantCulture);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>
    /// Die Person wird gelöscht (FR-14, AD-16): legt <see cref="PersonGeloescht"/> ab; die Zeile entfernt der Handler im selben
    /// <c>SaveChanges</c>, die Outbox-Stufe sammelt das Ereignis auch von gelöschten Aggregaten ein.
    /// </summary>
    public void Loeschen() => EreignisAblegen(new PersonGeloescht(Id));

    /// <summary>Das volljährige Kind übernimmt sein Profil (FR-4): Die Eltern verlieren den Zugriff.</summary>
    public void FamilieVerlassen() => FamilieId = null;

    public static string? Normalisieren(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
