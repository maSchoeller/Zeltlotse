namespace Zeltlotse.Konten.Contracts.Konten;

/// <summary>
/// Lesende Schnittstelle von Konten (AD-2, AD-12): Global-Admin-Kennzeichen für die Rechteprüfung des API-Hosts und die
/// Mail-Adresse eines Kontos, z. B. für die Bindung einer Einladung an die Mail-Adresse (AD-11).
/// </summary>
public interface IKontoLesen
{
    Task<bool> IstGlobalAdminAsync(Guid kontoId, CancellationToken abbruch = default);

    /// <summary>Die normalisierte Mail-Adresse des Kontos oder <c>null</c>, wenn es das Konto nicht gibt.</summary>
    Task<string?> MailAsync(Guid kontoId, CancellationToken abbruch = default);

    /// <summary>
    /// Die Mail-Adresse des Kontos einer Person (je Person höchstens ein Konto) oder <c>null</c>, wenn die Person kein Konto hat,
    /// z. B. für eine Erinnerungs-Mail, die erst beim Versand ihre Adresse bekommt (AD-13).
    /// </summary>
    Task<string?> MailDerPersonAsync(Guid personId, CancellationToken abbruch = default);
}
