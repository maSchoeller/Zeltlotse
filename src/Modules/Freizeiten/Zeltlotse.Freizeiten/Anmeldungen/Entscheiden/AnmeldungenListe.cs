using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/anmeldungen</c> (Leitung, FR-20, FR-21, FR-67): Höchstzahl, bestätigte Teilnehmende und alle
/// Anmeldungen der Freizeit nach Eingang, mit Vor- und Nachname aus Personen (AD-27). Eine gelöschte Person erscheint als
/// „Gelöschte Person“. Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class AnmeldungenListeEndpunkt
{
    public const string GeloeschtePersonText = "Gelöschte Person";

    public static async Task<Results<Ok<AnmeldungenUebersicht>, NotFound>> Laden(
        Guid freizeitId, FreizeitenLeseDbContext db, IPersonenNamen namen, CancellationToken abbruch)
    {
        var freizeit = await db.Freizeiten.Where(f => f.Id == freizeitId).Select(f => new { f.HoechstzahlTeilnehmende }).SingleOrDefaultAsync(abbruch);
        if (freizeit is null)
        {
            return TypedResults.NotFound();
        }

        var anmeldungen = await db.Anmeldungen
            .Where(a => a.FreizeitId == freizeitId)
            .OrderBy(a => a.EingegangenAm)
            .ThenBy(a => a.Id)
            .ToListAsync(abbruch);
        var personen = await namen.NamenAsync([.. anmeldungen.Select(a => a.PersonId).Where(p => p != GeloeschtePerson.Id).Distinct()], abbruch);
        return TypedResults.Ok(new AnmeldungenUebersicht(
            freizeit.HoechstzahlTeilnehmende,
            anmeldungen.Count(BestaetigteTeilnehmendeZaehlen.Zaehlt),
            [
                .. anmeldungen.Select(a =>
                {
                    var name = personen.GetValueOrDefault(a.PersonId);
                    return new LeitungAnmeldungZeile(
                        a.Id,
                        a.PersonId,
                        a.PersonId == GeloeschtePerson.Id ? GeloeschtePersonText : name?.Vorname,
                        a.PersonId == GeloeschtePerson.Id ? null : name?.Nachname,
                        AnmeldungWerte.Art(a.Art),
                        AnmeldungWerte.Status(a.Status),
                        a.EingegangenAm,
                        a.EntschiedenAm);
                }),
            ]));
    }
}

/// <summary>
/// <see cref="IBestaetigteTeilnehmende"/> über den Lese-Kontext unter dem Mandantenfilter (FR-67): bestätigte Anmeldungen der Art
/// Teilnehmende; Mitarbeitende zählen nie.
/// </summary>
internal sealed class BestaetigteTeilnehmendeZaehlen(FreizeitenLeseDbContext db) : IBestaetigteTeilnehmende
{
    public static bool Zaehlt(Domaene.Anmeldung anmeldung) => anmeldung is { Art: AnmeldeArt.Teilnehmende, Status: AnmeldeStatus.Bestaetigt };

    public Task<int> AnzahlAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        db.Anmeldungen.CountAsync(
            a => a.FreizeitId == freizeitId && a.Art == AnmeldeArt.Teilnehmende && a.Status == AnmeldeStatus.Bestaetigt, abbruch);
}
