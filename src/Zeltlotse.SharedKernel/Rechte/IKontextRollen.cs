namespace Zeltlotse.SharedKernel.Rechte;

/// <summary>
/// Rollen der angemeldeten Person der laufenden Anfrage (AD-12). Der API-Host setzt die Schnittstelle bei jeder Anfrage
/// aus den lesenden Schnittstellen von Konten und Organisationen um; in der Sitzung liegen nie Rollen.
/// Ohne Sitzung: kein Global Admin, keine Rollen.
/// </summary>
public interface IKontextRollen
{
    Task<bool> IstGlobalAdminAsync(CancellationToken abbruch = default);

    /// <summary>Die eigenen Rollen in der Organisation; leer für eine fremde oder unbekannte Organisation.</summary>
    Task<IReadOnlyCollection<OrgRolle>> OrgRollenAsync(Guid organisationId, CancellationToken abbruch = default);
}
