using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Organisationen.Contracts.Rollen;

/// <summary>
/// Lesende Schnittstelle von Organisationen für die Rechteprüfung des API-Hosts (AD-12): die Rollen einer Person in einer
/// Organisation. Läuft unter dem Mandantenfilter; der Mandanten-Kontext der Anfrage muss diese Organisation sein (AD-6).
/// </summary>
public interface IOrgRollenLesen
{
    Task<IReadOnlyCollection<OrgRolle>> RollenAsync(Guid organisationId, Guid personId, CancellationToken abbruch = default);
}
