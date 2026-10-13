using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Daten;
using ApiKette = Zeltlotse.Api.Contracts.Sicherheit.Sicherheitskette;

namespace Zeltlotse.Sicherheit.Sicherheitskette;

/// <summary>
/// Story 2-8: Ändert genau ein Feld der Sicherheitskette einer Freizeit (AD-19, nur Leitung). Gibt es noch keine Zeile, legt der erste
/// Aufruf sie an. Konfliktregel: Weicht der aktuelle Wert desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis
/// <see cref="SicherheitsketteAenderung.Konflikt"/>. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record SicherheitsketteFeldAendernCommand(Guid FreizeitId, SicherheitsketteFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<SicherheitsketteAenderung>, IModulCommand<SicherheitDbContext>, IBenutzerCommand
{
    /// <summary>Die Werte nicht ausgeben; Name und Telefon am Heimatort sind Angaben zu Menschen (AD-22).</summary>
    public override string ToString() => $"{nameof(SicherheitsketteFeldAendernCommand)}({FreizeitId}, {Feld})";
}

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> die verfolgte Kette (nach dem Speichern mit neuer Version).</summary>
internal abstract record SicherheitsketteAenderung
{
    private SicherheitsketteAenderung()
    {
    }

    public sealed record Gespeichert(Sicherheitskette Kette) : SicherheitsketteAenderung;

    public sealed record Konflikt : SicherheitsketteAenderung;

    public sealed record Abgeschlossen : SicherheitsketteAenderung;

    public sealed record NichtGefunden : SicherheitsketteAenderung;
}

internal sealed class SicherheitsketteFeldAendernValidator : AbstractValidator<SicherheitsketteFeldAendernCommand>
{
    /// <summary>Nur Ziffern, Leerzeichen und + ( ) / -; ein „+“ nur ganz vorn (sonst wählt der Telefon-Link eine falsche Nummer).</summary>
    public static bool IstTelefon(string wert) =>
        wert.Select((z, i) => char.IsAsciiDigit(z) || z is ' ' or '(' or ')' or '/' or '-' || (z == '+' && i == 0)).All(ok => ok);

    public SicherheitsketteFeldAendernValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            var wert = Sicherheitskette.Normalisieren(c.NeuerWert);
            if (wert is null)
            {
                return;
            }

            var name = SicherheitsketteFelder.JsonName(c.Feld);
            var hoechstens = SicherheitsketteFelder.MaximaleLaenge(c.Feld);
            if (c.Feld is SicherheitsketteFeld.LeitungTelefon or SicherheitsketteFeld.HeimatTelefon && !wert.Any(char.IsAsciiDigit))
            {
                kontext.AddFailure(new ValidationFailure(name, "Die Telefonnummer braucht mindestens eine Ziffer."));
            }
            else if (c.Feld is SicherheitsketteFeld.LeitungTelefon or SicherheitsketteFeld.HeimatTelefon && !IstTelefon(wert))
            {
                kontext.AddFailure(new ValidationFailure(name, "Die Telefonnummer darf nur Ziffern, Leerzeichen und + ( ) / - enthalten."));
            }
            else if (wert.Length > hoechstens)
            {
                kontext.AddFailure(new ValidationFailure(name, $"Höchstens {hoechstens} Zeichen."));
            }
        });
    }
}

internal sealed class SicherheitsketteFeldAendernHandler(SicherheitDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<SicherheitsketteFeldAendernCommand, SicherheitsketteAenderung>
{
    public async ValueTask<SicherheitsketteAenderung> Handle(SicherheitsketteFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new SicherheitsketteAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new SicherheitsketteAenderung.Abgeschlossen();
        }

        // Erste Anlage: eindeutig je Freizeit. Zwei gleichzeitige erste Aufrufe legen nur eine Zeile an; der zweite wartet auf den ersten
        // und findet danach dessen Zeile. Die Zeilensperre (FOR UPDATE) lässt Änderungen nacheinander laufen, allein die Regel zum alten
        // Wert entscheidet.
        var id = Guid.CreateVersion7(uhr.GetUtcNow());
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO sicherheit.sicherheitskette (\"Id\", \"OrganisationId\", \"FreizeitId\") VALUES ({id}, {organisationId}, {command.FreizeitId}) ON CONFLICT (\"FreizeitId\") DO NOTHING",
            cancellationToken);
        var kette = (await db.Sicherheitsketten
                .FromSql($"SELECT *, xmin FROM sicherheit.sicherheitskette WHERE \"FreizeitId\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (kette is null)
        {
            return new SicherheitsketteAenderung.NichtGefunden();
        }

        return kette.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new SicherheitsketteAenderung.Gespeichert(kette)
            : new SicherheitsketteAenderung.Konflikt();
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c> (Leitung und Team, FR-29): die Kette mit Version (AD-19). Ohne
/// Zeile sind alle Felder leer, nicht eingerichtet, Version <c>0</c>. Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class SicherheitsketteLadenEndpunkt
{
    public static async Task<Ok<ApiKette>> Laden(Guid freizeitId, SicherheitLeseDbContext db, CancellationToken abbruch)
    {
        var kette = await db.Sicherheitsketten.SingleOrDefaultAsync(k => k.FreizeitId == freizeitId, abbruch);
        return TypedResults.Ok(kette is null
            ? new ApiKette(null, null, null, null, false, 0)
            : new ApiKette(kette.WasTun, kette.LeitungTelefon, kette.HeimatName, kette.HeimatTelefon, kette.Eingerichtet(), kette.Version));
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c> (Leitung, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "wasTun"|"leitungTelefon"|"heimatName"|"heimatTelefon": neu }</c> mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>;
/// ungültiger Wert, kein, mehr als ein oder ein unbekanntes Feld <c>400</c>; anderer Wert desselben Felds <c>409</c> mit Code
/// <c>konflikt</c>; abgeschlossene Freizeit <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.
/// </summary>
internal static class SicherheitsketteFeldAendernEndpunkt
{
    /// <summary>Erlaubte Eigenschaften neben den Feldern.</summary>
    private static readonly string[] Rahmen = ["version", "alterWert"];

    public static async Task<IResult> Aendern(Guid freizeitId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new SicherheitsketteFeldAendernCommand(freizeitId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            SicherheitsketteAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Kette.Version,
                [SicherheitsketteFelder.JsonName(feld)] = gespeichert.Kette.Wert(feld),
            }),
            SicherheitsketteAenderung.Konflikt => Problem(Problemcodes.Konflikt, "Der Wert wurde inzwischen geändert."),
            SicherheitsketteAenderung.Abgeschlossen => Problem(Problemcodes.FreizeitAbgeschlossen, "Diese Freizeit ist abgeschlossen und nur lesbar."),
            _ => TypedResults.NotFound(),
        };
    }

    private static ProblemHttpResult Problem(string code, string titel) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: titel,
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = code });

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (uint Version, string? AlterWert, SicherheitsketteFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
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
        var felder = Enum.GetValues<SicherheitsketteFeld>();
        var genannt = koerper.EnumerateObject().Select(e => e.Name).Where(n => !Rahmen.Contains(n)).ToList();
        if (genannt.Count != 1 || felder.Where(f => SicherheitsketteFelder.JsonName(f) == genannt[0]).ToList() is not [var feld])
        {
            throw Fehler("feld", $"Genau ein Feld: {string.Join(", ", felder.Select(SicherheitsketteFelder.JsonName))}.");
        }

        var name = SicherheitsketteFelder.JsonName(feld);
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
