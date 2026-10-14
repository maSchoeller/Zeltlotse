using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Inventar;

/// <summary>
/// Story 3-3: Rollenlisten der Inventar-Endpunkte (AD-12). Ein Test hält sie gleich mit Organisationen (<c>OrgRechte.InventarSehen</c>,
/// <c>OrgRechte.InventarVerwalten</c>, daraus die Aktionsschlüssel <c>inventar-sehen</c> und <c>inventar-verwalten</c>) und Freizeiten
/// (<c>FreizeitRechte.Mitarbeit</c>).
/// </summary>
internal static class InventarRechte
{
    /// <summary>Das Inventar der Organisation mit allen Reservierungen sehen: Org-Admin und Org-Überblick.</summary>
    public static readonly OrgRolleArt[] OrgLesen = [OrgRolleArt.OrgAdmin, OrgRolleArt.OrgUeberblick];

    /// <summary>Artikel hinzufügen, bearbeiten und löschen: nur Org-Admin.</summary>
    public static readonly OrgRolleArt[] OrgVerwalten = [OrgRolleArt.OrgAdmin];

    /// <summary>Das Inventar in der Freizeit sehen, reservieren und Reservierungen aufheben: Leitung und Team.</summary>
    public static readonly RolleArt[] Mitarbeit = [RolleArt.Leitung, RolleArt.Team];
}
