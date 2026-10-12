using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Rollenlisten der Prüf-Endpunkte (AD-12). Die erlaubten Aktionen <c>nachweise-pruefen</c> berechnen Freizeiten (Kontext) und
/// Organisationen (<c>erlaubt</c>) aus denselben Listen; ein HTTP-Test hält sie gleich.
/// </summary>
internal static class NachweisRechte
{
    /// <summary>Im Freizeit-Kontext prüft die Leitung.</summary>
    public static readonly RolleArt[] FreizeitPruefen = [RolleArt.Leitung];

    /// <summary>Auf Organisationsebene prüft der Org-Admin.</summary>
    public static readonly OrgRolleArt[] OrgPruefen = [OrgRolleArt.OrgAdmin];
}

/// <summary>
/// Wo geprüft wird: Organisation (Mandant aus der Route, AD-6) und bei Freizeit-Routen die Freizeit. Übernehmbar ist eine
/// ungebundene Einsicht über die Route jeder Freizeit, in der die Person eine Rolle hat, und über die Org-Route.
/// </summary>
internal sealed record PruefOrt(Guid OrganisationId, Guid? FreizeitId);

/// <summary>
/// Die sieben Prüf-Endpunkte unter <c>/api/freizeiten/{freizeitId}/personen/nachweise</c> (Leitung, FR-10, AD-9) und „Erinnern“
/// (FR-11, <c>POST …/erinnerungen</c>). Die Rolle prüft der Host; Personen prüft zusätzlich die Bindung an die eine prüfende
/// Person. Der Mandant ist die Organisation der Freizeit.
/// </summary>
internal static class FreizeitPruefEndpunkt
{
    public static void Abbilden(RouteGroupBuilder gruppe)
    {
        gruppe.MapGet("", Liste);
        gruppe.MapPost("/anfragen", Anfragen);
        gruppe.MapGet("/einsichten/{einsichtId:guid}", Detail);
        gruppe.MapPost("/einsichten/{einsichtId:guid}/pruefen", Pruefen);
        gruppe.MapGet("/einsichten/{einsichtId:guid}/nachweise/{nachweisId:guid}/datei", Datei);
        gruppe.MapPost("/einsichten/{einsichtId:guid}/nachweise/{nachweisId:guid}/bestaetigen", Bestaetigen);
        gruppe.MapPost("/einsichten/{einsichtId:guid}/nachweise/{nachweisId:guid}/ablehnen", Ablehnen);
        gruppe.MapPost("/erinnerungen", Erinnern);
    }

    /// <summary>
    /// <c>POST …/erinnerungen {personId}</c> (FR-11, AD-13): <c>200 {erinnertAm}</c>; ohne Rolle in der Freizeit oder für sich selbst
    /// <c>404</c>; heute schon erinnert (Europe/Berlin) <c>409</c>; ohne <c>personId</c> <c>400</c>. Die Mail geht über die Outbox.
    /// </summary>
    public static async Task<Results<Ok<NachweisErinnert>, NotFound, ProblemHttpResult>> Erinnern(
        Guid freizeitId, NachweisErinnerungSenden anfrage, IMandantenKontext mandant, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new NachweisErinnern(Ort(mandant, freizeitId).OrganisationId, freizeitId, FamilieAntworten.PersonId(angemeldet), anfrage.PersonId), abbruch) switch
        {
            ErinnerungErgebnis.Erinnert erinnert => TypedResults.Ok(new NachweisErinnert(erinnert.ErinnertAm)),
            ErinnerungErgebnis.SchonHeute => FamilieAntworten.Konflikt("Die Person wurde heute schon erinnert."),
            _ => TypedResults.NotFound(),
        };

    public static Task<Ok<List<PruefPerson>>> Liste(Guid freizeitId, IMandantenKontext mandant, IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch) =>
        PruefAbfragen.ListeAsync(Ort(mandant, freizeitId), angemeldet, db, uhr, abbruch);

