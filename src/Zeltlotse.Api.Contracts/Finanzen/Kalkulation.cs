using System.Globalization;
using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Finanzen;

/// <summary>
/// Story 5-1 (FR-54): Die sechs festen Kostenkategorien der Kalkulation in fester Reihenfolge (Roadmap V5). In JSON und in der Route
/// <c>…/kategorien/{kategorie}</c> steht der Name, z. B. <c>Verpflegung</c>. Nur anhängen.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Kostenkategorie>))]
public enum Kostenkategorie
{
    Unterkunft,
    Verpflegung,
    Fahrt,
    ProgrammUndMaterial,
    Mitarbeitende,
    Sonstiges,
}

/// <summary>Story 5-1: Namen und Lesen der Kostenkategorien.</summary>
public static class Kostenkategorien
{
    /// <summary>Alle sechs in fester Reihenfolge.</summary>
    public static IReadOnlyList<Kostenkategorie> Alle { get; } = Enum.GetValues<Kostenkategorie>();

    /// <summary>Der Anzeigename, z. B. „Programm und Material“.</summary>
    public static string Text(Kostenkategorie kategorie) => kategorie switch
    {
        Kostenkategorie.Unterkunft => "Unterkunft",
        Kostenkategorie.Verpflegung => "Verpflegung",
        Kostenkategorie.Fahrt => "Fahrt",
        Kostenkategorie.ProgrammUndMaterial => "Programm und Material",
        Kostenkategorie.Mitarbeitende => "Mitarbeitende",
        Kostenkategorie.Sonstiges => "Sonstiges",
        _ => throw new ArgumentOutOfRangeException(nameof(kategorie), kategorie, null),
    };

