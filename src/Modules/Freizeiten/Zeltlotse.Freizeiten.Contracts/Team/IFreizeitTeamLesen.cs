namespace Zeltlotse.Freizeiten.Contracts.Team;

/// <summary>
/// Story 2-9: Lesende Schnittstelle von Freizeiten (AD-2, Pfeil Sicherheit → Freizeiten): wer in einer Freizeit mitarbeitet. Läuft unter
/// dem Mandantenfilter: Freizeiten anderer Organisationen liefern eine leere Liste.
/// </summary>
public interface IFreizeitTeamLesen
{
    /// <summary>Je Person mit Freizeit-Rolle Leitung oder Team genau ein Eintrag.</summary>
    Task<IReadOnlyList<FreizeitTeamMitglied>> TeamAsync(Guid freizeitId, CancellationToken abbruch = default);
}

/// <summary>Eine Person des Teams einer Freizeit.</summary>
/// <param name="Leitung">Eine ihrer Rollen ist Leitung.</param>
public sealed record FreizeitTeamMitglied(Guid PersonId, bool Leitung);