    public static Task<Results<Created<EinsichtAngefragt>, Ok<EinsichtAngefragt>, NotFound>> Anfragen(
        Guid freizeitId, EinsichtAnfragen anfrage, IMandantenKontext mandant, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.AnfragenAsync(Ort(mandant, freizeitId), anfrage, angemeldet, sender, abbruch);

    public static Task<Results<Ok<EinsichtDetail>, NotFound>> Detail(
        Guid freizeitId, Guid einsichtId, IMandantenKontext mandant, IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch) =>
        PruefAbfragen.DetailAsync(Ort(mandant, freizeitId), einsichtId, angemeldet, db, uhr, abbruch);

    public static Task<Results<NoContent, NotFound, ProblemHttpResult>> Pruefen(
        Guid freizeitId, Guid einsichtId, IMandantenKontext mandant, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.PruefenAsync(Ort(mandant, freizeitId), einsichtId, angemeldet, sender, abbruch);

    public static Task<IResult> Datei(
        Guid freizeitId, Guid einsichtId, Guid nachweisId, IAngemeldet angemeldet, PersonenLeseDbContext db, IGeschuetzteDateien dateien, TimeProvider uhr, CancellationToken abbruch) =>
        PruefAbfragen.DateiAsync(einsichtId, nachweisId, angemeldet, db, dateien, uhr, abbruch);

    public static Task<Results<NoContent, NotFound>> Bestaetigen(Guid freizeitId, Guid einsichtId, Guid nachweisId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.EntscheidenAsync(einsichtId, nachweisId, true, null, angemeldet, sender, abbruch);

    public static Task<Results<NoContent, NotFound>> Ablehnen(
        Guid freizeitId, Guid einsichtId, Guid nachweisId, NachweisAblehnen anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.EntscheidenAsync(einsichtId, nachweisId, false, anfrage.Hinweis, angemeldet, sender, abbruch);

    /// <summary>Der Mandant einer Freizeit-Route ist die Organisation der Freizeit; der Host hat ihn gesetzt.</summary>
    private static PruefOrt Ort(IMandantenKontext mandant, Guid freizeitId) =>
        new(mandant.OrganisationId ?? throw new InvalidOperationException("Ohne Mandanten-Kontext erreicht niemand diesen Endpunkt."), freizeitId);
}

/// <summary>
/// Dieselben sieben Prüf-Endpunkte unter <c>/api/organisationen/{orgId}/personen/nachweise</c> (Org-Admin). Übernehmbar ist hier
/// jede ungebundene Einsicht der Organisation außer der eigenen, auch eine automatische.
/// </summary>
internal static class OrgPruefEndpunkt
{
    public static void Abbilden(RouteGroupBuilder gruppe)
    {
        gruppe.MapGet("", Liste);
        gruppe.MapPost("/anfragen", Anfragen);
        gruppe.MapGet("/einsichten/{einsichtId:guid}", Detail);
        gruppe.MapPost("/einsichten/{einsichtId:guid}/pruefen", Pruefen);
        gruppe.MapGet("/einsichten/{einsichtId:guid}/nachweise/{nachweisId:guid}/datei", Datei);
        gruppe.MapPost("/einsichten/{einsichtId:guid}/nachweise/{nachweisId:guid}/bestaetigen", Bestaetigen);
        gruppe.MapPost("/einsichten/{einsichtId:guid}/nachweise/{nachweisId:guid}/ablehnen", Ablehnen);
    }

    public static Task<Ok<List<PruefPerson>>> Liste(Guid orgId, IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch) =>
        PruefAbfragen.ListeAsync(new PruefOrt(orgId, null), angemeldet, db, uhr, abbruch);

    public static Task<Results<Created<EinsichtAngefragt>, Ok<EinsichtAngefragt>, NotFound>> Anfragen(
        Guid orgId, EinsichtAnfragen anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.AnfragenAsync(new PruefOrt(orgId, null), anfrage, angemeldet, sender, abbruch);

    public static Task<Results<Ok<EinsichtDetail>, NotFound>> Detail(
        Guid orgId, Guid einsichtId, IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch) =>
        PruefAbfragen.DetailAsync(new PruefOrt(orgId, null), einsichtId, angemeldet, db, uhr, abbruch);

    public static Task<Results<NoContent, NotFound, ProblemHttpResult>> Pruefen(Guid orgId, Guid einsichtId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.PruefenAsync(new PruefOrt(orgId, null), einsichtId, angemeldet, sender, abbruch);

    public static Task<IResult> Datei(
        Guid orgId, Guid einsichtId, Guid nachweisId, IAngemeldet angemeldet, PersonenLeseDbContext db, IGeschuetzteDateien dateien, TimeProvider uhr, CancellationToken abbruch) =>
        PruefAbfragen.DateiAsync(einsichtId, nachweisId, angemeldet, db, dateien, uhr, abbruch);

    public static Task<Results<NoContent, NotFound>> Bestaetigen(Guid orgId, Guid einsichtId, Guid nachweisId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.EntscheidenAsync(einsichtId, nachweisId, true, null, angemeldet, sender, abbruch);

    public static Task<Results<NoContent, NotFound>> Ablehnen(
        Guid orgId, Guid einsichtId, Guid nachweisId, NachweisAblehnen anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        PruefAbfragen.EntscheidenAsync(einsichtId, nachweisId, false, anfrage.Hinweis, angemeldet, sender, abbruch);
}

/// <summary>Lesen und Auslösen der Prüf-Endpunkte; gemeinsam für Freizeit- und Organisations-Routen.</summary>
internal static class PruefAbfragen
{
    /// <summary>
    /// Personen mit Rolle in der Freizeit bzw. in einer Freizeit der Organisation, außer der eigenen, je mit der jüngsten Einsicht
    /// in dieser Organisation; auf der Freizeit-Route dazu die letzte Erinnerung in dieser Freizeit (auf der Org-Route <c>null</c>).
    /// </summary>
    public static async Task<Ok<List<PruefPerson>>> ListeAsync(PruefOrt ort, IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch)
    {
        var ich = FamilieAntworten.PersonId(angemeldet);
        var jetzt = uhr.GetUtcNow();
        var rollen = db.FreizeitRollen.AsQueryable();
        if (ort.FreizeitId is { } freizeit)
        {
            rollen = rollen.Where(r => r.FreizeitId == freizeit);
        }

        var personIds = await rollen.Where(r => r.PersonId != ich).Select(r => r.PersonId).Distinct().ToListAsync(abbruch);
        var namen = await db.Personen.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Vorname, p.Nachname }, abbruch);
        var einsichten = (await db.Einsichten
                .Where(e => e.OrganisationId == ort.OrganisationId && personIds.Contains(e.PersonId))
                .Select(e => new { e.Id, e.PersonId, e.PruefendePersonId, e.Status, e.AngefragtAm, e.FreigegebenBis, e.AbgeschlossenAm })
                .ToListAsync(abbruch))
            .ToLookup(e => e.PersonId);
        var erinnert = ort.FreizeitId is { } erinnerungsFreizeit
            ? await db.Erinnerungen
                .Where(e => e.FreizeitId == erinnerungsFreizeit && personIds.Contains(e.PersonId))
                .ToDictionaryAsync(e => e.PersonId, e => (DateTimeOffset?)e.ErinnertAm, abbruch)
            : new Dictionary<Guid, DateTimeOffset?>();

        return TypedResults.Ok(personIds
            .Select(id => (Id: id, Name: namen.GetValueOrDefault(id)))
            .OrderBy(p => string.IsNullOrWhiteSpace(p.Name?.Nachname) && string.IsNullOrWhiteSpace(p.Name?.Vorname))
            .ThenBy(p => p.Name?.Nachname ?? "", StringComparer.CurrentCulture)
            .ThenBy(p => p.Name?.Vorname ?? "", StringComparer.CurrentCulture)
            .ThenBy(p => p.Id)
            .Select(p =>
            {
                var eigene = einsichten[p.Id].ToList();
                var offen = eigene.Any(e => e.Status != EinsichtStatus.Abgeschlossen);
                // Bei gleicher Zeit (eine Ablehnung fragt im selben Augenblick neu an) zählt die nicht abgeschlossene.
                var juengste = eigene
                    .OrderByDescending(e => e.AngefragtAm)
                    .ThenBy(e => e.Status == EinsichtStatus.Abgeschlossen)
                    .ThenByDescending(e => e.Id)
                    .FirstOrDefault();
                PruefEinsicht? einsicht = null;
                if (juengste is not null)
                {
                    var (status, bis) = NachweisApi.PruefStatus(juengste.Status, juengste.FreigegebenBis, jetzt);
                    // Jede Person der Liste hat eine Rolle in der Freizeit bzw. Organisation der Route.
                    einsicht = new PruefEinsicht(
                        juengste.Id, status, juengste.AngefragtAm, bis, juengste.AbgeschlossenAm,
                        EinsichtZugang.Pruefbar(juengste.PruefendePersonId, p.Id, ich, rolleInRoutenFreizeit: true));
                }

                return new PruefPerson(p.Id, p.Name?.Vorname, p.Name?.Nachname, !offen, einsicht, erinnert.GetValueOrDefault(p.Id));
            })
            .ToList());
    }

    /// <summary><c>POST …/anfragen</c>: <c>201 {einsichtId}</c> neu, <c>200</c> schon offen, <c>404</c> ohne Rolle oder für sich selbst.</summary>
    public static async Task<Results<Created<EinsichtAngefragt>, Ok<EinsichtAngefragt>, NotFound>> AnfragenAsync(
        PruefOrt ort, EinsichtAnfragen anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinsichtAnfragenCommand(ort.OrganisationId, ort.FreizeitId, FamilieAntworten.PersonId(angemeldet), anfrage.PersonId), abbruch) switch
        {
            EinsichtAnfrageErgebnis.Neu neu => TypedResults.Created((string?)null, new EinsichtAngefragt(neu.EinsichtId)),
            EinsichtAnfrageErgebnis.Vorhanden vorhanden => TypedResults.Ok(new EinsichtAngefragt(vorhanden.EinsichtId)),
            _ => TypedResults.NotFound(),
        };

    /// <summary><c>GET …/einsichten/{einsichtId}</c>: nur gebunden oder übernehmbar, sonst <c>404</c>.</summary>
    public static async Task<Results<Ok<EinsichtDetail>, NotFound>> DetailAsync(
        PruefOrt ort, Guid einsichtId, IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch)
    {
        var einsicht = await db.Einsichten.Include(e => e.Posten).SingleOrDefaultAsync(e => e.Id == einsichtId && e.OrganisationId == ort.OrganisationId, abbruch);
        if (einsicht is null
            || !await EinsichtZugang.PruefbarAsync(
                einsicht.PruefendePersonId, einsicht.PersonId, ort.FreizeitId, FamilieAntworten.PersonId(angemeldet), db.FreizeitRollen, abbruch))
        {
            return TypedResults.NotFound();
        }

        var person = await db.Personen.Where(p => p.Id == einsicht.PersonId).Select(p => new { p.Vorname, p.Nachname }).SingleOrDefaultAsync(abbruch);
        var nachweisIds = einsicht.Posten.Select(p => p.NachweisId).ToList();
        var nachweise = await db.Nachweise.Where(n => nachweisIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, abbruch);
        var jetzt = uhr.GetUtcNow();
        var (status, bis) = NachweisApi.PruefStatus(einsicht.Status, einsicht.FreigegebenBis, jetzt);
        return TypedResults.Ok(new EinsichtDetail(
            einsicht.Id,
            einsicht.PersonId,
            person?.Vorname,
            person?.Nachname,
            status,
            bis,
            einsicht.FreigabeAbgelaufen(jetzt),
            [
                .. einsicht.Posten.OrderBy(p => p.Art).Select(p => new EinsichtPosten(
                    p.NachweisId,
                    NachweisApi.Art(p.Art),
                    nachweise.GetValueOrDefault(p.NachweisId)?.Ausstellungsdatum ?? default,
                    nachweise.GetValueOrDefault(p.NachweisId)?.HochgeladenAm,
                    NachweisApi.Ergebnis(p.Ergebnis),
                    p.EntschiedenAm)),
            ]));
    }

    /// <summary><c>POST …/pruefen</c>: <c>204</c> gebunden; <c>404</c> nicht übernehmbar; <c>409</c> nicht freigegeben oder abgelaufen.</summary>
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> PruefenAsync(
        PruefOrt ort, Guid einsichtId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinsichtPruefungUebernehmen(ort.FreizeitId, FamilieAntworten.PersonId(angemeldet), einsichtId), abbruch) switch
        {
            PruefungUebernahme.Gebunden => TypedResults.NoContent(),
            PruefungUebernahme.NichtFreigegeben => FamilieAntworten.Konflikt("Die Einsicht ist nicht freigegeben."),
            _ => TypedResults.NotFound(),
        };

    /// <summary>
    /// <c>GET …/nachweise/{nachweisId}/datei</c> (AD-15): nur gebunden, bei gültiger Freigabe und für einen offenen Posten;
    /// Bilder <c>inline</c>, PDF als Anhang, Dateiname <c>&lt;art&gt;.&lt;endung&gt;</c>. Danach <c>404</c>.
    /// </summary>
    public static async Task<IResult> DateiAsync(
        Guid einsichtId, Guid nachweisId, IAngemeldet angemeldet, PersonenLeseDbContext db, IGeschuetzteDateien dateien, TimeProvider uhr, CancellationToken abbruch)
    {
        var einsicht = await db.Einsichten.Include(e => e.Posten).SingleOrDefaultAsync(e => e.Id == einsichtId, abbruch);
        var posten = einsicht?.OffenerPosten(FamilieAntworten.PersonId(angemeldet), nachweisId, uhr.GetUtcNow());
        if (posten is null)
        {
            return TypedResults.NotFound();
        }

        var inhalt = await dateien.LesenAsync(Nachweis.DateiPraefix, nachweisId, abbruch);
        if (inhalt is null || DateiPruefung.ArtErkennen(inhalt) is not { } art)
        {
            return TypedResults.NotFound();
        }

        return DateiAuslieferung.Ausliefern(inhalt, $"{NachweisFelder.JsonName(NachweisApi.Art(posten.Art))}.{NachweisRegeln.Endung(art)}");
    }

    /// <summary><c>POST …/bestaetigen</c> bzw. <c>…/ablehnen</c>: <c>204</c>; nicht gebunden, abgelaufen oder schon entschieden → <c>404</c>.</summary>
    public static async Task<Results<NoContent, NotFound>> EntscheidenAsync(
        Guid einsichtId, Guid nachweisId, bool bestaetigt, string? hinweis, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new NachweisEntscheiden(FamilieAntworten.PersonId(angemeldet), einsichtId, nachweisId, bestaetigt, hinweis), abbruch)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}
