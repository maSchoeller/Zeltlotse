using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Anmeldungen;
using Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Freizeiten.Personenliste;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/personenliste</c> (Leitung und Team, Story 2-5, FR-24): die eingegangenen und bestätigten
/// Anmeldungen der Freizeit mit Vor- und Nachname aus Personen (AD-27), ohne abgesagte, sortiert nach Art (Teilnehmende zuerst), Nachname
/// und Vorname. Eine gelöschte Person heißt „Gelöschte Person“. Keine Antworten aus dem Freizeitpass: Den Pass liest der Client bei
/// Personen (AD-2). Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class PersonenlisteEndpunkt
{
    public static async Task<Results<Ok<List<PersonenlisteZeile>>, NotFound>> Laden(
        Guid freizeitId, FreizeitenLeseDbContext db, IPersonenNamen namen, CancellationToken abbruch)
    {
        if (!await db.Freizeiten.AnyAsync(f => f.Id == freizeitId, abbruch))
        {
            return TypedResults.NotFound();
        }

        var anmeldungen = await db.Anmeldungen
            .Where(a => a.FreizeitId == freizeitId && a.Status != AnmeldeStatus.Abgesagt)
            .Select(a => new { a.Id, a.PersonId, a.Art, a.Status })
            .ToListAsync(abbruch);
        var personen = await namen.NamenAsync([.. anmeldungen.Select(a => a.PersonId).Where(p => p != GeloeschtePerson.Id).Distinct()], abbruch);
        return TypedResults.Ok(anmeldungen
            .Select(a =>
            {
                var name = personen.GetValueOrDefault(a.PersonId);
                var geloescht = a.PersonId == GeloeschtePerson.Id;
                return new PersonenlisteZeile(
                    a.Id,
                    a.PersonId,
                    geloescht ? AnmeldungenListeEndpunkt.GeloeschtePersonText : name?.Vorname,
                    geloescht ? null : name?.Nachname,
                    AnmeldungWerte.Art(a.Art),
                    AnmeldungWerte.Status(a.Status));
            })
            .OrderBy(z => z.Art)
            .ThenBy(z => z.Nachname ?? "", StringComparer.CurrentCulture)
            .ThenBy(z => z.Vorname ?? "", StringComparer.CurrentCulture)
            .ThenBy(z => z.AnmeldungId)
            .ToList());
    }
}
