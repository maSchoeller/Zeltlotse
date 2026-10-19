namespace Zeltlotse.Api.Contracts.Kueche;

/// <summary>
/// Story 4-4: Antwort von <c>GET /api/freizeiten/{freizeitId}/kueche/tagesansicht?tag=JJJJ-MM-TT</c> (Leitung und Team, Modul Verpflegung, FR-52, FR-53):
/// die Sicht der Küche für einen Kochtag. Ohne <c>tag</c> wählt der Server heute (Europe/Berlin), wenn heute ein Tag der Freizeit ist, sonst den ersten
/// Tag. Allergien und Sonderkost nur als Anzahlen aus bestätigten Freizeitpässen, ohne Namen und ohne Freitext.
/// </summary>
/// <param name="Tag">Der gezeigte Tag.</param>
/// <param name="Tage">Alle Tage der Freizeit, aufsteigend (für die Tag-Reiter).</param>
/// <param name="Personenzahl">Wie <see cref="Rezeptliste.Personenzahl"/>.</param>
/// <param name="AllergienAbgefragt">Die Freizeit fragt die Allergie-Frage ab; sonst ist <paramref name="Allergene"/> leer.</param>
/// <param name="Allergene">Je Allergen die Zahl der Personen, in Katalogreihenfolge, nur Anzahlen größer 0 (wie die Einkaufsliste).</param>
/// <param name="SonderkostAbgefragt">Die Freizeit fragt die Frage „Ernährung“ ab; sonst ist <paramref name="Sonderkost"/> leer.</param>
/// <param name="Sonderkost">Je Sonderkost die Zahl der Personen, in Katalogreihenfolge (<see cref="Kueche.Sonderkost.Alle"/>), nur Anzahlen größer 0.</param>
/// <param name="Mahlzeiten">Die Mahlzeiten des Tags in Tagesreihenfolge.</param>
public sealed record Tagesansicht(
    DateOnly Tag,
    IReadOnlyList<DateOnly> Tage,
    int Personenzahl,
    bool AllergienAbgefragt,
    IReadOnlyList<AllergenAnzahl> Allergene,
    bool SonderkostAbgefragt,
    IReadOnlyList<SonderkostAnzahl> Sonderkost,
    IReadOnlyList<TagesansichtMahlzeit> Mahlzeiten);

/// <summary>Story 4-4: Eine Mahlzeit der Tagesansicht mit ihren Gerichten (wie im Essensplan) und dem Restetag.</summary>
/// <param name="ProgrammpunktId">Kennung des Mahlzeiten-Programmpunkts; mit ihr wird der Restetag gesetzt.</param>
/// <param name="Beginn">Uhrzeit als <c>HH:mm</c> oder <c>null</c>.</param>
/// <param name="Restetag">FR-53: Die Küche hat die Mahlzeit als Restetag markiert (reine Markierung); ohne Stand <c>false</c>.</param>
/// <param name="Version">Versionsnummer des Stands für den Autosave (AD-19); ohne Stand 0.</param>
public sealed record TagesansichtMahlzeit(
    Guid ProgrammpunktId,
    string? Beginn,
    string Titel,
    MahlzeitArt Art,
    bool Restetag,
    uint Version,
    IReadOnlyList<Gericht> Gerichte);

/// <summary>Story 4-4: So viele bestätigte Personen haben diese Sonderkost im Freizeitpass gewählt.</summary>
/// <param name="Sonderkost">Schlüssel aus <see cref="Kueche.Sonderkost.Alle"/>, z. B. <c>vegetarisch</c>.</param>
public sealed record SonderkostAnzahl(string Sonderkost, int Anzahl);

/// <summary>Story 4-4: Die bearbeitbaren Felder einer Mahlzeit in der Küche (<c>PATCH …/kueche/mahlzeiten/{programmpunktId}</c>, AD-19).</summary>
public enum MahlzeitFeld
{
    Restetag,
}

/// <summary>
/// Story 4-4: JSON-Namen der Felder einer Mahlzeit. Körper <c>{ "version": n, "alterWert": "true"|"false", "restetag": "true"|"false" }</c>; ohne Stand gilt
/// <c>"false"</c>. Antwort <c>{ "version": n, "restetag": "true"|"false" }</c>.
/// </summary>
public static class MahlzeitFelder
{
    public const string Restetag = "restetag";

    public static string JsonName(MahlzeitFeld feld) => feld switch
    {
        MahlzeitFeld.Restetag => Restetag,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}

/// <summary>Story 4-4: Eine Sonderkost: gespeicherter Schlüssel und Anzeigetext.</summary>
public sealed record Sonderkostart(string Wert, string Text);

/// <summary>
/// Story 4-4: Die Auswahl der Frage „Ernährung“ des Freizeitpasses, mit denselben Schlüsseln und in derselben Reihenfolge (Besitzer: Personen, AD-10).
/// Kopie für die Clients, die Modul-Contracts nicht kennen (AD-5); ein Test hält sie gleich.
/// </summary>
public static class Sonderkost
{
    public static IReadOnlyList<Sonderkostart> Alle { get; } =
    [
        new("vegetarisch", "Vegetarisch"),
        new("vegan", "Vegan"),
        new("kein-schweinefleisch", "Kein Schweinefleisch"),
    ];

    /// <summary>Der Anzeigetext eines Schlüssels; Unbekanntes bleibt, wie es ist.</summary>
    public static string Text(string wert) => Alle.FirstOrDefault(s => s.Wert == wert)?.Text ?? wert;
}
