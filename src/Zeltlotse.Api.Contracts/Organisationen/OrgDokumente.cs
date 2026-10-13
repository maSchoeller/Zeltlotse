namespace Zeltlotse.Api.Contracts.Organisationen;

/// <summary>
/// Ein Org-Dokument aus <c>GET /api/organisationen/{orgId}/dokumente</c> (Org-Admin, Story 2-7): Freizeiten der Organisation binden es
/// ein, ohne es zu kopieren.
/// </summary>
/// <param name="Dateiart">„PDF“, „JPEG“, „PNG“ oder „WebP“, am Inhalt erkannt.</param>
/// <param name="Groesse">Größe in Bytes.</param>
public sealed record OrgDokument(Guid Id, string Titel, string Dateiart, long Groesse, DateTimeOffset HochgeladenAm)
{
    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(OrgDokument)}({Id})";
}

/// <summary>Antwort von <c>GET /api/organisationen/{orgId}/dokumente</c>: alle Org-Dokumente, nach Titel sortiert.</summary>
public sealed record OrgDokumente(IReadOnlyList<OrgDokument> Dokumente);
