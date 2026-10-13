using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Freizeiten.Contracts.Dokumente;
using Zeltlotse.Freizeiten.Contracts.Team;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Daten;
using ApiSchutzkonzept = Zeltlotse.Api.Contracts.Sicherheit.Schutzkonzept;

namespace Zeltlotse.Sicherheit.Schutzkonzept;

/// <summary>
/// Story 2-9: Wählt das Dokument der Freizeit, das als Schutzkonzept gilt (AD-19, nur Leitung). Gibt es noch keine Zeile, legt der erste
/// Aufruf sie an. Konfliktregel gegen den wirksamen Wert (ein gelöschtes Dokument gilt als keins). Eine abgeschlossene Freizeit ist nur
/// lesbar (AD-17).
/// </summary>
internal sealed record SchutzkonzeptFeldAendernCommand(Guid FreizeitId, SchutzkonzeptFeld Feld, uint Version, Guid? AlterWert, Guid? NeuerWert)
    : ICommand<SchutzkonzeptAenderung>, IModulCommand<SicherheitDbContext>, IBenutzerCommand;

/// <summary>Ergebnis der Feldänderung; bei <see cref="Gespeichert"/> das verfolgte Schutzkonzept (nach dem Speichern mit neuer Version).</summary>
internal abstract record SchutzkonzeptAenderung
{
    private SchutzkonzeptAenderung()
    {
    }

    public sealed record Gespeichert(Schutzkonzept Schutzkonzept) : SchutzkonzeptAenderung;

    public sealed record Konflikt : SchutzkonzeptAenderung;

    public sealed record Abgeschlossen : SchutzkonzeptAenderung;

    /// <summary>Das neue Dokument gibt es in dieser Freizeit nicht.</summary>
    public sealed record UnbekanntesDokument : SchutzkonzeptAenderung;

    public sealed record NichtGefunden : SchutzkonzeptAenderung;
}

internal sealed class SchutzkonzeptFeldAendernValidator : AbstractValidator<SchutzkonzeptFeldAendernCommand>
{
    public SchutzkonzeptFeldAendernValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
        });
    }
}

internal sealed class SchutzkonzeptFeldAendernHandler(
    SicherheitDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitDokumenteLesen dokumente, TimeProvider uhr)
    : ICommandHandler<SchutzkonzeptFeldAendernCommand, SchutzkonzeptAenderung>
{
    public async ValueTask<SchutzkonzeptAenderung> Handle(SchutzkonzeptFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new SchutzkonzeptAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new SchutzkonzeptAenderung.Abgeschlossen();
        }

        if (command.NeuerWert is { } neu && await dokumente.InfoAsync(command.FreizeitId, neu, cancellationToken) is null)
        {
            return new SchutzkonzeptAenderung.UnbekanntesDokument();
        }

        // Erste Anlage wie bei der Sicherheitskette: eindeutig je Freizeit, danach die Zeilensperre (FOR UPDATE).
        var id = Guid.CreateVersion7(uhr.GetUtcNow());
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO sicherheit.schutzkonzept (\"Id\", \"OrganisationId\", \"FreizeitId\") VALUES ({id}, {organisationId}, {command.FreizeitId}) ON CONFLICT (\"FreizeitId\") DO NOTHING",
            cancellationToken);
        var konzept = (await db.Schutzkonzepte
                .FromSql($"SELECT *, xmin FROM sicherheit.schutzkonzept WHERE \"FreizeitId\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (konzept is null)
        {
            return new SchutzkonzeptAenderung.NichtGefunden();
        }

        var vorhanden = konzept.DokumentId is { } gespeichert && await dokumente.InfoAsync(command.FreizeitId, gespeichert, cancellationToken) is not null;
        return konzept.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert, vorhanden)
            ? new SchutzkonzeptAenderung.Gespeichert(konzept)
            : new SchutzkonzeptAenderung.Konflikt();
    }
}

/// <summary>
/// Story 2-9: Die Person bestätigt, das wirksame Schutzkonzept gelesen zu haben (Leitung und Team). Gilt nur für genau dieses Dokument; eine
/// zweite Bestätigung legt keine zweite Zeile an.
/// </summary>
internal sealed record SchutzkonzeptGelesenCommand(Guid FreizeitId, Guid DokumentId, Guid PersonId)
    : ICommand<SchutzkonzeptBestaetigen>, IModulCommand<SicherheitDbContext>, IBenutzerCommand
{
    /// <summary>Keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(SchutzkonzeptGelesenCommand)}({FreizeitId})";
}

/// <summary>Ergebnis einer Bestätigung.</summary>
internal enum SchutzkonzeptBestaetigen
{
    Bestaetigt,

    /// <summary>Kein wirksames Schutzkonzept oder ein anderes Dokument als das wirksame.</summary>
    Konflikt,

    Abgeschlossen,

    NichtGefunden,
}

