using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Liest den Freizeitpass einer Person über den Lese-Kontext: je Katalogfrage die entschlüsselte Antwort, Version und „veraltet“
/// (der Server rechnet mit <see cref="TimeProvider"/>). Von der Konto-Ansicht und vom Gast-Link genutzt.
/// </summary>
internal static class FreizeitpassAbfrage
{
    public static async Task<FreizeitpassAnsicht> LadenAsync(
        PersonenLeseDbContext db, IVerschluesselung verschluesselung, Guid personId, DateTimeOffset jetzt, CancellationToken abbruch)
    {
        var vorname = await db.Personen.Where(p => p.Id == personId).Select(p => p.Vorname).SingleOrDefaultAsync(abbruch);
        var antworten = await db.FreizeitpassAntworten.Where(a => a.PersonId == personId).ToDictionaryAsync(a => a.Kennung, StringComparer.Ordinal, abbruch);
        var fragen = new List<FreizeitpassFrage>();
        foreach (var frage in Fragenkatalog.Fragen)
        {
            var antwort = antworten.GetValueOrDefault(frage.Kennung);
            fragen.Add(new FreizeitpassFrage(
                frage.Kennung,
                frage.Frage,
                Art(frage.Typ),
                [.. frage.Optionen.Select(o => new FreizeitpassOption(o.Wert, o.Text))],
                frage.MitSonstiges,
                await FreizeitpassRegeln.TextAsync(verschluesselung, antwort?.Wert, abbruch),
                await FreizeitpassRegeln.TextAsync(verschluesselung, antwort?.Sonstiges, abbruch),
                antwort?.Version ?? 0,
                antwort is not null && FreizeitpassAntwort.IstVeraltet(antwort.Beantwortet, antwort.BestaetigtAm, jetzt)));
        }

        return new FreizeitpassAnsicht(personId, vorname, fragen);
    }

    public static FrageArt Art(Antworttyp typ) => typ switch
    {
        Antworttyp.Freitext => FrageArt.Freitext,
        Antworttyp.Datum => FrageArt.Datum,
        Antworttyp.JaNein => FrageArt.JaNein,
        Antworttyp.Auswahl => FrageArt.Auswahl,
        _ => throw new ArgumentOutOfRangeException(nameof(typ), typ, null),
    };

    public static Guid PersonId(IAngemeldet angemeldet) =>
        angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
}

/// <summary>
/// <c>GET /api/ich/freizeitpass/{personId}</c> (AD-26, organisationsübergreifend): der Freizeitpass der Person selbst oder eines Kindes
/// unter 18 der eigenen Familie (<see cref="IVertretung"/>); jede andere Person <c>404</c>.
/// </summary>
internal static class FreizeitpassLadenEndpunkt
{
    public static async Task<Results<Ok<FreizeitpassAnsicht>, NotFound>> Laden(
        Guid personId, IAngemeldet angemeldet, IVertretung vertretung, PersonenLeseDbContext db, IVerschluesselung verschluesselung, TimeProvider uhr,
        CancellationToken abbruch)
    {
        if (!await vertretung.DarfAnmeldenAsync(FreizeitpassAbfrage.PersonId(angemeldet), personId, abbruch)
            || !await db.Personen.AnyAsync(p => p.Id == personId, abbruch))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await FreizeitpassAbfrage.LadenAsync(db, verschluesselung, personId, uhr.GetUtcNow(), abbruch));
    }
}

