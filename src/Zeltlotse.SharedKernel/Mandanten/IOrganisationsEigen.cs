namespace Zeltlotse.SharedKernel.Mandanten;

/// <summary>Organisationseigene Entität: jeder Modul-DbContext filtert sie global nach dem Mandanten-Kontext (AD-6).</summary>
public interface IOrganisationsEigen
{
    Guid OrganisationId { get; }
}
