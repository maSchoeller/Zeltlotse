namespace Zeltlotse.SharedKernel.Commands;

/// <summary>
/// Formprüfung eines Commands fehlgeschlagen (AD-27). Schlüssel sind die JSON-Feldnamen,
/// damit der Client den Fehler direkt am Feld zeigen kann.
/// </summary>
public sealed class ValidierungsAusnahme(IReadOnlyDictionary<string, string[]> fehler)
    : Exception("Die Eingaben sind nicht gültig.")
{
    public IReadOnlyDictionary<string, string[]> Fehler { get; } = fehler;
}
