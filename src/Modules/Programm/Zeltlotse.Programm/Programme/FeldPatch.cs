using System.Text.Json;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Programme;

/// <summary>
/// Liest den Körper eines Autosave-PATCH nach AD-19 (<c>{ "version": n, "alterWert": …, "&lt;feld&gt;": neu }</c>) mit genau einem Feld als Text;
/// jede andere Form ist ein Validierungsfehler (<c>400</c>). Gleich für Programmpunkt (Story 3-1) und Material (Story 3-2).
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

    public static ValidierungsAusnahme Fehler(string feld, string text) =>
        new(new Dictionary<string, string[]> { [feld] = [text] });
}
