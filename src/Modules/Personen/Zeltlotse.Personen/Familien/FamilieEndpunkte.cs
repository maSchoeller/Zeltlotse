using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Personen.Familien;

/// <summary>Gemeinsames der Familien-Endpunkte: Person der Sitzung und die Antwort <c>409</c> mit Code <c>konflikt</c>.</summary>
internal static class FamilieAntworten
{
    public static Guid PersonId(IAngemeldet angemeldet) =>
        angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");

    public static Guid KontoId(IAngemeldet angemeldet) =>
        angemeldet.KontoId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");

    /// <summary>AD-27: Fachregeln enden in <c>409</c> mit dem Code <c>konflikt</c>; die Seite unterscheidet die Fälle am Endpunkt.</summary>
    public static ProblemHttpResult Konflikt(string titel) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: titel,
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt });
}

/// <summary>
/// <c>GET /api/ich/familie</c> (AD-26, organisationsübergreifend): nur die eigene Familie – Eltern mit Namen, Kinder nach
/// Geburtsdatum mit der vom Server berechneten Volljährigkeit und der offenen Übergabe, offene Einladungen. Ohne Familie sind
/// alle Listen leer.
/// </summary>
internal static class FamilieLadenEndpunkt
{
    public static async Task<Ok<FamilieAntwort>> Laden(IAngemeldet angemeldet, PersonenLeseDbContext db, TimeProvider uhr, CancellationToken abbruch)
    {
        var personId = FamilieAntworten.PersonId(angemeldet);
        var familieId = await db.Elternteile.Where(e => e.PersonId == personId).Select(e => (Guid?)e.FamilieId).SingleOrDefaultAsync(abbruch);
        if (familieId is not { } familie)
        {
            return TypedResults.Ok(new FamilieAntwort([], [], []));
        }

        var jetzt = uhr.GetUtcNow();
        var heute = Volljaehrigkeit.Heute(uhr);
        var elternIds = await db.Elternteile.Where(e => e.FamilieId == familie).Select(e => e.PersonId).ToListAsync(abbruch);
        var namen = await db.Personen.Where(p => elternIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Vorname, p.Nachname }, abbruch);
        var kinder = await db.Personen
            .Where(p => p.FamilieId == familie)
            .OrderBy(p => p.Geburtsdatum)
            .ThenBy(p => p.Vorname)
            .ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Version, p.Vorname, p.Nachname, p.Geburtsdatum })
            .ToListAsync(abbruch);
        var kinderIds = kinder.Select(k => k.Id).ToList();
        var uebergaben = (await db.Uebergaben
                .Where(u => kinderIds.Contains(u.PersonId) && u.Status == UebergabeStatus.Offen && u.GueltigBis > jetzt)
                .Select(u => new { u.PersonId, u.Mail, u.GueltigBis, u.AngelegtAm })
                .ToListAsync(abbruch))
            .GroupBy(u => u.PersonId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.AngelegtAm).First());
        var einladungen = await db.FamilienEinladungen
            .Where(e => e.FamilieId == familie && e.Status == FamilienEinladungStatus.Offen && e.GueltigBis > jetzt)
            .OrderBy(e => e.AngelegtAm)
            .Select(e => new OffeneFamilienEinladung(e.Id, e.Mail, e.GueltigBis))
            .ToListAsync(abbruch);

        return TypedResults.Ok(new FamilieAntwort(
            [
                .. elternIds
                    .Select(id => new ElternteilZeile(id, namen.GetValueOrDefault(id)?.Vorname, namen.GetValueOrDefault(id)?.Nachname))
                    .OrderBy(e => e.Nachname ?? "", StringComparer.CurrentCulture)
                    .ThenBy(e => e.Vorname ?? "", StringComparer.CurrentCulture)
                    .ThenBy(e => e.PersonId),
            ],
            [
                .. kinder.Select(k => new KindZeile(
                    k.Id,
                    k.Version,
                    k.Vorname,
                    k.Nachname,
                    k.Geburtsdatum!.Value,
                    Volljaehrigkeit.Ist(k.Geburtsdatum.Value, heute),
                    uebergaben.TryGetValue(k.Id, out var u) ? new KindUebergabe(u.Mail, u.GueltigBis) : null)),
            ],
            einladungen));
    }
}

