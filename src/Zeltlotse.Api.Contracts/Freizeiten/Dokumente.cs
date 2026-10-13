using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>
/// Story 2-7, CAP-25: Wer ein Dokument der Freizeit sieht. <see cref="Team"/> nur Leitung und Team, <see cref="ElternUndTeilnehmende"/>
/// zusätzlich Eltern und Gast-Anmeldende in der Elterninfo (FR-68), <see cref="Oeffentlich"/> auf der öffentlichen Freizeitseite.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DokumentSichtbarkeit>))]
public enum DokumentSichtbarkeit
{
    Team,
    ElternUndTeilnehmende,
    Oeffentlich,
}

/// <summary>
/// Ein Dokument der Freizeit aus <c>GET /api/freizeiten/{freizeitId}/dokumente</c> (Leitung und Team): eine eigene Datei oder eine
/// Einbindung eines Org-Dokuments (<see cref="AusOrganisation"/>); dann kommen Titel, Art, Größe und Datum aus der Organisation.
/// </summary>
/// <param name="Dateiart">„PDF“, „JPEG“, „PNG“ oder „WebP“, am Inhalt erkannt.</param>
/// <param name="Groesse">Größe in Bytes.</param>
/// <param name="Version">Version der Zeile für den Autosave (AD-19).</param>
public sealed record FreizeitDokument(
    Guid Id, string Titel, DokumentSichtbarkeit Sichtbarkeit, string Dateiart, long Groesse, DateTimeOffset HochgeladenAm, bool AusOrganisation, uint Version)
{
    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(FreizeitDokument)}({Id})";
}

/// <summary>Antwort von <c>GET /api/freizeiten/{freizeitId}/dokumente</c>: alle Sichtbarkeiten, nach Titel sortiert.</summary>
public sealed record FreizeitDokumente(IReadOnlyList<FreizeitDokument> Dokumente);

/// <summary>Ein Org-Dokument, das die Freizeit noch einbinden kann.</summary>
public sealed record OrgDokumentAuswahl(Guid Id, string Titel)
{
    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(OrgDokumentAuswahl)}({Id})";
}

/// <summary>Antwort von <c>GET /api/freizeiten/{freizeitId}/dokumente/org</c>: die noch nicht eingebundenen Org-Dokumente, nach Titel.</summary>
public sealed record OrgDokumenteAuswahl(IReadOnlyList<OrgDokumentAuswahl> Dokumente);

/// <summary>Körper von <c>POST /api/freizeiten/{freizeitId}/dokumente/einbindungen</c>: das Org-Dokument (Pflicht).</summary>
public sealed record DokumentEinbinden(Guid? OrgDokumentId);

/// <summary>Ein Dokument als Verweis für Elterninfo und Freizeitseite: Kennung, Titel und Art, ohne Größe und Version.</summary>
public sealed record DokumentVerweis(Guid Id, string Titel, string Dateiart)
{
    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(DokumentVerweis)}({Id})";
}

/// <summary>
/// Antwort der Elterninfo zu einer Anmeldung (FR-68): der Name der Freizeit und ihre Dokumente mit Sichtbarkeit
/// <see cref="DokumentSichtbarkeit.ElternUndTeilnehmende"/>, nach Titel.
/// </summary>
public sealed record Elterninfo(string FreizeitName, IReadOnlyList<DokumentVerweis> Dokumente);

/// <summary>Bearbeitbare Felder eines Dokuments für <c>PATCH /api/freizeiten/{freizeitId}/dokumente/{dokumentId}</c> (AD-19).</summary>
public enum DokumentFeld
{
    Titel,
    Sichtbarkeit,
}

/// <summary>
/// JSON-Namen der Dokumente (Fehlerschlüssel in ProblemDetails, AD-27): das Multipart-Feld <c>datei</c>, das optionale Feld
/// <c>sichtbarkeit</c> und im PATCH <c>titel</c> oder <c>sichtbarkeit</c> (die Sichtbarkeit als Name, z. B. „Oeffentlich“).
/// </summary>
public static class DokumentFelder
{
    public const string Datei = "datei";

    public const string Sichtbarkeit = "sichtbarkeit";

    public const string Titel = "titel";

    /// <summary>Feld im Körper beim Einbinden.</summary>
    public const string OrgDokumentId = "orgDokumentId";

    public const int TitelMaximaleLaenge = 200;

    public static string JsonName(DokumentFeld feld) => feld switch
    {
        DokumentFeld.Titel => Titel,
        DokumentFeld.Sichtbarkeit => Sichtbarkeit,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
