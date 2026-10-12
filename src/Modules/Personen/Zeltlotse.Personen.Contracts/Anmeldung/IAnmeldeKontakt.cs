namespace Zeltlotse.Personen.Contracts.Anmeldung;

/// <summary>
/// Lesende Schnittstelle von Personen (AD-2, AD-13, Story 2-3): an wen die Bestätigungs- oder Absage-Mail einer Anmeldung geht. Der
/// Zweck ist allein der Mail-Versand beim Zustellen; die Adresse steht nie in der Outbox. Liest über Organisationen hinweg.
/// </summary>
public interface IAnmeldeKontakt
{
    /// <summary>
    /// Die Mail-Adresse für die angemeldete Person: ihr eigenes Konto; sonst der zuerst eingetragene Elternteil ihrer Familie mit
    /// Konto; sonst die Mail des Gast-Zugangs der Familie; sonst (auch bei unbekannter oder gelöschter Person) <c>null</c> – dann
    /// geht keine Mail hinaus. Immer genau eine Adresse, nie beide Elternteile.
    /// </summary>
    Task<string?> MailAsync(Guid personId, CancellationToken abbruch = default);
}
