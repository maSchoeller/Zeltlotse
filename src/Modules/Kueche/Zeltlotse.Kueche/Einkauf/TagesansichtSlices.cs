using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;
using ApiGericht = Zeltlotse.Api.Contracts.Kueche.Gericht;
using ApiTagesansicht = Zeltlotse.Api.Contracts.Kueche.Tagesansicht;

namespace Zeltlotse.Kueche.Einkauf;

// ---------- Tagesansicht lesen ----------

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/kueche/tagesansicht?tag=JJJJ-MM-TT</c> (Leitung und Team, Modul Verpflegung, Story 4-4, FR-52): die Mahlzeiten
/// eines Tags mit ihren Gerichten und Ersatzportionen (wie im Essensplan), je Mahlzeit der Restetag, dazu die Allergen- und die Sonderkost-Übersicht nur
/// als Anzahlen. Ohne <c>tag</c>: heute (Europe/Berlin), wenn heute ein Tag der Freizeit ist, sonst der erste Tag. Allergien und Sonderkost liest
/// Personen nur, wenn die Freizeit die Frage abfragt, und protokolliert dann je Zweck ohne lesende Person. Auch nach dem Abschluss lesbar. <c>tag</c> kein
/// Datum oder außerhalb der Freizeit <c>400</c> am Feld <c>tag</c>.
/// </summary>
internal static class TagesansichtLadenEndpunkt
{
    public static async Task<Results<Ok<ApiTagesansicht>, NotFound>> Laden(
        Guid freizeitId, [FromQuery] string? tag, EinkaufAbfrage abfrage, IFreizeitTageLesen tageLesen, KuecheLeseDbContext db, TimeProvider uhr,
        CancellationToken abbruch)
    {
        if (!(await tageLesen.TageAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var tage))
        {
            return TypedResults.NotFound();
        }

        var gewaehlt = TagWaehlen(tag, tage, uhr.GetUtcNow());
        var mahlzeiten = (await abfrage.MahlzeitenAsync(freizeitId, abbruch)).Where(m => m.Tag == gewaehlt).ToList();
        var gerichte = (await abfrage.GerichteAsync(freizeitId, mahlzeiten, abbruch)).ToLookup(g => g.Mahlzeit.ProgrammpunktId);
        var punkte = mahlzeiten.Select(m => m.ProgrammpunktId).ToList();
        var staende = await db.MahlzeitStaende.Where(s => s.FreizeitId == freizeitId && punkte.Contains(s.ProgrammpunktId)).ToDictionaryAsync(s => s.ProgrammpunktId, abbruch);
        var allergien = await abfrage.AbgleichAsync(freizeitId, abbruch);
        var sonderkost = await abfrage.SonderkostAsync(freizeitId, abbruch);
        return TypedResults.Ok(new ApiTagesansicht(
            gewaehlt,
            [.. tage.Alle()],
            await abfrage.PersonenzahlAsync(freizeitId, abbruch),
            allergien.AllergienAbgefragt,
            EinkaufRechnen.Uebersicht(allergien.Profile),
            sonderkost.AllergienAbgefragt,
            EinkaufRechnen.SonderkostUebersicht(sonderkost.Profile),
            [
                .. mahlzeiten.Select(m => new TagesansichtMahlzeit(
                    m.ProgrammpunktId,
                    m.Beginn?.ToString("HH:mm", CultureInfo.InvariantCulture),
                    m.Titel,
                    (MahlzeitArt)(int)m.MahlzeitArt,
                    staende.GetValueOrDefault(m.ProgrammpunktId)?.Restetag ?? false,
                    staende.GetValueOrDefault(m.ProgrammpunktId)?.Version ?? 0u,
                    [
                        .. gerichte[m.ProgrammpunktId].Select(g => new ApiGericht(
                            g.Gericht.Id, g.Gericht.Version, g.Rezept.Id, g.Rezept.Name, EinkaufRechnen.Ersatz(allergien.Profile, g.Zutaten))),
                    ])),
            ]));
    }

