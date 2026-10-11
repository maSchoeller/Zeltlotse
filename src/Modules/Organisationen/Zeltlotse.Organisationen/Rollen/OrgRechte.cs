using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Organisationen.Daten;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Organisationen.Contracts.Rollen;
using Zeltlotse.SharedKernel.Mandanten;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;
using ApiOrgRolle = Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle;

namespace Zeltlotse.Organisationen.Rollen;

/// <summary>
/// Die Rollenlisten der Endpunkte (AD-12) und daraus die erlaubten Aktionen (AD-29): Die Merkmale <c>.NurOrgRolle(…)</c>
/// und <see cref="Erlaubt"/> nutzen dieselben Listen, damit Oberfläche und Server nie auseinanderlaufen.
/// </summary>
internal static class OrgRechte
{
    /// <summary>Die Organisation sehen: Name und eigene Rollen.</summary>
    public static readonly OrgRolleArt[] Lesen = [OrgRolleArt.OrgAdmin, OrgRolleArt.OrgUeberblick];

    /// <summary>Rollen und offene Einladungen sehen, Personen einladen.</summary>
    public static readonly OrgRolleArt[] RollenVergeben = [OrgRolleArt.OrgAdmin];

    public static IReadOnlyList<string> Erlaubt(IReadOnlyCollection<OrgRolleArt> rollen) =>
        rollen.Any(RollenVergeben.Contains) ? [Aktionsschluessel.OrgRollenVergeben] : [];

    public static ApiOrgRolle Api(OrgRolleArt rolle) => rolle switch
    {
        OrgRolleArt.OrgAdmin => ApiOrgRolle.OrgAdmin,
        OrgRolleArt.OrgUeberblick => ApiOrgRolle.OrgUeberblick,
        _ => throw new ArgumentOutOfRangeException(nameof(rolle), rolle, null),
    };

    public static OrgRolleArt? Server(ApiOrgRolle? rolle) => rolle switch
    {
        ApiOrgRolle.OrgAdmin => OrgRolleArt.OrgAdmin,
        ApiOrgRolle.OrgUeberblick => OrgRolleArt.OrgUeberblick,
        null => null,
        _ => (OrgRolleArt)(-1),
    };

    /// <summary>Wie die Rolle in Mails und Oberfläche heißt.</summary>
    public static string Text(OrgRolleArt rolle) => rolle switch
    {
        OrgRolleArt.OrgAdmin => "Org-Admin",
        OrgRolleArt.OrgUeberblick => "Org-Überblick",
        _ => throw new ArgumentOutOfRangeException(nameof(rolle), rolle, null),
    };
}

/// <summary>
/// <see cref="IOrgRollenLesen"/> über den Lese-Kontext unter dem Mandantenfilter: Der Kontext der Anfrage muss die
/// gefragte Organisation sein (der Host setzt ihn aus der Route, AD-6); sonst ist das ein Programmierfehler.
/// </summary>
internal sealed class OrgRollenLesen(OrganisationenLeseDbContext db, IMandantenKontext mandant) : IOrgRollenLesen
{
    public async Task<IReadOnlyCollection<OrgRolleArt>> RollenAsync(Guid organisationId, Guid personId, CancellationToken abbruch = default)
    {
        if (mandant.OrganisationId != organisationId)
        {
            throw new InvalidOperationException("Rollen werden nur im Mandanten-Kontext der Organisation gelesen (AD-6).");
        }

        return await db.Rollen.Where(r => r.PersonId == personId).Select(r => r.Rolle).ToListAsync(abbruch);
    }
}
