using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// Story 4-1: Ändert genau ein Feld eines Rezepts (Name, Basis-Personenzahl oder Zubereitung, AD-19). Konfliktregel: Weicht der aktuelle Wert
/// desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="RezeptAenderung.Konflikt"/>. Eine abgeschlossene Freizeit ist nur
/// lesbar (AD-17).
/// </summary>
internal sealed record RezeptFeldAendernCommand(Guid FreizeitId, Guid RezeptId, RezeptFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<RezeptAenderung>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> das verfolgte Rezept (nach dem Speichern mit neuer Version).</summary>
internal abstract record RezeptAenderung
{
    private RezeptAenderung()
    {
    }

    public sealed record Gespeichert(Rezept Rezept) : RezeptAenderung;

    public sealed record Konflikt : RezeptAenderung;

    public sealed record Abgeschlossen : RezeptAenderung;

    public sealed record NichtGefunden : RezeptAenderung;
}

internal sealed class RezeptFeldAendernValidator : AbstractValidator<RezeptFeldAendernCommand>
{
    public RezeptFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var fehler = c.Feld switch
            {
                RezeptFeld.Name => KuecheWerte.PflichtFehler(c.NeuerWert, RezeptFelder.NameMaximaleLaenge, "Name fehlt."),
                RezeptFeld.BasisPersonen => KuecheWerte.BasisFehler(c.NeuerWert),
                RezeptFeld.Zubereitung => KuecheWerte.LaengeFehler(c.NeuerWert, RezeptFelder.ZubereitungMaximaleLaenge),
                _ => null,
            };
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
            else if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(RezeptFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class RezeptFeldAendernHandler(KuecheDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<RezeptFeldAendernCommand, RezeptAenderung>
{
    public async ValueTask<RezeptAenderung> Handle(RezeptFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new RezeptAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new RezeptAenderung.Abgeschlossen();
        }

        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var rezept = await db.RezeptSperrenAsync(command.FreizeitId, command.RezeptId, cancellationToken);
        if (rezept is null)
        {
            return new RezeptAenderung.NichtGefunden();
        }

        return rezept.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new RezeptAenderung.Gespeichert(rezept)
            : new RezeptAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/kueche/rezepte/{rezeptId}</c> (Leitung und Team, Modul Verpflegung, AD-19): Körper
/// <c>{ "version": n, "alterWert": …, "name"|"basisPersonen"|"zubereitung": neu }</c> mit genau einem Feld als Text.
/// <c>200 { "version", "&lt;feld&gt;" }</c> mit dem normalisierten Wert; ungültiger Wert, kein, mehr als ein oder ein unbekanntes Feld <c>400</c>;
/// anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; Rezept einer anderen Freizeit
/// <c>404</c>.
/// </summary>
internal static class RezeptFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid rezeptId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<RezeptFeld>(koerper, RezeptFelder.JsonName);
        return await sender.Send(new RezeptFeldAendernCommand(freizeitId, rezeptId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            RezeptAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Rezept.Version,
                [RezeptFelder.JsonName(feld)] = gespeichert.Rezept.Wert(feld),
            }),
            RezeptAenderung.Konflikt => KuecheProbleme.Konflikt(),
            RezeptAenderung.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
