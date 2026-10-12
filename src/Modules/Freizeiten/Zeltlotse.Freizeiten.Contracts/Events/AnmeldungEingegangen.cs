namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Eine Anmeldung ist eingegangen (FR-16 bis FR-19, Tabelle Ereignis-Fluss): Personen legt daraufhin die Freigabe der Stufe
/// Leitung an (AD-8), gültig bis <paramref name="FreigabeGueltigBis"/> (Ende des Freigabefensters der Freizeit). Datensatz im
/// Umschlag ist die Anmeldung, die Organisation die der Freizeit.
/// </summary>
public sealed record AnmeldungEingegangen(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art, DateTimeOffset FreigabeGueltigBis);

/// <summary>Art einer Anmeldung, wie sie das Ereignis trägt. Werte sind gespeichert; nur anhängen.</summary>
public enum AnmeldungArt
{
    Teilnehmende = 1,
    Mitarbeitende = 2,
}
