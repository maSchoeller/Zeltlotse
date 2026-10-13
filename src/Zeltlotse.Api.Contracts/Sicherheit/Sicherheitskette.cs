namespace Zeltlotse.Api.Contracts.Sicherheit;

/// <summary>
/// Story 2-8: Antwort von <c>GET /api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c> (Leitung und Team, FR-29): was im Notfall zu
/// tun ist und wen man anruft, mit Version für den Autosave (AD-19). Ohne gespeicherte Kette sind alle Felder <c>null</c>,
/// <see cref="Eingerichtet"/> ist <c>false</c> und <see cref="Version"/> ist <c>0</c>.
/// </summary>
/// <param name="WasTun">Schritte im Notfall, ein Schritt je Zeile.</param>
/// <param name="LeitungTelefon">Telefon der Leitung im Notfall.</param>
/// <param name="HeimatName">Ansprechperson am Heimatort (freiwillig).</param>
/// <param name="HeimatTelefon">Telefon der Ansprechperson am Heimatort.</param>
/// <param name="Eingerichtet">Was zu tun ist, Telefon der Leitung und Telefon am Heimatort sind gesetzt.</param>
public sealed record Sicherheitskette(
    string? WasTun,
    string? LeitungTelefon,
    string? HeimatName,
    string? HeimatTelefon,
    bool Eingerichtet,
    uint Version);

/// <summary>Bearbeitbare Felder der Sicherheitskette für <c>PATCH /api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c> (AD-19).</summary>
public enum SicherheitsketteFeld
{
    WasTun,
    LeitungTelefon,
    HeimatName,
    HeimatTelefon,
}

/// <summary>
/// JSON-Namen und Grenzen der Felder für <c>PATCH /api/freizeiten/{freizeitId}/sicherheit/sicherheitskette</c>: Körper
/// <c>{ "version": n, "alterWert": …, "&lt;feld&gt;": neu }</c> mit genau einem Feld als Text (leer = kein Wert), Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>.
/// </summary>
public static class SicherheitsketteFelder
{
    public const string WasTun = "wasTun";

    public const string LeitungTelefon = "leitungTelefon";

    public const string HeimatName = "heimatName";

    public const string HeimatTelefon = "heimatTelefon";

    public const int WasTunMaximaleLaenge = 2000;

    public const int LeitungTelefonMaximaleLaenge = 40;

    public const int HeimatNameMaximaleLaenge = 200;

    public const int HeimatTelefonMaximaleLaenge = 40;

    public static string JsonName(SicherheitsketteFeld feld) => feld switch
    {
        SicherheitsketteFeld.WasTun => WasTun,
        SicherheitsketteFeld.LeitungTelefon => LeitungTelefon,
        SicherheitsketteFeld.HeimatName => HeimatName,
        SicherheitsketteFeld.HeimatTelefon => HeimatTelefon,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Höchstlänge eines Felds.</summary>
    public static int MaximaleLaenge(SicherheitsketteFeld feld) => feld switch
    {
        SicherheitsketteFeld.WasTun => WasTunMaximaleLaenge,
        SicherheitsketteFeld.LeitungTelefon => LeitungTelefonMaximaleLaenge,
        SicherheitsketteFeld.HeimatName => HeimatNameMaximaleLaenge,
        SicherheitsketteFeld.HeimatTelefon => HeimatTelefonMaximaleLaenge,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
