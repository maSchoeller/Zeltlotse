using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using ApiMaterial = Zeltlotse.Api.Contracts.Programm.Material;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Materialien;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/programm/material</c> (Leitung und Team, FR-69): das Material aller Programmpunkte der Freizeit, sortiert
/// wie der Wochenraster und dann nach Bezeichnung; ohne Material eine leere Liste. Die Detailseite eines Programmpunkts filtert dieselbe Liste.
/// Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class MaterialListeLadenEndpunkt
{
    public static async Task<Ok<Materialliste>> Laden(Guid freizeitId, ProgrammLeseDbContext db, IAngemeldet angemeldet, CancellationToken abbruch)
    {
        var zeilen = await (
                from material in db.Material
                where material.FreizeitId == freizeitId
                join punkt in db.Programmpunkte on material.ProgrammpunktId equals punkt.Id
                select new { Material = material, Punkt = punkt })
            .ToListAsync(abbruch);
        var reihenfolge = ProgrammLesen.Sortiert(zeilen.Select(z => z.Punkt).DistinctBy(p => p.Id))
            .Select((punkt, index) => (punkt.Id, index))
            .ToDictionary(p => p.Id, p => p.index);
        return TypedResults.Ok(new Materialliste(
        [
            .. zeilen
                .OrderBy(z => reihenfolge[z.Punkt.Id])
                .ThenBy(z => z.Material.Bezeichnung, MaterialLesen.Vergleich)
                .ThenBy(z => z.Material.Id)
                .Select(z => MaterialLesen.AlsApi(z.Material, z.Punkt, angemeldet.PersonId)),
        ]));
    }
}

/// <summary>Gemeinsame Lesart des Materials für die Liste und die Antworten der schreibenden Endpunkte.</summary>
internal static class MaterialLesen
{
    public static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    /// <param name="ich">Die angemeldete Person; „Meins“ heißt: sie bringt das Material mit.</param>
    public static ApiMaterial AlsApi(Material material, Programmpunkt punkt, Guid? ich) => new(
        material.Id,
        material.Version,
        punkt.Id,
        punkt.Titel,
        punkt.Tag,
        ProgrammpunktFelder.UhrzeitText(punkt.Beginn),
        material.Bezeichnung,
        material.Menge,
        material.Status,
        material.BringtMit,
        ich is { } person && material.BringtMit == person);
}

/// <summary>Texte der Formprüfung, gleich für Anlegen und Bearbeiten.</summary>
internal static class MaterialTexte
{
    /// <summary>Der Fehler zur Bezeichnung oder <c>null</c>: Pflicht, getrimmt höchstens <see cref="MaterialFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public static string? BezeichnungFehler(string? bezeichnung)
    {
        var text = bezeichnung?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return "Bezeichnung fehlt.";
        }

        return text.Length > MaterialFelder.BezeichnungMaximaleLaenge ? $"Höchstens {MaterialFelder.BezeichnungMaximaleLaenge} Zeichen." : null;
    }

    /// <summary>Der Fehler zur Menge oder <c>null</c>: freiwillig, getrimmt höchstens <see cref="MaterialFelder.MengeMaximaleLaenge"/> Zeichen.</summary>
    public static string? MengeFehler(string? menge) =>
        (menge?.Trim().Length ?? 0) > MaterialFelder.MengeMaximaleLaenge ? $"Höchstens {MaterialFelder.MengeMaximaleLaenge} Zeichen." : null;
}
