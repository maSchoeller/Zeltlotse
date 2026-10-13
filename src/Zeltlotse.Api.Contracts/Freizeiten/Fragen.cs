namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/fragen</c> (Leitung, FR-22, AD-10): welche Fragen des festen Katalogs der
/// Freizeitpass dieser Freizeit abfragt, getrennt für Teilnehmende und Mitarbeitende, mit Version (AD-19) und dem Katalog.
/// </summary>
/// <param name="Teilnehmende">Gewählte Kennungen für Teilnehmende als Komma-Liste in Katalogreihenfolge; <c>null</c> = keine Fragen.</param>
/// <param name="Mitarbeitende">Gewählte Kennungen für Mitarbeitende, Form wie <paramref name="Teilnehmende"/>.</param>
/// <param name="Katalog">Alle Fragen des Katalogs in Anzeigereihenfolge.</param>
public sealed record FragenAuswahl(uint Version, string? Teilnehmende, string? Mitarbeitende, IReadOnlyList<KatalogEintrag> Katalog);

/// <summary>Eine Frage des Katalogs: Kennung und Text.</summary>
public sealed record KatalogEintrag(string Kennung, string Frage);

/// <summary>Bearbeitbare Felder der Fragen-Auswahl für <c>PATCH /api/freizeiten/{freizeitId}/fragen</c> (AD-19).</summary>
public enum FragenFeld
{
    Teilnehmende,
    Mitarbeitende,
}

/// <summary>
/// JSON-Namen der Felder der Fragen-Auswahl: Körper <c>{ "version": n, "alterWert": …, "teilnehmende"|"mitarbeitende": "allergien,medikamente" }</c>,
/// Antwort <c>{ "version": n, "&lt;feld&gt;": normalisiert }</c> (Katalogreihenfolge, ohne Doppelte, leer = <c>null</c>).
/// </summary>
public static class FragenFelder
{
    public const string Teilnehmende = "teilnehmende";

    public const string Mitarbeitende = "mitarbeitende";

    public static string JsonName(FragenFeld feld) => feld switch
    {
        FragenFeld.Teilnehmende => Teilnehmende,
        FragenFeld.Mitarbeitende => Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
