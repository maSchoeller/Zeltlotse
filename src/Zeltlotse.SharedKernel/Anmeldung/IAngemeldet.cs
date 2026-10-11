namespace Zeltlotse.SharedKernel.Anmeldung;

/// <summary>
/// Die angemeldete Person der laufenden Anfrage (AD-12): Konto und Person aus der Sitzung, ohne Rollen.
/// Der API-Host setzt die Schnittstelle aus <c>HttpContext.User</c> um; ohne gültige Sitzung sind beide Werte <c>null</c>.
/// </summary>
public interface IAngemeldet
{
    Guid? KontoId { get; }

    Guid? PersonId { get; }
}
