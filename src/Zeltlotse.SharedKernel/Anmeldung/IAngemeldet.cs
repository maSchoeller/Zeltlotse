namespace Zeltlotse.SharedKernel.Anmeldung;

/// <summary>
/// Die angemeldete Person der laufenden Anfrage (AD-12): Konto und Person aus der Sitzung, ohne Rollen, dazu der Zeitpunkt
/// der Anmeldung (AD-25). Der API-Host setzt die Schnittstelle aus <c>HttpContext.User</c> um; ohne gültige Sitzung sind alle
/// Werte <c>null</c>.
/// </summary>
public interface IAngemeldet
{
    Guid? KontoId { get; }

    Guid? PersonId { get; }

    /// <summary>Wann die Anmeldung dieser Sitzung stattfand; <c>null</c> ohne Sitzung oder bei älteren Sitzungen ohne Zeitpunkt.</summary>
    DateTimeOffset? AngemeldetAm { get; }
}