/// <summary>
/// <c>PATCH /api/ich/freizeitpass/{personId}/antworten/{kennung}</c> (AD-19): Körper <c>{ "version": n, "alterWert": …, "wert"|"sonstiges": neu }</c>
/// mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c> mit dem normalisierten Wert; ungültiger Wert <c>400</c> am Feld; anderer
/// Wert desselben Felds <c>409</c> mit Code <c>konflikt</c>; unbekannte Kennung oder fremde Person <c>404</c>.
/// </summary>
internal static class FreizeitpassAntwortAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid personId, string kennung, JsonElement koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        if (Fragenkatalog.Finden(kennung) is null)
        {
            return TypedResults.NotFound();
        }

        var (alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new FreizeitpassAntwortAendern(FreizeitpassAbfrage.PersonId(angemeldet), personId, kennung, feld, alterWert, neuerWert), abbruch) switch
        {
            AntwortAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Antwort?.Version ?? 0,
                [FreizeitpassFelder.JsonName(feld)] = gespeichert.Wert,
            }),
            AntwortAenderung.Konflikt => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Der Wert wurde inzwischen geändert.",
                extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt }),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (string? AlterWert, FreizeitpassFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw Fehler("version", "Der Körper ist kein JSON-Objekt.");
        }

        if (!koerper.TryGetProperty("version", out var versionWert) || versionWert.ValueKind != JsonValueKind.Number || !versionWert.TryGetUInt32(out _))
        {
            throw Fehler("version", "Version fehlt.");
        }

        var alterWert = koerper.TryGetProperty("alterWert", out var alt) ? Text(alt, "alterWert") : null;
        var felder = new[] { FreizeitpassFeld.Wert, FreizeitpassFeld.Sonstiges }
            .Where(f => koerper.TryGetProperty(FreizeitpassFelder.JsonName(f), out _))
            .ToList();
        if (felder.Count != 1)
        {
            throw Fehler("feld", $"Genau ein Feld: {FreizeitpassFelder.Wert} oder {FreizeitpassFelder.Sonstiges}.");
        }

        var feld = felder[0];
        var name = FreizeitpassFelder.JsonName(feld);
        return (alterWert, feld, Text(koerper.GetProperty(name), name));
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
/// <c>POST /api/ich/freizeitpass/{personId}/antworten/{kennung}/bestaetigen</c> („Stimmt“, FR-23): <c>204</c>; ohne Antwort, mit
/// unbekannter Kennung oder für eine fremde Person <c>404</c>.
/// </summary>
internal static class FreizeitpassAntwortBestaetigenEndpunkt
{
    public static async Task<Results<NoContent, NotFound>> Bestaetigen(Guid personId, string kennung, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        Fragenkatalog.Finden(kennung) is not null
        && await sender.Send(new FreizeitpassAntwortBestaetigen(FreizeitpassAbfrage.PersonId(angemeldet), personId, kennung), abbruch)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}

/// <summary>
/// <c>GET /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId}</c> (anonym, zugriffsbegrenzt, AD-11): der Freizeitpass
/// eines Kindes der Gast-Familie. Ungültiges, abgelaufenes oder übernommenes Token, anderer Zweck oder ein Kind einer anderen Familie
/// <c>404</c>. Nur eine Ansicht, darum kein Eintrag im Sicherheitsprotokoll.
/// </summary>
internal static class GastFreizeitpassLadenEndpunkt
{
    public static async Task<Results<Ok<FreizeitpassAnsicht>, NotFound>> Laden(
        string gastToken, Guid personId, IGastZugaenge gaeste, PersonenLeseDbContext db, IVerschluesselung verschluesselung, TimeProvider uhr,
        CancellationToken abbruch) =>
        await gaeste.PruefenAsync(gastToken, abbruch) is { } gast && gast.KindPersonIds.Contains(personId)
            ? TypedResults.Ok(await FreizeitpassAbfrage.LadenAsync(db, verschluesselung, personId, uhr.GetUtcNow(), abbruch))
            : TypedResults.NotFound();
}

/// <summary>
/// <c>POST /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId}</c> (anonym, zugriffsbegrenzt; Antiforgery über die
/// Dienst-Kennung der öffentlichen Seiten, AD-25): speichert das Formular (<see cref="FreizeitpassFormular"/>). <c>204</c>; ungültige
/// Werte <c>400</c> mit Fehlern je <c>&lt;kennung&gt;.wert</c> bzw. <c>&lt;kennung&gt;.sonstiges</c>; ungültiger Link oder fremdes Kind
/// <c>404</c> (mit Eintrag im Sicherheitsprotokoll).
/// </summary>
internal static class GastFreizeitpassSpeichernEndpunkt
{
    public static async Task<Results<NoContent, NotFound>> Speichern(string gastToken, Guid personId, FreizeitpassFormular formular, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new FreizeitpassFormularSpeichern(gastToken, personId, formular.Antworten ?? [], formular.Bestaetigt ?? []), abbruch)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}
