using System.Text.Json;
using FluentValidation.Results;

namespace Zeltlotse.SharedKernel.Commands;

/// <summary>
/// Formprüfung eines Commands fehlgeschlagen (AD-27). Schlüssel sind die JSON-Feldnamen,
/// damit der Client den Fehler direkt am Feld zeigen kann. Der API-Host antwortet darauf mit <c>400</c>.
/// </summary>
public sealed class ValidierungsAusnahme(IReadOnlyDictionary<string, string[]> fehler)
    : Exception("Die Eingaben sind nicht gültig.")
{
    public IReadOnlyDictionary<string, string[]> Fehler { get; } = fehler;

    /// <summary>Fasst FluentValidation-Fehler je JSON-Feldname zusammen.</summary>
    public static ValidierungsAusnahme Aus(IEnumerable<ValidationFailure> fehler) =>
        new(fehler
            .GroupBy(f => JsonName(f.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray()));

    /// <summary>„Adresse.Strasse“ → „adresse.strasse“, „Posten[0].Name“ → „posten[0].name“.</summary>
    public static string JsonName(string eigenschaft) =>
        string.Join('.', eigenschaft.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
