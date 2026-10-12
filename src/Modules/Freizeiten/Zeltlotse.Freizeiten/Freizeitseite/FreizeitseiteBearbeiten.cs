using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.Freizeitseite;

/// <summary>
/// Ändert genau ein Feld der Freizeitseite (AD-19, Leitung): Beschreibung, Ort, Höchstzahl Teilnehmende oder Teilnahmebedingungen. Konfliktregel: Weicht
/// der aktuelle Wert desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="FreizeitseiteAenderung.Konflikt"/>.
/// Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record FreizeitseiteFeldAendernCommand(Guid FreizeitId, FreizeitseiteFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<FreizeitseiteAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand
{
    /// <summary>Die Werte nicht ausgeben; eine Beschreibung kann Namen enthalten (AD-22).</summary>
    public override string ToString() => $"{nameof(FreizeitseiteFeldAendernCommand)}({FreizeitId}, {Feld})";
}

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> die verfolgte Freizeit (nach dem Speichern mit neuer Version).</summary>
internal abstract record FreizeitseiteAenderung
{
    private FreizeitseiteAenderung()
    {
    }

    public sealed record Gespeichert(Freizeit Freizeit) : FreizeitseiteAenderung;

    public sealed record Konflikt : FreizeitseiteAenderung;

    public sealed record NichtGefunden : FreizeitseiteAenderung;

    public sealed record Abgeschlossen : FreizeitseiteAenderung;
}

internal sealed class FreizeitseiteFeldAendernValidator : AbstractValidator<FreizeitseiteFeldAendernCommand>
{
    public FreizeitseiteFeldAendernValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            var wert = Freizeit.Normalisieren(c.NeuerWert);
            var name = FreizeitseiteFelder.JsonName(c.Feld);
            switch (c.Feld)
            {
                case FreizeitseiteFeld.Beschreibung when wert is { Length: > FreizeitseiteFelder.BeschreibungMaximaleLaenge }:
                    kontext.AddFailure(new ValidationFailure(name, $"Höchstens {FreizeitseiteFelder.BeschreibungMaximaleLaenge} Zeichen."));
                    break;
                case FreizeitseiteFeld.Teilnahmebedingungen when wert is { Length: > FreizeitseiteFelder.TeilnahmebedingungenMaximaleLaenge }:
                    kontext.AddFailure(new ValidationFailure(name, $"Höchstens {FreizeitseiteFelder.TeilnahmebedingungenMaximaleLaenge} Zeichen."));
                    break;
                case FreizeitseiteFeld.KontaktMail when wert is { Length: > FreizeitseiteFelder.KontaktMailMaximaleLaenge }:
                    kontext.AddFailure(new ValidationFailure(name, $"Höchstens {FreizeitseiteFelder.KontaktMailMaximaleLaenge} Zeichen."));
                    break;
                case FreizeitseiteFeld.KontaktMail when wert is not null && !MailAdresse.IstGueltig(wert):
                    kontext.AddFailure(new ValidationFailure(name, "Das ist keine gültige Mail-Adresse."));
                    break;
                case FreizeitseiteFeld.KontaktTelefon when wert is not null && !wert.Any(char.IsAsciiDigit):
                    kontext.AddFailure(new ValidationFailure(name, "Die Telefonnummer braucht mindestens eine Ziffer."));
                    break;
                case FreizeitseiteFeld.KontaktTelefon when wert is { Length: > FreizeitseiteFelder.KontaktTelefonMaximaleLaenge }:
                    kontext.AddFailure(new ValidationFailure(name, $"Höchstens {FreizeitseiteFelder.KontaktTelefonMaximaleLaenge} Zeichen."));
                    break;
                case FreizeitseiteFeld.Ort when wert is { Length: > FreizeitseiteFelder.OrtMaximaleLaenge }:
                    kontext.AddFailure(new ValidationFailure(name, $"Höchstens {FreizeitseiteFelder.OrtMaximaleLaenge} Zeichen."));
                    break;
                case FreizeitseiteFeld.HoechstzahlTeilnehmende when wert is not null
                                                                    && (!int.TryParse(wert, NumberStyles.None, CultureInfo.InvariantCulture, out var zahl)
                                                                        || zahl is < FreizeitseiteFelder.HoechstzahlMindestens or > FreizeitseiteFelder.HoechstzahlHoechstens):
                    kontext.AddFailure(new ValidationFailure(
                        name, $"Die Höchstzahl muss leer sein oder zwischen {FreizeitseiteFelder.HoechstzahlMindestens} und {FreizeitseiteFelder.HoechstzahlHoechstens} liegen."));
                    break;
            }
        });
    }
}

