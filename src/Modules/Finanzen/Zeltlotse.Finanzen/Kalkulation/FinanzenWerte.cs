using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1: Formprüfung und Normalisierung der Finanzen, gleich für Anlegen und Bearbeiten. Werte reisen als Text (AD-19). Planzahlen sind ganze Zahlen
/// von 0 bis 1000. Beträge sind Euro von 0 bis 1.000.000 mit höchstens zwei Nachkommastellen; Komma und Punkt sind beide Dezimaltrenner, Tausenderpunkte
/// („1.250“) und Vorzeichen nicht. Geliefert werden Beträge deutsch mit Komma ohne Null-Nachkommastellen (<see cref="KategorieFelder.BetragText"/>), damit
/// der Vergleich mit dem alten Wert stabil bleibt.
/// </summary>
internal static partial class FinanzenWerte
{
    public const string PlanzahlFehlerText = "Nur eine ganze Zahl von 0 bis 1000.";

    public const string BetragFehlerText = "Nur ein Betrag von 0 bis 1000000 Euro mit höchstens 2 Nachkommastellen, keine Tausenderpunkte.";

    /// <summary>Getrimmter Text; leer oder nur Leerzeichen heißt <c>null</c>.</summary>
    public static string? TextOderNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Der Fehler zu einem Pflichttext oder <c>null</c>: nicht leer, getrimmt höchstens <paramref name="hoechstens"/> Zeichen.</summary>
    public static string? PflichtFehler(string? text, int hoechstens, string fehlt) =>
        TextOderNull(text) is not { } wert ? fehlt : wert.Length > hoechstens ? $"Höchstens {hoechstens} Zeichen." : null;

    // ---------- Planzahlen ----------

    /// <summary>Die Planzahl als ganze Zahl von 0 bis 1000 oder <c>null</c>, wenn der Text keine ist.</summary>
    public static int? PlanzahlLesen(string? text) =>
        TextOderNull(text) is { } wert
        && int.TryParse(wert, NumberStyles.None, CultureInfo.InvariantCulture, out var zahl)
        && zahl is >= KalkulationFelder.PlanzahlMinimum and <= KalkulationFelder.PlanzahlMaximum
            ? zahl
            : null;

    public static string PlanzahlText(int zahl) => zahl.ToString(CultureInfo.InvariantCulture);

    /// <summary>Der Fehler zu einer Planzahl oder <c>null</c>: Pflicht, ganze Zahl von 0 bis 1000.</summary>
    public static string? PlanzahlFehler(string? text) => PlanzahlLesen(text) is null ? PlanzahlFehlerText : null;

    // ---------- Beträge ----------

    /// <summary>Der Betrag oder <c>null</c>, wenn der Text keiner ist; das Ergebnis trägt keine Null-Nachkommastellen.</summary>
    public static decimal? BetragLesen(string? text)
    {
        if (TextOderNull(text) is not { } wert
            || Tausenderpunkte().IsMatch(wert)
            || !decimal.TryParse(wert.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var betrag))
        {
            return null;
        }

        // Die Nachkommastellen zählen wie getippt: „1,000“ oder „2,500“ haben drei (sonst würde „1,000“ still zu 1 €).
        return betrag >= 0 && betrag <= KategorieFelder.BetragMaximum && betrag.Scale <= KategorieFelder.BetragNachkommastellen ? OhneNullen(betrag) : null;
    }

    public static string BetragText(decimal betrag) => KategorieFelder.BetragText(betrag);

    /// <summary>Der Fehler zu einem Betrag oder <c>null</c>: Pflicht, ein gültiger Betrag.</summary>
    public static string? BetragFehler(string? text) => BetragLesen(text) is null ? BetragFehlerText : null;

    /// <summary>Der alte Wert eines Betrags in derselben Form wie der gespeicherte: lesbar normalisiert, sonst getrimmt.</summary>
    public static string? BetragVergleichswert(string? text) => BetragLesen(text) is { } betrag ? BetragText(betrag) : TextOderNull(text);

    /// <summary>Der alte Wert einer Planzahl in derselben Form wie die gespeicherte: lesbar normalisiert, sonst getrimmt.</summary>
    public static string? PlanzahlVergleichswert(string? text) => PlanzahlLesen(text) is { } zahl ? PlanzahlText(zahl) : TextOderNull(text);

    /// <summary>Deutsche Tausenderpunkte („1.000“, „1.250“, „1.000.000“) sind mehrdeutig und gelten nicht.</summary>
    [GeneratedRegex(@"^[1-9]\d{0,2}(\.\d{3})+$")]
    private static partial Regex Tausenderpunkte();

    /// <summary>Dieselbe Zahl ohne Null-Nachkommastellen (7,50 → 7,5); so liefert die API Beträge auch als JSON-Zahl.</summary>
    public static decimal OhneNullen(decimal zahl) => zahl / 1.000000000000000000000000000000000m;
}

/// <summary>Story 5-1: <c>409</c>-Antworten der Finanzen mit den Problemcodes aus <c>Api.Contracts/Gemeinsam</c>.</summary>
internal static class FinanzenProbleme
{
    public static ProblemHttpResult Problem(string code, string titel) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: titel,
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = code });

    public static ProblemHttpResult Konflikt() => Problem(Problemcodes.Konflikt, "Der Wert wurde inzwischen geändert.");

    public static ProblemHttpResult Abgeschlossen() => Problem(Problemcodes.FreizeitAbgeschlossen, "Diese Freizeit ist abgeschlossen und nur lesbar.");
}

/// <summary>
/// Liest den Körper eines Autosave-PATCH nach AD-19 (<c>{ "version": n, "alterWert": …, "&lt;feld&gt;": neu }</c>) mit genau einem Feld als Text;
/// jede andere Form ist ein Validierungsfehler (<c>400</c>). Eigene Kopie der Finanzen (Muster aus Programm und Küche).
/// </summary>
internal static class FeldPatch
{
    /// <summary>Erlaubte Eigenschaften neben den Feldern.</summary>
    private static readonly string[] Rahmen = ["version", "alterWert"];

    public static (uint Version, string? AlterWert, TFeld Feld, string? NeuerWert) Lesen<TFeld>(JsonElement koerper, Func<TFeld, string> jsonName)
        where TFeld : struct, Enum
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
        var felder = Enum.GetValues<TFeld>();
        var genannt = koerper.EnumerateObject().Select(e => e.Name).Where(n => !Rahmen.Contains(n)).ToList();
        if (genannt.Count != 1 || felder.Where(f => jsonName(f) == genannt[0]).ToList() is not [var feld])
        {
            throw Fehler("feld", $"Genau ein Feld: {string.Join(", ", felder.Select(jsonName))}.");
        }

        var name = jsonName(feld);
        return (version, alterWert, feld, Text(koerper.GetProperty(name), name));
    }

    private static string? Text(JsonElement wert, string name) => wert.ValueKind switch
    {
        JsonValueKind.String => wert.GetString(),
        JsonValueKind.Null => null,
        _ => throw Fehler(name, "Nur Text oder null."),
    };

    private static ValidierungsAusnahme Fehler(string feld, string text) =>
        new(new Dictionary<string, string[]> { [feld] = [text] });
}
