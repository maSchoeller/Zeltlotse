using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Organisationen.Contracts.Nachweise;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Anmeldung;
using StandArt = Zeltlotse.Organisationen.Contracts.Nachweise.NachweisArt;
using WarnArt = Zeltlotse.Api.Contracts.Freizeiten.NachweisArt;

namespace Zeltlotse.Freizeiten.Nachweise;

/// <summary>
/// Die eine Stelle der Warnregel für Pflicht-Nachweise (FR-11, CAP-5): Je Pflicht-Art ohne Bestätigung → <c>fehlt</c>. Gilt die
/// Bestätigung nicht bis zum letzten Freizeittag (einschließlich <c>gültig bis</c>), dann <c>abgelaufen</c>, wenn sie vor heute
/// endet (Kalendertag Europe/Berlin), sonst <c>läuft ab</c>. Sonst keine Warnung. Warnungen in der Reihenfolge der Pflicht-Arten.
/// </summary>
internal static class NachweisWarnregel
{
    public static IReadOnlyList<NachweisWarnung> Warnungen(
        IReadOnlyList<StandArt> pflichtArten, IEnumerable<NachweisGueltigkeit> bestaetigungenDerPerson, DateOnly letzterTag, DateOnly heute)
    {
        var gueltigBis = bestaetigungenDerPerson
            .GroupBy(b => b.Art)
            .ToDictionary(g => g.Key, g => g.Max(b => b.GueltigBis));
        var warnungen = new List<NachweisWarnung>();
        foreach (var art in pflichtArten.Distinct().Order())
        {
            if (!gueltigBis.TryGetValue(art, out var bis))
            {
                warnungen.Add(new NachweisWarnung((WarnArt)(int)art, NachweisWarnungGrund.Fehlt, null));
            }
            else if (bis < letzterTag)
            {
                warnungen.Add(new NachweisWarnung((WarnArt)(int)art, bis < heute ? NachweisWarnungGrund.Abgelaufen : NachweisWarnungGrund.LaeuftAb, bis));
            }
        }

        return warnungen;
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/nachweis-warnungen</c> (Leitung, FR-11): alle Personen mit Freizeit-Rolle in dieser
/// Freizeit außer der anfragenden, die mindestens eine Warnung haben, nach Nachname und Vorname. Pflicht und „gültig bis“ liest
/// Freizeiten bei Organisationen (Pfeil Freizeiten → Organisationen, Mandant = Organisation der Freizeit), die Namen bei Personen.
/// </summary>
internal static class NachweisWarnungenEndpunkt
{
    public static async Task<Results<Ok<NachweisWarnungen>, NotFound>> Laden(
        Guid freizeitId,
        IAngemeldet angemeldet,
        FreizeitenLeseDbContext db,
        INachweisStandLesen nachweisStand,
        IPersonenNamen namen,
        TimeProvider uhr,
        CancellationToken abbruch)
    {
        var ich = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var freizeit = await db.Freizeiten.Where(f => f.Id == freizeitId).Select(f => new { f.LetzterTag }).SingleOrDefaultAsync(abbruch);
        if (freizeit is null)
        {
            return TypedResults.NotFound();
        }

        var personen = await db.Rollen
            .Where(r => r.FreizeitId == freizeitId && r.PersonId != ich)
            .Select(r => r.PersonId)
            .Distinct()
            .ToListAsync(abbruch);
        var stand = await nachweisStand.StandAsync(personen, abbruch);
        var heute = FreizeitZeiten.Heute(uhr.GetUtcNow());
        var bestaetigungen = stand.Bestaetigungen.ToLookup(b => b.PersonId);
        var mitWarnung = personen
            .Select(p => (PersonId: p, Warnungen: NachweisWarnregel.Warnungen(stand.PflichtArten, bestaetigungen[p], freizeit.LetzterTag, heute)))
            .Where(p => p.Warnungen.Count > 0)
            .ToList();
        var gefunden = mitWarnung.Count == 0
            ? new Dictionary<Guid, PersonenName>()
            : await namen.NamenAsync([.. mitWarnung.Select(p => p.PersonId)], abbruch);

        return TypedResults.Ok(new NachweisWarnungen(
        [
            .. mitWarnung
                .Select(p => (p.PersonId, p.Warnungen, Name: gefunden.GetValueOrDefault(p.PersonId)))
                .OrderBy(p => string.IsNullOrWhiteSpace(p.Name?.Nachname) && string.IsNullOrWhiteSpace(p.Name?.Vorname))
                .ThenBy(p => p.Name?.Nachname ?? "", StringComparer.CurrentCulture)
                .ThenBy(p => p.Name?.Vorname ?? "", StringComparer.CurrentCulture)
                .ThenBy(p => p.PersonId)
                .Select(p => new NachweisWarnungPerson(p.PersonId, p.Name?.Vorname, p.Name?.Nachname, p.Warnungen)),
        ]));
    }
}
