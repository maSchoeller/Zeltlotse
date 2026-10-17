using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Kueche.Daten;
using ApiRezept = Zeltlotse.Api.Contracts.Kueche.Rezept;
using ApiZutat = Zeltlotse.Api.Contracts.Kueche.Zutat;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kueche/rezepte</c> (Leitung und Team, Modul Verpflegung, FR-46): die Rezepte der Freizeit nach Name mit
/// Basis, Anzahl der Zutaten und der Vereinigung ihrer Allergene sowie die Personenzahl der Freizeit; ohne Rezepte eine leere Liste. Den
/// Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class RezepteLadenEndpunkt
{
    public static async Task<Ok<Rezeptliste>> Laden(Guid freizeitId, KuecheLeseDbContext db, IFreizeitPersonenzahlLesen personenzahl, CancellationToken abbruch)
    {
        var rezepte = await db.Rezepte.Where(r => r.FreizeitId == freizeitId).ToListAsync(abbruch);
        var zutaten = (await db.Zutaten
                .Where(z => z.FreizeitId == freizeitId)
                .Select(z => new { z.RezeptId, z.Allergene })
                .ToListAsync(abbruch))
            .ToLookup(z => z.RezeptId);
        var anzahl = await personenzahl.AnzahlAsync(freizeitId, abbruch);
        return TypedResults.Ok(new Rezeptliste(
            anzahl,
            [
                .. rezepte
                    .OrderBy(r => r.Name, KuecheLesen.Vergleich)
                    .ThenBy(r => r.Id)
                    .Select(r => new RezeptKurz(
                        r.Id,
                        r.Name,
                        r.BasisPersonen,
                        zutaten[r.Id].Count(),
                        KuecheWerte.AllergeneNormalisieren(string.Join(',', zutaten[r.Id].Select(z => z.Allergene))))),
            ]));
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kueche/rezepte/{rezeptId}</c> (Leitung und Team, Modul Verpflegung, FR-46): das Rezept mit seinen Zutaten,
/// jede mit der auf die Personenzahl der Freizeit hochgerechneten Menge. Unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class RezeptLadenEndpunkt
{
    public static async Task<Results<Ok<ApiRezept>, NotFound>> Laden(
        Guid freizeitId, Guid rezeptId, KuecheLeseDbContext db, IFreizeitPersonenzahlLesen personenzahl, CancellationToken abbruch)
    {
        var rezept = await db.Rezepte.SingleOrDefaultAsync(r => r.Id == rezeptId && r.FreizeitId == freizeitId, abbruch);
        if (rezept is null)
        {
            return TypedResults.NotFound();
        }

        var zutaten = await db.Zutaten.Where(z => z.RezeptId == rezept.Id).OrderBy(z => z.Id).ToListAsync(abbruch);
        return TypedResults.Ok(KuecheLesen.AlsApi(rezept, zutaten, await personenzahl.AnzahlAsync(freizeitId, abbruch)));
    }
}

/// <summary>Gemeinsame Lesart der Küche für die Lese-Endpunkte und die Antworten der schreibenden Endpunkte.</summary>
internal static class KuecheLesen
{
    public static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    public static ApiRezept AlsApi(Rezept rezept, IEnumerable<Zutat> zutaten, int personenzahl) => new(
        rezept.Id,
        rezept.Version,
        rezept.Name,
        rezept.BasisPersonen,
        rezept.Zubereitung,
        personenzahl,
        [.. zutaten.OrderBy(z => z.Id).Select(z => AlsApi(z, rezept.BasisPersonen, personenzahl))]);

    public static ApiZutat AlsApi(Zutat zutat, int basisPersonen, int personenzahl) => new(
        zutat.Id,
        zutat.Version,
        zutat.Bezeichnung,
        zutat.Menge is { } menge ? KuecheWerte.OhneNullen(menge) : null,
        zutat.Einheit,
        zutat.Allergene,
        KuecheWerte.Hochrechnen(zutat.Menge, basisPersonen, personenzahl));
}
