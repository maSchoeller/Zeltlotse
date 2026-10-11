using Zeltlotse.SharedKernel.Mandanten;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Domaene;

/// <summary>
/// Vergebene Rolle einer Person in einer Organisation (FR-5, AD-12): eindeutig je (Organisation, Person, Rolle); eine
/// Person kann beide Rollen haben. Entsteht nur durch eine angenommene Einladung.
/// </summary>
internal sealed class OrgRolle : IOrganisationsEigen
{
    private OrgRolle()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid PersonId { get; private init; }

    public OrgRolleArt Rolle { get; private init; }

    public DateTimeOffset VergebenAm { get; private init; }

    public static OrgRolle Vergeben(Guid organisationId, Guid personId, OrgRolleArt rolle, DateTimeOffset jetzt) => new()
    {
        OrganisationId = organisationId,
        PersonId = personId,
        Rolle = rolle,
        VergebenAm = jetzt,
    };
}
