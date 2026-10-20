using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using ApiEinnahmeposten = Zeltlotse.Api.Contracts.Finanzen.Einnahmeposten;
using ApiKalkulation = Zeltlotse.Api.Contracts.Finanzen.Kalkulation;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/finanzen/kalkulation</c> (Leitung und Team, Modul Finanzen, Story 5-1, CAP-32, FR-54 bis FR-57): Planzahlen, die
/// sechs Kategorien in fester Reihenfolge mit ihren Kosten, die Einnahmeposten in der Reihenfolge des Anlegens und das Ergebnis nach der einen Rechenregel
/// <see cref="Beitragsrechnung"/>. Die Tage kommen aus Freizeiten (<see cref="IFreizeitTageLesen"/>). Was noch nicht gespeichert ist, steht mit 0 und
/// Version 0 da. Auch nach dem Abschluss lesbar.
/// </summary>
internal static class KalkulationLadenEndpunkt
{
    public static async Task<Results<Ok<ApiKalkulation>, NotFound>> Laden(Guid freizeitId, IFreizeitTageLesen tageLesen, FinanzenLeseDbContext db, CancellationToken abbruch) =>
        await KalkulationAbfrage.LadenAsync(freizeitId, tageLesen, db, abbruch) is { } kalkulation ? TypedResults.Ok(kalkulation) : TypedResults.NotFound();

    /// <summary>Ein Einnahmeposten, wie die API ihn liefert (Betrag ohne Null-Nachkommastellen).</summary>
    public static ApiEinnahmeposten AlsApi(Einnahmeposten posten) => new(posten.Id, posten.Version, posten.Bezeichnung, FinanzenWerte.OhneNullen(posten.Betrag));
}

/// <summary>
/// Die Rechnung der Kalkulation einer Freizeit, gemeinsam für <c>GET …/kalkulation</c> und (Story 5-2) das Soll in <c>GET …/soll-ist</c>; <c>null</c>, wenn
/// es die Freizeit hier nicht gibt.
/// </summary>
internal static class KalkulationAbfrage
{
    public static async Task<ApiKalkulation?> LadenAsync(Guid freizeitId, IFreizeitTageLesen tageLesen, FinanzenLeseDbContext db, CancellationToken abbruch)
    {
        if (!(await tageLesen.TageAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var freizeitTage))
        {
            return null;
        }

        var kalkulation = await db.Kalkulationen.Where(k => k.FreizeitId == freizeitId).SingleOrDefaultAsync(abbruch);
        var posten = await db.KategoriePosten.Where(k => k.FreizeitId == freizeitId).ToDictionaryAsync(k => k.Kategorie, abbruch);
        var einnahmen = await db.Einnahmeposten.Where(e => e.FreizeitId == freizeitId).OrderBy(e => e.Id).ToListAsync(abbruch);

        var (teilnehmende, mitarbeitende) = (kalkulation?.Teilnehmende ?? 0, kalkulation?.Mitarbeitende ?? 0);
        var tage = Beitragsrechnung.Tage(freizeitTage.ErsterTag, freizeitTage.LetzterTag);
        // Beträge ohne Null-Nachkommastellen (numeric(12,2) liest 7,50), damit Kosten und Ergebnis als JSON-Zahl schlicht bleiben.
        var ansaetze = Kostenkategorien.Alle
            .Select(k => (Kategorie: k, Stand: posten.GetValueOrDefault(k)))
            .Select(k => (k.Kategorie, k.Stand, Ansatz: new Kostenansatz(
                FinanzenWerte.OhneNullen(k.Stand?.Fixkosten ?? 0m), FinanzenWerte.OhneNullen(k.Stand?.ProPersonUndTag ?? 0m))))
            .ToList();
        return new ApiKalkulation(
            kalkulation?.Version ?? 0u,
            teilnehmende,
            mitarbeitende,
            tage,
            [
                .. ansaetze.Select(a => new KalkulationKategorie(
                    a.Kategorie, a.Stand?.Version ?? 0u, a.Ansatz.Fixkosten, a.Ansatz.ProPersonUndTag, Beitragsrechnung.Kosten(a.Ansatz, teilnehmende, mitarbeitende, tage))),
            ],
            [.. einnahmen.Select(KalkulationLadenEndpunkt.AlsApi)],
            Beitragsrechnung.Rechnen(teilnehmende, mitarbeitende, tage, ansaetze.Select(a => a.Ansatz), einnahmen.Select(e => FinanzenWerte.OhneNullen(e.Betrag))));
    }
}
