using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Organisationen.Contracts.Rollen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// <see cref="IKontextRollen"/> im API-Host (AD-12): Global Admin aus Konten, Org-Rollen aus Organisationen, Freizeit-Rollen
/// aus Freizeiten, je Anfrage frisch gelesen (Scope = Anfrage) und nie in der Sitzung gespeichert. Ohne Sitzung: keine Rollen.
/// </summary>
internal sealed class KontextRollen(IAngemeldet angemeldet, IKontoLesen konten, IOrgRollenLesen orgRollen, IFreizeitRollenLesen freizeitRollen)
    : IKontextRollen
{
    private readonly Dictionary<Guid, IReadOnlyCollection<OrgRolle>> _orgRollen = [];
    private readonly Dictionary<Guid, IReadOnlyCollection<FreizeitRolle>> _freizeitRollen = [];
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

    public async Task<IReadOnlyCollection<FreizeitRolle>> FreizeitRollenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        if (angemeldet.PersonId is not { } personId)
        {
            return [];
        }

        if (!_freizeitRollen.TryGetValue(freizeitId, out var rollen))
        {
            rollen = await freizeitRollen.RollenAsync(freizeitId, personId, abbruch);
            _freizeitRollen[freizeitId] = rollen;
        }

        return rollen;
    }
}

/// <summary>
/// <see cref="IAngemeldeteMail"/> im API-Host (AD-11, AD-12): die Mail-Adresse des Kontos der Sitzung aus Konten, je Anfrage
/// gelesen. So braucht kein Modul einen Pfeil zu Konten, nur um die Mail-Bindung einer Einladung zu prüfen.
/// </summary>
internal sealed class AngemeldeteMail(IAngemeldet angemeldet, IKontoLesen konten) : IAngemeldeteMail
{
    public async Task<string?> MailAsync(CancellationToken abbruch = default) =>
        angemeldet.KontoId is { } kontoId ? await konten.MailAsync(kontoId, abbruch) : null;
}
