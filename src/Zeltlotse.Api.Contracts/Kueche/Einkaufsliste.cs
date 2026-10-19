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
/// <param name="Freigegeben">Story 4-3, FR-51: Die Küche hat den Posten zum Einkaufen freigegeben; ohne Stand nicht freigegeben.</param>
/// <param name="Gekauft">Story 4-3, FR-51: Der Posten ist abgehakt; unabhängig von <paramref name="Freigegeben"/>.</param>
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
    uint Version,
    bool Freigegeben,
    bool Gekauft);

/// <summary>Story 4-2: Körper von <c>POST …/kueche/einkaufsliste/posten</c>: nur die Bezeichnung ist Pflicht.</summary>
/// <param name="Menge">Zahl größer 0 als Text wie bei Zutaten; leer heißt ohne Menge.</param>
/// <param name="Tag">Ein Tag der Freizeit als <c>JJJJ-MM-TT</c>; leer heißt ohne Tag.</param>
public sealed record EinkaufspostenNeu(string? Bezeichnung, string? Menge, string? Einheit, string? Tag);

/// <summary>
/// Story 4-2: Die bearbeitbaren Felder eines berechneten Postens (<c>PATCH …/einkaufsliste/gerichte/{gerichtId}/zutaten/{zutatId}</c>, AD-19). Story 4-3:
/// dazu <see cref="Freigegeben"/> und <see cref="Gekauft"/>.
/// </summary>
public enum HandMengeFeld
{
    Menge,
    Freigegeben,
    Gekauft,
}

/// <summary>
/// Story 4-2: JSON-Namen der Felder eines berechneten Postens. Körper <c>{ "version": n, "alterWert": …, "menge": neu }</c>; der alte Wert ist der
/// angezeigte Text (Hand-Menge oder berechnete Menge). Leer heißt: zurück zur berechneten Menge. Antwort <c>{ "version": n, "menge": angezeigt }</c>,
/// deutsch mit Komma. Story 4-3: <c>"freigegeben"</c> und <c>"gekauft"</c> mit den Werten <c>"true"</c>/<c>"false"</c> (alter Wert ebenso); ohne Stand gilt
/// <c>"false"</c>. Antwort <c>{ "version": n, "&lt;feld&gt;": "true"|"false" }</c>.
/// </summary>
public static class HandMengeFelder
{
    public const string Menge = "menge";

    public const string Freigegeben = "freigegeben";

    public const string Gekauft = "gekauft";

    public static string JsonName(HandMengeFeld feld) => feld switch
    {
        HandMengeFeld.Menge => Menge,
        HandMengeFeld.Freigegeben => Freigegeben,
        HandMengeFeld.Gekauft => Gekauft,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}

/// <summary>
/// Story 4-2: Bearbeitbare Felder eines Hand-Postens (<c>PATCH …/kueche/einkaufsliste/posten/{postenId}</c>, AD-19). Story 4-3: dazu
/// <see cref="Freigegeben"/> und <see cref="Gekauft"/>.
/// </summary>
public enum EinkaufspostenFeld
{
    Bezeichnung,
    Menge,
    Einheit,
    Tag,
    Freigegeben,
    Gekauft,
}

/// <summary>
/// Story 4-2: JSON-Namen und Grenzen der Felder eines Hand-Postens. Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"menge"|"einheit"|"tag"|"freigegeben"|"gekauft": neu }</c> mit genau einem Feld als Text; die
/// Menge wie bei Zutaten (<see cref="ZutatFelder.ZahlText"/>), der Tag als <c>JJJJ-MM-TT</c> oder leer; Story 4-3: <c>freigegeben</c> und
/// <c>gekauft</c> als <c>"true"</c>/<c>"false"</c>.
/// </summary>
public static class EinkaufspostenFelder
{
    public const string Bezeichnung = "bezeichnung";

    public const string Menge = "menge";

    public const string Einheit = "einheit";

    public const string Tag = "tag";

    public const string Freigegeben = "freigegeben";

    public const string Gekauft = "gekauft";

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
        EinkaufspostenFeld.Freigegeben => Freigegeben,
        EinkaufspostenFeld.Gekauft => Gekauft,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}

/// <summary>
/// Story 4-3, FR-51: Körper von <c>POST /api/freizeiten/{freizeitId}/kueche/einkaufsliste/freigabe</c>: setzt <c>freigegeben</c> für alle Posten einer
/// Tagesgruppe der Einkaufsliste (berechnete Posten der Gerichte an Mahlzeiten dieses Tags und Hand-Posten mit diesem Tag); <c>gekauft</c> bleibt.
/// Antwort <c>204</c>, auch wenn sich nichts ändert.
/// </summary>
/// <param name="Tag">Ein Tag der Freizeit als <c>JJJJ-MM-TT</c>; <c>null</c> steht für die Hand-Posten ohne Tag. Sonst <c>400</c> am Feld <c>tag</c>.</param>
/// <param name="Freigegeben">Pflicht; fehlt es, <c>400</c> am Feld <c>freigegeben</c>.</param>
public sealed record EinkaufFreigabe(string? Tag, bool? Freigegeben);

/// <summary>
/// Story 4-3, FR-51: Antwort von <c>GET /api/freizeiten/{freizeitId}/kueche/einkauf</c> (Leitung und Team, Modul Verpflegung): die Seite der
/// Einkaufenden. Die Tage wie in der <see cref="Einkaufsliste"/>, aber nur Posten mit <see cref="EinkaufslistenPosten.Freigegeben"/>; Tage ohne solche
/// Posten fehlen. Ohne Allergen-Übersicht; <see cref="EinkaufsTag.Ersatzportionen"/> ist immer leer (kein Abgleich, kein Protokoll).
/// </summary>
public sealed record Einkauf(IReadOnlyList<EinkaufsTag> Tage);
