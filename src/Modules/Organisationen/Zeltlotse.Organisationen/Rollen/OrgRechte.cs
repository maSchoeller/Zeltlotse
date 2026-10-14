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

    /// <summary>Die Nachweis-Richtlinie sehen und bearbeiten (FR-9).</summary>
    public static readonly OrgRolleArt[] RichtlinieVerwalten = [OrgRolleArt.OrgAdmin];

    /// <summary>
    /// Nachweise auf Organisationsebene prüfen (FR-10). Die Endpunkte dazu besitzt Personen; dort steht dieselbe Liste
    /// (<c>NachweisRechte.OrgPruefen</c>), ein HTTP-Test hält beide gleich.
    /// </summary>
    public static readonly OrgRolleArt[] NachweisePruefen = [OrgRolleArt.OrgAdmin];

    /// <summary>Die Löschfristen sehen und bearbeiten (NFR-5).</summary>
    public static readonly OrgRolleArt[] LoeschfristenVerwalten = [OrgRolleArt.OrgAdmin];

    /// <summary>Story 2-7: Org-Dokumente sehen, hochladen und entfernen (FR-43).</summary>
    public static readonly OrgRolleArt[] Dokumente = [OrgRolleArt.OrgAdmin];

    /// <summary>
    /// Story 2-10: das Archiv der Vorfall-Notizen sehen (CAP-17, FR-31). Den Endpunkt besitzt Sicherheit; dort steht dieselbe Liste
    /// (<c>SicherheitRechte.Archiv</c>), ein Test hält beide gleich.
    /// </summary>
    public static readonly OrgRolleArt[] VorfallArchiv = [OrgRolleArt.OrgAdmin];

    /// <summary>
    /// Story 3-3: das Inventar mit allen Reservierungen sehen (FR-37). Die Endpunkte besitzt Inventar; dort steht dieselbe Liste
    /// (<c>InventarRechte.OrgLesen</c>), ein Test hält beide gleich.
    /// </summary>
    public static readonly OrgRolleArt[] InventarSehen = [OrgRolleArt.OrgAdmin, OrgRolleArt.OrgUeberblick];

    /// <summary>Story 3-3: das Inventar pflegen (FR-37); gleich mit <c>InventarRechte.OrgVerwalten</c>.</summary>
    public static readonly OrgRolleArt[] InventarVerwalten = [OrgRolleArt.OrgAdmin];

    /// <summary>
    /// Story 3-5: die Aufgaben-Vorlagen sehen und pflegen (FR-40). Die Endpunkte besitzt Programm; dort steht dieselbe Liste
    /// (<c>ProgrammRechte.Vorlagen</c>), ein Test hält beide gleich.
    /// </summary>
    public static readonly OrgRolleArt[] AufgabenVorlagen = [OrgRolleArt.OrgAdmin];

    /// <summary>
    /// Story 3-6: die Programmbibliothek sehen und Einträge entfernen (FR-41). Die Endpunkte besitzt Programm; dort steht dieselbe Liste
    /// (<c>ProgrammRechte.Bibliothek</c>), ein Test hält beide gleich.
    /// </summary>
    public static readonly OrgRolleArt[] Programmbibliothek = [OrgRolleArt.OrgAdmin];

    public static IReadOnlyList<string> Erlaubt(IReadOnlyCollection<OrgRolleArt> rollen)
    {
        var erlaubt = new List<string>();
        if (rollen.Any(RollenVergeben.Contains))
        {
            erlaubt.Add(Aktionsschluessel.OrgRollenVergeben);
        }

        if (rollen.Any(NachweisePruefen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.NachweisePruefen);
        }

        if (rollen.Any(RichtlinieVerwalten.Contains))
        {
            erlaubt.Add(Aktionsschluessel.NachweisRichtlinieVerwalten);
        }

        if (rollen.Any(LoeschfristenVerwalten.Contains))
        {
            erlaubt.Add(Aktionsschluessel.LoeschfristenVerwalten);
        }

        if (rollen.Any(Dokumente.Contains))
        {
            erlaubt.Add(Aktionsschluessel.OrgDokumenteVerwalten);
        }

        if (rollen.Any(VorfallArchiv.Contains))
        {
            erlaubt.Add(Aktionsschluessel.VorfallArchivSehen);
        }

        if (rollen.Any(InventarSehen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.InventarSehen);
        }

        if (rollen.Any(InventarVerwalten.Contains))
        {
            erlaubt.Add(Aktionsschluessel.InventarVerwalten);
        }

        if (rollen.Any(AufgabenVorlagen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.AufgabenVorlagenVerwalten);
        }

        if (rollen.Any(Programmbibliothek.Contains))
        {
            erlaubt.Add(Aktionsschluessel.ProgrammbibliothekVerwalten);
        }

        return erlaubt;
    }

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
