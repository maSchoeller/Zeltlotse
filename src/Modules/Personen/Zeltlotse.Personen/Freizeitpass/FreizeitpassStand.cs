using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.SharedKernel.Anmeldung;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Stand-Regel der Freizeitpässe einer Freizeit (Story 2-6, FR-25, FR-27), an genau einer Stelle: Je Person zählt die jüngste Freigabe dieser
/// Freizeit („angelegt am“, dann Kennung absteigend, wie <see cref="FreizeitpassEinsichtEndpunkt"/>), aber nur, wenn sie aktiv ist
/// (<c>GueltigBis &gt; jetzt</c>); die Stufe spielt keine Rolle. <c>gefragt</c> ist die Zahl ihrer Fragen, <c>offen</c> die Zahl davon ohne
/// beantwortete Antwort-Zeile oder mit einer, die älter als 12 Monate ist (wie <see cref="FreizeitpassAntwort.IstVeraltet"/>). Gelesen werden
/// nur Kennungen und Zeitpunkte, nie Antworttexte; darum gibt es keine Entschlüsselung und keinen Eintrag im Einsicht-Protokoll.
/// </summary>
internal static class FreizeitpassStandRegel
{
    /// <summary>
    /// Die Personen der Freizeit mit offenen Angaben, ohne die anfragende Person und ohne Personen ohne Profil, sortiert nach Nachname, dann
    /// Vorname; je mit der letzten Freizeitpass-Erinnerung dieser Freizeit.
    /// </summary>
    public static async Task<List<FreizeitpassStandPerson>> LadenAsync(
        PersonenLeseDbContext db, Guid freizeitId, Guid anfragendePersonId, DateTimeOffset jetzt, CancellationToken abbruch)
    {
        var freigaben = await db.Freigaben
            .Where(f => f.FreizeitId == freizeitId && f.PersonId != anfragendePersonId)
            .Select(f => new { f.PersonId, f.AngelegtAm, f.Id, f.GueltigBis, f.Fragen })
            .ToListAsync(abbruch);
        var aktive = freigaben
            .GroupBy(f => f.PersonId)
            .Select(g => g.OrderByDescending(f => f.AngelegtAm).ThenByDescending(f => f.Id).First())
            .Where(f => f.GueltigBis > jetzt && f.Fragen.Length > 0)
            .Select(f => (f.PersonId, Fragen: f.Fragen.Distinct(StringComparer.Ordinal).ToList()))
            .ToList();
        if (aktive.Count == 0)
        {
            return [];
        }

        var ids = aktive.Select(f => f.PersonId).ToList();
        var grenze = jetzt.ToUniversalTime().AddMonths(-12);
        // Nur über die Spalten: beantwortet heißt Wert oder „Sonstiges“ vorhanden, aktuell heißt nicht älter als 12 Monate.
        var vollstaendig = (await db.FreizeitpassAntworten
                .Where(a => ids.Contains(a.PersonId) && (a.Wert != null || a.Sonstiges != null) && a.BestaetigtAm >= grenze)
                .Select(a => new { a.PersonId, a.Kennung })
                .ToListAsync(abbruch))
            .Select(a => (a.PersonId, a.Kennung))
            .ToHashSet();
        var mitOffenen = aktive
            .Select(f => (f.PersonId, Gefragt: f.Fragen.Count, Offen: f.Fragen.Count(k => !vollstaendig.Contains((f.PersonId, k)))))
            .Where(f => f.Offen > 0)
            .ToList();
        if (mitOffenen.Count == 0)
        {
            return [];
        }

        var offenIds = mitOffenen.Select(f => f.PersonId).ToList();
        var namen = await db.Personen.Where(p => offenIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => (p.Vorname, p.Nachname), abbruch);
        var erinnert = await db.FreizeitpassErinnerungen
            .Where(e => e.FreizeitId == freizeitId && offenIds.Contains(e.PersonId))
            .ToDictionaryAsync(e => e.PersonId, e => e.ErinnertAm, abbruch);
        return
        [
            .. mitOffenen
                .Where(f => namen.ContainsKey(f.PersonId))
                .Select(f => (f.PersonId, f.Gefragt, f.Offen, Name: namen[f.PersonId]))
                .OrderBy(f => string.IsNullOrWhiteSpace(f.Name.Nachname) && string.IsNullOrWhiteSpace(f.Name.Vorname))
                .ThenBy(f => f.Name.Nachname ?? "", StringComparer.CurrentCulture)
                .ThenBy(f => f.Name.Vorname ?? "", StringComparer.CurrentCulture)
                .ThenBy(f => f.PersonId)
                .Select(f => new FreizeitpassStandPerson(
                    f.PersonId, f.Name.Vorname, f.Name.Nachname, f.Gefragt, f.Offen, erinnert.TryGetValue(f.PersonId, out var am) ? am : null)),
        ];
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass-stand</c> (nur Leitung, Story 2-6, FR-25, FR-27): wer in dieser Freizeit offene
/// Angaben im Freizeitpass hat (<see cref="FreizeitpassStandRegel"/>). Die Rolle prüft der Host (<see cref="FreizeitpassRechte.Erinnern"/>);
/// der Mandant ist die Organisation der Freizeit.
/// </summary>
internal static class FreizeitpassStandEndpunkt
{
    public static async Task<Ok<FreizeitpassStand>> Laden(
        Guid freizeitId, IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch) =>
        TypedResults.Ok(new FreizeitpassStand(
            await FreizeitpassStandRegel.LadenAsync(db, freizeitId, FamilieAntworten.PersonId(angemeldet), uhr.GetUtcNow(), abbruch)));
}
