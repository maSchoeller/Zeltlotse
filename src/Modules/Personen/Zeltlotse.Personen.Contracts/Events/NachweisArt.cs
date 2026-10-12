namespace Zeltlotse.Personen.Contracts.Events;

/// <summary>
/// Fester Katalog der Nachweisarten (FR-8, Besitzer Personen); Unter-Typ von <see cref="NachweisEingesehen"/> (AD-4).
/// Werte sind gespeichert; nur anhängen. Organisationen nutzt den Typ intern für Richtlinie und Bestätigung.
/// </summary>
public enum NachweisArt
{
    Fuehrungszeugnis = 1,
    Juleica = 2,
    ErsteHilfe = 3,
    Praeventionsschulung = 4,
}
