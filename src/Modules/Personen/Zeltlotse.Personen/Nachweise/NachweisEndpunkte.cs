using System.Globalization;
using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Datenschutz;
using ApiArt = Zeltlotse.Api.Contracts.Personen.NachweisArt;
using ApiStatus = Zeltlotse.Api.Contracts.Personen.EinsichtStatus;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>Abbildungen zwischen Fachmodell und Datenformen der API.</summary>
internal static class NachweisApi
{
    public static ApiArt Art(NachweisArt art) => (ApiArt)(int)art;

    /// <summary>Für die Person selbst: Eine abgelaufene Freigabe erscheint als „angefragt“, ohne „freigegeben bis“.</summary>
    public static (ApiStatus Status, DateTimeOffset? FreigegebenBis) Status(EinsichtStatus status, DateTimeOffset? freigegebenBis, DateTimeOffset jetzt) => status switch
    {
        EinsichtStatus.Abgeschlossen => (ApiStatus.Abgeschlossen, freigegebenBis),
        EinsichtStatus.Freigegeben when freigegebenBis >= jetzt => (ApiStatus.Freigegeben, freigegebenBis),
        _ => (ApiStatus.Angefragt, null),
    };

    /// <summary>Für die Prüfseiten: Eine abgelaufene Freigabe ist „abgelaufen“, mit dem Zeitpunkt des Ablaufs.</summary>
    public static (ApiStatus Status, DateTimeOffset? FreigegebenBis) PruefStatus(EinsichtStatus status, DateTimeOffset? freigegebenBis, DateTimeOffset jetzt) => status switch
    {
        EinsichtStatus.Abgeschlossen => (ApiStatus.Abgeschlossen, freigegebenBis),
        EinsichtStatus.Freigegeben when freigegebenBis >= jetzt => (ApiStatus.Freigegeben, freigegebenBis),
        EinsichtStatus.Freigegeben => (ApiStatus.Abgelaufen, freigegebenBis),
        _ => (ApiStatus.Angefragt, null),
    };

    /// <summary>Text der Antwort <c>409</c>, solange ein Nachweis in einer laufenden Prüfung steht.</summary>
    public const string InPruefung = "Der Nachweis ist gerade zur Prüfung freigegeben.";

    public static PostenErgebnis Ergebnis(PostenErgebnisArt ergebnis) => ergebnis switch
    {
        PostenErgebnisArt.Bestaetigt => PostenErgebnis.Bestaetigt,
        PostenErgebnisArt.Abgelehnt => PostenErgebnis.Abgelehnt,
        _ => PostenErgebnis.Offen,
    };

    /// <summary>Liest eine Art als camelCase-Wort (ohne Rücksicht auf Groß- und Kleinschreibung); Zahlen und Unbekanntes sind <c>null</c>.</summary>
    public static NachweisArt? ArtAusText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().All(char.IsAsciiLetter)
        && Enum.TryParse<NachweisArt>(text.Trim(), ignoreCase: true, out var art) && Enum.IsDefined(art)
            ? art
            : null;

    public static ValidierungsAusnahme Fehler(string feld, string text) =>
        new(new Dictionary<string, string[]> { [feld] = [text] });
}

