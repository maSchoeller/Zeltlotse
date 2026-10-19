using System.Globalization;

namespace Zeltlotse.Api.Contracts.Kueche;

/// <summary>
/// Story 4-1: Antwort von <c>GET /api/freizeiten/{freizeitId}/kueche/rezepte</c> (Leitung und Team, Modul Verpflegung, CAP-28, FR-46): die
/// Rezepte der Freizeit nach Name sortiert und die Personenzahl der Freizeit, auf die Zeltlotse hochrechnet.
/// </summary>
/// <param name="Personenzahl">
/// Bestätigte Teilnehmende und alle Personen mit Freizeit-Rolle (Leitung oder Team), jede Person einmal; 0, solange niemand bestätigt ist.
/// </param>
public sealed record Rezeptliste(int Personenzahl, IReadOnlyList<RezeptKurz> Rezepte);

/// <summary>Story 4-1: Ein Rezept in der Liste.</summary>
/// <param name="BasisPersonen">Für so viele Personen gilt das Rezept.</param>
/// <param name="Zutaten">Anzahl der Zutaten.</param>
/// <param name="Allergene">Vereinigung der Allergene aller Zutaten als Komma-Liste der Schlüssel in Katalogreihenfolge, ohne Allergene <c>null</c>.</param>
public sealed record RezeptKurz(Guid Id, string Name, int BasisPersonen, int Zutaten, string? Allergene);

/// <summary>
/// Story 4-1: Antwort von <c>GET …/kueche/rezepte/{rezeptId}</c> und <c>POST …/kueche/rezepte</c>: das Rezept mit seinen Zutaten (nach Kennung, also
/// in der Reihenfolge des Anlegens) und der Personenzahl der Freizeit.
/// </summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
/// <param name="Zubereitung">Freitext oder <c>null</c>.</param>
/// <param name="Personenzahl">Wie <see cref="Rezeptliste.Personenzahl"/>.</param>
public sealed record Rezept(Guid Id, uint Version, string Name, int BasisPersonen, string? Zubereitung, int Personenzahl, IReadOnlyList<Zutat> Zutaten);

/// <summary>Story 4-1: Eine Zutat eines Rezepts.</summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
/// <param name="Menge">Menge für die Basis-Personenzahl, ohne Menge (z. B. „Salz“) <c>null</c>.</param>
/// <param name="Einheit">Freitext, z. B. „g“ oder „Stück“, oder <c>null</c>; Zeltlotse rechnet keine Einheiten um.</param>
/// <param name="Allergene">Komma-Liste der Schlüssel aus <see cref="Kueche.Allergene"/> in Katalogreihenfolge oder <c>null</c>.</param>
/// <param name="HochgerechneteMenge">
/// Menge × Personenzahl ÷ Basis-Personenzahl, auf 2 Nachkommastellen gerundet; <c>null</c> ohne Menge oder bei Personenzahl 0.
/// </param>
public sealed record Zutat(Guid Id, uint Version, string Bezeichnung, decimal? Menge, string? Einheit, string? Allergene, decimal? HochgerechneteMenge);

/// <summary>Story 4-1: Körper von <c>POST …/kueche/rezepte</c>: Name und Basis-Personenzahl sind Pflicht.</summary>
/// <param name="Name">1 bis <see cref="RezeptFelder.NameMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="BasisPersonen">Ganze Zahl von 1 bis <see cref="RezeptFelder.BasisMaximum"/> als Text (wie im Autosave, AD-19).</param>
public sealed record RezeptNeu(string? Name, string? BasisPersonen);

/// <summary>Story 4-1: Körper von <c>POST …/kueche/rezepte/{rezeptId}/zutaten</c>: nur die Bezeichnung ist Pflicht.</summary>
/// <param name="Bezeichnung">1 bis <see cref="ZutatFelder.BezeichnungMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="Menge">Zahl größer 0 als Text, Komma oder Punkt; leer heißt ohne Menge.</param>
/// <param name="Einheit">Höchstens <see cref="ZutatFelder.EinheitMaximaleLaenge"/> Zeichen; leer heißt ohne Einheit.</param>
public sealed record ZutatNeu(string? Bezeichnung, string? Menge, string? Einheit);

/// <summary>Story 4-1: Bearbeitbare Felder eines Rezepts für <c>PATCH …/kueche/rezepte/{rezeptId}</c> (AD-19).</summary>
public enum RezeptFeld
{
    Name,
    BasisPersonen,
    Zubereitung,
}

/// <summary>
/// Story 4-1: JSON-Namen und Grenzen der Felder eines Rezepts. <c>PATCH …/kueche/rezepte/{rezeptId}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "name"|"basisPersonen"|"zubereitung": neu }</c> mit genau einem Feld als Text, Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>; die Basis kommt als Text ohne Nachkommastellen zurück, z. B. <c>"4"</c>.
/// </summary>
public static class RezeptFelder
{
    public const string Name = "name";

    public const string BasisPersonen = "basisPersonen";

    public const string Zubereitung = "zubereitung";

    public const int NameMaximaleLaenge = 200;

