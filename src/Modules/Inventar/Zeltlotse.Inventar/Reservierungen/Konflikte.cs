using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Inventar.Daten;
using ApiReservierung = Zeltlotse.Api.Contracts.Inventar.Reservierung;

namespace Zeltlotse.Inventar.Reservierungen;

/// <summary>
/// Story 3-3: Abfrage-Klasse (AD-27) für die Reservierungen mit Konflikthinweis, gemeinsam für beide Inventar-Antworten und die Antwort auf das
/// Reservieren. Lädt die Reservierungen der Organisation (unter dem Mandantenfilter) und die Namen ihrer Freizeiten; je Reservierung nennt sie die
/// Namen der anderen Freizeiten mit überschneidendem Zeitraum für denselben Artikel (<see cref="Reservierung.KonfliktMit"/>), sortiert und ohne
/// Doppelte. Zeltlotse verhindert und löst nichts.
/// </summary>
internal sealed class Konflikte
{
    private static readonly StringComparer NamenVergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: false);

    private readonly ILookup<Guid, Reservierung> _jeArtikel;
    private readonly IReadOnlyDictionary<Guid, string> _namen;

    private Konflikte(IEnumerable<Reservierung> reservierungen, IReadOnlyDictionary<Guid, string> namen)
    {
        _jeArtikel = reservierungen.ToLookup(r => r.ArtikelId);
        _namen = namen;
    }

    /// <param name="artikelIds">Nur die Reservierungen dieser Artikel; <c>null</c> heißt alle der Organisation.</param>
    public static async Task<Konflikte> LadenAsync(
        InventarLeseDbContext db, IFreizeitNamenLesen freizeitNamen, IReadOnlyCollection<Guid>? artikelIds, CancellationToken abbruch)
    {
        var abfrage = db.Reservierungen.AsQueryable();
        if (artikelIds is not null)
        {
            abfrage = abfrage.Where(r => artikelIds.Contains(r.ArtikelId));
        }

        var reservierungen = await abfrage.ToListAsync(abbruch);
        var namen = await freizeitNamen.NamenAsync([.. reservierungen.Select(r => r.FreizeitId).Distinct()], abbruch);
        return new Konflikte(reservierungen, namen);
    }

    /// <summary>Die Reservierungen eines Artikels nach Von (dann Bis, Kennung), auf Wunsch nur die einer Freizeit, mit Konflikthinweisen.</summary>
    public IReadOnlyList<ApiReservierung> Von(Guid artikelId, Guid? nurFreizeit = null) =>
    [
        .. _jeArtikel[artikelId]
            .Where(r => nurFreizeit is null || r.FreizeitId == nurFreizeit)
            .OrderBy(r => r.Von)
            .ThenBy(r => r.Bis)
            .ThenBy(r => r.Id)
            .Select(AlsApi),
    ];

    /// <summary>Eine Reservierung in der Form der API, mit den Namen der Freizeiten, die zur gleichen Zeit denselben Artikel reserviert haben.</summary>
    public ApiReservierung AlsApi(Reservierung reservierung) => new(
        reservierung.Id,
        reservierung.FreizeitId,
        Name(reservierung.FreizeitId),
        reservierung.Von,
        reservierung.Bis,
        reservierung.MaterialId,
        [
            .. _jeArtikel[reservierung.ArtikelId]
                .Where(reservierung.KonfliktMit)
                .Select(r => Name(r.FreizeitId))
                .Distinct()
                .Order(NamenVergleich),
        ]);

    private string Name(Guid freizeitId) => _namen.GetValueOrDefault(freizeitId) ?? "";
}
