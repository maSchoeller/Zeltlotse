using System.Globalization;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Ui.Texte;

namespace Zeltlotse.App.Features.Finanzen;

/// <summary>Story 5-1: Gemeinsames der Finanzen-Seiten: Texte, Ziele der Links und die Rechnung für die Balken.</summary>
internal static class FinanzenTexte
{
    public const string AbgeschlossenText = "Diese Freizeit ist abgeschlossen und nur lesbar.";

    /// <summary>Satz statt eines Beitrags bei 0 Teilnehmenden.</summary>
    public const string KeinBeitragText = "Ohne Teilnehmende lässt sich kein Beitrag berechnen.";

    /// <summary>Zahl eines Balkens ohne Beitrag.</summary>
    public const string OhneBeitrag = "–";

    /// <summary>Die Kalkulation der Freizeit.</summary>
    public static string KalkulationZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/finanzen/kalkulation";

    /// <summary>Der Beitragsrechner der Freizeit.</summary>
    public static string BeitragsrechnerZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/finanzen/beitragsrechner";

    /// <summary>Der Beitrag als „275,00 €“ oder ohne Teilnehmende „–“.</summary>
    public static string Beitrag(decimal? beitrag) => beitrag is { } wert ? Anzeige.Euro(wert) : OhneBeitrag;

    /// <summary>Bezeichnung des zweiten Werts einer Kategorie (FR-55): bei Verpflegung der Tagessatz.</summary>
    public static string ProPersonUndTagText(Kostenkategorie kategorie) =>
        kategorie == Kostenkategorie.Verpflegung ? "Tagessatz pro Person und Tag" : "Kosten pro Person und Tag";

    /// <summary>Kennung einer Kategorie-Card, z. B. „kategorie-Verpflegung“.</summary>
    public static string KategorieId(Kostenkategorie kategorie) => $"kategorie-{kategorie}";

    /// <summary>„12 Tage“, „1 Tag“.</summary>
    public static string Tage(int tage) => Anzeige.Tage(tage);

    /// <summary>Anteil in Prozent (0 bis 100) von <paramref name="wert"/> an <paramref name="ganzes"/>; bei 0 als Ganzem 0.</summary>
    public static double Prozent(decimal wert, decimal ganzes) => ganzes <= 0 ? 0 : (double)(Math.Clamp(wert, 0m, ganzes) / ganzes * 100m);

    /// <summary>Statuswort eines Kategorie-Balkens, z. B. „66 % der Kosten“.</summary>
    public static string Anteil(decimal kosten, decimal gesamtkosten) =>
        $"{Math.Round((decimal)Prozent(kosten, gesamtkosten), 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)} % der Kosten";

    /// <summary>
    /// Statuswort des Balkens „Teilnehmerbeitrag“: welcher Anteil der Gesamtkosten über die Beiträge läuft, z. B. „90 % der Kosten über Beiträge“; ohne
    /// Teilnehmende „Kein Beitrag“, ohne Kosten „Keine Kosten“.
    /// </summary>
    public static string Beitragsanteil(Beitragsergebnis ergebnis, int teilnehmende) => ergebnis switch
    {
        { Teilnehmerbeitrag: null } => "Kein Beitrag",
        { Gesamtkosten: <= 0 } => "Keine Kosten",
        _ => $"{Math.Round((decimal)Prozent(ergebnis.Teilnehmerbeitrag.Value * teilnehmende, ergebnis.Gesamtkosten), 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)} % der Kosten über Beiträge",
    };

    /// <summary>
    /// Statuswort des Beitragsrechners: „Günstiger“, „Gleich“ oder „Teurer“ als „Mit Reglern“ gegenüber „Laut Kalkulation“. Fehlt auf einer Seite der
    /// Beitrag (0 Teilnehmende), gibt es keinen Vergleich: „–“, ohne Beitrag auf beiden Seiten „Gleich“.
    /// </summary>
    public static string Vergleich(decimal? laut, decimal? mitReglern) => (laut, mitReglern) switch
    {
        (null, null) => "Gleich",
        (null, _) or (_, null) => OhneBeitrag,
        ({ } a, { } b) when a > b => "Günstiger",
        ({ } a, { } b) when a < b => "Teurer",
        _ => "Gleich",
    };

    /// <summary>Die Differenz mit Vorzeichen, z. B. „+0,20 €“, „−14,20 €“, „0,00 €“; nur mit Beitrag auf beiden Seiten.</summary>
    public static string Differenz(decimal laut, decimal mitReglern)
    {
        var differenz = mitReglern - laut;
        return differenz > 0 ? $"+{Anzeige.Euro(differenz)}" : Anzeige.Euro(differenz);
    }

    /// <summary>Story 5-1: Kleinster Wert des Tagessatz-Reglers: der gespeicherte Tagessatz liegt im Raster von 0,50 € (7,25 → 0,25).</summary>
    public static decimal TagessatzMinimum(decimal laut) => laut % TagessatzSchritt;

    /// <summary>Größter Wert des Tagessatz-Reglers: mindestens 30 € bzw. der gespeicherte Wert, auf das Raster ab <see cref="TagessatzMinimum"/> aufgerundet.</summary>
    public static decimal TagessatzMaximum(decimal laut)
    {
        var minimum = TagessatzMinimum(laut);
        return minimum + Math.Ceiling((Math.Max(30m, laut) - minimum) / TagessatzSchritt) * TagessatzSchritt;
    }

    public const decimal TagessatzSchritt = 0.5m;
}
