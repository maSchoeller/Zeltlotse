using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Organisationen.Domaene;

/// <summary>
/// Eine Person ist Mitarbeitende der Organisation (FR-7, CAP-3): entsteht mit der ersten bestätigten Mitarbeit in einer Freizeit der
/// Organisation (Ereignis <c>AnmeldungBestaetigt</c>), eindeutig je (Organisation, Person). Eine Absage beendet sie nicht.
/// Personeneigen (AD-16): Sie wird mit der Person gelöscht und steht in ihrer Auskunft. Keine Org-Rolle.
/// </summary>
internal sealed class Mitgliedschaft : IOrganisationsEigen
{
    private Mitgliedschaft()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid PersonId { get; private init; }

    public DateTimeOffset SeitAm { get; private init; }

    public static Mitgliedschaft Anlegen(Guid organisationId, Guid personId, DateTimeOffset jetzt) => new()
    {
        OrganisationId = organisationId,
        PersonId = personId,
        SeitAm = jetzt,
    };
}
