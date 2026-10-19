using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Fragen;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Programm.Contracts.Mahlzeiten;

namespace Zeltlotse.Kueche.Einkauf;

/// <summary>
/// Story 4-2: Ergebnis des Allergen-Abgleichs: ob die Freizeit Allergien abfragt, und die anonymen Profile (sonst leer). Story 4-4: ebenso für die
/// Sonderkost (<see cref="EinkaufAbfrage.SonderkostAsync"/>).
/// </summary>
internal sealed record Abgleich(bool AllergienAbgefragt, IReadOnlyList<IReadOnlyList<string>> Profile);

/// <summary>
/// Story 4-2: Gemeinsame Abfrage von Essensplan, Einkaufsliste und Tausch-Vorschau (AD-27: Abfrage-Klasse statt Mediator) über den Lese-Kontext unter
/// dem Mandantenfilter, dazu die Leseschnittstellen von Programm (Mahlzeiten), Freizeiten (Personen, Fragen) und Personen (Allergen-Profile).
/// </summary>
internal sealed class EinkaufAbfrage(
    KuecheLeseDbContext db,
    IFreizeitMahlzeitenLesen mahlzeitenLesen,
    IFreizeitPersonenzahlLesen personenLesen,
    IFreizeitFragenLesen fragenLesen,
    IFreizeitpassAllergene allergene,
    IFreizeitpassSonderkost sonderkost)
{
    /// <summary>Die Mahlzeiten der Freizeit (fehlende legt Programm vorher an).</summary>
    public Task<IReadOnlyList<FreizeitMahlzeit>> MahlzeitenAsync(Guid freizeitId, CancellationToken abbruch) => mahlzeitenLesen.MahlzeitenAsync(freizeitId, abbruch);

    public Task<int> PersonenzahlAsync(Guid freizeitId, CancellationToken abbruch) => personenLesen.AnzahlAsync(freizeitId, abbruch);

    /// <summary>
    /// Die Gerichte der Freizeit (oder nur eines) mit Mahlzeit, Rezept, Zutaten nach Kennung, Hand-Mengen und (Story 4-3) dem Stand der Posten; ein Gericht
    /// ohne bekannte Mahlzeit fehlt.
    /// </summary>
    public async Task<List<GerichtDaten>> GerichteAsync(Guid freizeitId, IReadOnlyList<FreizeitMahlzeit> mahlzeiten, CancellationToken abbruch, Guid? nurGericht = null)
    {
        var gerichte = await db.Gerichte
            .Where(g => g.FreizeitId == freizeitId && (nurGericht == null || g.Id == nurGericht))
            .OrderBy(g => g.Id)
            .ToListAsync(abbruch);
        if (gerichte.Count == 0)
        {
            return [];
        }

        var rezeptIds = gerichte.Select(g => g.RezeptId).Distinct().ToList();
        var gerichtIds = gerichte.Select(g => g.Id).ToList();
        var rezepte = await db.Rezepte.Where(r => rezeptIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, abbruch);
        var zutaten = (await db.Zutaten.Where(z => rezeptIds.Contains(z.RezeptId)).OrderBy(z => z.Id).ToListAsync(abbruch)).ToLookup(z => z.RezeptId);
        var hand = (await db.MengenVonHand.Where(h => gerichtIds.Contains(h.GerichtId)).ToListAsync(abbruch)).ToLookup(h => h.GerichtId);
        // Story 4-3: der Stand der Posten (Freigabe, Gekauft).
        var staende = (await db.PostenStaende.Where(s => gerichtIds.Contains(s.GerichtId)).ToListAsync(abbruch)).ToLookup(s => s.GerichtId);
        var nachId = mahlzeiten.ToDictionary(m => m.ProgrammpunktId);
        return
        [
            .. gerichte
                .Where(g => nachId.ContainsKey(g.ProgrammpunktId) && rezepte.ContainsKey(g.RezeptId))
                .Select(g => new GerichtDaten(g, nachId[g.ProgrammpunktId], rezepte[g.RezeptId], [.. zutaten[g.RezeptId]], [.. hand[g.Id]], [.. staende[g.Id]])),
        ];
    }

    /// <summary>
    /// FR-48: Fragt die Freizeit die Allergie-Frage ab, liefert Personen die anonymen Profile derselben Personen, die die Personenzahl bilden (Personen
    /// protokolliert den Zweck ohne lesende Person). Sonst kein Abgleich und keine Abfrage bei Personen.
    /// </summary>
    public async Task<Abgleich> AbgleichAsync(Guid freizeitId, CancellationToken abbruch, bool profileLesen = true)
    {
        if (!await fragenLesen.FragtAbAsync(freizeitId, Fragenkatalog.Allergien, abbruch))
        {
            return new Abgleich(false, []);
        }

        if (!profileLesen)
        {
            return new Abgleich(true, []);
        }

        var personen = await personenLesen.PersonenAsync(freizeitId, abbruch);
        return new Abgleich(true, personen.Count == 0 ? [] : await allergene.ProfileAsync(freizeitId, personen, abbruch));
    }

    /// <summary>
    /// Story 4-4, FR-52: Fragt die Freizeit die Frage „Ernährung“ ab, liefert Personen die anonymen Sonderkost-Profile derselben Personen, die die
    /// Personenzahl bilden (Personen protokolliert den Zweck „Sonderkost-Abgleich der Küche“ ohne lesende Person). Sonst kein Abgleich und keine Abfrage
    /// bei Personen.
    /// </summary>
    public async Task<Abgleich> SonderkostAsync(Guid freizeitId, CancellationToken abbruch)
    {
        if (!await fragenLesen.FragtAbAsync(freizeitId, Fragenkatalog.Ernaehrung, abbruch))
        {
            return new Abgleich(false, []);
        }

        var personen = await personenLesen.PersonenAsync(freizeitId, abbruch);
        return new Abgleich(true, personen.Count == 0 ? [] : await sonderkost.ProfileAsync(freizeitId, personen, abbruch));
    }
}