internal sealed class FreizeitseiteFeldAendernHandler(FreizeitenDbContext db, TimeProvider uhr)
    : ICommandHandler<FreizeitseiteFeldAendernCommand, FreizeitseiteAenderung>
{
    public async ValueTask<FreizeitseiteAenderung> Handle(FreizeitseiteFeldAendernCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE) unter dem Mandantenfilter: Gleichzeitige Änderungen laufen nacheinander, allein die Regel zum
        // alten Wert entscheidet.
        var freizeit = (await db.Freizeiten
                .FromSql($"SELECT *, xmin FROM freizeiten.freizeit WHERE \"Id\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (freizeit is null)
        {
            return new FreizeitseiteAenderung.NichtGefunden();
        }

        if (freizeit.Phase(uhr.GetUtcNow()) == FreizeitPhase.Gelaufen)
        {
            return new FreizeitseiteAenderung.Abgeschlossen();
        }

        return freizeit.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new FreizeitseiteAenderung.Gespeichert(freizeit)
            : new FreizeitseiteAenderung.Konflikt();
    }
}

/// <summary>Antworten der Einstellungs-Endpunkte für eine abgeschlossene Freizeit und einen Konflikt.</summary>
internal static class FreizeitseiteProbleme
{
    public static ProblemHttpResult Abgeschlossen() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Diese Freizeit ist abgeschlossen und nur lesbar.",
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.FreizeitAbgeschlossen });

    public static ProblemHttpResult Konflikt() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Der Wert wurde inzwischen geändert.",
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt });
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/freizeitseite</c> (Leitung): Beschreibung, Ort und Höchstzahl mit Version (AD-19) und die
/// beiden Anmeldelinks mit Adresse (Teilnehmende zuerst). Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class FreizeitseiteLadenEndpunkt
{
    public static async Task<Results<Ok<FreizeitseiteEinstellungen>, NotFound>> Laden(
        Guid freizeitId, FreizeitenLeseDbContext db, ILinkSchutz schutz, IConfiguration konfiguration, CancellationToken abbruch)
    {
        var freizeit = await db.Freizeiten.SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        if (freizeit is null)
        {
            return TypedResults.NotFound();
        }

        var links = await db.Anmeldelinks.Where(l => l.FreizeitId == freizeitId).OrderBy(l => l.Art).ToListAsync(abbruch);
        return TypedResults.Ok(new FreizeitseiteEinstellungen(
            freizeit.Version,
            freizeit.Beschreibung,
            freizeit.Ort,
            freizeit.HoechstzahlTeilnehmende,
            [.. links.Select(l => Anzeigen(l, schutz, konfiguration)).OfType<Api.Contracts.Freizeiten.Anmeldelink>()],
            freizeit.Teilnahmebedingungen,
            freizeit.KontaktMail,
            freizeit.KontaktTelefon));
    }

    /// <summary>
    /// Der Link mit Adresse; lässt sich das Token nicht mehr lesen (etwa nach einem Wechsel des Hauptschlüssels), fehlt er, und die
    /// Leitung erzeugt einen neuen.
    /// </summary>
    private static Api.Contracts.Freizeiten.Anmeldelink? Anzeigen(Domaene.Anmeldelink link, ILinkSchutz schutz, IConfiguration konfiguration)
    {
        try
        {
            return new(AnmeldelinkErzeugen.Api(link.Art), AnmeldelinkErzeugen.Adresse(konfiguration, AnmeldelinkErzeugen.Token(schutz, link)));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/freizeitseite</c> (AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "beschreibung"|"ort"|"hoechstzahlTeilnehmende": neu }</c> mit genau einem Feld (die Höchstzahl als Text oder Zahl, leer = keine).
/// <c>200 { "version", "&lt;feld&gt;" }</c> mit dem Wert als Text; ungültiger Wert <c>400</c> am Feld; anderer Wert desselben Felds
/// <c>409</c> mit Code <c>konflikt</c>; abgeschlossene Freizeit <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.
/// </summary>
internal static class FreizeitseiteFeldAendernEndpunkt
{
    private static readonly FreizeitseiteFeld[] Felder =
        [
            FreizeitseiteFeld.Beschreibung, FreizeitseiteFeld.Ort, FreizeitseiteFeld.HoechstzahlTeilnehmende, FreizeitseiteFeld.Teilnahmebedingungen,
            FreizeitseiteFeld.KontaktMail, FreizeitseiteFeld.KontaktTelefon,
        ];

    public static async Task<IResult> Aendern(Guid freizeitId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new FreizeitseiteFeldAendernCommand(freizeitId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            FreizeitseiteAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Freizeit.Version,
                [FreizeitseiteFelder.JsonName(feld)] = gespeichert.Freizeit.Wert(feld),
            }),
            FreizeitseiteAenderung.Konflikt => FreizeitseiteProbleme.Konflikt(),
            FreizeitseiteAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (uint Version, string? AlterWert, FreizeitseiteFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw Fehler("version", "Der Körper ist kein JSON-Objekt.");
        }

        if (!koerper.TryGetProperty("version", out var versionWert) || versionWert.ValueKind != JsonValueKind.Number || !versionWert.TryGetUInt32(out var version))
        {
            throw Fehler("version", "Version fehlt.");
        }

        var alterWert = koerper.TryGetProperty("alterWert", out var alt) ? Text(alt, "alterWert") : null;
        var felder = Felder.Where(f => koerper.TryGetProperty(FreizeitseiteFelder.JsonName(f), out _)).ToList();
        if (felder.Count != 1)
        {
            throw Fehler(
                "feld",
                $"Genau ein Feld: {string.Join(", ", Felder.Select(FreizeitseiteFelder.JsonName))}.");
        }

        var feld = felder[0];
        var name = FreizeitseiteFelder.JsonName(feld);
        return (version, alterWert, feld, Text(koerper.GetProperty(name), name));
    }

    private static string? Text(JsonElement wert, string name) => wert.ValueKind switch
    {
        JsonValueKind.String => wert.GetString(),
        JsonValueKind.Number => wert.GetRawText(),
        JsonValueKind.Null => null,
        _ => throw Fehler(name, "Nur Text, Zahl oder null."),
    };

    private static ValidierungsAusnahme Fehler(string feld, string text) =>
        new(new Dictionary<string, string[]> { [feld] = [text] });
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/anmeldelinks/{art}/erneuern</c> (Leitung, AD-11): <c>200</c> mit dem neuen Link; der alte
/// ist sofort ungültig. Unbekannte Art <c>404</c>; abgeschlossene Freizeit <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.
/// </summary>
internal static class AnmeldelinkErneuernEndpunkt
{
    public static async Task<IResult> Erneuern(Guid freizeitId, string art, ISender sender, IConfiguration konfiguration, CancellationToken abbruch)
    {
        if (AnmeldelinkArten.Aus(art) is not { } apiArt)
        {
            return TypedResults.NotFound();
        }

        return await sender.Send(new AnmeldelinkErneuernCommand(freizeitId, AnmeldelinkErzeugen.Domaene(apiArt)), abbruch) switch
        {
            AnmeldelinkErneuerung.Erneuert erneuert => TypedResults.Ok(
                new Api.Contracts.Freizeiten.Anmeldelink(apiArt, AnmeldelinkErzeugen.Adresse(konfiguration, erneuert.Token))),
            AnmeldelinkErneuerung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
