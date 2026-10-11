namespace Zeltlotse.SharedKernel.Mandanten;

/// <summary>
/// Der Mandant (die Organisation) der laufenden Anfrage bzw. des zugestellten Ereignisses (AD-6).
/// Quelle ist die adressierte Ressource, das Token oder der Umschlag, nie eine Auswahl im Client.
/// </summary>
public interface IMandantenKontext
{
    Guid? OrganisationId { get; }
}

/// <summary>Scoped-Umsetzung; gesetzt wird der Kontext genau einmal am Rand (Endpunkt, Zustellung).</summary>
public sealed class MandantenKontext : IMandantenKontext
{
    public Guid? OrganisationId { get; private set; }

    public void Setzen(Guid? organisationId) => OrganisationId = organisationId;
}
