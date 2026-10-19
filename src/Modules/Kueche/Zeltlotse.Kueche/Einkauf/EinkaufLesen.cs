using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.SharedKernel.Commands;
using ApiGericht = Zeltlotse.Api.Contracts.Kueche.Gericht;
using ApiEinkaufsliste = Zeltlotse.Api.Contracts.Kueche.Einkaufsliste;
using ApiEssensplan = Zeltlotse.Api.Contracts.Kueche.Essensplan;

namespace Zeltlotse.Kueche.Einkauf;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kueche/essensplan</c> (Leitung und Team, Modul Verpflegung, Story 4-2, FR-47): je Tag die festen Mahlzeiten
/// (fehlende legt Programm an) mit ihren Gerichten und deren Ersatzportionen, die Rezepte zur Auswahl und die Personenzahl. Den Abgleich mit den
/// Freizeitpässen gibt es nur, wenn die Freizeit Allergien abfragt und Gerichte vorgemerkt sind; dann protokolliert Personen den Zweck.
/// </summary>
internal static class EssensplanLadenEndpunkt
{
    public static async Task<Ok<ApiEssensplan>> Laden(Guid freizeitId, EinkaufAbfrage abfrage, KuecheLeseDbContext db, CancellationToken abbruch)
    {
        var mahlzeiten = await abfrage.MahlzeitenAsync(freizeitId, abbruch);
        var gerichte = await abfrage.GerichteAsync(freizeitId, mahlzeiten, abbruch);
        var abgleich = await abfrage.AbgleichAsync(freizeitId, abbruch, profileLesen: gerichte.Count > 0);
        var rezepte = await db.Rezepte.Where(r => r.FreizeitId == freizeitId).Select(r => new { r.Id, r.Name }).ToListAsync(abbruch);
        var nachMahlzeit = gerichte.ToLookup(g => g.Mahlzeit.ProgrammpunktId);
        return TypedResults.Ok(new ApiEssensplan(
            await abfrage.PersonenzahlAsync(freizeitId, abbruch),
            abgleich.AllergienAbgefragt,
            [
                .. mahlzeiten.GroupBy(m => m.Tag).OrderBy(t => t.Key).Select(tag => new EssensplanTag(
                    tag.Key,
                    [
                        .. tag.Select(m => new EssensplanMahlzeit(
                            m.ProgrammpunktId,
                            m.Beginn?.ToString("HH:mm", CultureInfo.InvariantCulture),
                            m.Titel,
                            (MahlzeitArt)(int)m.MahlzeitArt,
                            [
                                .. nachMahlzeit[m.ProgrammpunktId].Select(g => new ApiGericht(
                                    g.Gericht.Id, g.Gericht.Version, g.Rezept.Id, g.Rezept.Name, EinkaufRechnen.Ersatz(abgleich.Profile, g.Zutaten))),
                            ])),
                    ])),
            ],
            [.. rezepte.OrderBy(r => r.Name, KuecheLesen.Vergleich).ThenBy(r => r.Id).Select(r => new RezeptWahl(r.Id, r.Name))]));
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kueche/gerichte/{gerichtId}/tausch?rezeptId=…</c> (Leitung und Team, Modul Verpflegung, Story 4-2, FR-50, AD-13):
/// die Posten des Gerichts vorher und genau die, die „Übernehmen“ speichern würde, nachher, mit geänderten Zeilen markiert. Schreibt nichts, auch nicht
/// nach dem Abschluss verboten. Ohne oder mit fremdem Rezept <c>400</c> am Feld <c>rezeptId</c>; Gericht unbekannt oder aus einer anderen Freizeit
/// <c>404</c>.
/// </summary>
internal static class TauschvorschauEndpunkt
{
    public static async Task<Results<Ok<Tauschvorschau>, NotFound>> Laden(
        Guid freizeitId, Guid gerichtId, [FromQuery] Guid? rezeptId, EinkaufAbfrage abfrage, KuecheLeseDbContext db, CancellationToken abbruch)
    {
        var neues = rezeptId is { } id ? await db.Rezepte.SingleOrDefaultAsync(r => r.Id == id && r.FreizeitId == freizeitId, abbruch) : null;
        if (!await db.Gerichte.AnyAsync(g => g.Id == gerichtId && g.FreizeitId == freizeitId, abbruch))
        {
            return TypedResults.NotFound();
        }

        if (neues is null)
        {
            throw GerichtFehler.RezeptUnbekannt();
        }

        var mahlzeiten = await abfrage.MahlzeitenAsync(freizeitId, abbruch);
        if ((await abfrage.GerichteAsync(freizeitId, mahlzeiten, abbruch, gerichtId)).SingleOrDefault() is not { } gericht)
        {
            return TypedResults.NotFound();
        }

        var personenzahl = await abfrage.PersonenzahlAsync(freizeitId, abbruch);
        var neueZutaten = await db.Zutaten.Where(z => z.RezeptId == neues.Id).OrderBy(z => z.Id).ToListAsync(abbruch);
        var vorher = EinkaufRechnen.Posten(gericht.Rezept, gericht.Zutaten, gericht.HandMengen, personenzahl);
        var nachher = EinkaufRechnen.PostenNachTausch(gericht.Rezept, gericht.Zutaten, gericht.HandMengen, neues, neueZutaten, personenzahl);
        var (links, rechts) = EinkaufRechnen.Vergleich(vorher, nachher);
        return TypedResults.Ok(new Tauschvorschau(
            gericht.Gericht.Id, gericht.Gericht.Version, new RezeptWahl(gericht.Rezept.Id, gericht.Rezept.Name), new RezeptWahl(neues.Id, neues.Name), links, rechts));
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kueche/einkaufsliste</c> (Leitung und Team, Modul Verpflegung, Story 4-2, FR-47 bis FR-49): bei jedem Abruf aus
/// den Gerichten berechnet (<see cref="EinkaufRechnen.Tage"/>), mit Hand-Mengen und Hand-Posten, der Allergen-Übersicht und den Ersatzportionen je
/// Gericht. Fragt die Freizeit Allergien ab, liest Personen die anonymen Profile und protokolliert den Zweck ohne lesende Person.
/// </summary>
internal static class EinkaufslisteLadenEndpunkt
{
    public static async Task<Ok<ApiEinkaufsliste>> Laden(Guid freizeitId, EinkaufAbfrage abfrage, KuecheLeseDbContext db, CancellationToken abbruch)
    {
        var mahlzeiten = await abfrage.MahlzeitenAsync(freizeitId, abbruch);
        var gerichte = await abfrage.GerichteAsync(freizeitId, mahlzeiten, abbruch);
        var handPosten = await db.Einkaufsposten.Where(p => p.FreizeitId == freizeitId).OrderBy(p => p.Id).ToListAsync(abbruch);
        var abgleich = await abfrage.AbgleichAsync(freizeitId, abbruch);
        var personenzahl = await abfrage.PersonenzahlAsync(freizeitId, abbruch);
        return TypedResults.Ok(new ApiEinkaufsliste(
            personenzahl,
            abgleich.AllergienAbgefragt,
            EinkaufRechnen.Uebersicht(abgleich.Profile),
            EinkaufRechnen.Tage(gerichte, handPosten, personenzahl, abgleich.Profile)));
    }
}

/// <summary>Story 4-2: Feldfehler (<c>400</c>) der Gerichte, die erst der Handler erkennen kann (AD-27: Fehlerschlüssel sind die JSON-Namen).</summary>
internal static class GerichtFehler
{
    public static ValidierungsAusnahme RezeptUnbekannt() => Fehler("rezeptId", "Nur ein Rezept dieser Freizeit.");

    public static ValidierungsAusnahme MahlzeitUnbekannt() => Fehler("programmpunktId", "Nur eine Mahlzeit dieser Freizeit.");

    public static ValidierungsAusnahme TagAusserhalb() => Fehler(EinkaufspostenFelder.Tag, KuecheWerte.TagAusserhalbText);

    private static ValidierungsAusnahme Fehler(string feld, string text) => new(new Dictionary<string, string[]> { [feld] = [text] });
}
