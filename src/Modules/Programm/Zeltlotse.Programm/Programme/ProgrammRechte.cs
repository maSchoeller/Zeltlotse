using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 3-1: Rollenliste der Programm-Endpunkte im Freizeit-Kontext (AD-12). Ein Test hält sie gleich mit <c>FreizeitRechte.Mitarbeit</c>.
/// Jede Freizeit-Rolle darf alles im Programm; die Leitung teilt nichts zu, übernehmen und abgeben kann nur die angemeldete Person für sich.
/// Story 3-5: dazu die Rollenliste der Aufgaben-Vorlagen im Org-Kontext; ein Test hält sie gleich mit <c>OrgRechte.AufgabenVorlagen</c> (daraus
/// der Aktionsschlüssel <c>aufgaben-vorlagen-verwalten</c>). Story 3-6: dazu die Rollenliste der Programmbibliothek im Org-Kontext; ein Test hält
/// sie gleich mit <c>OrgRechte.Programmbibliothek</c> (daraus der Aktionsschlüssel <c>programmbibliothek-verwalten</c>).
/// </summary>
internal static class ProgrammRechte
{
    /// <summary>Programm lesen und planen: Leitung und Team.</summary>
    public static readonly RolleArt[] Mitarbeit = [RolleArt.Leitung, RolleArt.Team];

    /// <summary>Story 3-5: Aufgaben-Vorlagen der Organisation sehen und pflegen: nur Org-Admin.</summary>
    public static readonly OrgRolleArt[] Vorlagen = [OrgRolleArt.OrgAdmin];

    /// <summary>Story 3-6: die Programmbibliothek der Organisation sehen und Einträge entfernen: nur Org-Admin (Org-Überblick nicht).</summary>
    public static readonly OrgRolleArt[] Bibliothek = [OrgRolleArt.OrgAdmin];
}