internal sealed class SchutzkonzeptGelesenValidator : AbstractValidator<SchutzkonzeptGelesenCommand>
{
    public SchutzkonzeptGelesenValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.DokumentId == Guid.Empty)
            {
                kontext.AddFailure(new ValidationFailure(SchutzkonzeptFelder.DokumentId, "Das Dokument fehlt."));
            }

            if (c.PersonId == Guid.Empty)
            {
                kontext.AddFailure(new ValidationFailure("personId", "Die Person fehlt."));
            }
        });
    }
}

internal sealed class SchutzkonzeptGelesenHandler(
    SicherheitDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitDokumenteLesen dokumente, TimeProvider uhr)
    : ICommandHandler<SchutzkonzeptGelesenCommand, SchutzkonzeptBestaetigen>
{
    public async ValueTask<SchutzkonzeptBestaetigen> Handle(SchutzkonzeptGelesenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return SchutzkonzeptBestaetigen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return SchutzkonzeptBestaetigen.Abgeschlossen;
        }

        // Zeilensperre (FOR SHARE): Ein gleichzeitiger Austausch (PATCH mit FOR UPDATE) läuft davor oder danach, nie dazwischen.
        var gespeichert = (await db.Schutzkonzepte
                .FromSql($"SELECT *, xmin FROM sicherheit.schutzkonzept WHERE \"FreizeitId\" = {command.FreizeitId} FOR SHARE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault()?.DokumentId;
        if (gespeichert != command.DokumentId || await dokumente.InfoAsync(command.FreizeitId, command.DokumentId, cancellationToken) is null)
        {
            return SchutzkonzeptBestaetigen.Konflikt;
        }

        // Eindeutig je (Freizeit, Dokument, Person): Auch zwei gleichzeitige Bestätigungen ergeben genau eine Zeile.
        var jetzt = uhr.GetUtcNow();
        var id = Guid.CreateVersion7(jetzt);
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO sicherheit.schutzkonzept_bestaetigung ("Id", "OrganisationId", "FreizeitId", "DokumentId", "PersonId", "GelesenAm")
             VALUES ({id}, {organisationId}, {command.FreizeitId}, {command.DokumentId}, {command.PersonId}, {jetzt})
             ON CONFLICT ("FreizeitId", "DokumentId", "PersonId") DO NOTHING
             """,
            cancellationToken);
        return SchutzkonzeptBestaetigen.Bestaetigt;
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept</c> (Leitung und Team, FR-30): das wirksame Dokument mit Titel und Art aus
/// Freizeiten, das Team aus Freizeiten mit Namen aus Personen und je Person die Bestätigung des wirksamen Dokuments. Ohne Zeile
/// <c>Version 0</c>; ohne wirksames Dokument hat niemand gelesen.
/// </summary>
internal static class SchutzkonzeptLadenEndpunkt
{
    private static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    public static async Task<Ok<ApiSchutzkonzept>> Laden(
        Guid freizeitId, SicherheitLeseDbContext db, IFreizeitDokumenteLesen dokumente, IFreizeitTeamLesen teamLesen, IPersonenNamen namen,
        IAngemeldet angemeldet, CancellationToken abbruch)
    {
        var konzept = await db.Schutzkonzepte.SingleOrDefaultAsync(k => k.FreizeitId == freizeitId, abbruch);
        var info = konzept?.DokumentId is { } gespeichert ? await dokumente.InfoAsync(freizeitId, gespeichert, abbruch) : null;
        var gelesen = info is null
            ? new Dictionary<Guid, DateTimeOffset>()
            : await db.SchutzkonzeptBestaetigungen
                .Where(b => b.FreizeitId == freizeitId && b.DokumentId == info.Id)
                .ToDictionaryAsync(b => b.PersonId, b => b.GelesenAm, abbruch);
        var team = await teamLesen.TeamAsync(freizeitId, abbruch);
        var personen = team.Count == 0 ? new Dictionary<Guid, PersonenName>() : await namen.NamenAsync([.. team.Select(t => t.PersonId)], abbruch);
        var leser = team
            .Select(t => (Mitglied: t, Name: personen.GetValueOrDefault(t.PersonId)))
            .OrderBy(t => t.Name?.Nachname ?? "", Vergleich)
            .ThenBy(t => t.Name?.Vorname ?? "", Vergleich)
            .ThenBy(t => t.Mitglied.PersonId)
            .Select(t => new SchutzkonzeptLeser(
                t.Mitglied.PersonId, t.Name?.Vorname, t.Name?.Nachname, t.Mitglied.Leitung, gelesen.TryGetValue(t.Mitglied.PersonId, out var am) ? am : null))
            .ToList();
        DateTimeOffset? selbst = angemeldet.PersonId is { } person && gelesen.TryGetValue(person, out var selbstAm) ? selbstAm : null;
        return TypedResults.Ok(new ApiSchutzkonzept(info?.Id, info?.Titel, info?.Dateiart, selbst, leser, konzept?.Version ?? 0));
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept</c> (Leitung, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "dokumentId": neu }</c> mit der Kennung als Text oder <c>null</c>. <c>200 { "version", "dokumentId" }</c>; kein Kennungs-Text, kein, mehr
/// als ein oder ein unbekanntes Feld und ein Dokument, das es in der Freizeit nicht gibt, <c>400</c>; anderer wirksamer Wert <c>409</c> mit
/// Code <c>konflikt</c>; abgeschlossene Freizeit <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.
/// </summary>
internal static class SchutzkonzeptFeldAendernEndpunkt
{
    public const string UnbekanntesDokument = "Dieses Dokument gibt es in der Freizeit nicht.";

    /// <summary>Erlaubte Eigenschaften neben dem Feld.</summary>
    private static readonly string[] Rahmen = ["version", "alterWert"];

    public static async Task<IResult> Aendern(Guid freizeitId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new SchutzkonzeptFeldAendernCommand(freizeitId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            SchutzkonzeptAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Schutzkonzept.Version,
                [SchutzkonzeptFelder.JsonName(feld)] = gespeichert.Schutzkonzept.DokumentId,
            }),
            SchutzkonzeptAenderung.UnbekanntesDokument => throw Fehler(SchutzkonzeptFelder.JsonName(feld), UnbekanntesDokument),
            SchutzkonzeptAenderung.Konflikt => SchutzkonzeptProbleme.Problem(Problemcodes.Konflikt, "Der Wert wurde inzwischen geändert."),
            SchutzkonzeptAenderung.Abgeschlossen => SchutzkonzeptProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (uint Version, Guid? AlterWert, SchutzkonzeptFeld Feld, Guid? NeuerWert) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw Fehler("version", "Der Körper ist kein JSON-Objekt.");
        }

        if (!koerper.TryGetProperty("version", out var versionWert) || versionWert.ValueKind != JsonValueKind.Number || !versionWert.TryGetUInt32(out var version))
        {
            throw Fehler("version", "Version fehlt.");
        }

        var alterWert = koerper.TryGetProperty("alterWert", out var alt) ? Kennung(alt, "alterWert") : null;
        var felder = Enum.GetValues<SchutzkonzeptFeld>();
        var genannt = koerper.EnumerateObject().Select(e => e.Name).Where(n => !Rahmen.Contains(n)).ToList();
        if (genannt.Count != 1 || felder.Where(f => SchutzkonzeptFelder.JsonName(f) == genannt[0]).ToList() is not [var feld])
        {
            throw Fehler("feld", $"Genau ein Feld: {string.Join(", ", felder.Select(SchutzkonzeptFelder.JsonName))}.");
        }

        var name = SchutzkonzeptFelder.JsonName(feld);
        return (version, alterWert, feld, Kennung(koerper.GetProperty(name), name));
    }

    /// <summary>Eine Kennung als Text; leerer Text oder <c>null</c> heißt „kein Schutzkonzept“.</summary>
    private static Guid? Kennung(JsonElement wert, string name) => wert.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when string.IsNullOrWhiteSpace(wert.GetString()) => null,
        JsonValueKind.String when Guid.TryParse(wert.GetString(), out var kennung) => kennung,
        _ => throw Fehler(name, "Nur die Kennung eines Dokuments oder null."),
    };

    private static ValidierungsAusnahme Fehler(string feld, string text) =>
        new(new Dictionary<string, string[]> { [feld] = [text] });
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept/gelesen</c> (Leitung und Team, FR-30): Körper <c>{ "dokumentId" }</c>; die
/// Person ist die angemeldete. <c>204</c>, auch wenn schon bestätigt; kein wirksames oder ein anderes Dokument <c>409 konflikt</c>; abgeschlossene
/// Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class SchutzkonzeptGelesenEndpunkt
{
    public static async Task<IResult> Bestaetigen(Guid freizeitId, SchutzkonzeptGelesen koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        return await sender.Send(new SchutzkonzeptGelesenCommand(freizeitId, koerper.DokumentId, personId), abbruch) switch
        {
            SchutzkonzeptBestaetigen.Bestaetigt => TypedResults.NoContent(),
            SchutzkonzeptBestaetigen.Konflikt => SchutzkonzeptProbleme.Problem(Problemcodes.Konflikt, "Das Schutzkonzept wurde inzwischen ausgetauscht."),
            SchutzkonzeptBestaetigen.Abgeschlossen => SchutzkonzeptProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}

internal static class SchutzkonzeptProbleme
{
    public static ProblemHttpResult Problem(string code, string titel) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: titel,
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = code });

    public static ProblemHttpResult Abgeschlossen() => Problem(Problemcodes.FreizeitAbgeschlossen, "Diese Freizeit ist abgeschlossen und nur lesbar.");
}