/// <summary><c>POST /api/ich/familie/kinder</c>: <c>201 {personId}</c>; <c>400</c> je Feld.</summary>
internal static class KindAnlegenEndpunkt
{
    public static async Task<Created<KindHinzugefuegt>> Anlegen(KindHinzufuegen anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var id = await sender.Send(new KindAnlegen(FamilieAntworten.PersonId(angemeldet), anfrage.Vorname, anfrage.Nachname, anfrage.Geburtsdatum), abbruch);
        return TypedResults.Created($"/api/ich/familie/kinder/{id}", new KindHinzugefuegt(id));
    }
}

/// <summary>
/// <c>PATCH /api/ich/familie/kinder/{personId}</c> (AD-19): Körper <c>{ "version": n, "alterWert": …, "vorname"|"nachname"|
/// "geburtsdatum": neu }</c> mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>; anderer Wert desselben Felds →
/// <c>409</c> mit Code <c>konflikt</c>; kein Kind der eigenen Familie → <c>404</c>.
/// </summary>
internal static class KindFeldAendernEndpunkt
{
    private static readonly KindFeld[] Felder = [KindFeld.Vorname, KindFeld.Nachname, KindFeld.Geburtsdatum];

    public static async Task<IResult> Aendern(Guid personId, JsonElement koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        var ergebnis = await sender.Send(new KindFeldAendern(FamilieAntworten.PersonId(angemeldet), personId, feld, version, alterWert, neuerWert), abbruch);

        return ergebnis switch
        {
            KindAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Person.Version,
                [KindFelder.JsonName(feld)] = gespeichert.Person.Wert(feld),
            }),
            KindAenderung.Konflikt => FamilieAntworten.Konflikt("Der Wert wurde inzwischen geändert."),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (uint Version, string? AlterWert, KindFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw Fehler("version", "Der Körper ist kein JSON-Objekt.");
        }

        if (!koerper.TryGetProperty("version", out var versionWert) || !versionWert.TryGetUInt32(out var version))
        {
            throw Fehler("version", "Version fehlt.");
        }

        var alterWert = koerper.TryGetProperty("alterWert", out var alt) ? Text(alt, "alterWert") : null;
        var felder = Felder.Where(f => koerper.TryGetProperty(KindFelder.JsonName(f), out _)).ToList();
        if (felder.Count != 1)
        {
            throw Fehler("feld", $"Genau ein Feld: {KindFelder.Vorname}, {KindFelder.Nachname} oder {KindFelder.Geburtsdatum}.");
        }

        var feld = felder[0];
        var name = KindFelder.JsonName(feld);
        return (version, alterWert, feld, Text(koerper.GetProperty(name), name));
    }

    private static string? Text(JsonElement wert, string name) => wert.ValueKind switch
    {
        JsonValueKind.String => wert.GetString(),
        JsonValueKind.Null => null,
        _ => throw Fehler(name, "Nur Text oder null."),
    };

    private static ValidierungsAusnahme Fehler(string feld, string text) =>
        new(new Dictionary<string, string[]> { [feld] = [text] });
}

/// <summary>
/// <c>POST /api/ich/familie/einladungen</c>: <c>202 {id}</c> und die Einladungs-Mail; <c>400</c> je Feld; Familie mit schon
/// zwei Eltern → <c>409</c>.
/// </summary>
internal static class ElternteilEinladenEndpunkt
{
    public static async Task<Results<Accepted<ElternteilEingeladen>, ProblemHttpResult>> Einladen(
        ElternteilEinladen anfrage, IAngemeldet angemeldet, ISender sender, IMailVersand mail, IConfiguration konfiguration, CancellationToken abbruch)
    {
        var ergebnis = await sender.Send(new ElternteilEinladenCommand(FamilieAntworten.PersonId(angemeldet), anfrage.Mail), abbruch);
        if (ergebnis is not ElternteilEinladung.Neu neu)
        {
            return FamilieAntworten.Konflikt("Das Familienkonto hat schon zwei Eltern.");
        }

        await FamilienMails.EinladungVerschickenAsync(mail, konfiguration, neu, abbruch);
        return TypedResults.Accepted((string?)null, new ElternteilEingeladen(neu.EinladungId));
    }
}

