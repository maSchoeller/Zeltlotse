using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// Story 4-1: Formprüfung, Normalisierung und Hochrechnung der Küche, gleich für Anlegen, Bearbeiten und Lesen. Zahlen reisen als Text (AD-19) und
/// werden deutsch mit Komma gespeichert und geliefert (<see cref="ZutatFelder.ZahlText"/>); Punkt und Komma sind beide als Dezimaltrenner erlaubt,
/// Tausenderpunkte und Vorzeichen nicht.
/// </summary>
internal static partial class KuecheWerte
{
    public const string BasisFehlerText = "Nur eine ganze Zahl von 1 bis 1000.";

    public const string MengeFehlerText = "Nur eine Zahl größer 0 bis 1000000 mit höchstens 3 Nachkommastellen, keine Tausenderpunkte.";

    /// <summary>Getrimmter Text; leer oder nur Leerzeichen heißt <c>null</c>.</summary>
    public static string? TextOderNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Der Fehler zu einem Pflichttext oder <c>null</c>: nicht leer, getrimmt höchstens <paramref name="hoechstens"/> Zeichen.</summary>
    public static string? PflichtFehler(string? text, int hoechstens, string fehlt) =>
        TextOderNull(text) is not { } wert ? fehlt : LaengeFehler(wert, hoechstens);

    /// <summary>Der Fehler zu einem freiwilligen Text oder <c>null</c>: getrimmt höchstens <paramref name="hoechstens"/> Zeichen.</summary>
    public static string? LaengeFehler(string? text, int hoechstens) =>
        (TextOderNull(text)?.Length ?? 0) > hoechstens ? $"Höchstens {hoechstens} Zeichen." : null;

    // ---------- Basis-Personenzahl ----------

    /// <summary>Die Basis als ganze Zahl von 1 bis 1000 oder <c>null</c>, wenn der Text keine ist.</summary>
    public static int? BasisLesen(string? text) =>
        TextOderNull(text) is { } wert
        && int.TryParse(wert, NumberStyles.None, CultureInfo.InvariantCulture, out var basis)
        && basis is >= RezeptFelder.BasisMinimum and <= RezeptFelder.BasisMaximum
            ? basis
            : null;

    public static string BasisText(int basis) => basis.ToString(CultureInfo.InvariantCulture);

    /// <summary>Der Fehler zur Basis oder <c>null</c>: Pflicht, ganze Zahl von 1 bis 1000.</summary>
    public static string? BasisFehler(string? text) =>
        TextOderNull(text) is null ? "Basis-Personenzahl fehlt." : BasisLesen(text) is null ? BasisFehlerText : null;

    // ---------- Menge ----------

    /// <summary>
    /// Die Menge oder <c>null</c>, wenn der Text keine gültige ist: größer 0, höchstens <see cref="ZutatFelder.MengeMaximum"/>, höchstens 3
    /// Nachkommastellen, Komma oder Punkt als Dezimaltrenner, keine Tausenderpunkte. Das Ergebnis trägt keine Null-Nachkommastellen.
    /// </summary>
    public static decimal? MengeLesen(string? text)
    {
        if (TextOderNull(text) is not { } wert
            || Tausenderpunkte().IsMatch(wert)
            || !decimal.TryParse(wert.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var menge))
        {
            return null;
        }

        menge = OhneNullen(menge);
        return menge > 0 && menge <= ZutatFelder.MengeMaximum && menge.Scale <= ZutatFelder.MengeNachkommastellen ? menge : null;
    }

    /// <summary>Deutsche Tausenderpunkte („1.000“, „12.500“, „1.000.000“) sind mehrdeutig und gelten nicht; „0.125“ bleibt eine Dezimalzahl.</summary>
    [GeneratedRegex(@"^[1-9]\d{0,2}(\.\d{3})+$")]
    private static partial Regex Tausenderpunkte();

    /// <summary>Der Fehler zur Menge oder <c>null</c>: freiwillig (leer heißt ohne Menge), sonst eine gültige Menge.</summary>
    public static string? MengeFehler(string? text) =>
        TextOderNull(text) is null || MengeLesen(text) is not null ? null : MengeFehlerText;

    /// <summary>Dieselbe Zahl ohne Null-Nachkommastellen (300,000 → 300).</summary>
    public static decimal OhneNullen(decimal zahl) => zahl / 1.000000000000000000000000000000000m;

    /// <summary>
    /// Story 4-1, FR-46: Menge × Personenzahl ÷ Basis-Personenzahl, auf 2 Nachkommastellen kaufmännisch gerundet; ohne Menge oder ohne Personen
    /// <c>null</c>.
    /// </summary>
    public static decimal? Hochrechnen(decimal? menge, int basisPersonen, int personenzahl) =>
        menge is { } wert && personenzahl > 0 && basisPersonen > 0
            ? OhneNullen(Math.Round(wert * personenzahl / basisPersonen, 2, MidpointRounding.AwayFromZero))
            : null;

    // ---------- Tag (Story 4-2) ----------

    public const string TagFehlerText = "Nur ein Datum im Format JJJJ-MM-TT.";

    public const string TagAusserhalbText = "Nur ein Tag der Freizeit.";

    /// <summary>Der Tag aus einem Text <c>JJJJ-MM-TT</c> oder <c>null</c>.</summary>
    public static DateOnly? TagLesen(string? text) =>
        TextOderNull(text) is { } wert
        && DateOnly.TryParseExact(wert, Api.Contracts.Kueche.EinkaufspostenFelder.TagFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag)
            ? tag
            : null;

    public static string? TagText(DateOnly? tag) => tag?.ToString(Api.Contracts.Kueche.EinkaufspostenFelder.TagFormat, CultureInfo.InvariantCulture);

    /// <summary>Der Fehler zum Tag oder <c>null</c>: freiwillig (leer heißt ohne Tag), sonst ein Datum <c>JJJJ-MM-TT</c>.</summary>
    public static string? TagFehler(string? text) => TextOderNull(text) is null || TagLesen(text) is not null ? null : TagFehlerText;

    // ---------- Allergene ----------

    private static List<string> Schluessel(string? liste) =>
        [.. (liste ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>Der Fehler zu den Allergenen oder <c>null</c>: nur Schlüssel aus <see cref="Fragenkatalog.Allergene"/>, leer heißt keine.</summary>
    public static string? AllergeneFehler(string? liste) =>
        Schluessel(liste).FirstOrDefault(s => Fragenkatalog.Allergene.All(a => a.Wert != s)) is { } unbekannt ? $"Unbekanntes Allergen: {unbekannt}." : null;

    /// <summary>Die bekannten Schlüssel einer Komma-Liste in Katalogreihenfolge ohne Doppelte; ohne Allergene <c>null</c>.</summary>
    public static string? AllergeneNormalisieren(string? liste)
    {
        var menge = Schluessel(liste).ToHashSet(StringComparer.Ordinal);
        var text = string.Join(',', Fragenkatalog.Allergene.Where(a => menge.Contains(a.Wert)).Select(a => a.Wert));
        return text.Length == 0 ? null : text;
    }
}

/// <summary>Story 4-1: <c>409</c>-Antworten der Küche mit den Problemcodes aus <c>Api.Contracts/Gemeinsam</c>.</summary>
internal static class KuecheProbleme
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
/// jede andere Form ist ein Validierungsfehler (<c>400</c>). Eigene Kopie der Küche (Muster aus Programm).
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
