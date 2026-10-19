using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.Programm.Contracts.Mahlzeiten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kueche.Einkauf;

// ---------- Vormerken ----------

/// <summary>
/// Story 4-2, FR-47: Merkt ein Rezept der Freizeit an einer Mahlzeit vor (Leitung und Team, Modul Verpflegung). Eine ausdrückliche Handlung; die
/// Einkaufsliste rechnet danach von selbst. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record GerichtVormerkenCommand(Guid FreizeitId, Guid? ProgrammpunktId, Guid? RezeptId)
    : ICommand<GerichtVormerken>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="GerichtVormerkenCommand"/>; bei <see cref="Angelegt"/> das verfolgte Gericht (nach dem Speichern mit Version).</summary>
internal abstract record GerichtVormerken
{
    private GerichtVormerken()
    {
    }

    public sealed record Angelegt(Gericht Gericht) : GerichtVormerken;

    public sealed record Abgeschlossen : GerichtVormerken;

    public sealed record NichtGefunden : GerichtVormerken;
}

internal sealed class GerichtVormerkenValidator : AbstractValidator<GerichtVormerkenCommand>
{
    public GerichtVormerkenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.ProgrammpunktId is null)
            {
                kontext.AddFailure(new ValidationFailure("programmpunktId", "Mahlzeit fehlt."));
            }

            if (c.RezeptId is null)
            {
                kontext.AddFailure(new ValidationFailure("rezeptId", "Rezept fehlt."));
            }
        });
}