/// <summary>
/// <c>POST /api/ich/familie/kinder/{personId}/uebergabe</c> (erneute Anmeldung, AD-25): <c>202 {id}</c> und die Mail an das
/// volljährige Kind; <c>400</c> je Feld; kein Kind der eigenen Familie → <c>404</c>; noch nicht volljährig → <c>409</c>.
/// </summary>
internal static class ProfilUebergebenEndpunkt
{
    public static async Task<Results<Accepted<ProfilUebergabeVerschickt>, NotFound, ProblemHttpResult>> Uebergeben(
        Guid personId, ProfilUebergeben anfrage, IAngemeldet angemeldet, ISender sender, IMailVersand mail, IConfiguration konfiguration, CancellationToken abbruch)
    {
        var ergebnis = await sender.Send(new ProfilUebergebenCommand(FamilieAntworten.PersonId(angemeldet), personId, anfrage.Mail), abbruch);
        switch (ergebnis)
        {
            case UebergabeAusloesung.Neu neu:
                await FamilienMails.UebergabeVerschickenAsync(mail, konfiguration, neu, abbruch);
                return TypedResults.Accepted((string?)null, new ProfilUebergabeVerschickt(neu.UebergabeId));
            case UebergabeAusloesung.NichtVolljaehrig:
                return FamilieAntworten.Konflikt("Das Kind ist noch nicht volljährig.");
            default:
                return TypedResults.NotFound();
        }
    }
}

/// <summary>
/// <c>POST /api/personen/familie/einladungen/annehmen</c> (angemeldet, zugriffsbegrenzt): <c>200 {}</c>; ungültig → <c>404</c>
/// ohne Hinweis auf den Grund; Elternteil einer anderen Familie oder volle Familie → <c>409</c>. Die Mail-Adresse des Kontos
/// kommt aus der Sitzung (<see cref="IAngemeldeteMail"/>). Erfolg steht als <c>Rollenvergabe</c> im Sicherheitsprotokoll.
/// </summary>
internal static class FamilienEinladungAnnehmenEndpunkt
{
    public static async Task<Results<Ok<FamilienEinladungAngenommen>, NotFound, ProblemHttpResult>> Annehmen(
        FamilienEinladungAnnehmen anfrage, IAngemeldet angemeldet, IAngemeldeteMail angemeldeteMail, ISender sender, ISicherheitsprotokoll protokoll, CancellationToken abbruch)
    {
        var personId = FamilieAntworten.PersonId(angemeldet);
        var ergebnis = await sender.Send(new FamilienEinladungAnnehmenCommand(anfrage.Token, await angemeldeteMail.MailAsync(abbruch), personId), abbruch);
        switch (ergebnis)
        {
            case FamilienAnnahme.Angenommen angenommen:
                await protokoll.SchreibenAsync(SicherheitsEreignisArt.Rollenvergabe, personId, bezugId: angenommen.EinladungId, abbruch: abbruch);
                return TypedResults.Ok(new FamilienEinladungAngenommen());
            case FamilienAnnahme.Konflikt:
                return FamilieAntworten.Konflikt("Das Konto gehört schon zu einer anderen Familie, oder das Familienkonto hat schon zwei Eltern.");
            default:
                return TypedResults.NotFound();
        }
    }
}

/// <summary>
/// <c>POST /api/personen/uebergabe/annehmen</c> (angemeldet, zugriffsbegrenzt): <c>200 {personId}</c>; ungültig → <c>404</c>;
/// eigenes Profil mit Inhalt → <c>409</c>. Erfolg steht als <c>ProfilUebergabe</c> im Sicherheitsprotokoll (Person: das Kind).
/// Das Konto verbindet Konten über das Ereignis; danach meldet sich die Person neu an.
/// </summary>
internal static class ProfilUebernehmenEndpunkt
{
    public static async Task<Results<Ok<ProfilUebernommen>, NotFound, ProblemHttpResult>> Uebernehmen(
        ProfilUebernehmen anfrage, IAngemeldet angemeldet, IAngemeldeteMail angemeldeteMail, ISender sender, ISicherheitsprotokoll protokoll, CancellationToken abbruch)
    {
        var ergebnis = await sender.Send(
            new ProfilUebernehmenCommand(anfrage.Token, await angemeldeteMail.MailAsync(abbruch), FamilieAntworten.KontoId(angemeldet), FamilieAntworten.PersonId(angemeldet)),
            abbruch);
        switch (ergebnis)
        {
            case Uebernahme.Uebernommen uebernommen:
                await protokoll.SchreibenAsync(SicherheitsEreignisArt.ProfilUebergabe, uebernommen.KindId, bezugId: uebernommen.UebergabeId, abbruch: abbruch);
                return TypedResults.Ok(new ProfilUebernommen(uebernommen.KindId));
            case Uebernahme.EigenesProfil:
                return FamilieAntworten.Konflikt("Dieses Konto hat schon ein eigenes Profil.");
            default:
                return TypedResults.NotFound();
        }
    }
}
