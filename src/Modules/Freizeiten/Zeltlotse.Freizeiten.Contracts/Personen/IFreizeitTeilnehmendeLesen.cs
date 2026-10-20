namespace Zeltlotse.Freizeiten.Contracts.Personen;

/// <summary>
/// Story 5-3: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Finanzen → Freizeiten): wer als teilnehmende Person einer Freizeit zählt, für den Haken
/// „Beitrag bezahlt“ (FR-60). Liefert nur Kennungen, keine Namen und keine Kennung einer Anmeldung (AD-7). Läuft unter dem Mandantenfilter: Freizeiten
/// anderer Organisationen liefern nichts.
/// </summary>
public interface IFreizeitTeilnehmendeLesen
{
    /// <summary>
    /// Die verschiedenen Personen aus Anmeldungen mit Art Teilnehmende und Status bestätigt, jede einmal. Mitarbeitende, eingegangene und abgesagte
    /// Anmeldungen gehören nicht dazu, gelöschte Personen (Platzhalter) auch nicht.
    /// </summary>
    Task<IReadOnlyList<Guid>> TeilnehmendeAsync(Guid freizeitId, CancellationToken abbruch = default);
}
