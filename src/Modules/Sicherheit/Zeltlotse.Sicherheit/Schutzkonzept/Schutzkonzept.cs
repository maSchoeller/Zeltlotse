using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Sicherheit.Schutzkonzept;

/// <summary>
/// Story 2-9: Welches Dokument der Freizeit das Schutzkonzept ist (FR-30, AD-3). Das Dokument selbst besitzt Freizeiten (AD-15); hier liegt
/// nur der Verweis. Je Freizeit höchstens eine Zeile, bearbeitet nach AD-19 über <see cref="FeldAendern"/>.
/// </summary>
/// <remarks>
/// Regel „wirksames Schutzkonzept“: Ein gespeichertes Dokument, das es in der Freizeit nicht mehr gibt, gilt als kein Schutzkonzept
/// (<see cref="Wirksam"/>). Löschen in Freizeiten oder Organisationen meldet kein Ereignis; darum gilt die Regel beim Lesen.
/// </remarks>
internal sealed class Schutzkonzept : IOrganisationsEigen
{
    private Schutzkonzept()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public Guid? DokumentId { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Schutzkonzept Anlegen(Guid id, Guid organisationId, Guid freizeitId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
    };

    /// <summary>Das wirksame Dokument: das gespeicherte, wenn es in der Freizeit noch vorhanden ist, sonst <c>null</c>.</summary>
    public Guid? Wirksam(bool dokumentVorhanden) => dokumentVorhanden ? DokumentId : null;

    /// <summary>
    /// Ändert das Feld nach der Konfliktregel aus AD-19: Weicht der wirksame Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es ist,
    /// und das Ergebnis ist <c>false</c>. <paramref name="dokumentVorhanden"/> sagt, ob es das gespeicherte Dokument noch gibt.
    /// </summary>
    public bool FeldAendern(SchutzkonzeptFeld feld, Guid? alterWert, Guid? neuerWert, bool dokumentVorhanden)
    {
        if (feld != SchutzkonzeptFeld.DokumentId)
        {
            throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        if (Wirksam(dokumentVorhanden) != alterWert)
        {
            return false;
        }

        DokumentId = neuerWert;
        return true;
    }
}

/// <summary>
/// Story 2-9: Eine Person aus dem Team hat das Schutzkonzept (genau dieses Dokument) als gelesen bestätigt (FR-30). Unveränderlich, eindeutig
/// je (Freizeit, Dokument, Person). Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16): Bei <c>PersonGeloescht</c> fällt sie weg, nach der Frist
/// für Freizeit-Daten löscht sie der Lösch-Lauf.
/// </summary>
internal sealed class SchutzkonzeptBestaetigung : IOrganisationsEigen
{
    private SchutzkonzeptBestaetigung()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid DokumentId { get; private init; }

    public Guid PersonId { get; private init; }

    public DateTimeOffset GelesenAm { get; private init; }

    public static SchutzkonzeptBestaetigung Anlegen(Guid id, Guid organisationId, Guid freizeitId, Guid dokumentId, Guid personId, DateTimeOffset gelesenAm) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        DokumentId = dokumentId,
        PersonId = personId,
        GelesenAm = gelesenAm,
    };
}
