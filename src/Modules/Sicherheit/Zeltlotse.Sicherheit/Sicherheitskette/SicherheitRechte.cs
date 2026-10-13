using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Sicherheit.Sicherheitskette;

/// <summary>
/// Story 2-8: Rollenlisten der Sicherheitskette im Freizeit-Kontext (AD-12); Story 2-10: dazu das Archiv der Vorfall-Notizen im
/// Org-Kontext. Ein Test hält sie gleich mit Freizeiten:
/// <see cref="Lesen"/> mit <c>FreizeitRechte.Mitarbeit</c>, <see cref="Einrichten"/> mit <c>FreizeitRechte.Einstellungen</c>. In der
/// Oberfläche gilt zum Einrichten die erlaubte Aktion <c>freizeit-einstellungen</c>; lesen dürfen alle mit Freizeit-Kontext.
/// </summary>
internal static class SicherheitRechte
{
    /// <summary>Die Sicherheitskette lesen: Leitung und Team.</summary>
    public static readonly RolleArt[] Lesen = [RolleArt.Leitung, RolleArt.Team];

    /// <summary>Die Sicherheitskette einrichten: nur die Leitung.</summary>
    public static readonly RolleArt[] Einrichten = [RolleArt.Leitung];

    /// <summary>
    /// Story 2-10: das Archiv der Vorfall-Notizen der Organisation sehen: nur Org-Admins. Ein Test hält die Liste gleich mit
    /// <c>OrgRechte.VorfallArchiv</c> (Aktionsschlüssel <c>vorfall-archiv-sehen</c>).
    /// </summary>
    public static readonly OrgRolleArt[] Archiv = [OrgRolleArt.OrgAdmin];
}
