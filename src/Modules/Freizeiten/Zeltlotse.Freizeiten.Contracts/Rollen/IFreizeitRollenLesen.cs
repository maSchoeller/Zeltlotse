using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Freizeiten.Contracts.Rollen;

/// <summary>
/// Lesende Schnittstelle von Freizeiten für die Rechteprüfung des API-Hosts (AD-6, AD-12).
/// </summary>
public interface IFreizeitRollenLesen
{
    /// <summary>
    /// Die Organisation einer Freizeit oder <c>null</c>, wenn es sie nicht gibt. Die einzige Lesestelle außerhalb des
    /// Mandantenfilters für die Kontext-Auflösung: Die adressierte Freizeit ist die Quelle des Mandanten-Kontexts (AD-6).
    /// </summary>
    Task<Guid?> OrganisationAsync(Guid freizeitId, CancellationToken abbruch = default);

    /// <summary>
    /// Die Rollen einer Person in einer Freizeit. Läuft unter dem Mandantenfilter; der Mandanten-Kontext muss die
    /// Organisation der Freizeit sein (der Host setzt ihn aus <see cref="OrganisationAsync"/>).
    /// </summary>
    Task<IReadOnlyCollection<FreizeitRolle>> RollenAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default);
}
