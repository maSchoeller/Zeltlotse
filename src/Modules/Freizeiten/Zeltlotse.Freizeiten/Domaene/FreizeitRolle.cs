using Zeltlotse.SharedKernel.Mandanten;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>
/// Rolle einer Person in einer Freizeit (FR-6, AD-12): eindeutig je (Freizeit, Person). Entsteht nur über
/// <see cref="Freizeit.LeitungEinsetzen"/> oder, mit der Bestätigung von Mitarbeitenden, <see cref="Freizeit.TeamEinsetzen"/>.
/// </summary>
internal sealed class FreizeitRolle : IOrganisationsEigen
{
    private FreizeitRolle()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid PersonId { get; private init; }

    public RolleArt Rolle { get; private init; }

    public DateTimeOffset VergebenAm { get; private init; }

    public static FreizeitRolle Vergeben(Guid organisationId, Guid freizeitId, Guid personId, RolleArt rolle, DateTimeOffset jetzt) => new()
    {
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        PersonId = personId,
        Rolle = rolle,
        VergebenAm = jetzt,
    };
}
