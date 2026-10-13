using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using ApiFreizeitRolle = Zeltlotse.Api.Contracts.Gemeinsam.FreizeitRolle;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Rechte;

/// <summary>
/// Die Rollenlisten der Endpunkte (AD-12) und daraus die erlaubten Aktionen (AD-29): Die Merkmale an den Endpunkten und
/// <see cref="ErlaubtInOrganisation"/> nutzen dieselben Listen, damit Oberfläche und Server nie auseinanderlaufen.
/// </summary>
internal static class FreizeitRechte
{
    /// <summary>Die Freizeiten der Organisation als Liste sehen (nur Name, Zeitraum, Phase).</summary>
    public static readonly OrgRolleArt[] ListeSehen = [OrgRolleArt.OrgAdmin, OrgRolleArt.OrgUeberblick];

    /// <summary>Freizeiten anlegen.</summary>
    public static readonly OrgRolleArt[] Anlegen = [OrgRolleArt.OrgAdmin];

    /// <summary>Eine Freizeit verwalten und die Leitung einladen.</summary>
    public static readonly OrgRolleArt[] LeitungEinsetzen = [OrgRolleArt.OrgAdmin];

    /// <summary>Freizeit-Endpunkte für alle, die in der Freizeit mitarbeiten; Org-Rollen zählen nicht (AD-12).</summary>
    public static readonly RolleArt[] Mitarbeit = [RolleArt.Leitung, RolleArt.Team];

    /// <summary>
    /// Nachweise im Freizeit-Kontext prüfen (FR-10) und Warnhinweise sehen (FR-11). Die Prüf-Endpunkte und „Erinnern“ besitzt
    /// Personen; dort steht dieselbe Liste (<c>NachweisRechte.FreizeitPruefen</c>), ein HTTP-Test hält beide gleich.
    /// </summary>
    public static readonly RolleArt[] NachweisePruefen = [RolleArt.Leitung];

    /// <summary>
    /// Einstellungen der Freizeit (EXPERIENCE.md → Fläche „Einstellungen der Freizeit“, nur Leitung): Freizeitseite bearbeiten,
    /// Anmeldelinks sehen und erneuern (FR-15, FR-16).
    /// </summary>
    public static readonly RolleArt[] Einstellungen = [RolleArt.Leitung];

    /// <summary>
    /// Anmeldungen sehen, bestätigen und Absagen eintragen (EXPERIENCE.md → Fläche „Anmeldungen“, Nur-Leitung-Funktion; FR-20, FR-21).
    /// </summary>
    public static readonly RolleArt[] Anmeldungen = [RolleArt.Leitung];

    /// <summary>
    /// Story 2-5: Freizeitpässe sehen (Personenliste, Team-Notizen; FR-24, FR-26). Den Pass selbst liefert Personen mit derselben Liste
    /// (<c>FreizeitpassRechte.Einsicht</c>); ein Test hält beide gleich. Was das Team vor der Bestätigung sieht, entscheidet die Freigabe.
    /// </summary>
    public static readonly RolleArt[] Freizeitpaesse = [RolleArt.Leitung, RolleArt.Team];

    /// <summary>Erlaubte Aktionen im Freizeit-Kontext (AD-29) aus den eigenen Freizeit-Rollen.</summary>
    public static IReadOnlyList<string> Erlaubt(IReadOnlyCollection<RolleArt> rollen)
    {
        var erlaubt = new List<string>();
        if (rollen.Any(NachweisePruefen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.NachweisePruefen);
        }

        if (rollen.Any(Einstellungen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.FreizeitEinstellungen);
        }

        if (rollen.Any(Anmeldungen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.AnmeldungenBearbeiten);
        }

        if (rollen.Any(Freizeitpaesse.Contains))
        {
            erlaubt.Add(Aktionsschluessel.FreizeitpaesseSehen);
        }

        return erlaubt;
    }

    public static IReadOnlyList<string> ErlaubtInOrganisation(IReadOnlyCollection<OrgRolleArt> rollen)
    {
        var erlaubt = new List<string>();
        if (rollen.Any(Anlegen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.FreizeitenAnlegen);
        }

        if (rollen.Any(LeitungEinsetzen.Contains))
        {
            erlaubt.Add(Aktionsschluessel.LeitungEinsetzen);
        }

        return erlaubt;
    }

    public static Phase Api(FreizeitPhase phase) => phase switch
    {
        FreizeitPhase.InVorbereitung => Phase.InVorbereitung,
        FreizeitPhase.Laeuft => Phase.Laeuft,
        FreizeitPhase.Nachbereitung => Phase.Nachbereitung,
        FreizeitPhase.Gelaufen => Phase.Gelaufen,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    public static ApiFreizeitRolle Api(RolleArt rolle) => rolle switch
    {
        RolleArt.Leitung => ApiFreizeitRolle.Leitung,
        RolleArt.Team => ApiFreizeitRolle.Team,
        _ => throw new ArgumentOutOfRangeException(nameof(rolle), rolle, null),
    };
}

/// <summary>
/// <see cref="IFreizeitRollenLesen"/> über den Lese-Kontext: die Organisation ohne Mandantenfilter (die adressierte Freizeit
/// ist die Kontext-Quelle, AD-6), die Rollen unter dem Filter.
/// </summary>
internal sealed class FreizeitRollenLesen(FreizeitenLeseDbContext db) : IFreizeitRollenLesen
{
    public async Task<Guid?> OrganisationAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        await db.Freizeiten.IgnoreQueryFilters()
            .Where(f => f.Id == freizeitId)
            .Select(f => (Guid?)f.OrganisationId)
            .SingleOrDefaultAsync(abbruch);

    public async Task<IReadOnlyCollection<RolleArt>> RollenAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default) =>
        await db.Rollen
            .Where(r => r.FreizeitId == freizeitId && r.PersonId == personId)
            .Select(r => r.Rolle)
            .ToListAsync(abbruch);
}

/// <summary><see cref="IFreizeitZeitpunkte"/> über den Lese-Kontext unter dem Mandantenfilter, eine Abfrage für die ganze Menge.</summary>
internal sealed class FreizeitZeitpunkteLesen(FreizeitenLeseDbContext db, TimeProvider uhr) : IFreizeitZeitpunkte
{
    public async Task<IReadOnlyDictionary<Guid, FreizeitZeitpunkte>> ZeitpunkteAsync(IReadOnlyCollection<Guid> freizeitIds, CancellationToken abbruch = default)
    {
        if (freizeitIds.Count == 0)
        {
            return new Dictionary<Guid, FreizeitZeitpunkte>();
        }

        var ids = freizeitIds.Distinct().ToList();
        var jetzt = uhr.GetUtcNow();
        var freizeiten = await db.Freizeiten.Where(f => ids.Contains(f.Id)).ToListAsync(abbruch);
        return freizeiten.ToDictionary(f => f.Id, f => f.Zeitpunkte(jetzt));
    }
}
