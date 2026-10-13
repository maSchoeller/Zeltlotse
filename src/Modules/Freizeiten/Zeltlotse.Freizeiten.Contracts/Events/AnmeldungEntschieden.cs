namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Die Leitung hat eine Anmeldung bestätigt (FR-20, Tabelle Ereignis-Fluss): Personen setzt die Freigabe auf die Stufe Team (AD-8),
/// Organisationen macht Mitarbeitende zu Mitarbeitenden der Organisation (FR-7). Datensatz im Umschlag ist die Anmeldung, die
/// Organisation die der Freizeit.
/// </summary>
/// <param name="Fragen">
/// Story 2-5: die Fragen, die die Freizeit für die Art der Anmeldung bei der Bestätigung abfragt; Personen ersetzt damit die Fragen der
/// Freigabe. Fehlt in Einträgen von vor Story 2-5 (dann bleiben die bisherigen).
/// </param>
public sealed record AnmeldungBestaetigt(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art, FreizeitpassFrage[]? Fragen = null);

/// <summary>
/// Die Leitung hat eine Absage eingetragen (FR-21, Tabelle Ereignis-Fluss): Personen beendet die Freigabe sofort (AD-8). Datensatz im
/// Umschlag ist die Anmeldung, die Organisation die der Freizeit.
/// </summary>
public sealed record AnmeldungAbgesagt(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art);
