using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Programm.Daten;
using ApiEintrag = Zeltlotse.Api.Contracts.Programm.BibliotheksEintrag;

namespace Zeltlotse.Programm.Bibliothek;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/programm/bibliothek</c> (Leitung und Team, FR-41): die Programmbibliothek der Organisation, der die Freizeit
/// gehört, auch nach dem Abschluss. Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt; Einträge anderer Organisationen sieht niemand.
/// </summary>
internal static class BibliothekLadenEndpunkt
{
    public static async Task<Ok<IReadOnlyList<ApiEintrag>>> Laden(ProgrammLeseDbContext db, CancellationToken abbruch) =>
        TypedResults.Ok(await BibliothekLesen.AlleAsync(db, abbruch));
}

/// <summary>
/// <c>GET /api/organisationen/{orgId}/programmbibliothek</c> (Org-Admin, FR-41): die Programmbibliothek der Organisation. Den Mandanten-Kontext
/// hat der Host aus der Route gesetzt.
/// </summary>
internal static class OrgBibliothekLadenEndpunkt
{
    public static async Task<Ok<IReadOnlyList<ApiEintrag>>> Laden(ProgrammLeseDbContext db, CancellationToken abbruch) =>
        TypedResults.Ok(await BibliothekLesen.AlleAsync(db, abbruch));
}

/// <summary>Gemeinsame Lesart der Programmbibliothek für die Freizeit und die Organisation.</summary>
internal static class BibliothekLesen
{
    private static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    /// <summary>Alle Einträge des Mandanten nach Titel, dann die neueste Fassung zuerst, dann Kennung.</summary>
    public static async Task<IReadOnlyList<ApiEintrag>> AlleAsync(ProgrammLeseDbContext db, CancellationToken abbruch)
    {
        var eintraege = await db.Bibliothek.ToListAsync(abbruch);
        return
        [
            .. eintraege
                .OrderBy(e => e.Titel, Vergleich)
                .ThenByDescending(e => e.AbgelegtAm)
                .ThenBy(e => e.Id)
                .Select(AlsApi),
        ];
    }

    public static ApiEintrag AlsApi(BibliotheksEintrag eintrag) => new(
        eintrag.Id,
        eintrag.Titel,
        eintrag.AbgelegtAm,
        [.. eintrag.Material.Select(m => new BibliotheksMaterial(m.Bezeichnung, m.Menge))],
        [.. eintrag.Aufgaben.Select(a => new BibliotheksAufgabe(a.Titel, a.Phase))]);
}
