using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.Inventar.Reservierungen;
using ApiArtikel = Zeltlotse.Api.Contracts.Inventar.InventarArtikel;
using ApiReservierung = Zeltlotse.Api.Contracts.Inventar.Reservierung;

namespace Zeltlotse.Inventar.Artikel;

/// <summary>
/// <c>GET /api/organisationen/{orgId}/inventar</c> (Org-Admin und Org-Überblick, FR-37): alle Artikel der Organisation nach Bezeichnung, je
/// Artikel alle Reservierungen aller Freizeiten nach Von, mit Konflikthinweisen. Den Mandanten-Kontext hat der Host aus der Route gesetzt.
/// </summary>
internal static class OrgInventarLadenEndpunkt
{
    public static async Task<Ok<OrgInventar>> Laden(InventarLeseDbContext db, IFreizeitNamenLesen namen, CancellationToken abbruch)
    {
        var artikel = await db.Artikel.ToListAsync(abbruch);
        var konflikte = await Konflikte.LadenAsync(db, namen, null, abbruch);
        return TypedResults.Ok(new OrgInventar(ArtikelLesen.Sortiert(artikel, a => konflikte.Von(a.Id))));
    }
}

/// <summary>Gemeinsame Lesart der Artikel für die beiden Inventar-Antworten und das Anlegen.</summary>
internal static class ArtikelLesen
{
    public static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    public static ApiArtikel AlsApi(InventarArtikel artikel, IReadOnlyList<ApiReservierung> reservierungen) =>
        new(artikel.Id, artikel.Version, artikel.Bezeichnung, artikel.Menge, reservierungen);

    /// <summary>Die Artikel nach Bezeichnung (dann Kennung), je Artikel mit den Reservierungen aus <paramref name="reservierungen"/>.</summary>
    public static IReadOnlyList<ApiArtikel> Sortiert(IEnumerable<InventarArtikel> artikel, Func<InventarArtikel, IReadOnlyList<ApiReservierung>> reservierungen) =>
    [
        .. artikel.OrderBy(a => a.Bezeichnung, Vergleich).ThenBy(a => a.Id).Select(a => AlsApi(a, reservierungen(a))),
    ];
}

/// <summary>Texte der Formprüfung, gleich für Anlegen und Bearbeiten.</summary>
internal static class InventarTexte
{
    /// <summary>Der Fehler zur Bezeichnung oder <c>null</c>: Pflicht, getrimmt höchstens <see cref="InventarFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public static string? BezeichnungFehler(string? bezeichnung)
    {
        var text = bezeichnung?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return "Bezeichnung fehlt.";
        }

        return text.Length > InventarFelder.BezeichnungMaximaleLaenge ? $"Höchstens {InventarFelder.BezeichnungMaximaleLaenge} Zeichen." : null;
    }

    /// <summary>Der Fehler zur Menge oder <c>null</c>: freiwillig, getrimmt höchstens <see cref="InventarFelder.MengeMaximaleLaenge"/> Zeichen.</summary>
    public static string? MengeFehler(string? menge) =>
        (menge?.Trim().Length ?? 0) > InventarFelder.MengeMaximaleLaenge ? $"Höchstens {InventarFelder.MengeMaximaleLaenge} Zeichen." : null;
}
