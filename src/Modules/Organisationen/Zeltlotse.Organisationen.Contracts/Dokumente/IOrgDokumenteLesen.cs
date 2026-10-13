using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.Organisationen.Contracts.Dokumente;

/// <summary>
/// Lesende Schnittstelle von Organisationen (AD-2, Pfeil Freizeiten → Organisationen, Story 2-7): die Org-Dokumente der Organisation
/// des Mandanten-Kontexts, die Freizeiten per Kennung einbinden, ohne sie zu kopieren (AD-15). Ohne Kontext liefert sie nichts.
/// </summary>
public interface IOrgDokumenteLesen
{
    /// <summary>Alle Org-Dokumente der Organisation, nach Titel (deutsch, ohne Groß- und Kleinschreibung).</summary>
    Task<IReadOnlyList<OrgDokumentInfo>> ListeAsync(CancellationToken abbruch = default);

    /// <summary>Die Angaben der übergebenen Org-Dokumente; unbekannte (auch gelöschte oder fremde) fehlen im Ergebnis.</summary>
    Task<IReadOnlyDictionary<Guid, OrgDokumentInfo>> InfosAsync(IReadOnlyCollection<Guid> ids, CancellationToken abbruch = default);

    /// <summary>Die Datei eines Org-Dokuments oder <c>null</c>, wenn es das Dokument oder seine Datei nicht gibt.</summary>
    Task<OrgDokumentDatei?> DateiAsync(Guid id, CancellationToken abbruch = default);
}

/// <summary>Angaben eines Org-Dokuments: Titel, am Inhalt erkannte Art, Größe in Bytes und Zeitpunkt des Hochladens.</summary>
public sealed record OrgDokumentInfo(Guid Id, string Titel, DateiArt Dateiart, long Groesse, DateTimeOffset HochgeladenAm)
{
    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(OrgDokumentInfo)}({Id})";
}

/// <summary>Inhalt einer Datei mit Content-Type und Art aus der Prüfung des Inhalts.</summary>
public sealed record OrgDokumentDatei(byte[] Inhalt, string ContentType, DateiArt Dateiart)
{
    /// <summary>Keinen Inhalt ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(OrgDokumentDatei)}({Dateiart})";
}
