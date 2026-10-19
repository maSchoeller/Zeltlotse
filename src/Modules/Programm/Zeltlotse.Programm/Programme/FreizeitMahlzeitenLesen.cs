using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Contracts.Mahlzeiten;
using Zeltlotse.Programm.Daten;
using MahlzeitArt = Zeltlotse.Api.Contracts.Programm.Mahlzeit;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 4-2: <see cref="IFreizeitMahlzeitenLesen"/> über den Lese-Kontext unter dem Mandantenfilter. Fehlen Mahlzeiten (weniger als drei je Tag),
/// legt der interne Command <see cref="MahlzeitenSicherstellen"/> des eigenen Moduls sie vorher an, wie beim Laden des Wochenrasters; das ist kein
/// Schreiben über Modulgrenzen (AD-4), sondern eine Ableitung im eigenen Modul.
/// </summary>
internal sealed class FreizeitMahlzeitenLesen(ProgrammLeseDbContext db, IFreizeitTageLesen tageLesen, ISender sender) : IFreizeitMahlzeitenLesen
{
    public async Task<IReadOnlyList<FreizeitMahlzeit>> MahlzeitenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        if (!(await tageLesen.TageAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var tage))
        {
            return [];
        }

        var tageAnzahl = tage.LetzterTag.DayNumber - tage.ErsterTag.DayNumber + 1;
        if (await db.Programmpunkte.CountAsync(p => p.FreizeitId == freizeitId && p.Mahlzeit != null, abbruch) < Programmpunkt.Mahlzeiten.Count * tageAnzahl)
        {
            await sender.Send(new MahlzeitenSicherstellen(freizeitId), abbruch);
        }

        var mahlzeiten = await db.Programmpunkte.Where(p => p.FreizeitId == freizeitId && p.Mahlzeit != null).ToListAsync(abbruch);
        return
        [
            .. mahlzeiten
                .OrderBy(p => p.Tag)
                .ThenBy(p => p.Beginn is null)
                .ThenBy(p => p.Beginn)
                .ThenBy(p => p.Mahlzeit)
                .ThenBy(p => p.Id)
                .Select(p => new FreizeitMahlzeit(p.Id, p.Tag, p.Beginn, p.Titel, Art(p.Mahlzeit!.Value))),
        ];
    }

    private static FreizeitMahlzeitArt Art(MahlzeitArt mahlzeit) => mahlzeit switch
    {
        MahlzeitArt.Fruehstueck => FreizeitMahlzeitArt.Fruehstueck,
        MahlzeitArt.Mittagessen => FreizeitMahlzeitArt.Mittagessen,
        MahlzeitArt.Abendessen => FreizeitMahlzeitArt.Abendessen,
        _ => throw new ArgumentOutOfRangeException(nameof(mahlzeit), mahlzeit, null),
    };
}
