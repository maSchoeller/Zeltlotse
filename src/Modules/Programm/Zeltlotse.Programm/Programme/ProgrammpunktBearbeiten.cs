using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Story 3-1: Ändert genau ein Feld eines Programmpunkts (Titel oder Uhrzeit, AD-19), auch bei Mahlzeiten. Konfliktregel: Weicht der aktuelle
/// Wert desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="ProgrammpunktAenderung.Konflikt"/>. Eine abgeschlossene
/// Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ProgrammpunktFeldAendernCommand(Guid FreizeitId, Guid PunktId, ProgrammpunktFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<ProgrammpunktAenderung>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> der verfolgte Punkt (nach dem Speichern mit neuer Version).</summary>
internal abstract record ProgrammpunktAenderung
{
    private ProgrammpunktAenderung()
    {
    }

    public sealed record Gespeichert(Programmpunkt Punkt) : ProgrammpunktAenderung;

    public sealed record Konflikt : ProgrammpunktAenderung;

    public sealed record Abgeschlossen : ProgrammpunktAenderung;

    public sealed record NichtGefunden : ProgrammpunktAenderung;
}

internal sealed class ProgrammpunktFeldAendernValidator : AbstractValidator<ProgrammpunktFeldAendernCommand>
{
    public ProgrammpunktFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            switch (c.Feld)
            {
                case ProgrammpunktFeld.Titel when ProgrammpunktTexte.TitelFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Titel, fehler));
                    break;
                case ProgrammpunktFeld.Beginn when !ProgrammpunktFelder.UhrzeitLesen(c.NeuerWert, out _):
                    kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Beginn, ProgrammpunktTexte.KeineUhrzeit));
                    break;
                case ProgrammpunktFeld.Titel or ProgrammpunktFeld.Beginn:
                    break;
                default:
                    kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                    break;
            }
        });
}

internal sealed class ProgrammpunktFeldAendernHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<ProgrammpunktFeldAendernCommand, ProgrammpunktAenderung>
{
    public async ValueTask<ProgrammpunktAenderung> Handle(ProgrammpunktFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new ProgrammpunktAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new ProgrammpunktAenderung.Abgeschlossen();
        }

        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var punkt = await db.ProgrammpunktSperrenAsync(command.FreizeitId, command.PunktId, cancellationToken);
        if (punkt is null)
        {
            return new ProgrammpunktAenderung.NichtGefunden();
        }

        return punkt.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new ProgrammpunktAenderung.Gespeichert(punkt)
            : new ProgrammpunktAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/programm/punkte/{punktId}</c> (Leitung und Team, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "titel"|"beginn": neu }</c> mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>; ungültiger Wert, kein, mehr als ein oder ein
/// unbekanntes Feld <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>;
/// Punkt einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class ProgrammpunktFeldAendernEndpunkt
{
    /// <summary>Erlaubte Eigenschaften neben den Feldern.</summary>
    private static readonly string[] Rahmen = ["version", "alterWert"];

    public static async Task<IResult> Aendern(Guid freizeitId, Guid punktId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new ProgrammpunktFeldAendernCommand(freizeitId, punktId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            ProgrammpunktAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Punkt.Version,
                [ProgrammpunktFelder.JsonName(feld)] = gespeichert.Punkt.Wert(feld),
            }),
            ProgrammpunktAenderung.Konflikt => ProgrammProbleme.Konflikt("Der Wert wurde inzwischen geändert."),
            ProgrammpunktAenderung.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (uint Version, string? AlterWert, ProgrammpunktFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
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
        var felder = Enum.GetValues<ProgrammpunktFeld>();
        var genannt = koerper.EnumerateObject().Select(e => e.Name).Where(n => !Rahmen.Contains(n)).ToList();
        if (genannt.Count != 1 || felder.Where(f => ProgrammpunktFelder.JsonName(f) == genannt[0]).ToList() is not [var feld])
        {
            throw Fehler("feld", $"Genau ein Feld: {string.Join(", ", felder.Select(ProgrammpunktFelder.JsonName))}.");
        }

        var name = ProgrammpunktFelder.JsonName(feld);
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
