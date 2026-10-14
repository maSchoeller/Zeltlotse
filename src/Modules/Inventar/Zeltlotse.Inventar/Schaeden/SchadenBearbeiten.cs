using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>
/// Story 3-4: Ändert die Bezeichnung eines Eintrags der Schadens- und Verlustliste (AD-19). Konfliktregel: Weicht der aktuelle Wert vom
/// mitgeschickten alten Wert ab, ist das Ergebnis <see cref="SchadenAenderung.Konflikt"/>. Ohne Phasenprüfung (AD-17, Anschaffungswünsche).
/// </summary>
internal sealed record SchadenFeldAendernCommand(Guid FreizeitId, Guid EintragId, SchadenFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<SchadenAenderung>, IModulCommand<InventarDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> der verfolgte Eintrag (nach dem Speichern mit neuer Version).</summary>
internal abstract record SchadenAenderung
{
    private SchadenAenderung()
    {
    }

    public sealed record Gespeichert(SchadenVerlust Eintrag) : SchadenAenderung;

    public sealed record Konflikt : SchadenAenderung;

    public sealed record NichtGefunden : SchadenAenderung;
}

internal sealed class SchadenFeldAendernValidator : AbstractValidator<SchadenFeldAendernCommand>
{
    public SchadenFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            switch (c.Feld)
            {
                case SchadenFeld.Bezeichnung when SchadenTexte.BezeichnungFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(SchadenFelder.Bezeichnung, fehler));
                    break;
                case SchadenFeld.Bezeichnung:
                    break;
                default:
                    kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                    break;
            }
        });
}

internal sealed class SchadenFeldAendernHandler(InventarDbContext db) : ICommandHandler<SchadenFeldAendernCommand, SchadenAenderung>
{
    public async ValueTask<SchadenAenderung> Handle(SchadenFeldAendernCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var eintrag = await db.SchadenSperrenAsync(command.FreizeitId, command.EintragId, cancellationToken);
        if (eintrag is null)
        {
            return new SchadenAenderung.NichtGefunden();
        }

        return eintrag.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new SchadenAenderung.Gespeichert(eintrag)
            : new SchadenAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/inventar/schaeden/{eintragId}</c> (Leitung und Team, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "bezeichnung": neu }</c>. <c>200 { "version", "bezeichnung" }</c>; ungültiger Wert, kein, mehr als ein oder ein unbekanntes Feld <c>400</c>;
/// anderer Wert <c>409 konflikt</c>; Eintrag einer anderen Freizeit oder unbekannt <c>404</c>.
/// </summary>
internal static class SchadenFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid eintragId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<SchadenFeld>(koerper, SchadenFelder.JsonName);
        return await sender.Send(new SchadenFeldAendernCommand(freizeitId, eintragId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            SchadenAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Eintrag.Version,
                [SchadenFelder.JsonName(feld)] = gespeichert.Eintrag.Wert(feld),
            }),
            SchadenAenderung.Konflikt => InventarProbleme.Konflikt("Der Wert wurde inzwischen geändert."),
            _ => TypedResults.NotFound(),
        };
    }
}
