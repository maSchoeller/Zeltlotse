using Microsoft.EntityFrameworkCore;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;

namespace Zeltlotse.Core.Persistenz;

/// <summary>Was jemand in einer Organisation darf.</summary>
public sealed record OrgRechte(OrgRolle? Rolle)
{
    public static readonly OrgRechte Keine = new((OrgRolle?)null);

    public bool DarfLesen => Rolle is not null;

    public bool DarfVerwalten => Rolle == OrgRolle.OrgAdmin;
}

/// <summary>
/// Was jemand bei einer Freizeit darf. Additiv: Wer Leitung <em>und</em>
/// OrgAdmin ist, bekommt die Vereinigung beider Rechte.
/// </summary>
public sealed record FreizeitRechte(FreizeitRolle? Rolle, bool IstOrgAdmin)
{
    public static readonly FreizeitRechte Keine = new(null, false);

    public bool DarfLesen => Rolle is not null || IstOrgAdmin;

    /// <summary>Name, Zeitraum, Ort, Status — der Rahmen, den der OrgAdmin verantwortet.</summary>
    public bool DarfEckdatenAendern => Rolle == FreizeitRolle.Leitung || IstOrgAdmin;

    public bool DarfTeamVerwalten => Rolle == FreizeitRolle.Leitung || IstOrgAdmin;

    /// <summary>Löschen bleibt beim OrgAdmin — die Leitung entfernt ihre eigene Freizeit nicht.</summary>
    public bool DarfLoeschen => IstOrgAdmin;
}

public interface IBerechtigung
{
    Task<OrgRechte> FuerOrganisationAsync(Guid nutzerId, Guid organisationId, CancellationToken abbruch);

    Task<FreizeitRechte> FuerFreizeitAsync(Guid nutzerId, Guid freizeitId, CancellationToken abbruch);
}

/// <summary>
/// Liest die Zuordnungen einmal je Anfrage aus der Datenbank statt aus
/// Token-Claims: Rechte in Claims sind nach jeder Rollenänderung falsch, bis
/// sich jemand neu anmeldet. Der Preis ist eine Abfrage je Anfrage.
/// </summary>
public sealed class Berechtigung(ZeltlotseDbContext datenbank) : IBerechtigung
{
    private readonly Dictionary<(Guid, Guid), OrgRechte> _orgZwischenspeicher = [];

    public async Task<OrgRechte> FuerOrganisationAsync(
        Guid nutzerId, Guid organisationId, CancellationToken abbruch)
    {
        if (_orgZwischenspeicher.TryGetValue((nutzerId, organisationId), out var bekannt))
        {
            return bekannt;
        }

        var mitgliedschaft = await datenbank.OrgMitgliedschaften
            .Where(m => m.NutzerId == nutzerId && m.OrganisationId == organisationId)
            .Select(m => (OrgRolle?)m.Rolle)
            .FirstOrDefaultAsync(abbruch);

        var rechte = new OrgRechte(mitgliedschaft);
        _orgZwischenspeicher[(nutzerId, organisationId)] = rechte;

        return rechte;
    }

    public async Task<FreizeitRechte> FuerFreizeitAsync(
        Guid nutzerId, Guid freizeitId, CancellationToken abbruch)
    {
        var freizeit = await datenbank.Freizeiten
            .Where(f => f.Id == freizeitId)
            .Select(f => new { f.TenantId })
            .FirstOrDefaultAsync(abbruch);

        if (freizeit is null)
        {
            return FreizeitRechte.Keine;
        }

        var zuordnung = await datenbank.FreizeitZuordnungen
            .Where(z => z.NutzerId == nutzerId && z.FreizeitId == freizeitId)
            .Select(z => (FreizeitRolle?)z.Rolle)
            .FirstOrDefaultAsync(abbruch);

        var org = await FuerOrganisationAsync(nutzerId, freizeit.TenantId, abbruch);

        return new FreizeitRechte(zuordnung, org.DarfVerwalten);
    }
}