    /// <summary>Der gewählte Tag; ohne Wahl heute oder der erste Tag. Kein Datum oder ein Tag außerhalb der Freizeit ist ein Feldfehler am Feld <c>tag</c>.</summary>
    private static DateOnly TagWaehlen(string? tag, FreizeitTage tage, DateTimeOffset jetzt)
    {
        if (KuecheWerte.TextOderNull(tag) is null)
        {
            return KuecheWerte.TagOhneWahl(tage, jetzt);
        }

        if (KuecheWerte.TagLesen(tag) is not { } wert)
        {
            throw new ValidierungsAusnahme(new Dictionary<string, string[]> { [EinkaufspostenFelder.Tag] = [KuecheWerte.TagFehlerText] });
        }

        return tage.Enthaelt(wert) ? wert : throw GerichtFehler.TagAusserhalb();
    }
}

// ---------- Restetag ----------

/// <summary>
/// Story 4-4, CAP-31, FR-53: Markiert eine Mahlzeit als Restetag oder nimmt die Markierung zurück (Leitung und Team, Modul Verpflegung). Konfliktregel nach
/// AD-19: Der alte Wert ist der aktuelle (<c>"true"</c>/<c>"false"</c>, ohne Stand <c>"false"</c>); weicht er ab, ist das Ergebnis
/// <see cref="RestetagAenderung.Konflikt"/>. Eine reine Markierung: Gerichte, Einkaufsliste und Einkauf bleiben, wie sie sind.
/// </summary>
internal sealed record RestetagAendernCommand(Guid FreizeitId, Guid ProgrammpunktId, MahlzeitFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<RestetagAenderung>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="RestetagAendernCommand"/>; bei <see cref="Gespeichert"/> der verfolgte Stand (nach dem Speichern mit Version).</summary>
internal abstract record RestetagAenderung
{
    private RestetagAenderung()
    {
    }

    public sealed record Gespeichert(MahlzeitStand Stand) : RestetagAenderung;

    public sealed record Konflikt : RestetagAenderung;

    public sealed record Abgeschlossen : RestetagAenderung;

    public sealed record NichtGefunden : RestetagAenderung;
}

internal sealed class RestetagAendernValidator : AbstractValidator<RestetagAendernCommand>
{
    public RestetagAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            if (KuecheWerte.JaNeinFehler(c.NeuerWert) is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(MahlzeitFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class RestetagAendernHandler(KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, EinkaufAbfrage abfrage, TimeProvider uhr)
    : ICommandHandler<RestetagAendernCommand, RestetagAenderung>
{
    public async ValueTask<RestetagAenderung> Handle(RestetagAendernCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new RestetagAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new RestetagAenderung.Abgeschlossen();
        }

        // Nur eine Mahlzeit dieser Freizeit (Programm, AD-2); ein normaler, fremder oder unbekannter Programmpunkt ist nicht da.
        if ((await abfrage.MahlzeitenAsync(command.FreizeitId, cancellationToken)).All(m => m.ProgrammpunktId != command.ProgrammpunktId))
        {
            return new RestetagAenderung.NichtGefunden();
        }

        var stand = await db.MahlzeitStandSperrenAsync(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, command.ProgrammpunktId, cancellationToken);
        return stand.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new RestetagAenderung.Gespeichert(stand)
            : new RestetagAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/kueche/mahlzeiten/{programmpunktId}</c> (Leitung und Team, Modul Verpflegung, Story 4-4, AD-19): Körper
/// <c>{ "version": n, "alterWert": "true"|"false", "restetag": "true"|"false" }</c>. <c>200 { "version", "restetag" }</c>; anderer Wert als
/// <c>"true"</c>/<c>"false"</c> <c>400</c> am Feld <c>restetag</c>; anderer aktueller Wert <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; keine Mahlzeit dieser Freizeit <c>404</c>.
/// </summary>
internal static class RestetagAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid programmpunktId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<MahlzeitFeld>(koerper, MahlzeitFelder.JsonName);
        return await sender.Send(new RestetagAendernCommand(freizeitId, programmpunktId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            RestetagAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Stand.Version,
                [MahlzeitFelder.JsonName(feld)] = MahlzeitStand.Wert(gespeichert.Stand, feld),
            }),
            RestetagAenderung.Konflikt => KuecheProbleme.Konflikt(),
            RestetagAenderung.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
