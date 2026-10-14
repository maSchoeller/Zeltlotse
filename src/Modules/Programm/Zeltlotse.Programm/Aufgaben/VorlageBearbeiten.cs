using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// Story 3-5: Ändert genau ein Feld einer Aufgaben-Vorlage (Titel oder Phase, AD-19). Konfliktregel: Weicht der aktuelle Wert desselben Felds vom
/// mitgeschickten alten Wert ab, ist das Ergebnis <see cref="VorlageAenderung.Konflikt"/>. Bereits übernommene Aufgaben bleiben, wie sie sind.
/// </summary>
internal sealed record VorlageFeldAendernCommand(Guid VorlageId, AufgabenVorlageFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<VorlageAenderung>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> die verfolgte Vorlage (nach dem Speichern mit neuer Version).</summary>
internal abstract record VorlageAenderung
{
    private VorlageAenderung()
    {
    }

    public sealed record Gespeichert(AufgabenVorlage Vorlage) : VorlageAenderung;

    public sealed record Konflikt : VorlageAenderung;

    public sealed record NichtGefunden : VorlageAenderung;
}

internal sealed class VorlageFeldAendernValidator : AbstractValidator<VorlageFeldAendernCommand>
{
    public VorlageFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            switch (c.Feld)
            {
                case AufgabenVorlageFeld.Titel when AufgabenTexte.TitelFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(AufgabenFelder.Titel, fehler));
                    break;
                case AufgabenVorlageFeld.Phase when !AufgabenFelder.PhaseLesen(c.NeuerWert, out _):
                    kontext.AddFailure(new ValidationFailure(AufgabenFelder.Phase, AufgabenTexte.UnbekanntePhase));
                    break;
                case AufgabenVorlageFeld.Titel or AufgabenVorlageFeld.Phase:
                    break;
                default:
                    kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                    break;
            }
        });
}

internal sealed class VorlageFeldAendernHandler(ProgrammDbContext db) : ICommandHandler<VorlageFeldAendernCommand, VorlageAenderung>
{
    public async ValueTask<VorlageAenderung> Handle(VorlageFeldAendernCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var vorlage = await db.VorlageSperrenAsync(command.VorlageId, cancellationToken);
        if (vorlage is null)
        {
            return new VorlageAenderung.NichtGefunden();
        }

        return vorlage.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new VorlageAenderung.Gespeichert(vorlage)
            : new VorlageAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/organisationen/{orgId}/aufgaben-vorlagen/{vorlageId}</c> (Org-Admin, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "titel"|"phase": neu }</c> mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>; ungültiger Wert, kein, mehr als ein oder ein
/// unbekanntes Feld <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; unbekannt oder aus einer anderen Organisation <c>404</c>.
/// </summary>
internal static class VorlageFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid vorlageId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<AufgabenVorlageFeld>(koerper, AufgabenFelder.JsonName);
        return await sender.Send(new VorlageFeldAendernCommand(vorlageId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            VorlageAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Vorlage.Version,
                [AufgabenFelder.JsonName(feld)] = gespeichert.Vorlage.Wert(feld),
            }),
            VorlageAenderung.Konflikt => ProgrammProbleme.Konflikt("Der Wert wurde inzwischen geändert."),
            _ => TypedResults.NotFound(),
        };
    }
}
