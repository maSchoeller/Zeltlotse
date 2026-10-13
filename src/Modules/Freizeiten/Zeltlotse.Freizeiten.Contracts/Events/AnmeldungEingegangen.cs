namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Eine Anmeldung ist eingegangen (FR-16 bis FR-19, Tabelle Ereignis-Fluss): Personen legt daraufhin die Freigabe der Stufe
/// Leitung an (AD-8), gültig bis <paramref name="FreigabeGueltigBis"/> (Ende des Freigabefensters der Freizeit). Datensatz im
/// Umschlag ist die Anmeldung, die Organisation die der Freizeit.
/// </summary>
/// <param name="Fragen">
/// Story 2-5: die Fragen, die die Freizeit für die Art der Anmeldung beim Eingang abfragt; Personen legt sie in der Freigabe ab. Fehlt in
/// Einträgen von vor Story 2-5.
/// </param>
public sealed record AnmeldungEingegangen(
    Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art, DateTimeOffset FreigabeGueltigBis, FreizeitpassFrage[]? Fragen = null);

/// <summary>Art einer Anmeldung, wie sie das Ereignis trägt. Werte sind gespeichert; nur anhängen.</summary>
public enum AnmeldungArt
{
    Teilnehmende = 1,
    Mitarbeitende = 2,
}
