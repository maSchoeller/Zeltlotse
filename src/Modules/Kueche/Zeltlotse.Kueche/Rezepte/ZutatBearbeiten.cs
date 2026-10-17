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
/// Story 4-1: Ändert genau ein Feld einer Zutat (Bezeichnung, Menge, Einheit oder Allergene, AD-19). Konfliktregel: Weicht der aktuelle Wert
/// desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="ZutatAenderung.Konflikt"/>. Eine abgeschlossene Freizeit ist nur
/// lesbar (AD-17).
/// </summary>
internal sealed record ZutatFeldAendernCommand(Guid FreizeitId, Guid RezeptId, Guid ZutatId, ZutatFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<ZutatAenderung>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> die verfolgte Zutat (nach dem Speichern mit neuer Version).</summary>
internal abstract record ZutatAenderung
{
    private ZutatAenderung()
    {
    }

    public sealed record Gespeichert(Zutat Zutat) : ZutatAenderung;

    public sealed record Konflikt : ZutatAenderung;

    public sealed record Abgeschlossen : ZutatAenderung;

    public sealed record NichtGefunden : ZutatAenderung;
}

internal sealed class ZutatFeldAendernValidator : AbstractValidator<ZutatFeldAendernCommand>
{
    public ZutatFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var fehler = c.Feld switch
            {
                ZutatFeld.Bezeichnung => KuecheWerte.PflichtFehler(c.NeuerWert, ZutatFelder.BezeichnungMaximaleLaenge, "Bezeichnung fehlt."),
                ZutatFeld.Menge => KuecheWerte.MengeFehler(c.NeuerWert),
                ZutatFeld.Einheit => KuecheWerte.LaengeFehler(c.NeuerWert, ZutatFelder.EinheitMaximaleLaenge),
                ZutatFeld.Allergene => KuecheWerte.AllergeneFehler(c.NeuerWert),
                _ => null,
            };
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
            else if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(ZutatFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class ZutatFeldAendernHandler(KuecheDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<ZutatFeldAendernCommand, ZutatAenderung>
{
    public async ValueTask<ZutatAenderung> Handle(ZutatFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new ZutatAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new ZutatAenderung.Abgeschlossen();
        }

        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var zutat = await db.ZutatSperrenAsync(command.FreizeitId, command.RezeptId, command.ZutatId, cancellationToken);
        if (zutat is null)
        {
            return new ZutatAenderung.NichtGefunden();
        }

        return zutat.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new ZutatAenderung.Gespeichert(zutat)
            : new ZutatAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/kueche/rezepte/{rezeptId}/zutaten/{zutatId}</c> (Leitung und Team, Modul Verpflegung, AD-19): Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"menge"|"einheit"|"allergene": neu }</c> mit genau einem Feld als Text.
/// <c>200 { "version", "&lt;feld&gt;" }</c> mit dem normalisierten Wert (Menge „1,5“, Allergene in Katalogreihenfolge); ungültiger Wert, kein, mehr
/// als ein oder ein unbekanntes Feld <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; Zutat eines anderen Rezepts oder einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class ZutatFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid rezeptId, Guid zutatId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<ZutatFeld>(koerper, ZutatFelder.JsonName);
        return await sender.Send(new ZutatFeldAendernCommand(freizeitId, rezeptId, zutatId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            ZutatAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Zutat.Version,
                [ZutatFelder.JsonName(feld)] = gespeichert.Zutat.Wert(feld),
            }),
            ZutatAenderung.Konflikt => KuecheProbleme.Konflikt(),
            ZutatAenderung.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