internal sealed class GerichtVormerkenHandler(
    KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitMahlzeitenLesen mahlzeiten, TimeProvider uhr)
    : ICommandHandler<GerichtVormerkenCommand, GerichtVormerken>
{
    public async ValueTask<GerichtVormerken> Handle(GerichtVormerkenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new GerichtVormerken.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new GerichtVormerken.Abgeschlossen();
        }

        // Nur eine Mahlzeit dieser Freizeit (kein normaler Programmpunkt, keiner einer anderen Freizeit); Mahlzeiten lassen sich nicht löschen.
        if ((await mahlzeiten.MahlzeitenAsync(command.FreizeitId, cancellationToken)).All(m => m.ProgrammpunktId != command.ProgrammpunktId))
        {
            throw GerichtFehler.MahlzeitUnbekannt();
        }

        // Zeilensperre am Rezept: Ein gleichzeitiges Löschen des Rezepts wartet (und nimmt das Gericht mit) oder lässt hier nichts mehr übrig.
        var rezept = await db.RezeptSperrenAsync(command.FreizeitId, command.RezeptId!.Value, cancellationToken) ?? throw GerichtFehler.RezeptUnbekannt();
        var gericht = Gericht.Vormerken(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, command.ProgrammpunktId!.Value, rezept.Id);
        db.Gerichte.Add(gericht);
        return new GerichtVormerken.Angelegt(gericht);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kueche/gerichte</c> (Leitung und Team, Modul Verpflegung): Körper <see cref="GerichtNeu"/>. <c>201</c> mit
/// <see cref="GerichtStand"/>; keine Mahlzeit dieser Freizeit <c>400</c> am Feld <c>programmpunktId</c>, kein Rezept dieser Freizeit <c>400</c> am
/// Feld <c>rezeptId</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class GerichtVormerkenEndpunkt
{
    public static async Task<IResult> Vormerken(Guid freizeitId, GerichtNeu koerper, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new GerichtVormerkenCommand(freizeitId, koerper.ProgrammpunktId, koerper.RezeptId), abbruch) switch
        {
            GerichtVormerken.Angelegt angelegt => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/kueche/essensplan",
                new GerichtStand(angelegt.Gericht.Id, angelegt.Gericht.Version, angelegt.Gericht.ProgrammpunktId, angelegt.Gericht.RezeptId)),
            GerichtVormerken.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}

// ---------- Entfernen ----------

/// <summary>Story 4-2: Entfernt ein Gericht samt Hand-Mengen (Leitung und Team, Modul Verpflegung). Eine abgeschlossene Freizeit ist nur lesbar.</summary>
internal sealed record GerichtEntfernenCommand(Guid FreizeitId, Guid GerichtId)
    : ICommand<KuecheLoeschen>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

internal sealed class GerichtEntfernenHandler(KuecheDbContext db, IFreizeitZeitpunkte zeitpunkte) : ICommandHandler<GerichtEntfernenCommand, KuecheLoeschen>
{
    public async ValueTask<KuecheLoeschen> Handle(GerichtEntfernenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return KuecheLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return KuecheLoeschen.Abgeschlossen;
        }

        var gericht = await db.GerichtSperrenAsync(command.FreizeitId, command.GerichtId, cancellationToken);
        if (gericht is null)
        {
            return KuecheLoeschen.NichtGefunden;
        }

        db.Gerichte.Remove(gericht);
        return KuecheLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/kueche/gerichte/{gerichtId}</c> (Leitung und Team, Modul Verpflegung): <c>204</c> (die Hand-Mengen gehen per
/// Cascade mit); abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class GerichtEntfernenEndpunkt
{
    public static async Task<IResult> Entfernen(Guid freizeitId, Guid gerichtId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new GerichtEntfernenCommand(freizeitId, gerichtId), abbruch) switch
        {
            KuecheLoeschen.Geloescht => TypedResults.NoContent(),
            KuecheLoeschen.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}

// ---------- Tauschen ----------

/// <summary>
/// Story 4-2, FR-50, AD-13: Übernimmt den Tausch des Rezepts eines Gerichts, den die Vorschau gezeigt hat; das ist die Bestätigung. Die Hand-Mengen
/// wandern nach <see cref="EinkaufRechnen.TauschAbbilden"/>, die übrigen entfallen. Ist die Version veraltet (z. B. inzwischen getauscht), ist das
/// Ergebnis <see cref="GerichtTausch.Konflikt"/>, und nichts ändert sich.
/// </summary>
internal sealed record GerichtTauschenCommand(Guid FreizeitId, Guid GerichtId, uint Version, Guid? RezeptId)
    : ICommand<GerichtTausch>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="GerichtTauschenCommand"/>; bei <see cref="Getauscht"/> das verfolgte Gericht (nach dem Speichern mit neuer Version).</summary>
internal abstract record GerichtTausch
{
    private GerichtTausch()
    {
    }

    public sealed record Getauscht(Gericht Gericht) : GerichtTausch;

    public sealed record Konflikt : GerichtTausch;

    public sealed record Abgeschlossen : GerichtTausch;

    public sealed record NichtGefunden : GerichtTausch;
}

internal sealed class GerichtTauschenValidator : AbstractValidator<GerichtTauschenCommand>
{
    public GerichtTauschenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.RezeptId is null)
            {
                kontext.AddFailure(new ValidationFailure("rezeptId", "Rezept fehlt."));
            }
        });
}

internal sealed class GerichtTauschenHandler(KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<GerichtTauschenCommand, GerichtTausch>
{
    public async ValueTask<GerichtTausch> Handle(GerichtTauschenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new GerichtTausch.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new GerichtTausch.Abgeschlossen();
        }

        // Zeilensperre: Zwei Tausche desselben Gerichts laufen nacheinander; der zweite sieht die neue Version und endet im Konflikt.
        var gericht = await db.GerichtSperrenAsync(command.FreizeitId, command.GerichtId, cancellationToken);
        if (gericht is null)
        {
            return new GerichtTausch.NichtGefunden();
        }

        var neues = await db.RezeptSperrenAsync(command.FreizeitId, command.RezeptId!.Value, cancellationToken) ?? throw GerichtFehler.RezeptUnbekannt();
        if (gericht.Version != command.Version)
        {
            return new GerichtTausch.Konflikt();
        }

        if (neues.Id == gericht.RezeptId)
        {
            return new GerichtTausch.Getauscht(gericht);
        }

        var alteZutaten = await db.Zutaten.Where(z => z.RezeptId == gericht.RezeptId).OrderBy(z => z.Id).ToListAsync(cancellationToken);
        var neueZutaten = await db.Zutaten.Where(z => z.RezeptId == neues.Id).OrderBy(z => z.Id).ToListAsync(cancellationToken);
        var handMengen = await db.MengenVonHand.Where(h => h.GerichtId == gericht.Id).ToListAsync(cancellationToken);
        var abgebildet = EinkaufRechnen.TauschAbbilden(alteZutaten, handMengen, neueZutaten);
        db.MengenVonHand.RemoveRange(handMengen);
        var jetzt = uhr.GetUtcNow();
        foreach (var (zutatId, menge) in abgebildet)
        {
            db.MengenVonHand.Add(MengeVonHand.Anlegen(Guid.CreateVersion7(jetzt), organisationId, command.FreizeitId, gericht.Id, zutatId, menge));
        }

        gericht.Tauschen(neues.Id);
        return new GerichtTausch.Getauscht(gericht);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kueche/gerichte/{gerichtId}/tauschen</c> (Leitung und Team, Modul Verpflegung, AD-13): Körper
/// <see cref="GerichtTauschen"/> mit der Version aus der Vorschau. <c>200</c> mit <see cref="GerichtStand"/>; Version veraltet <c>409 konflikt</c>; ohne
/// oder mit fremdem Rezept <c>400</c> am Feld <c>rezeptId</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; Gericht unbekannt oder aus
/// einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class GerichtTauschenEndpunkt
{
    public static async Task<IResult> Tauschen(Guid freizeitId, Guid gerichtId, GerichtTauschen koerper, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new GerichtTauschenCommand(freizeitId, gerichtId, koerper.Version, koerper.RezeptId), abbruch) switch
        {
            GerichtTausch.Getauscht getauscht => TypedResults.Ok(
                new GerichtStand(getauscht.Gericht.Id, getauscht.Gericht.Version, getauscht.Gericht.ProgrammpunktId, getauscht.Gericht.RezeptId)),
            GerichtTausch.Konflikt => KuecheProbleme.Problem(Zeltlotse.Api.Contracts.Gemeinsam.Problemcodes.Konflikt, "Das Gericht wurde inzwischen geändert."),
            GerichtTausch.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
