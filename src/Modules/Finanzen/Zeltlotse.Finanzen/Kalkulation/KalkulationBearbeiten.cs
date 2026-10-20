using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1: Ändert genau eine Planzahl der Kalkulation (Teilnehmende oder Mitarbeitende, AD-19). Die Zeile entsteht beim ersten Ändern. Konfliktregel:
/// Weicht der aktuelle Wert desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="FinanzenAenderung{T}.Konflikt"/>. Eine
/// abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record KalkulationFeldAendernCommand(Guid FreizeitId, KalkulationFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<FinanzenAenderung<Kalkulation>>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

/// <summary>Ergebnis einer Feldänderung in den Finanzen; bei <see cref="Gespeichert"/> die verfolgte Zeile (nach dem Speichern mit neuer Version).</summary>
internal abstract record FinanzenAenderung<T>
{
    private FinanzenAenderung()
    {
    }

    public sealed record Gespeichert(T Zeile) : FinanzenAenderung<T>;

    public sealed record Konflikt : FinanzenAenderung<T>;

    public sealed record Abgeschlossen : FinanzenAenderung<T>;

    public sealed record NichtGefunden : FinanzenAenderung<T>;
}

internal sealed class KalkulationFeldAendernValidator : AbstractValidator<KalkulationFeldAendernCommand>
{
    public KalkulationFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
            else if (FinanzenWerte.PlanzahlFehler(c.NeuerWert) is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(KalkulationFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class KalkulationFeldAendernHandler(FinanzenDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<KalkulationFeldAendernCommand, FinanzenAenderung<Kalkulation>>
{
    public async ValueTask<FinanzenAenderung<Kalkulation>> Handle(KalkulationFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new FinanzenAenderung<Kalkulation>.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new FinanzenAenderung<Kalkulation>.Abgeschlossen();
        }

        // Zeile sicherstellen und sperren: Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var kalkulation = await db.KalkulationSperrenAsync(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, cancellationToken);
        return kalkulation.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new FinanzenAenderung<Kalkulation>.Gespeichert(kalkulation)
            : new FinanzenAenderung<Kalkulation>.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/finanzen/kalkulation</c> (Leitung und Team, Modul Finanzen, AD-19): Körper
/// <c>{ "version": n, "alterWert": …, "teilnehmende"|"mitarbeitende": neu }</c> mit genau einem Feld als Text. <c>200 { "version", "&lt;feld&gt;" }</c>;
/// keine ganze Zahl von 0 bis 1000, kein, mehr als ein oder ein unbekanntes Feld <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class KalkulationFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<KalkulationFeld>(koerper, KalkulationFelder.JsonName);
        return await sender.Send(new KalkulationFeldAendernCommand(freizeitId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            FinanzenAenderung<Kalkulation>.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Zeile.Version,
                [KalkulationFelder.JsonName(feld)] = gespeichert.Zeile.Wert(feld),
            }),
            FinanzenAenderung<Kalkulation>.Konflikt => FinanzenProbleme.Konflikt(),
            FinanzenAenderung<Kalkulation>.Abgeschlossen => FinanzenProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
