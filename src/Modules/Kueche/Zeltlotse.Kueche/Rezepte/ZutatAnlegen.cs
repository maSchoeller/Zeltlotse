using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// Story 4-1: Fügt einem Rezept eine Zutat hinzu (Leitung und Team, Modul Verpflegung, FR-46). Nur die Bezeichnung ist Pflicht; Menge und Einheit
/// sind freiwillig, die Allergene folgen per Autosave. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ZutatAnlegenCommand(Guid FreizeitId, Guid RezeptId, string? Bezeichnung, string? Menge, string? Einheit)
    : ICommand<ZutatAnlegen>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="ZutatAnlegenCommand"/>; bei <see cref="Angelegt"/> die verfolgte Zutat (nach dem Speichern mit Version) und ihr Rezept.</summary>
internal abstract record ZutatAnlegen
{
    private ZutatAnlegen()
    {
    }

    public sealed record Angelegt(Zutat Zutat, Rezept Rezept) : ZutatAnlegen;

    public sealed record Abgeschlossen : ZutatAnlegen;

    public sealed record NichtGefunden : ZutatAnlegen;
}

internal sealed class ZutatAnlegenValidator : AbstractValidator<ZutatAnlegenCommand>
{
    public ZutatAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (KuecheWerte.PflichtFehler(c.Bezeichnung, ZutatFelder.BezeichnungMaximaleLaenge, "Bezeichnung fehlt.") is { } bezeichnungFehler)
            {
                kontext.AddFailure(new ValidationFailure(ZutatFelder.Bezeichnung, bezeichnungFehler));
            }

            if (KuecheWerte.MengeFehler(c.Menge) is { } mengeFehler)
            {
                kontext.AddFailure(new ValidationFailure(ZutatFelder.Menge, mengeFehler));
            }

            if (KuecheWerte.LaengeFehler(c.Einheit, ZutatFelder.EinheitMaximaleLaenge) is { } einheitFehler)
            {
                kontext.AddFailure(new ValidationFailure(ZutatFelder.Einheit, einheitFehler));
            }
        });
}

internal sealed class ZutatAnlegenHandler(KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<ZutatAnlegenCommand, ZutatAnlegen>
{
    public async ValueTask<ZutatAnlegen> Handle(ZutatAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new ZutatAnlegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new ZutatAnlegen.Abgeschlossen();
        }

        // Zeilensperre am Rezept: Zutaten desselben Rezepts entstehen nacheinander, ein gleichzeitiges Löschen des Rezepts wartet (und nimmt sie mit).
        var rezept = await db.RezeptSperrenAsync(command.FreizeitId, command.RezeptId, cancellationToken);
        if (rezept is null)
        {
            return new ZutatAnlegen.NichtGefunden();
        }

        var letzte = await db.Zutaten.Where(z => z.RezeptId == rezept.Id).OrderByDescending(z => z.Id).Select(z => (Guid?)z.Id).FirstOrDefaultAsync(cancellationToken);
        var zutat = Zutat.Anlegen(
            NeueKennung(uhr.GetUtcNow(), letzte), organisationId, command.FreizeitId, rezept.Id, command.Bezeichnung!, KuecheWerte.MengeLesen(command.Menge), command.Einheit);
        db.Zutaten.Add(zutat);
        return new ZutatAnlegen.Angelegt(zutat, rezept);
    }

    /// <summary>
    /// Eine UUID v7, die nach der bisher letzten Zutat des Rezepts liegt: Die Reihenfolge der Zutaten ist die ihrer Kennungen, auch wenn zwei Zutaten
    /// in derselben Millisekunde entstehen.
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
/// <c>POST /api/freizeiten/{freizeitId}/kueche/rezepte/{rezeptId}/zutaten</c> (Leitung und Team, Modul Verpflegung): Körper <see cref="ZutatNeu"/>.
/// <c>201</c> mit der Zutat (mit Version und hochgerechneter Menge; <c>Location</c> ist das Rezept); Bezeichnung fehlt oder zu lang, Menge
/// ungültig, Einheit zu lang <c>400</c> am Feld; Rezept unbekannt oder aus einer anderen Freizeit <c>404</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class ZutatAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(
        Guid freizeitId, Guid rezeptId, ZutatNeu koerper, ISender sender, IFreizeitPersonenzahlLesen personenzahl, CancellationToken abbruch) =>
        await sender.Send(new ZutatAnlegenCommand(freizeitId, rezeptId, koerper.Bezeichnung, koerper.Menge, koerper.Einheit), abbruch) switch
        {
            ZutatAnlegen.Angelegt angelegt => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/kueche/rezepte/{rezeptId}",
                KuecheLesen.AlsApi(angelegt.Zutat, angelegt.Rezept.BasisPersonen, await personenzahl.AnzahlAsync(freizeitId, abbruch))),
            ZutatAnlegen.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
