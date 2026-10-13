using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 3-1: Rollenliste der Programm-Endpunkte im Freizeit-Kontext (AD-12). Ein Test hält sie gleich mit <c>FreizeitRechte.Mitarbeit</c>.
/// Jede Freizeit-Rolle darf alles im Programm; die Leitung teilt nichts zu, übernehmen und abgeben kann nur die angemeldete Person für sich.
/// </summary>
internal static class ProgrammRechte
{
    /// <summary>Programm lesen und planen: Leitung und Team.</summary>
    public static readonly RolleArt[] Mitarbeit = [RolleArt.Leitung, RolleArt.Team];
}