/// <summary>
/// <c>GET /api/ich/nachweise</c> (AD-26, organisationsübergreifend): nur die eigenen Nachweise (mit entschlüsseltem
/// Ablehnungshinweis) und die eigenen offenen Einsichten aller Organisationen; eine abgelaufene Freigabe erscheint als
/// „angefragt“.
/// </summary>
internal static class NachweiseLadenEndpunkt
{
    public static async Task<Ok<NachweiseAntwort>> Laden(
        IAngemeldet angemeldet, PersonenLeseDbContext db, IVerschluesselung verschluesselung, TimeProvider uhr, CancellationToken abbruch)
    {
        var personId = FamilieAntworten.PersonId(angemeldet);
        var jetzt = uhr.GetUtcNow();
        var nachweise = await db.Nachweise.Where(n => n.PersonId == personId).OrderBy(n => n.Art).ToListAsync(abbruch);
        var zeilen = new List<NachweisZeile>();
        foreach (var n in nachweise)
        {
            var hinweis = n.Ablehnungshinweis is null ? null : await verschluesselung.EntschluesselnAsync(n.Ablehnungshinweis, abbruch);
            zeilen.Add(new NachweisZeile(n.Id, n.Version, NachweisApi.Art(n.Art), n.Ausstellungsdatum, n.HochgeladenAm, hinweis));
        }

        // Personenbezogener Lesepfad über Organisationen hinweg (AD-26): nur die eigenen Einsichten.
        var einsichten = await db.Einsichten.IgnoreQueryFilters()
            .Where(e => e.PersonId == personId && e.Status != EinsichtStatus.Abgeschlossen)
            .OrderBy(e => e.AngefragtAm)
            .ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.OrganisationId, e.FreizeitId, e.AngefragtVon, e.Status, e.AngefragtAm, e.FreigegebenBis })
            .ToListAsync(abbruch);
        var anfragendeIds = einsichten.Where(e => e.AngefragtVon != null).Select(e => e.AngefragtVon!.Value).Distinct().ToList();
        var namen = await db.Personen.Where(p => anfragendeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new PersonenNameAntwort(p.Vorname, p.Nachname), abbruch);

        return TypedResults.Ok(new NachweiseAntwort(
            zeilen,
            [
                .. einsichten.Select(e =>
                {
                    var (status, bis) = NachweisApi.Status(e.Status, e.FreigegebenBis, jetzt);
                    var von = e.AngefragtVon is { } id ? namen.GetValueOrDefault(id) ?? new PersonenNameAntwort(null, null) : null;
                    return new EinsichtAnfrage(e.Id, e.OrganisationId, e.FreizeitId, von, status, e.AngefragtAm, bis);
                }),
            ]));
    }
}

/// <summary><c>POST /api/ich/nachweise</c>: <c>201 {nachweisId}</c>; <c>400</c> je Feld; Art schon vorhanden → <c>409</c>.</summary>
internal static class NachweisAnlegenEndpunkt
{
    public static async Task<Results<Created<NachweisHinzugefuegt>, ProblemHttpResult>> Anlegen(
        JsonElement koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var (art, datum) = Lesen(koerper);
        try
        {
            var ergebnis = await sender.Send(new NachweisAnlegen(FamilieAntworten.PersonId(angemeldet), art, datum), abbruch);
            if (ergebnis is NachweisAnlage.Angelegt angelegt)
            {
                return TypedResults.Created($"/api/ich/nachweise/{angelegt.NachweisId}", new NachweisHinzugefuegt(angelegt.NachweisId));
            }
        }
        catch (DbUpdateException fehler) when (fehler.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Gleichzeitig dieselbe Art angelegt: wie „schon vorhanden“.
        }

        return FamilieAntworten.Konflikt("Einen Nachweis dieser Art gibt es schon.");
    }

    /// <summary>
    /// Liest Art und Ausstellungsdatum. Eine unbekannte Art gilt als fehlend; ein Datum, das sich nicht lesen lässt, als ungültig
    /// (Jahr 1) – die Fehlertexte kommen vom Validator.
    /// </summary>
    private static (NachweisArt? Art, DateOnly? Datum) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw NachweisApi.Fehler(NachweisFelder.Art, "Art fehlt.");
        }

        var art = koerper.TryGetProperty(NachweisFelder.Art, out var a) && a.ValueKind == JsonValueKind.String ? NachweisApi.ArtAusText(a.GetString()) : null;
        DateOnly? datum = null;
        if (koerper.TryGetProperty(NachweisFelder.Ausstellungsdatum, out var d) && d.ValueKind != JsonValueKind.Null)
        {
            datum = d.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(d.GetString(), NachweisFelder.Datumsformat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag)
                ? tag
                : DateOnly.MinValue;
        }

        return (art, datum);
    }
}

