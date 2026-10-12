namespace Zeltlotse.Freizeiten.Contracts.Events;

/// <summary>
/// Eine Person hat in einer Freizeit eine Rolle bekommen oder verloren (FR-6, Tabelle Ereignis-Fluss): Personen pflegt damit die
/// eigene Liste der Freizeit-Rollen (AD-8). Datensatz im Umschlag ist die Freizeit; die Folgenummer ordnet alle Rollenwechsel
/// einer Freizeit.
/// </summary>
public sealed record FreizeitRolleGeaendert(Guid FreizeitId, Guid PersonId, FreizeitRolleArt Rolle);

/// <summary>
/// Rolle in einer Freizeit, wie sie das Ereignis trägt; <see cref="Entzogen"/> heißt: Die Person hat keine Rolle mehr (Absage von
/// Mitarbeitenden, Story 2-3). Werte sind gespeichert; nur anhängen.
/// </summary>
public enum FreizeitRolleArt
{
    Leitung = 1,
    Team = 2,
    Entzogen = 3,
}
