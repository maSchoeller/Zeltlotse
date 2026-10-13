namespace Zeltlotse.Freizeiten.Contracts.Dokumente;

/// <summary>
/// Story 2-9: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Sicherheit → Freizeiten): Titel und Art eines Dokuments der Freizeit, ohne
/// Datei. Eingebundene Org-Dokumente bekommen beides bei jedem Lesen aus Organisationen (keine Kopie). Läuft unter dem Mandantenfilter.
/// </summary>
public interface IFreizeitDokumenteLesen
{
    /// <summary>Das Dokument oder <c>null</c>, wenn es in dieser Freizeit nicht (mehr) existiert, auch wenn sein Org-Dokument gelöscht ist.</summary>
    Task<FreizeitDokumentInfo?> InfoAsync(Guid freizeitId, Guid dokumentId, CancellationToken abbruch = default);
}

/// <summary>Ein Dokument der Freizeit.</summary>
/// <param name="Dateiart">„PDF“, „JPEG“, „PNG“ oder „WebP“.</param>
public sealed record FreizeitDokumentInfo(Guid Id, string Titel, string Dateiart);
