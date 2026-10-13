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
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Freizeiten.Fragen;

/// <summary>
/// Ändert die Fragen-Auswahl einer Zielgruppe (AD-19, FR-22, nur Leitung): Kennungen mit Komma, geprüft gegen den festen Katalog von
/// Personen (AD-10). Konfliktregel: Weicht die aktuelle Auswahl derselben Zielgruppe vom mitgeschickten alten Wert ab, ist das
/// Ergebnis <see cref="FragenAenderung.Konflikt"/>. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record FragenAuswahlAendernCommand(Guid FreizeitId, FragenFeld Feld, string? AlterWert, string? NeuerWert)
    : ICommand<FragenAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Änderung; bei <see cref="Gespeichert"/> die verfolgte Freizeit (nach dem Speichern mit neuer Version).</summary>
internal abstract record FragenAenderung
{
    private FragenAenderung()
    {
    }

    public sealed record Gespeichert(Freizeit Freizeit) : FragenAenderung;

    public sealed record Konflikt : FragenAenderung;

    public sealed record NichtGefunden : FragenAenderung;

    public sealed record Abgeschlossen : FragenAenderung;
}

internal sealed class FragenAuswahlAendernValidator : AbstractValidator<FragenAuswahlAendernCommand>
{
    public FragenAuswahlAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
            else if (Fragenkatalog.KennungenPruefen(c.NeuerWert).Fehler is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(FragenFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class FragenAuswahlAendernHandler(FreizeitenDbContext db, TimeProvider uhr) : ICommandHandler<FragenAuswahlAendernCommand, FragenAenderung>
{
    public async ValueTask<FragenAenderung> Handle(FragenAuswahlAendernCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE) unter dem Mandantenfilter wie bei der Freizeitseite: allein die Regel zum alten Wert entscheidet.
        var freizeit = (await db.Freizeiten
                .FromSql($"SELECT *, xmin FROM freizeiten.freizeit WHERE \"Id\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (freizeit is null)
        {
            return new FragenAenderung.NichtGefunden();
        }

        if (freizeit.Phase(uhr.GetUtcNow()) == FreizeitPhase.Gelaufen)
        {
            return new FragenAenderung.Abgeschlossen();
        }

        return freizeit.FragenAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new FragenAenderung.Gespeichert(freizeit)
            : new FragenAenderung.Konflikt();
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/fragen</c> (Leitung): die gewählten Fragen je Zielgruppe mit Version (AD-19) und der Katalog
/// (Kennung, Frage) aus <see cref="Fragenkatalog"/>.
/// </summary>
internal static class FragenAuswahlLadenEndpunkt
{
    public static async Task<Results<Ok<FragenAuswahl>, NotFound>> Laden(Guid freizeitId, FreizeitenLeseDbContext db, CancellationToken abbruch)
    {
        var freizeit = await db.Freizeiten.SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        return freizeit is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new FragenAuswahl(
                freizeit.Version,
                freizeit.Fragen(FragenFeld.Teilnehmende),
                freizeit.Fragen(FragenFeld.Mitarbeitende),
                [.. Fragenkatalog.Fragen.Select(f => new KatalogEintrag(f.Kennung, f.Frage))]));
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/fragen</c> (AD-19): Körper <c>{ "version": n, "alterWert": …, "teilnehmende"|"mitarbeitende": "a,b" }</c>
/// mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c> normalisiert (Katalogreihenfolge, ohne Doppelte, leer = <c>null</c>);
/// unbekannte Kennung <c>400</c> am Feld; anderer Wert derselben Zielgruppe <c>409</c> mit Code <c>konflikt</c>; abgeschlossene
/// Freizeit <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.
/// </summary>
internal static class FragenAuswahlAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new FragenAuswahlAendernCommand(freizeitId, feld, alterWert, neuerWert), abbruch) switch
        {
            FragenAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Freizeit.Version,
                [FragenFelder.JsonName(feld)] = gespeichert.Freizeit.Fragen(feld),
            }),
            FragenAenderung.Konflikt => FreizeitseiteProbleme.Konflikt(),
            FragenAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (string? AlterWert, FragenFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
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
        var felder = new[] { FragenFeld.Teilnehmende, FragenFeld.Mitarbeitende }
            .Where(f => koerper.TryGetProperty(FragenFelder.JsonName(f), out _))
            .ToList();
        if (felder.Count != 1)
        {
            throw Fehler("feld", $"Genau ein Feld: {FragenFelder.Teilnehmende} oder {FragenFelder.Mitarbeitende}.");
        }

        var feld = felder[0];
        var name = FragenFelder.JsonName(feld);
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
