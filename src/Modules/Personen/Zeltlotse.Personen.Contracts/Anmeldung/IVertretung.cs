namespace Zeltlotse.Personen.Contracts.Anmeldung;

/// <summary>
/// Lesende Schnittstelle von Personen (AD-2, AD-26): Wen eine angemeldete Person vertreten darf. Grundlage für die Anmeldung mit
/// Konto und die eigenen Anmeldungen über Organisationen hinweg.
/// </summary>
public interface IVertretung
{
    /// <summary>
    /// Ob die angemeldete Person <paramref name="personId"/> anmelden darf: sich selbst oder ein Kind unter 18 der eigenen
    /// Familie (Volljährigkeit nach Personen, Kalendertag Europe/Berlin).
    /// </summary>
    Task<bool> DarfAnmeldenAsync(Guid angemeldetePersonId, Guid personId, CancellationToken abbruch = default);

    /// <summary>Die angemeldete Person selbst (zuerst) und alle Kinder ihrer Familie.</summary>
    Task<IReadOnlyList<Guid>> PersonenAsync(Guid angemeldetePersonId, CancellationToken abbruch = default);
}
