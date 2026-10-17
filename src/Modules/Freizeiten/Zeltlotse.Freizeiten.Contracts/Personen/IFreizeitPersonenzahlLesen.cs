namespace Zeltlotse.Freizeiten.Contracts.Personen;

/// <summary>
/// Story 4-1: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Küche → Freizeiten): wie viele Personen eine Freizeit verpflegt (FR-46, „jede
/// Person zählt gleich“). Liefert nur eine Anzahl, keine Kennungen, damit die Küche nutzbar bleibt, ohne die Anmeldungen zu sehen. Läuft unter dem
/// Mandantenfilter: Freizeiten anderer Organisationen liefern 0.
/// </summary>
public interface IFreizeitPersonenzahlLesen
{
    /// <summary>
    /// Die Anzahl verschiedener Personen aus bestätigten Anmeldungen von Teilnehmenden und mit Freizeit-Rolle Leitung oder Team; jede Person zählt
    /// einmal. Eingegangene und abgesagte Anmeldungen zählen nicht, Anmeldungen von Mitarbeitenden nur über die Team-Rolle, die ihre Bestätigung
    /// vergibt. Jede bestätigte Anmeldung einer gelöschten Person (Platzhalter) zählt einzeln.
    /// </summary>
    Task<int> AnzahlAsync(Guid freizeitId, CancellationToken abbruch = default);
}
