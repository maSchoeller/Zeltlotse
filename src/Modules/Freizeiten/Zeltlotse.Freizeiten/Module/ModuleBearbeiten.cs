using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Freizeiten.Module;

/// <summary>
/// Story 3-7: schaltet ein Modul der Freizeit (AD-19, AD-20, FR-44, nur Leitung). Konfliktregel: Weicht der aktuelle Wert desselben
/// Moduls vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="ModulAenderung.Konflikt"/>. Abschalten ändert nur den Schalter,
/// keine Daten. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ModulAendernCommand(Guid FreizeitId, ModulFeld Feld, string? AlterWert, string? NeuerWert)
    : ICommand<ModulAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis einer Änderung; bei <see cref="Gespeichert"/> die verfolgte Freizeit (nach dem Speichern mit neuer Version).</summary>
internal abstract record ModulAenderung
{
    private ModulAenderung()
    {
    }

    public sealed record Gespeichert(Freizeit Freizeit) : ModulAenderung;

    public sealed record Konflikt : ModulAenderung;

    public sealed record NichtGefunden : ModulAenderung;

    public sealed record Abgeschlossen : ModulAenderung;
}

internal sealed class ModulAendernValidator : AbstractValidator<ModulAendernCommand>
{
    public ModulAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Modul."));
            }
            else if (c.NeuerWert?.Trim() is not ("true" or "false"))
            {
                kontext.AddFailure(new ValidationFailure(ModulFelder.JsonName(c.Feld), "Ein Modul ist „true“ oder „false“."));
            }
        });
}

internal sealed class ModulAendernHandler(FreizeitenDbContext db, TimeProvider uhr) : ICommandHandler<ModulAendernCommand, ModulAenderung>
{
    public async ValueTask<ModulAenderung> Handle(ModulAendernCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE) unter dem Mandantenfilter wie bei Freizeitseite und Fragen: allein die Regel zum alten Wert entscheidet.
        var freizeit = (await db.Freizeiten
                .FromSql($"SELECT *, xmin FROM freizeiten.freizeit WHERE \"Id\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (freizeit is null)
        {
            return new ModulAenderung.NichtGefunden();
        }

        if (freizeit.Phase(uhr.GetUtcNow()) == FreizeitPhase.Gelaufen)
        {
            return new ModulAenderung.Abgeschlossen();
        }

        return freizeit.ModulAendern(command.Feld, command.AlterWert, command.NeuerWert!)
            ? new ModulAenderung.Gespeichert(freizeit)
            : new ModulAenderung.Konflikt();
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/module</c> (Leitung, auch nach dem Abschluss): die drei Module mit Version (AD-19).
/// </summary>
internal static class ModuleLadenEndpunkt
{
    public static async Task<Results<Ok<FreizeitModule>, NotFound>> Laden(Guid freizeitId, FreizeitenLeseDbContext db, CancellationToken abbruch)
    {
        var freizeit = await db.Freizeiten.SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        return freizeit is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new FreizeitModule(freizeit.Version, freizeit.Verpflegung, freizeit.Finanzen, freizeit.TeilnehmerZugang));
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/module</c> (AD-19): Körper <c>{ "version": n, "alterWert": "false", "verpflegung"|"finanzen"|"teilnehmerZugang": "true" }</c>
/// mit genau einem Modul. <c>200 { "version", "&lt;feld&gt;": "true"|"false" }</c>; kein, zwei oder ein unbekanntes Modul <c>400</c> an
/// <c>feld</c>, ein Wert außer <c>"true"</c>/<c>"false"</c> <c>400</c> am Modul; anderer Wert desselben Moduls <c>409</c> mit Code
/// <c>konflikt</c>; abgeschlossene Freizeit <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.
/// </summary>
internal static class ModulAendernEndpunkt
{
    private static readonly ModulFeld[] Felder = Enum.GetValues<ModulFeld>();

    public static async Task<IResult> Aendern(Guid freizeitId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new ModulAendernCommand(freizeitId, feld, alterWert, neuerWert), abbruch) switch
        {
            ModulAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Freizeit.Version,
                [ModulFelder.JsonName(feld)] = gespeichert.Freizeit.Modul(feld),
            }),
            ModulAenderung.Konflikt => FreizeitseiteProbleme.Konflikt(),
            ModulAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Modul; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (string? AlterWert, ModulFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
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
        var bekannt = new HashSet<string>(["version", "alterWert", .. Felder.Select(ModulFelder.JsonName)], StringComparer.Ordinal);
        var felder = Felder.Where(f => koerper.TryGetProperty(ModulFelder.JsonName(f), out _)).ToList();
        if (felder.Count != 1 || koerper.EnumerateObject().Any(e => !bekannt.Contains(e.Name)))
        {
            throw Fehler("feld", $"Genau ein Modul: {ModulFelder.Verpflegung}, {ModulFelder.Finanzen} oder {ModulFelder.TeilnehmerZugang}.");
        }

        var feld = felder[0];
        var name = ModulFelder.JsonName(feld);
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
