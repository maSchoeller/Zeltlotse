using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Personen.Profil;

/// <summary>Liest das Profil einer Person über den Lese-Kontext; von <c>GET</c> und <c>POST</c> genutzt.</summary>
internal static class ProfilAbfrage
{
    public static Task<ProfilAntwort?> LadenAsync(PersonenLeseDbContext db, Guid personId, CancellationToken abbruch) =>
        db.Personen
            .Where(p => p.Id == personId)
            .Select(p => new ProfilAntwort(p.Version, p.Vorname, p.Nachname, p.Herkunft))
            .SingleOrDefaultAsync(abbruch);

    public static Guid PersonId(IAngemeldet angemeldet) =>
        angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
}

/// <summary><c>GET /api/ich/profil</c>: das eigene Profil (AD-26); <c>404</c>, solange die Person noch nicht angelegt ist.</summary>
internal static class ProfilLadenEndpunkt
{
    public static async Task<Results<Ok<ProfilAntwort>, NotFound>> Laden(IAngemeldet angemeldet, PersonenLeseDbContext db, CancellationToken abbruch) =>
        await ProfilAbfrage.LadenAsync(db, ProfilAbfrage.PersonId(angemeldet), abbruch) is { } profil
            ? TypedResults.Ok(profil)
            : TypedResults.NotFound();
}

/// <summary>
/// <c>POST /api/ich/profil</c>: legt die Person der Sitzung samt Personen-Schlüssel an, falls sie fehlt (idempotent), und
/// liefert das Profil. Der Client ruft es nach jeder Anmeldung auf (AD-2: Konten und Personen ohne Ereignis).
/// </summary>
internal static class ProfilSicherstellenEndpunkt
{
    public static async Task<Ok<ProfilAntwort>> Sicherstellen(
        IAngemeldet angemeldet, ISender sender, PersonenLeseDbContext lesen, CancellationToken abbruch)
    {
        var personId = ProfilAbfrage.PersonId(angemeldet);
        try
        {
            var person = await sender.Send(new ProfilSicherstellen(personId), abbruch);
            return TypedResults.Ok(new ProfilAntwort(person.Version, person.Vorname, person.Nachname, person.Herkunft));
        }
        catch (DbUpdateException fehler) when (fehler.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Gleichzeitig schon angelegt (zweiter Aufruf in derselben Sekunde): Das Profil gibt es jetzt.
            return TypedResults.Ok(await ProfilAbfrage.LadenAsync(lesen, personId, abbruch)
                                   ?? throw new InvalidOperationException("Das Profil fehlt nach dem Anlegen."));
        }
    }
}

/// <summary>
/// <c>PATCH /api/ich/profil</c> (AD-19): Körper <c>{ "version": n, "alterWert": …, "vorname"|"nachname"|"herkunft": neu }</c> mit genau
/// einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>; anderer Wert desselben Felds → <c>409</c> mit Code <c>konflikt</c>.
/// </summary>
internal static class ProfilFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(JsonElement koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        // Kein Fang für DbUpdateConcurrencyException: Der Handler sperrt die Zeile, gleichzeitige Änderungen laufen nacheinander.
        var ergebnis = await sender.Send(new ProfilFeldAendern(ProfilAbfrage.PersonId(angemeldet), feld, version, alterWert, neuerWert), abbruch);

        return ergebnis switch
        {
            ProfilAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Person.Version,
                [ProfilFelder.JsonName(feld)] = gespeichert.Person.Wert(feld),
            }),
            ProfilAenderung.Konflikt => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Der Wert wurde inzwischen geändert.",
                extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt }),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (uint Version, string? AlterWert, ProfilFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
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
        var felder = new[] { ProfilFeld.Vorname, ProfilFeld.Nachname, ProfilFeld.Herkunft }
            .Where(f => koerper.TryGetProperty(ProfilFelder.JsonName(f), out _))
            .ToList();
        if (felder.Count != 1)
        {
            throw Fehler("feld", $"Genau ein Feld: {ProfilFelder.Vorname}, {ProfilFelder.Nachname} oder {ProfilFelder.Herkunft}.");
        }

        var feld = felder[0];
        var name = ProfilFelder.JsonName(feld);
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
