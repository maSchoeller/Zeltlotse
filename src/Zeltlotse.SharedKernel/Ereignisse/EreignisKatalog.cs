namespace Zeltlotse.SharedKernel.Ereignisse;

/// <summary>Alle angemeldeten Empfänger, nach Ereignis-Typ und Empfänger-Name nachschlagbar.</summary>
public sealed class EreignisKatalog(IEnumerable<EmpfaengerEintrag> eintraege)
{
    private readonly IReadOnlyList<EmpfaengerEintrag> _eintraege = [.. eintraege];

    public IReadOnlyList<EmpfaengerEintrag> Alle => _eintraege;

    public IEnumerable<EmpfaengerEintrag> EmpfaengerFuer(Type ereignisTyp) =>
        _eintraege.Where(e => e.EreignisTyp == ereignisTyp);

    public EmpfaengerEintrag? Finden(string ereignisName, string empfaenger) =>
        _eintraege.FirstOrDefault(e => e.EreignisName == ereignisName && e.Empfaenger == empfaenger);
}
