using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kueche.Einkauf;

/// <summary>
/// Story 4-2: Ein Gericht, also die Vormerkung eines Rezepts an einer Mahlzeit (CAP-29, FR-47). Die Mahlzeit ist ein Programmpunkt des Moduls
/// Programm und wird nur per Kennung referenziert (AD-3, kein Fremdschlüssel über Schemagrenzen); das Rezept hängt per Fremdschlüssel im eigenen
/// Schema daran, ein gelöschtes Rezept nimmt seine Gerichte mit (<c>ON DELETE CASCADE</c>). Das Rezept wechselt nur über <see cref="Tauschen"/>.
/// </summary>
/// <remarks>Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug: Nach der Frist für Freizeit-Daten löscht der Lösch-Lauf die Zeile.</remarks>
internal sealed class Gericht : IOrganisationsEigen
{
    private Gericht()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Die Mahlzeit (Programmpunkt im Modul Programm).</summary>
    public Guid ProgrammpunktId { get; private set; }

    public Guid RezeptId { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>); ein Tausch mit veralteter Version ist ein Konflikt.</summary>
    public uint Version { get; private set; }

    public static Gericht Vormerken(Guid id, Guid organisationId, Guid freizeitId, Guid programmpunktId, Guid rezeptId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        ProgrammpunktId = programmpunktId,
        RezeptId = rezeptId,
    };

    /// <summary>FR-50: Das Gericht bekommt ein anderes Rezept; was mit den Hand-Mengen geschieht, regelt <see cref="EinkaufRechnen.TauschAbbilden"/>.</summary>
    public void Tauschen(Guid rezeptId) => RezeptId = rezeptId;
}

/// <summary>
/// Story 4-2: Eine Hand-Menge auf der Einkaufsliste (CAP-29): ersetzt die berechnete Menge einer Zutat in genau einem Gericht und bleibt bei
/// geänderter Personenzahl und geändertem Rezept erhalten. Höchstens eine je (Gericht, Zutat); sie verschwindet mit dem Gericht oder der Zutat
/// (Fremdschlüssel im eigenen Schema, <c>ON DELETE CASCADE</c>). „Zurück zur berechneten Menge“ heißt: die Zeile löschen.
/// </summary>
internal sealed class MengeVonHand : IOrganisationsEigen
{
    private MengeVonHand()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public Guid GerichtId { get; private set; }

    public Guid ZutatId { get; private set; }

    /// <summary>Größer 0, höchstens 3 Nachkommastellen, ohne Null-Nachkommastellen.</summary>
    public decimal Menge { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static MengeVonHand Anlegen(Guid id, Guid organisationId, Guid freizeitId, Guid gerichtId, Guid zutatId, decimal menge) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        GerichtId = gerichtId,
        ZutatId = zutatId,
        Menge = Rezepte.KuecheWerte.OhneNullen(menge),
    };

    public void Setzen(decimal menge) => Menge = Rezepte.KuecheWerte.OhneNullen(menge);
}