    /// <summary>Die Kategorie zu ihrem Namen (Groß- und Kleinschreibung egal), sonst <c>null</c>; Zahlen gelten nicht.</summary>
    public static Kostenkategorie? Lesen(string? name) =>
        Alle.Select(k => (Kostenkategorie?)k).FirstOrDefault(k => string.Equals(k.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Story 5-1: Antwort von <c>GET /api/freizeiten/{freizeitId}/finanzen/kalkulation</c> (Leitung und Team, Modul Finanzen, CAP-32, FR-54 bis FR-57):
/// Planzahlen, die sechs Kategorien in fester Reihenfolge, die Einnahmeposten (nach Kennung, also in der Reihenfolge des Anlegens) und das Ergebnis
/// nach <see cref="Beitragsrechnung"/>. Ohne gespeicherte Kalkulation: Planzahlen 0, alle Beträge 0, Version 0, keine Einnahmen.
/// </summary>
/// <param name="Version">Versionsnummer der Planzahlen für den Autosave (AD-19); 0, solange nichts gespeichert ist.</param>
/// <param name="Teilnehmende">Geplante Teilnehmende, 0 bis <see cref="KalkulationFelder.PlanzahlMaximum"/>.</param>
/// <param name="Mitarbeitende">Geplante Mitarbeitende, 0 bis <see cref="KalkulationFelder.PlanzahlMaximum"/>.</param>
/// <param name="Tage">Kalendertage der Freizeit, erster und letzter Tag eingeschlossen.</param>
public sealed record Kalkulation(
    uint Version,
    int Teilnehmende,
    int Mitarbeitende,
    int Tage,
    IReadOnlyList<KalkulationKategorie> Kategorien,
    IReadOnlyList<Einnahmeposten> Einnahmen,
    Beitragsergebnis Ergebnis);

/// <summary>Story 5-1: Eine Kostenkategorie der Kalkulation.</summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19); 0, solange nichts gespeichert ist.</param>
/// <param name="Fixkosten">Feste Kosten in Euro, unabhängig von Personen und Tagen.</param>
/// <param name="ProPersonUndTag">Kosten pro Person und Tag in Euro; bei Verpflegung der Tagessatz.</param>
/// <param name="Kosten">Fixkosten + Kosten pro Person und Tag × Personen × Tage.</param>
public sealed record KalkulationKategorie(Kostenkategorie Kategorie, uint Version, decimal Fixkosten, decimal ProPersonUndTag, decimal Kosten);

/// <summary>Story 5-1: Ein Einnahmeposten, z. B. ein Zuschuss oder ein Rabatt der Organisation (positiver Betrag).</summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
public sealed record Einnahmeposten(Guid Id, uint Version, string Bezeichnung, decimal Betrag);

/// <summary>Story 5-1: Körper von <c>POST …/finanzen/kalkulation/einnahmen</c>: die Bezeichnung ist Pflicht, der Betrag beginnt ohne Angabe bei 0.</summary>
/// <param name="Bezeichnung">1 bis <see cref="EinnahmepostenFelder.BezeichnungMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="Betrag">Euro als Text mit Komma oder Punkt (wie im Autosave, AD-19); leer heißt 0.</param>
public sealed record EinnahmepostenNeu(string? Bezeichnung, string? Betrag);

/// <summary>Story 5-1: Ergebnis der Kalkulation.</summary>
/// <param name="Gesamtkosten">Summe der Kosten aller Kategorien.</param>
/// <param name="Einnahmen">Summe der Einnahmeposten.</param>
/// <param name="Teilnehmerbeitrag">
/// (Gesamtkosten − Einnahmen) ÷ Teilnehmende, kaufmännisch auf Cent gerundet, nie unter 0; ohne Teilnehmende <c>null</c>.
/// </param>
public sealed record Beitragsergebnis(decimal Gesamtkosten, decimal Einnahmen, decimal? Teilnehmerbeitrag);

/// <summary>Story 5-1: Fixkosten und Kosten pro Person und Tag einer Kategorie als Eingabe der <see cref="Beitragsrechnung"/>.</summary>
public sealed record Kostenansatz(decimal Fixkosten, decimal ProPersonUndTag);

/// <summary>
/// Story 5-1 (FR-54 bis FR-57): Die eine Rechenregel der Kalkulation, gleich für den Server (Ergebnis im GET) und den Beitragsrechner im Client, damit
/// beide nicht auseinanderlaufen. Personen = Teilnehmende + Mitarbeitende; die Kosten der Mitarbeitenden werden so auf die Teilnehmerbeiträge umgelegt.
/// </summary>
public static class Beitragsrechnung
{
    /// <summary>Kosten einer Kategorie: Fixkosten + Kosten pro Person und Tag × Personen × Tage.</summary>
    public static decimal Kosten(Kostenansatz ansatz, int teilnehmende, int mitarbeitende, int tage)
    {
        ArgumentNullException.ThrowIfNull(ansatz);
        return ansatz.Fixkosten + ansatz.ProPersonUndTag * (teilnehmende + mitarbeitende) * tage;
    }

    /// <summary>
    /// Gesamtkosten (Summe der Kategorien), Einnahmen (Summe der Posten) und Teilnehmerbeitrag: (Gesamtkosten − Einnahmen) ÷ Teilnehmende,
    /// kaufmännisch auf Cent gerundet (<see cref="MidpointRounding.AwayFromZero"/>), nie unter 0 €; bei 0 Teilnehmenden <c>null</c>.
    /// </summary>
    public static Beitragsergebnis Rechnen(int teilnehmende, int mitarbeitende, int tage, IEnumerable<Kostenansatz> kategorien, IEnumerable<decimal> einnahmen)
    {
        ArgumentNullException.ThrowIfNull(kategorien);
        ArgumentNullException.ThrowIfNull(einnahmen);
        var gesamtkosten = kategorien.Sum(k => Kosten(k, teilnehmende, mitarbeitende, tage));
        var summeEinnahmen = einnahmen.Sum();
        decimal? beitrag = teilnehmende > 0
            ? Math.Max(0m, Math.Round((gesamtkosten - summeEinnahmen) / teilnehmende, 2, MidpointRounding.AwayFromZero))
            : null;
        return new Beitragsergebnis(gesamtkosten, summeEinnahmen, beitrag);
    }

    /// <summary>Kalendertage vom ersten bis zum letzten Tag, beide eingeschlossen.</summary>
    public static int Tage(DateOnly ersterTag, DateOnly letzterTag) => letzterTag.DayNumber - ersterTag.DayNumber + 1;
}

/// <summary>Story 5-1: Bearbeitbare Felder der Planzahlen für <c>PATCH …/finanzen/kalkulation</c> (AD-19).</summary>
public enum KalkulationFeld
{
    Teilnehmende,
    Mitarbeitende,
}

/// <summary>
/// Story 5-1: JSON-Namen und Grenzen der Planzahlen. <c>PATCH …/finanzen/kalkulation</c>: Körper
/// <c>{ "version": n, "alterWert": …, "teilnehmende"|"mitarbeitende": neu }</c> mit genau einem Feld als Text (ganze Zahl), Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>, z. B. <c>"40"</c>.
/// </summary>
public static class KalkulationFelder
{
    public const string Teilnehmende = "teilnehmende";

    public const string Mitarbeitende = "mitarbeitende";

    public const int PlanzahlMinimum = 0;

    public const int PlanzahlMaximum = 1000;

    public static string JsonName(KalkulationFeld feld) => feld switch
    {
        KalkulationFeld.Teilnehmende => Teilnehmende,
        KalkulationFeld.Mitarbeitende => Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}

/// <summary>Story 5-1: Bearbeitbare Felder einer Kategorie für <c>PATCH …/finanzen/kalkulation/kategorien/{kategorie}</c> (AD-19).</summary>
public enum KategorieFeld
{
    Fixkosten,
    ProPersonUndTag,
}

/// <summary>
/// Story 5-1: JSON-Namen und Grenzen der Beträge einer Kategorie. <c>PATCH …/kategorien/{kategorie}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "fixkosten"|"proPersonUndTag": neu }</c> mit genau einem Feld als Text, Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>. Beträge kommen deutsch mit Komma zurück, ohne Tausenderpunkte und ohne Null-Nachkommastellen
/// (<c>"7.50"</c> → <c>"7,5"</c>, siehe <see cref="BetragText"/>).
/// </summary>
public static class KategorieFelder
{
    public const string Fixkosten = "fixkosten";

    public const string ProPersonUndTag = "proPersonUndTag";

    /// <summary>Größter erlaubter Betrag in Euro.</summary>
    public const decimal BetragMaximum = 1_000_000m;

    /// <summary>Höchstens so viele Nachkommastellen hat ein Betrag (Cent).</summary>
    public const int BetragNachkommastellen = 2;

    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    public static string JsonName(KategorieFeld feld) => feld switch
    {
        KategorieFeld.Fixkosten => Fixkosten,
        KategorieFeld.ProPersonUndTag => ProPersonUndTag,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Ein Betrag deutsch mit Komma, ohne Tausenderpunkte und ohne Null-Nachkommastellen: 7,50 → „7,5“, 2000,00 → „2000“, 0 → „0“.</summary>
    public static string BetragText(decimal betrag) => betrag.ToString("0.##", Deutsch);
}

/// <summary>Story 5-1: Bearbeitbare Felder eines Einnahmepostens für <c>PATCH …/finanzen/kalkulation/einnahmen/{id}</c> (AD-19).</summary>
public enum EinnahmepostenFeld
{
    Bezeichnung,
    Betrag,
}

/// <summary>
/// Story 5-1: JSON-Namen und Grenzen der Felder eines Einnahmepostens. <c>PATCH …/einnahmen/{id}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"betrag": neu }</c> mit genau einem Feld als Text; der Betrag wie bei
/// <see cref="KategorieFelder.BetragText"/>.
/// </summary>
public static class EinnahmepostenFelder
{
    public const string Bezeichnung = "bezeichnung";

    public const string Betrag = "betrag";

    public const int BezeichnungMaximaleLaenge = 200;

    public static string JsonName(EinnahmepostenFeld feld) => feld switch
    {
        EinnahmepostenFeld.Bezeichnung => Bezeichnung,
        EinnahmepostenFeld.Betrag => Betrag,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
