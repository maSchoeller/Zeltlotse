namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Die Leitung hat eine Anmeldung bestätigt (FR-20, Tabelle Ereignis-Fluss): Personen setzt die Freigabe auf die Stufe Team (AD-8),
/// Organisationen macht Mitarbeitende zu Mitarbeitenden der Organisation (FR-7). Datensatz im Umschlag ist die Anmeldung, die
/// Organisation die der Freizeit.
/// </summary>
public sealed record AnmeldungBestaetigt(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art);

/// <summary>
/// Die Leitung hat eine Absage eingetragen (FR-21, Tabelle Ereignis-Fluss): Personen beendet die Freigabe sofort (AD-8). Datensatz im
/// Umschlag ist die Anmeldung, die Organisation die der Freizeit.
/// </summary>
public sealed record AnmeldungAbgesagt(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art);