/// <summary>
/// <c>PATCH /api/ich/nachweise/{nachweisId}</c> (AD-19): Körper <c>{ "version": n, "alterWert": …, "ausstellungsdatum": … }</c>.
/// <c>200 { "version", "ausstellungsdatum" }</c>; anderer Wert oder laufende Prüfung → <c>409</c> mit Code <c>konflikt</c>;
/// fremd → <c>404</c>.
/// </summary>
internal static class NachweisFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid nachweisId, JsonElement koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, neuerWert) = Lesen(koerper);
        var ergebnis = await sender.Send(
            new NachweisFeldAendern(FamilieAntworten.PersonId(angemeldet), nachweisId, NachweisFeld.Ausstellungsdatum, version, alterWert, neuerWert), abbruch);

        return ergebnis switch
        {
            NachweisAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Nachweis.Version,
                [NachweisFelder.Ausstellungsdatum] = gespeichert.Nachweis.Wert(NachweisFeld.Ausstellungsdatum),
            }),
            NachweisAenderung.Konflikt => FamilieAntworten.Konflikt("Der Wert wurde inzwischen geändert."),
            NachweisAenderung.InPruefung => FamilieAntworten.Konflikt(NachweisApi.InPruefung),
            _ => TypedResults.NotFound(),
        };
    }

    private static (uint Version, string? AlterWert, string? NeuerWert) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw NachweisApi.Fehler("version", "Der Körper ist kein JSON-Objekt.");
        }

        if (!koerper.TryGetProperty("version", out var versionWert) || !versionWert.TryGetUInt32(out var version))
        {
            throw NachweisApi.Fehler("version", "Version fehlt.");
        }

        if (!koerper.TryGetProperty(NachweisFelder.Ausstellungsdatum, out var neu))
        {
            throw NachweisApi.Fehler("feld", $"Genau ein Feld: {NachweisFelder.Ausstellungsdatum}.");
        }

        var alterWert = koerper.TryGetProperty("alterWert", out var alt) ? Text(alt, "alterWert") : null;
        return (version, alterWert, Text(neu, NachweisFelder.Ausstellungsdatum));
    }

    private static string? Text(JsonElement wert, string name) => wert.ValueKind switch
    {
        JsonValueKind.String => wert.GetString(),
        JsonValueKind.Null => null,
        _ => throw NachweisApi.Fehler(name, "Nur Text oder null."),
    };
}

/// <summary>
/// <c>POST /api/ich/nachweise/{nachweisId}/datei</c> (Multipart-Feld <c>datei</c>, AD-15): Prüfung am Inhalt (JPEG, PNG, WebP,
/// PDF, höchstens 10 MB), dann verschlüsselt in den Dateispeicher. <c>200 {hochgeladenAm}</c>; <c>400</c> am Feld <c>datei</c>;
/// fremd → <c>404</c>; laufende Prüfung → <c>409</c>.
/// </summary>
internal static class NachweisDateiEndpunkt
{
    public static async Task<Results<Ok<NachweisDateiHochgeladen>, NotFound, ProblemHttpResult>> Hochladen(
        Guid nachweisId, HttpRequest anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var datei = anfrage.HasFormContentType ? (await anfrage.ReadFormAsync(abbruch)).Files.GetFile(NachweisFelder.Datei) : null;
        if (datei is null)
        {
            throw NachweisApi.Fehler(NachweisFelder.Datei, "Datei fehlt.");
        }

        var pruefung = await DateiPruefung.LesenUndPruefenAsync(datei, cancellationToken: abbruch);
        if (!pruefung.Angenommen)
        {
            throw NachweisApi.Fehler(NachweisFelder.Datei, pruefung.Grund switch
            {
                Ablehnungsgrund.Leer => "Die Datei ist leer.",
                Ablehnungsgrund.ZuGross => "Die Datei ist größer als 10 MB.",
                _ => "Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF.",
            });
        }

        return await sender.Send(new NachweisDateiHochladen(FamilieAntworten.PersonId(angemeldet), nachweisId, pruefung.Inhalt!), abbruch) switch
        {
            NachweisDateiErgebnis.Hochgeladen hochgeladen => TypedResults.Ok(new NachweisDateiHochgeladen(hochgeladen.Am)),
            NachweisDateiErgebnis.InPruefung => FamilieAntworten.Konflikt(NachweisApi.InPruefung),
            _ => TypedResults.NotFound(),
        };
    }
}

/// <summary>
/// <c>POST /api/ich/nachweise/einsichten/{einsichtId}/freigeben</c>: <c>200 {freigegebenBis}</c>; fremd oder abgeschlossen →
/// <c>404</c>; gültige Freigabe läuft schon oder kein passender Nachweis mit Datei → <c>409</c>.
/// </summary>
internal static class EinsichtFreigebenEndpunkt
{
    public static async Task<Results<Ok<EinsichtFreigegeben>, NotFound, ProblemHttpResult>> Freigeben(
        Guid einsichtId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinsichtFreigeben(FamilieAntworten.PersonId(angemeldet), einsichtId), abbruch) switch
        {
            EinsichtFreigabe.Freigegeben freigegeben => TypedResults.Ok(new EinsichtFreigegeben(freigegeben.Bis)),
            EinsichtFreigabe.OhneDatei => FamilieAntworten.Konflikt("Es gibt keinen Nachweis mit Datei, der noch zu prüfen ist."),
            EinsichtFreigabe.LaeuftSchon => FamilieAntworten.Konflikt("Die Einsicht ist schon freigegeben."),
            _ => TypedResults.NotFound(),
        };
}