    public const int BasisMinimum = 1;

    public const int BasisMaximum = 1000;

    public const int ZubereitungMaximaleLaenge = 8000;

    public static string JsonName(RezeptFeld feld) => feld switch
    {
        RezeptFeld.Name => Name,
        RezeptFeld.BasisPersonen => BasisPersonen,
        RezeptFeld.Zubereitung => Zubereitung,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}

/// <summary>Story 4-1: Bearbeitbare Felder einer Zutat für <c>PATCH …/kueche/rezepte/{rezeptId}/zutaten/{zutatId}</c> (AD-19).</summary>
public enum ZutatFeld
{
    Bezeichnung,
    Menge,
    Einheit,
    Allergene,
}

/// <summary>
/// Story 4-1: JSON-Namen und Grenzen der Felder einer Zutat. <c>PATCH …/zutaten/{zutatId}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"menge"|"einheit"|"allergene": neu }</c> mit genau einem Feld als Text, Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>. Die Menge kommt deutsch mit Komma zurück, ohne Tausenderpunkte und ohne Null-Nachkommastellen
/// (<c>"1.5"</c> → <c>"1,5"</c>, siehe <see cref="ZahlText"/>), die Allergene als Komma-Liste in Katalogreihenfolge ohne Doppelte.
/// </summary>
public static class ZutatFelder
{
    public const string Bezeichnung = "bezeichnung";

    public const string Menge = "menge";

    public const string Einheit = "einheit";

    public const string Allergene = "allergene";

    public const int BezeichnungMaximaleLaenge = 200;

    public const int EinheitMaximaleLaenge = 30;

    /// <summary>Größte erlaubte Menge.</summary>
    public const decimal MengeMaximum = 1_000_000m;

    /// <summary>Höchstens so viele Nachkommastellen hat eine Menge.</summary>
    public const int MengeNachkommastellen = 3;

    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    public static string JsonName(ZutatFeld feld) => feld switch
    {
        ZutatFeld.Bezeichnung => Bezeichnung,
        ZutatFeld.Menge => Menge,
        ZutatFeld.Einheit => Einheit,
        ZutatFeld.Allergene => Allergene,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Eine Zahl deutsch mit Komma, ohne Tausenderpunkte und ohne Null-Nachkommastellen: 1,5 → „1,5“, 6000,00 → „6000“.</summary>
    public static string ZahlText(decimal zahl) => zahl.ToString("0.############", Deutsch);
}

/// <summary>Story 4-1: Ein Allergen: gespeicherter Schlüssel und Anzeigetext.</summary>
/// <param name="Kurz">Story 4-2: kurzer Name für Hinweise wie „enthält Gluten“ und die Allergen-Übersicht der Einkaufsliste.</param>
public sealed record Allergen(string Wert, string Text, string Kurz);

/// <summary>
/// Story 4-1: Die 14 EU-Hauptallergene für die Oberfläche, mit denselben Schlüsseln und in derselben Reihenfolge wie die Allergie-Frage des
/// Freizeitpasses (Besitzer: Personen, AD-10). Kopie für die Clients, die Modul-Contracts nicht kennen (AD-5); ein Test hält sie gleich.
/// </summary>
public static class Allergene
{
    public static IReadOnlyList<Allergen> Alle { get; } =
    [
        new("gluten", "Glutenhaltiges Getreide", "Gluten"),
        new("krebstiere", "Krebstiere", "Krebstiere"),
        new("eier", "Eier", "Eier"),
        new("fisch", "Fisch", "Fisch"),
        new("erdnuesse", "Erdnüsse", "Erdnüsse"),
        new("soja", "Soja", "Soja"),
        new("milch", "Milch (inkl. Laktose)", "Milch"),
        new("schalenfruechte", "Schalenfrüchte (Nüsse)", "Schalenfrüchte"),
        new("sellerie", "Sellerie", "Sellerie"),
        new("senf", "Senf", "Senf"),
        new("sesam", "Sesam", "Sesam"),
        new("sulfite", "Schwefeldioxid und Sulfite", "Sulfite"),
        new("lupinen", "Lupinen", "Lupinen"),
        new("weichtiere", "Weichtiere", "Weichtiere"),
    ];

    /// <summary>Die Anzeigetexte einer Komma-Liste in Katalogreihenfolge; Unbekanntes fällt weg.</summary>
    public static IReadOnlyList<string> Texte(string? liste)
    {
        var schluessel = (liste ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return [.. Alle.Where(a => schluessel.Contains(a.Wert)).Select(a => a.Text)];
    }

    /// <summary>Story 4-2: Die kurzen Namen einer Menge von Schlüsseln in Katalogreihenfolge, z. B. „Gluten“, „Milch“; Unbekanntes fällt weg.</summary>
    public static IReadOnlyList<string> Kurztexte(IEnumerable<string> schluessel)
    {
        var menge = schluessel.ToHashSet(StringComparer.Ordinal);
        return [.. Alle.Where(a => menge.Contains(a.Wert)).Select(a => a.Kurz)];
    }
}
