namespace Zeltlotse.SharedKernel.Rechte;

/// <summary>
/// Story 6-4 (AD-12): Der QR-Gast der laufenden Anfrage, Muster <c>IAngemeldet</c>. Der API-Host füllt ihn erst, wenn ein Endpunkt mit
/// <c>.NurQrGast()</c> die Gast-Sitzung geprüft hat (Link mit diesem Stempel vorhanden, Teilnehmer-Zugang an, vor dem Freizeitende); sonst sind beide
/// Werte <c>null</c>. Er kennt keine Person und keinen Namen.
/// </summary>
public interface IQrGast
{
    /// <summary>Die Freizeit, für die der QR-Link gilt.</summary>
    Guid? FreizeitId { get; }

    /// <summary>Die Kennung des Geräts, neu bei jeder Einlösung des QR-Links (Besitzer des eigenen Push-Abos).</summary>
    Guid? GeraeteId { get; }

    /// <summary>Der Stempel des QR-Links, mit dem die Gast-Sitzung entstand; nach „Link erneuern“ gilt er nicht mehr (auch nicht für das Push-Abo).</summary>
    Guid? Stempel { get; }
}
