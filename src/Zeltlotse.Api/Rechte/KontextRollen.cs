using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Organisationen.Contracts.Rollen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// <see cref="IKontextRollen"/> im API-Host (AD-12): Global Admin aus Konten, Org-Rollen aus Organisationen, je Anfrage
/// frisch gelesen (Scope = Anfrage) und nie in der Sitzung gespeichert. Ohne Sitzung: keine Rollen.
/// </summary>
internal sealed class KontextRollen(IAngemeldet angemeldet, IKontoLesen konten, IOrgRollenLesen orgRollen) : IKontextRollen
{
    private readonly Dictionary<Guid, IReadOnlyCollection<OrgRolle>> _orgRollen = [];
    private bool? _globalAdmin;

    public async Task<bool> IstGlobalAdminAsync(CancellationToken abbruch = default) =>
        _globalAdmin ??= angemeldet.KontoId is { } kontoId && await konten.IstGlobalAdminAsync(kontoId, abbruch);

    public async Task<IReadOnlyCollection<OrgRolle>> OrgRollenAsync(Guid organisationId, CancellationToken abbruch = default)
    {
        if (angemeldet.PersonId is not { } personId)
        {
            return [];
        }

        if (!_orgRollen.TryGetValue(organisationId, out var rollen))
        {
            rollen = await orgRollen.RollenAsync(organisationId, personId, abbruch);
            _orgRollen[organisationId] = rollen;
        }

        return rollen;
    }
}
