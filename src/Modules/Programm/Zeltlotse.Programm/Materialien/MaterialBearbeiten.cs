using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Materialien;

/// <summary>
/// Story 3-2: Ändert genau ein Feld eines Materials (Bezeichnung oder Menge, AD-19); der Status ist ein eigener Übergang. Konfliktregel: Weicht
/// der aktuelle Wert desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="MaterialAenderung.Konflikt"/>. Eine
/// abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record MaterialFeldAendernCommand(Guid FreizeitId, Guid MaterialId, MaterialFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<MaterialAenderung>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> das verfolgte Material (nach dem Speichern mit neuer Version).</summary>
internal abstract record MaterialAenderung
{
    private MaterialAenderung()
    {
    }

    public sealed record Gespeichert(Material Material) : MaterialAenderung;

    public sealed record Konflikt : MaterialAenderung;

    public sealed record Abgeschlossen : MaterialAenderung;

    public sealed record NichtGefunden : MaterialAenderung;
}

internal sealed class MaterialFeldAendernValidator : AbstractValidator<MaterialFeldAendernCommand>
{
    public MaterialFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            switch (c.Feld)
            {
                case MaterialFeld.Bezeichnung when MaterialTexte.BezeichnungFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(MaterialFelder.Bezeichnung, fehler));
                    break;
                case MaterialFeld.Menge when MaterialTexte.MengeFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(MaterialFelder.Menge, fehler));
                    break;
                case MaterialFeld.Bezeichnung or MaterialFeld.Menge:
                    break;
                default:
                    kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                    break;
            }
        });
}

internal sealed class MaterialFeldAendernHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<MaterialFeldAendernCommand, MaterialAenderung>
{
    public async ValueTask<MaterialAenderung> Handle(MaterialFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new MaterialAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new MaterialAenderung.Abgeschlossen();
        }

        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var material = await db.MaterialSperrenAsync(command.FreizeitId, command.MaterialId, cancellationToken);
        if (material is null)
        {
            return new MaterialAenderung.NichtGefunden();
        }

        return material.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new MaterialAenderung.Gespeichert(material)
            : new MaterialAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/programm/material/{materialId}</c> (Leitung und Team, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "bezeichnung"|"menge": neu }</c> mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>; ungültiger Wert, kein, mehr als ein oder ein
/// unbekanntes Feld (auch <c>status</c>) <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; Material einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class MaterialFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid materialId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<MaterialFeld>(koerper, MaterialFelder.JsonName);
        return await sender.Send(new MaterialFeldAendernCommand(freizeitId, materialId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            MaterialAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Material.Version,
                [MaterialFelder.JsonName(feld)] = gespeichert.Material.Wert(feld),
            }),
            MaterialAenderung.Konflikt => ProgrammProbleme.Konflikt("Der Wert wurde inzwischen geändert."),
            MaterialAenderung.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
