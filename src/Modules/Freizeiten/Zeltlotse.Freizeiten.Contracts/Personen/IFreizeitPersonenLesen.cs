namespace Zeltlotse.Freizeiten.Contracts.Personen;

/// <summary>
/// Story 2-10: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Sicherheit → Freizeiten): ob eine Person zu einer Freizeit gehört. Läuft
/// unter dem Mandantenfilter: Freizeiten anderer Organisationen liefern immer <c>false</c>.
/// </summary>
public interface IFreizeitPersonenLesen
{
    /// <summary>
    /// Wahr, wenn die Person in der Freizeit eine Anmeldung hat, die nicht abgesagt ist, oder eine Freizeit-Rolle Leitung oder Team. Der
    /// Platzhalter einer gelöschten Person gehört nie dazu.
    /// </summary>
    Task<bool> GehoertDazuAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default);
}
