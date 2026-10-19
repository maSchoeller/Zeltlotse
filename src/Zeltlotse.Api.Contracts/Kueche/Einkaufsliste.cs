namespace Zeltlotse.Api.Contracts.Kueche;

/// <summary>
/// Story 4-2: Antwort von <c>GET /api/freizeiten/{freizeitId}/kueche/einkaufsliste</c> (Leitung und Team, Modul Verpflegung, CAP-29, FR-47 bis
/// FR-49): bei jedem Abruf aus den Gerichten berechnet. Je Gericht und Zutat ein Posten (nichts zusammengefasst), hochgerechnet auf die
/// Personenzahl; eine Hand-Menge ersetzt die berechnete Menge. Gruppiert nach dem Tag der Mahlzeit, darin nach Mahlzeit; Hand-Posten ohne Tag
/// stehen zuletzt (<see cref="EinkaufsTag.Tag"/> <c>null</c>).
/// </summary>
/// <param name="AllergienAbgefragt">Wie <see cref="Essensplan.AllergienAbgefragt"/>.</param>
/// <param name="Allergene">Allergen-Übersicht: je Allergen aus den Freizeitpässen (bestätigt, freigegeben) die Zahl der Personen, Katalogreihenfolge.</param>
public sealed record Einkaufsliste(int Personenzahl, bool AllergienAbgefragt, IReadOnlyList<AllergenAnzahl> Allergene, IReadOnlyList<EinkaufsTag> Tage);

/// <summary>Story 4-2: So viele Personen haben dieses Allergen (Schlüssel aus <see cref="Kueche.Allergene"/>).</summary>
public sealed record AllergenAnzahl(string Allergen, int Anzahl);

/// <summary>Story 4-2: Die Posten eines Tags und die Ersatzportionen seiner Gerichte.</summary>
/// <param name="Tag">Der Tag; <c>null</c> für Hand-Posten ohne Tag.</param>
/// <param name="Ersatzportionen">Nur Gerichte mit mindestens einer Ersatzportion.</param>
public sealed record EinkaufsTag(DateOnly? Tag, IReadOnlyList<EinkaufslistenPosten> Posten, IReadOnlyList<GerichtErsatz> Ersatzportionen);

/// <summary>Story 4-2: Ersatzportionen eines Gerichts auf der Einkaufsliste.</summary>
public sealed record GerichtErsatz(Guid GerichtId, string RezeptName, string MahlzeitName, Ersatzportionen Ersatzportionen);

/// <summary>
/// Story 4-2: Ein Posten der Einkaufsliste: berechnet (mit <see cref="GerichtId"/> und <see cref="ZutatId"/>) oder ein Hand-Posten (mit
/// <see cref="PostenId"/>).
/// </summary>
/// <param name="Menge">Berechnete Menge, Hand-Menge oder Menge des Hand-Postens; ohne Menge <c>null</c>.</param>
/// <param name="RezeptName">Bei berechneten Posten das Rezept, sonst <c>null</c>.</param>
/// <param name="MahlzeitName">Bei berechneten Posten die Mahlzeit, z. B. „Frühstück“, sonst <c>null</c>.</param>
/// <param name="VonHand">Hand-Menge oder Hand-Posten.</param>
/// <param name="Version">Versionsnummer für den Autosave (AD-19); berechnete Posten ohne Hand-Menge 0.</param>
public sealed record EinkaufslistenPosten(
    Guid? GerichtId,
    Guid? ZutatId,
    Guid? PostenId,
    string Bezeichnung,
    decimal? Menge,
    string? Einheit,
    string? RezeptName,
    string? MahlzeitName,
    bool VonHand,
    uint Version);

/// <summary>Story 4-2: Körper von <c>POST …/kueche/einkaufsliste/posten</c>: nur die Bezeichnung ist Pflicht.</summary>
/// <param name="Menge">Zahl größer 0 als Text wie bei Zutaten; leer heißt ohne Menge.</param>
/// <param name="Tag">Ein Tag der Freizeit als <c>JJJJ-MM-TT</c>; leer heißt ohne Tag.</param>
public sealed record EinkaufspostenNeu(string? Bezeichnung, string? Menge, string? Einheit, string? Tag);

/// <summary>Story 4-2: Das bearbeitbare Feld einer berechneten Menge (<c>PATCH …/einkaufsliste/gerichte/{gerichtId}/zutaten/{zutatId}</c>, AD-19).</summary>
public enum HandMengeFeld
{
    Menge,
}

/// <summary>
/// Story 4-2: JSON-Name des Felds einer Hand-Menge. Körper <c>{ "version": n, "alterWert": …, "menge": neu }</c>; der alte Wert ist der angezeigte Text
/// (Hand-Menge oder berechnete Menge). Leer heißt: zurück zur berechneten Menge. Antwort <c>{ "version": n, "menge": angezeigt }</c>, deutsch mit Komma.
/// </summary>
public static class HandMengeFelder
{
    public const string Menge = "menge";

    public static string JsonName(HandMengeFeld feld) => feld switch
    {
        HandMengeFeld.Menge => Menge,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}

/// <summary>Story 4-2: Bearbeitbare Felder eines Hand-Postens (<c>PATCH …/kueche/einkaufsliste/posten/{postenId}</c>, AD-19).</summary>
public enum EinkaufspostenFeld
{
    Bezeichnung,
    Menge,
    Einheit,
    Tag,
}

/// <summary>
/// Story 4-2: JSON-Namen und Grenzen der Felder eines Hand-Postens. Körper <c>{ "version": n, "alterWert": …, "bezeichnung"|"menge"|"einheit"|"tag": neu }</c>
/// mit genau einem Feld als Text; die Menge wie bei Zutaten (<see cref="ZutatFelder.ZahlText"/>), der Tag als <c>JJJJ-MM-TT</c> oder leer.
/// </summary>
public static class EinkaufspostenFelder
{
    public const string Bezeichnung = "bezeichnung";

    public const string Menge = "menge";

    public const string Einheit = "einheit";

    public const string Tag = "tag";

    public const int BezeichnungMaximaleLaenge = 200;

    public const int EinheitMaximaleLaenge = 30;

    /// <summary>Format des Tags.</summary>
    public const string TagFormat = "yyyy-MM-dd";

    public static string JsonName(EinkaufspostenFeld feld) => feld switch
    {
        EinkaufspostenFeld.Bezeichnung => Bezeichnung,
        EinkaufspostenFeld.Menge => Menge,
        EinkaufspostenFeld.Einheit => Einheit,
        EinkaufspostenFeld.Tag => Tag,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
