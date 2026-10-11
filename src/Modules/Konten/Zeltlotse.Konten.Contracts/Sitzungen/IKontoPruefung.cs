namespace Zeltlotse.Konten.Contracts.Sitzungen;

/// <summary>
/// Lesende Schnittstelle von Konten für die Sitzungsprüfung des API-Hosts (AD-12): Bei jeder Anfrage gilt eine Sitzung
/// nur, wenn das Konto noch existiert, zur Person passt und der Sitzungsstempel noch der aktuelle ist.
/// </summary>
public interface IKontoPruefung
{
    Task<bool> SitzungGueltigAsync(Guid kontoId, Guid personId, Guid sitzungsStempel, CancellationToken abbruch = default);
}
