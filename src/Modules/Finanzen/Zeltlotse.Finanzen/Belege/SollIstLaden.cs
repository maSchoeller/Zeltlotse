using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/finanzen/soll-ist</c> (Leitung und Team, Modul Finanzen, Story 5-2, FR-59, NS-8): bei jedem Aufruf neu gerechnet, ohne
/// Abrechnungsabschluss. Soll je Kategorie sind die Kosten laut Kalkulation (dieselbe Rechnung wie <c>GET …/kalkulation</c>, <see cref="KalkulationAbfrage"/>;
/// nicht gespeicherte Werte zählen als 0), Ist ist die Summe der Belege. Status und Erstattungen rechnet <see cref="SollIstRechnung"/>. Auch nach dem
/// Abschluss lesbar.
/// </summary>
internal static class SollIstLadenEndpunkt
{
    public static async Task<Results<Ok<SollIst>, NotFound>> Laden(Guid freizeitId, IFreizeitTageLesen tageLesen, FinanzenLeseDbContext db, CancellationToken abbruch)
    {
        if (await KalkulationAbfrage.LadenAsync(freizeitId, tageLesen, db, abbruch) is not { } kalkulation)
        {
            return TypedResults.NotFound();
        }

        var belege = await db.Belege.Where(b => b.FreizeitId == freizeitId).Select(b => new { b.Kategorie, b.Betrag, b.PersonId, b.Erstattet }).ToListAsync(abbruch);
        return TypedResults.Ok(SollIstRechnung.Rechnen(
            kalkulation.Kategorien.ToDictionary(k => k.Kategorie, k => k.Kosten),
            belege.Select(b => new BelegBetrag(b.Kategorie, FinanzenWerte.OhneNullen(b.Betrag), b.PersonId, b.Erstattet))));
    }
}
