namespace Zeltlotse.SharedKernel.Anmeldung;

/// <summary>
/// Die Mail-Adresse des Kontos der angemeldeten Person (AD-11, AD-12), z. B. für die Bindung einer Einladung an die
/// Mail-Adresse. Der API-Host setzt die Schnittstelle je Anfrage aus der Sitzung und Konten um; so braucht kein Modul einen
/// Pfeil zu Konten, nur um die eigene Mail-Adresse zu prüfen.
/// </summary>
public interface IAngemeldeteMail
{
    /// <summary>Die normalisierte Mail-Adresse oder <c>null</c> ohne Sitzung bzw. ohne Konto.</summary>
    Task<string?> MailAsync(CancellationToken abbruch = default);
}
