using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1 (FR-54): Legt einen Einnahmeposten der Kalkulation an (Leitung und Team, Modul Finanzen). Die Bezeichnung ist Pflicht, der Betrag beginnt
/// ohne Angabe bei 0. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record EinnahmeAnlegenCommand(Guid FreizeitId, string? Bezeichnung, string? Betrag)
    : ICommand<EinnahmeAnlegen>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

/// <summary>Ergebnis von <see cref="EinnahmeAnlegenCommand"/>; bei <see cref="Angelegt"/> der verfolgte Posten (nach dem Speichern mit Version).</summary>
internal abstract record EinnahmeAnlegen
{
    private EinnahmeAnlegen()
    {
    }

    public sealed record Angelegt(Einnahmeposten Posten) : EinnahmeAnlegen;

    public sealed record Abgeschlossen : EinnahmeAnlegen;

    public sealed record NichtGefunden : EinnahmeAnlegen;
}

internal sealed class EinnahmeAnlegenValidator : AbstractValidator<EinnahmeAnlegenCommand>
{
    public EinnahmeAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (FinanzenWerte.PflichtFehler(c.Bezeichnung, EinnahmepostenFelder.BezeichnungMaximaleLaenge, "Bezeichnung fehlt.") is { } bezeichnungFehler)
            {
                kontext.AddFailure(new ValidationFailure(EinnahmepostenFelder.Bezeichnung, bezeichnungFehler));
            }

            if (FinanzenWerte.TextOderNull(c.Betrag) is not null && FinanzenWerte.BetragFehler(c.Betrag) is { } betragFehler)
            {
                kontext.AddFailure(new ValidationFailure(EinnahmepostenFelder.Betrag, betragFehler));
            }
        });
}

internal sealed class EinnahmeAnlegenHandler(FinanzenDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<EinnahmeAnlegenCommand, EinnahmeAnlegen>
{
    public async ValueTask<EinnahmeAnlegen> Handle(EinnahmeAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new EinnahmeAnlegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new EinnahmeAnlegen.Abgeschlossen();
        }

        var letzte = await db.Einnahmeposten.Where(e => e.FreizeitId == command.FreizeitId).OrderByDescending(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);
        var posten = Einnahmeposten.Anlegen(
            NeueKennung(uhr.GetUtcNow(), letzte), organisationId, command.FreizeitId, command.Bezeichnung!, FinanzenWerte.BetragLesen(command.Betrag) ?? 0m);
        db.Einnahmeposten.Add(posten);
        return new EinnahmeAnlegen.Angelegt(posten);
    }

    /// <summary>
    /// Eine UUID v7, die nach dem bisher letzten Posten der Freizeit liegt: Die Reihenfolge der Einnahmen ist die ihrer Kennungen, auch wenn zwei Posten
    /// in derselben Millisekunde entstehen (Muster aus der Küche).
    /// </summary>
    internal static Guid NeueKennung(DateTimeOffset jetzt, Guid? letzte)
    {
        var kennung = Guid.CreateVersion7(jetzt);
        if (letzte is not { } bisher || kennung.CompareTo(bisher) > 0)
        {
            return kennung;
        }

        var bytes = bisher.ToByteArray(bigEndian: true);
        long millisekunden = 0;
        for (var i = 0; i < 6; i++)
        {
            millisekunden = (millisekunden << 8) | bytes[i];
        }

        return Guid.CreateVersion7(DateTimeOffset.FromUnixTimeMilliseconds(millisekunden + 1));
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/finanzen/kalkulation/einnahmen</c> (Leitung und Team, Modul Finanzen): Körper <see cref="EinnahmepostenNeu"/>.
/// <c>201</c> mit dem Posten (mit Version; <c>Location</c> ist die Kalkulation); Bezeichnung fehlt oder zu lang, Betrag ungültig <c>400</c> am Feld;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class EinnahmeAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid freizeitId, EinnahmepostenNeu koerper, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinnahmeAnlegenCommand(freizeitId, koerper.Bezeichnung, koerper.Betrag), abbruch) switch
        {
            EinnahmeAnlegen.Angelegt angelegt => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/finanzen/kalkulation", KalkulationLadenEndpunkt.AlsApi(angelegt.Posten)),
            EinnahmeAnlegen.Abgeschlossen => FinanzenProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
