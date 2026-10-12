using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Organisationen.Contracts.Loeschfristen;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Organisationen.Datenschutz;

/// <summary>
/// Ändert genau ein Feld der Löschfristen (AD-19, Org-Admin). Der Mandant kommt aus der Route; die Zeile einer anderen Organisation
/// ist <see cref="LoeschfristenAenderung.NichtGefunden"/>.
/// </summary>
internal sealed record LoeschfristFeldAendern(Guid OrganisationId, LoeschfristFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<LoeschfristenAenderung>, IModulCommand<OrganisationenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Feldänderung der Löschfristen; bei <see cref="Gespeichert"/> die verfolgte Zeile.</summary>
internal abstract record LoeschfristenAenderung
{
    private LoeschfristenAenderung()
    {
    }

    public sealed record Gespeichert(Loeschfristen Fristen) : LoeschfristenAenderung;

    public sealed record Konflikt : LoeschfristenAenderung;

    public sealed record NichtGefunden : LoeschfristenAenderung;
}

internal sealed class LoeschfristFeldAendernValidator : AbstractValidator<LoeschfristFeldAendern>
{
    public LoeschfristFeldAendernValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            if (!int.TryParse(c.NeuerWert?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var monate)
                || monate is < LoeschfristFelder.Mindestens or > LoeschfristFelder.Hoechstens)
            {
                kontext.AddFailure(new ValidationFailure(
                    LoeschfristFelder.JsonName(c.Feld),
                    $"Die Frist muss zwischen {LoeschfristFelder.Mindestens} und {LoeschfristFelder.Hoechstens} Monaten liegen."));
            }
        });
    }
}

internal sealed class LoeschfristFeldAendernHandler(OrganisationenDbContext db) : ICommandHandler<LoeschfristFeldAendern, LoeschfristenAenderung>
{
    public async ValueTask<LoeschfristenAenderung> Handle(LoeschfristFeldAendern command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE): Gleichzeitige Änderungen laufen nacheinander. Der Mandantenfilter gilt.
        var zeile = (await db.Loeschfristen
                .FromSql($"SELECT *, xmin FROM organisationen.loeschfristen WHERE \"OrganisationId\" = {command.OrganisationId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (zeile is null)
        {
            return new LoeschfristenAenderung.NichtGefunden();
        }

        return zeile.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert!)
            ? new LoeschfristenAenderung.Gespeichert(zeile)
            : new LoeschfristenAenderung.Konflikt();
    }
}

/// <summary>
/// <c>GET /api/organisationen/{orgId}/loeschfristen</c> (Org-Admin, NFR-5): beide Fristen in Monaten mit Version; ohne Zeile
/// <c>404</c>.
/// </summary>
internal static class LoeschfristenLadenEndpunkt
{
    public static async Task<Results<Ok<LoeschfristenAntwort>, NotFound>> Laden(Guid orgId, OrganisationenLeseDbContext db, CancellationToken abbruch) =>
        await db.Loeschfristen
                .Where(f => f.OrganisationId == orgId)
                .Select(f => new LoeschfristenAntwort(f.Version, f.FreizeitDatenMonate, f.NachweisBestaetigungMonate))
                .SingleOrDefaultAsync(abbruch) is { } fristen
            ? TypedResults.Ok(fristen)
            : TypedResults.NotFound();
}

/// <summary>
/// <c>PATCH /api/organisationen/{orgId}/loeschfristen</c> (AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "freizeitDatenMonate"|"nachweisBestaetigungMonate": "…" }</c> mit genau einem Feld als Text (1–120). <c>200 { "version",
/// "&lt;feld&gt;" }</c>; ungültiger Wert <c>400</c> am Feld; anderer Wert desselben Felds <c>409</c> mit Code <c>konflikt</c>.
/// </summary>
internal static class LoeschfristFeldAendernEndpunkt
{
    private static readonly LoeschfristFeld[] Felder = [LoeschfristFeld.FreizeitDatenMonate, LoeschfristFeld.NachweisBestaetigungMonate];

    public static async Task<IResult> Aendern(Guid orgId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new LoeschfristFeldAendern(orgId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            LoeschfristenAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Fristen.Version,
                [LoeschfristFelder.JsonName(feld)] = gespeichert.Fristen.Wert(feld),
            }),
            LoeschfristenAenderung.Konflikt => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Der Wert wurde inzwischen geändert.",
                extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt }),
            _ => TypedResults.NotFound(),
        };
    }

    private static (uint Version, string? AlterWert, LoeschfristFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw Fehler("version", "Der Körper ist kein JSON-Objekt.");
        }

        if (!koerper.TryGetProperty("version", out var versionWert) || versionWert.ValueKind != JsonValueKind.Number || !versionWert.TryGetUInt32(out var version))
        {
            throw Fehler("version", "Version fehlt.");
        }

        var alterWert = koerper.TryGetProperty("alterWert", out var alt) ? Text(alt, "alterWert") : null;
        var felder = Felder.Where(f => koerper.TryGetProperty(LoeschfristFelder.JsonName(f), out _)).ToList();
        if (felder.Count != 1)
        {
            throw Fehler("feld", $"Genau ein Feld: {LoeschfristFelder.FreizeitDatenMonate} oder {LoeschfristFelder.NachweisBestaetigungMonate}.");
        }

        var feld = felder[0];
        var name = LoeschfristFelder.JsonName(feld);
        return (version, alterWert, feld, Text(koerper.GetProperty(name), name));
    }

    private static string? Text(JsonElement wert, string name) => wert.ValueKind switch
    {
        JsonValueKind.String => wert.GetString(),
        JsonValueKind.Number => wert.GetRawText(),
        JsonValueKind.Null => null,
        _ => throw Fehler(name, "Nur Text, Zahl oder null."),
    };

    private static ValidierungsAusnahme Fehler(string feld, string text) =>
        new(new Dictionary<string, string[]> { [feld] = [text] });
}

/// <summary><see cref="ILoeschfristenLesen"/> über den Lese-Kontext, ohne Mandantenfilter (nur für den Lösch-Dienst, AD-6).</summary>
internal sealed class LoeschfristenLesen(OrganisationenLeseDbContext db) : ILoeschfristenLesen
{
    public async Task<IReadOnlyList<OrgLoeschfristen>> AlleAsync(CancellationToken abbruch = default)
    {
        var fristen = await db.Loeschfristen.IgnoreQueryFilters()
            .ToDictionaryAsync(f => f.OrganisationId, f => (f.FreizeitDatenMonate, f.NachweisBestaetigungMonate), abbruch);
        var organisationen = await db.Organisationen.Select(o => o.Id).ToListAsync(abbruch);
        return
        [
            .. organisationen.Select(id => fristen.TryGetValue(id, out var f)
                ? new OrgLoeschfristen(id, f.FreizeitDatenMonate, f.NachweisBestaetigungMonate)
                : new OrgLoeschfristen(id, Loeschfristen.VorgabeMonate, Loeschfristen.VorgabeMonate)),
        ];
    }
}
