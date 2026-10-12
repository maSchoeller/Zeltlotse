using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using ApiArt = Zeltlotse.Api.Contracts.Organisationen.NachweisArt;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Organisationen.Nachweise;

/// <summary>
/// <c>GET /api/organisationen/{orgId}/nachweis-richtlinie</c> (Org-Admin, FR-9): je gespeicherter Art Pflicht, Gültigkeit und
/// Version, in der Reihenfolge des Katalogs. Nur gespeicherte Zeilen, keine Ersatzwerte (sie ließen sich nicht speichern).
/// </summary>
internal static class RichtlinieLadenEndpunkt
{
    public static async Task<Ok<List<RichtlinieZeile>>> Laden(Guid orgId, OrganisationenLeseDbContext db, CancellationToken abbruch)
    {
        var zeilen = await db.Richtlinien.Where(r => r.OrganisationId == orgId).OrderBy(r => r.Art).ToListAsync(abbruch);
        return TypedResults.Ok(zeilen.Select(z => new RichtlinieZeile((ApiArt)(int)z.Art, z.Version, z.Pflicht, z.GueltigkeitMonate)).ToList());
    }
}

/// <summary>
/// <c>PATCH /api/organisationen/{orgId}/nachweis-richtlinie/{art}</c> (AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "pflicht"|"gueltigkeitMonate": "…" }</c> mit genau einem Feld als Text. <c>200 { "version", "&lt;feld&gt;" }</c>; anderer
/// Wert → <c>409</c> mit Code <c>konflikt</c>; unbekannte Art → <c>404</c>.
/// </summary>
internal static class RichtlinieFeldAendernEndpunkt
{
    private static readonly RichtlinieFeld[] Felder = [RichtlinieFeld.Pflicht, RichtlinieFeld.GueltigkeitMonate];

    public static async Task<IResult> Aendern(Guid orgId, string art, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        if (ArtAusPfad(art) is not { } nachweisArt)
        {
            return TypedResults.NotFound();
        }

        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        var ergebnis = await sender.Send(new RichtlinieFeldAendern(orgId, nachweisArt, feld, version, alterWert, neuerWert), abbruch);
        return ergebnis switch
        {
            RichtlinienAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Richtlinie.Version,
                [RichtlinieFelder.JsonName(feld)] = gespeichert.Richtlinie.Wert(feld),
            }),
            RichtlinienAenderung.Konflikt => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Der Wert wurde inzwischen geändert.",
                extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt }),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Die Art als camelCase-Wort ohne Rücksicht auf Groß- und Kleinschreibung; Zahlen und Unbekanntes sind <c>null</c>.</summary>
    internal static NachweisArt? ArtAusPfad(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.All(char.IsAsciiLetter)
        && Enum.TryParse<NachweisArt>(text, ignoreCase: true, out var art) && Enum.IsDefined(art)
            ? art
            : null;

    private static (uint Version, string? AlterWert, RichtlinieFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
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
        var felder = Felder.Where(f => koerper.TryGetProperty(RichtlinieFelder.JsonName(f), out _)).ToList();
        if (felder.Count != 1)
        {
            throw Fehler("feld", $"Genau ein Feld: {RichtlinieFelder.Pflicht} oder {RichtlinieFelder.GueltigkeitMonate}.");
        }

        var feld = felder[0];
        var name = RichtlinieFelder.JsonName(feld);
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
/// <c>GET /api/ich/organisationen/nachweis-bestaetigungen</c> (AD-26): die eigenen Bestätigungen aller Organisationen mit Name
/// der Organisation und der prüfenden Person (Namen über Personen, AD-2). Nur Art, eingesehen am, gültig bis und von wem;
/// <c>abgelaufen</c> rechnet der Server (gültig bis vor heute, Kalendertag Europe/Berlin).
/// </summary>
internal static class MeineBestaetigungenEndpunkt
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static async Task<Ok<List<NachweisBestaetigungZeile>>> Laden(
        IAngemeldet angemeldet, OrganisationenLeseDbContext db, IPersonenNamen namen, TimeProvider uhr, CancellationToken abbruch)
    {
        var heute = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(uhr.GetUtcNow(), Berlin).DateTime);
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var bestaetigungen = await db.Bestaetigungen.IgnoreQueryFilters()
            .Where(b => b.PersonId == personId)
            .Select(b => new { b.OrganisationId, b.Art, b.EingesehenAm, b.GueltigBis, b.GeprueftVon })
            .ToListAsync(abbruch);
        var orgIds = bestaetigungen.Select(b => b.OrganisationId).Distinct().ToList();
        var organisationen = await db.Organisationen.Where(o => orgIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, abbruch);
        var pruefende = await namen.NamenAsync([.. bestaetigungen.Select(b => b.GeprueftVon).Distinct()], abbruch);

        return TypedResults.Ok(bestaetigungen
            .OrderBy(b => b.Art)
            .ThenBy(b => organisationen.GetValueOrDefault(b.OrganisationId) ?? "", StringComparer.CurrentCulture)
            .ThenBy(b => b.OrganisationId)
            .Select(b => new NachweisBestaetigungZeile(
                b.OrganisationId,
                organisationen.GetValueOrDefault(b.OrganisationId) ?? "",
                (ApiArt)(int)b.Art,
                b.EingesehenAm,
                b.GueltigBis,
                b.GueltigBis < heute,
                new GeprueftVon(pruefende.GetValueOrDefault(b.GeprueftVon)?.Vorname, pruefende.GetValueOrDefault(b.GeprueftVon)?.Nachname)))
            .ToList());
    }
}

/// <summary>
/// <c>GET /api/ich/organisationen/namen?id=…&amp;id=…</c> (AD-26): nur die Namen der genannten, existierenden Organisationen,
/// damit die Person sieht, welche Organisation um Einsicht bittet. Namen von Organisationen sind keine Personendaten.
/// Höchstens <see cref="OrganisationsNamen.HoechstensKennungen"/> Kennungen, sonst <c>400</c>.
/// </summary>
internal static class OrganisationsNamenEndpunkt
{
    public static async Task<Ok<List<OrganisationName>>> Laden(
        [FromQuery(Name = OrganisationsNamen.Parameter)] Guid[]? id, OrganisationenLeseDbContext db, CancellationToken abbruch)
    {
        var kennungen = (id ?? []).Distinct().ToList();
        if (kennungen.Count > OrganisationsNamen.HoechstensKennungen)
        {
            throw new ValidierungsAusnahme(new Dictionary<string, string[]>
            {
                [OrganisationsNamen.Parameter] = [$"Höchstens {OrganisationsNamen.HoechstensKennungen} Kennungen."],
            });
        }

        return TypedResults.Ok(await db.Organisationen
            .Where(o => kennungen.Contains(o.Id))
            .OrderBy(o => o.Name)
            .Select(o => new OrganisationName(o.Id, o.Name))
            .ToListAsync(abbruch));
    }
}
