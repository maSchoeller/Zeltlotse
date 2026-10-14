using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Inventar.Daten;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/inventar/schaeden</c> (Leitung und Team, FR-32, FR-38): die Schadens- und Verlustliste der Freizeit, neueste
/// zuerst (bei gleichem Zeitpunkt nach Kennung absteigend). In jeder Phase lesbar.
/// </summary>
internal static class SchaedenLadenEndpunkt
{
    public static async Task<Ok<List<SchadenEintrag>>> Laden(Guid freizeitId, InventarLeseDbContext db, CancellationToken abbruch)
    {
        var eintraege = await db.Schaeden.Where(s => s.FreizeitId == freizeitId).ToListAsync(abbruch);
        return TypedResults.Ok(SchadenLesen.Sortiert(eintraege));
    }
}

/// <summary>Gemeinsame Lesart der Einträge für die Liste der Freizeit, das Eintragen und die Anschaffungswünsche.</summary>
internal static class SchadenLesen
{
    public static SchadenEintrag AlsApi(SchadenVerlust eintrag) =>
        new(eintrag.Id, eintrag.Version, eintrag.Art, eintrag.Bezeichnung, eintrag.ErfasstAm, eintrag.VorfallEintragId);

    /// <summary>Neueste zuerst, bei gleichem Zeitpunkt nach Kennung absteigend.</summary>
    public static List<SchadenEintrag> Sortiert(IEnumerable<SchadenVerlust> eintraege) =>
        [.. eintraege.OrderByDescending(s => s.ErfasstAm).ThenByDescending(s => s.Id).Select(AlsApi)];
}

/// <summary>Texte der Formprüfung, gleich für Eintragen und Ändern.</summary>
internal static class SchadenTexte
{
    /// <summary>Der Fehler zur Bezeichnung oder <c>null</c>: Pflicht, getrimmt höchstens <see cref="SchadenFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public static string? BezeichnungFehler(string? bezeichnung)
    {
        var text = bezeichnung?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return "Bezeichnung fehlt.";
        }

        return text.Length > SchadenFelder.BezeichnungMaximaleLaenge ? $"Höchstens {SchadenFelder.BezeichnungMaximaleLaenge} Zeichen." : null;
    }
}
