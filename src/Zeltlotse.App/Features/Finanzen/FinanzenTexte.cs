using System.Globalization;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Texte;
using ApiBeitraege = Zeltlotse.Api.Contracts.Finanzen.Beitraege;

namespace Zeltlotse.App.Features.Finanzen;

/// <summary>
/// Story 5-1: Gemeinsames der Finanzen-Seiten: Texte, Ziele der Links und die Rechnung für die Balken. Story 5-2: Ziele, Namen der Zahlenden und die Balken
/// von Soll-Ist und Erstattungen. Story 5-3: Ziel, Namen der Teilnehmenden, Balken und Hinweis der Beiträge.
/// </summary>
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

    // ---------- Story 5-2: Belege, Soll-Ist und Erstattung ----------

    /// <summary>Nach dem Abschluss: nur der Haken „Erstattet“ lässt sich noch setzen (AD-17).</summary>
    public const string NurErstattetText = "Diese Freizeit ist abgeschlossen. Nur „Erstattet“ lässt sich noch ändern.";

    public const string GeloeschtePersonText = "Gelöschte Person";

    public const string OhneNamenText = "Ohne Namen";

    /// <summary>Text der Meldung, wenn der Upload scheitert; „Erneut versuchen“ lädt an denselben Beleg.</summary>
    public const string NichtHochgeladenText = "Datei nicht hochgeladen.";

    /// <summary>Die Belege der Freizeit.</summary>
    public static string BelegeZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/finanzen/belege";

    /// <summary>Ein Beleg der Freizeit.</summary>
    public static string BelegZiel(Guid freizeitId, Guid belegId) => $"freizeiten/{freizeitId}/finanzen/belege/{belegId}";

    /// <summary>Der Soll-Ist-Vergleich der Freizeit.</summary>
    public static string SollIstZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/finanzen/soll-ist";

    /// <summary>
    /// Je Person der Leitung (aus der Freizeit) und des Teams (bestätigte Mitarbeitende der Personenliste) der Name, wie <c>ProgrammTexte.Namen</c> (AD-2:
    /// Finanzen kennt keine Namen). Fehlt eine Quelle, fehlen nur ihre Namen.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> Namen(FreizeitAntwort? freizeit, IEnumerable<PersonenlisteZeile>? personen)
    {
        var namen = new Dictionary<Guid, string>();
        foreach (var person in (personen ?? []).Where(p => p.Art == AnmeldungArt.Mitarbeitende && p.Status == AnmeldungStatus.Bestaetigt))
        {
            namen[person.PersonId] = Anzeige.Name(person.Vorname, person.Nachname);
        }

        foreach (var leitung in freizeit?.Leitung ?? [])
        {
            namen[leitung.PersonId] = Anzeige.Name(leitung.Vorname, leitung.Nachname);
        }

        return namen;
    }

    /// <summary>Lädt Freizeit und Personenliste für die Namen; scheitert ein Teil, fehlen nur dessen Namen.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> NamenLadenAsync(FreizeitenApiClient freizeiten, Guid freizeitId)
    {
        var freizeit = Leise(freizeiten.LadenAsync(freizeitId));
        var personen = Leise(freizeiten.PersonenlisteLadenAsync(freizeitId));
        await Task.WhenAll(freizeit, personen);
        return Namen(freizeit.Result, personen.Result);
    }

    private static async Task<T?> Leise<T>(Task<T?> abruf)
        where T : class
    {
        try
        {
            return await abruf;
        }
        catch (Exception fehler) when (Zeltlotse.App.Features.Konten.AnmeldungAbschluss.IstAktionsfehler(fehler))
        {
            return null;
        }
    }

    /// <summary>Der Name der Person, die bezahlt hat: nach dem Löschen „Gelöschte Person“, ohne bekannten Namen „Ohne Namen“ (AD-2: Namen setzt der Client zusammen).</summary>
    public static string Zahler(Guid personId, IReadOnlyDictionary<Guid, string> namen) => Name(personId, namen);

    /// <summary>Der Name einer Person: nach dem Löschen „Gelöschte Person“, ohne bekannten Namen „Ohne Namen“.</summary>
    public static string Name(Guid personId, IReadOnlyDictionary<Guid, string> namen) =>
        personId == BelegFelder.GeloeschtePerson ? GeloeschtePersonText : namen.GetValueOrDefault(personId) is { Length: > 0 } name ? name : OhneNamenText;

    /// <summary>Die Überschrift eines Belegs: die Bezeichnung oder, ohne Bezeichnung, die Kategorie.</summary>
    public static string Titel(Beleg beleg) => beleg.Bezeichnung ?? Kostenkategorien.Text(beleg.Kategorie);

    /// <summary>„Foto“ oder „PDF“ für den Link zur Datei.</summary>
    public static string DateiText(BelegDatei datei) => datei.IstBild ? "Foto" : "PDF";

    /// <summary>Die Optionen „Kategorie“ in fester Reihenfolge.</summary>
    public static IReadOnlyList<AuswahlOption> KategorieOptionen { get; } =
        [.. Kostenkategorien.Alle.Select(k => new AuswahlOption(k.ToString(), Kostenkategorien.Text(k)))];

    /// <summary>
    /// Die Optionen „Bezahlt von“: je Person der Leitung und des Teams der Name, alphabetisch; ist der aktuelle Zahler keiner davon (z. B. eine gelöschte
    /// Person), steht er vorne.
    /// </summary>
    public static IReadOnlyList<AuswahlOption> ZahlerOptionen(IReadOnlyDictionary<Guid, string> namen, Guid? aktuell = null)
    {
        var optionen = namen.OrderBy(n => n.Value, StringComparer.Create(Deutsch, ignoreCase: true)).Select(n => new AuswahlOption(n.Key.ToString("D"), n.Value)).ToList();
        if (aktuell is { } person && !namen.ContainsKey(person))
        {
            optionen.Insert(0, new AuswahlOption(person.ToString("D"), Zahler(person, namen)));
        }

        return optionen;
    }

    /// <summary>Das Statuswort einer Soll-Ist-Zeile (NFR-16).</summary>
    public static string StatusWort(SollIstStatus status) => status switch
    {
        SollIstStatus.Knapp => "Knapp",
        SollIstStatus.Ueberschritten => "Überschritten",
        _ => "Im Plan",
    };

    /// <summary>Die Art des Balkens: Knapp ist eine Warnung, Überschritten ein Fehler.</summary>
    public static BalkenArt Art(SollIstStatus status) => status switch
    {
        SollIstStatus.Knapp => BalkenArt.Warnung,
        SollIstStatus.Ueberschritten => BalkenArt.Fehler,
        _ => BalkenArt.Normal,
    };

    /// <summary>„3.900,00 € von 4.200,00 € (93 %)“; ohne Soll ohne Prozent.</summary>
    public static string IstVonSoll(SollIstZeile zeile) =>
        zeile.Soll > 0
            ? $"{Anzeige.Euro(zeile.Ist)} von {Anzeige.Euro(zeile.Soll)} ({Math.Round(zeile.Ist / zeile.Soll * 100m, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)} %)"
            : $"{Anzeige.Euro(zeile.Ist)} von {Anzeige.Euro(zeile.Soll)}";

    /// <summary>Die Füllung: Ist im Verhältnis zum Soll; ohne Soll mit Ist ganz voll.</summary>
    public static double IstProzent(SollIstZeile zeile) => zeile.Soll > 0 ? Prozent(zeile.Ist, zeile.Soll) : zeile.Ist > 0 ? 100 : 0;

    /// <summary>Statuswort einer Erstattung: „Bekommt noch Geld“ (Warnung) oder „Alles erstattet“.</summary>
    public static string ErstattungWort(Erstattung erstattung) => erstattung.Offen > 0 ? "Bekommt noch Geld" : "Alles erstattet";

    public static BalkenArt ErstattungArt(Erstattung erstattung) => erstattung.Offen > 0 ? BalkenArt.Warnung : BalkenArt.Normal;

    /// <summary>Die Zahl einer Erstattung, z. B. „noch 45,80 €“.</summary>
    public static string ErstattungZahl(Erstattung erstattung) => $"noch {Anzeige.Euro(erstattung.Offen)}";

    /// <summary>Der Anteil, der schon erstattet ist.</summary>
    public static double ErstattetProzent(Erstattung erstattung) => Prozent(erstattung.Erstattet, erstattung.Offen + erstattung.Erstattet);

    // ---------- Story 5-3: Beitrag bezahlt abhaken ----------

    /// <summary>Fehler am Haken, wenn das Speichern scheitert (Netz, Server).</summary>
    public const string BeitragNichtGespeichertText = "Nicht gespeichert. Bitte noch einmal tippen.";

    public const string KeineTeilnehmendenText = "Noch keine bestätigten Teilnehmenden.";

    /// <summary>Die Beiträge der Freizeit.</summary>
    public static string BeitraegeZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/finanzen/beitraege";

    /// <summary>Je bestätigter teilnehmender Person der Personenliste der Name (AD-2: Finanzen kennt keine Namen).</summary>
    public static IReadOnlyDictionary<Guid, string> TeilnehmendeNamen(IEnumerable<PersonenlisteZeile>? personen)
    {
        var namen = new Dictionary<Guid, string>();
        foreach (var person in (personen ?? []).Where(p => p.Art == AnmeldungArt.Teilnehmende && p.Status == AnmeldungStatus.Bestaetigt))
        {
            namen[person.PersonId] = Anzeige.Name(person.Vorname, person.Nachname);
        }

        return namen;
    }

    /// <summary>Die Zeilen nach dem Namen der teilnehmenden Person sortiert.</summary>
    public static IReadOnlyList<BeitragZeile> NachNamen(IEnumerable<BeitragZeile> zeilen, IReadOnlyDictionary<Guid, string> namen) =>
        [.. zeilen.OrderBy(z => Name(z.PersonId, namen), StringComparer.Create(Deutsch, ignoreCase: true)).ThenBy(z => z.PersonId)];

    /// <summary>Die Zahl des Balkens, z. B. „1 von 2 bezahlt“.</summary>
    public static string BezahltZahl(ApiBeitraege beitraege) => $"{beitraege.Bezahlt.ToString(Deutsch)} von {beitraege.Gesamt.ToString(Deutsch)} bezahlt";

    /// <summary>Statuswort des Balkens: „Alle bezahlt“ oder „Offen“ (NFR-16).</summary>
    public static string BezahltWort(ApiBeitraege beitraege) => beitraege.Bezahlt >= beitraege.Gesamt ? "Alle bezahlt" : "Offen";

    /// <summary>Solange Beiträge offen sind, ist der Balken eine Warnung.</summary>
    public static BalkenArt BezahltArt(ApiBeitraege beitraege) => beitraege.Bezahlt >= beitraege.Gesamt ? BalkenArt.Normal : BalkenArt.Warnung;

    public static double BezahltProzent(ApiBeitraege beitraege) => Prozent(beitraege.Bezahlt, beitraege.Gesamt);

    /// <summary>Der Hinweis am Haken, z. B. „Abgehakt von Lea Feuerstein am 20.10.2026“; ohne Haken <c>null</c>.</summary>
    public static string? AbgehaktHinweis(BeitragZeile zeile, IReadOnlyDictionary<Guid, string> namen) =>
        zeile is { Bezahlt: true, AbgehaktVon: { } von, AbgehaktAm: { } am } ? $"Abgehakt von {Name(von, namen)} am {Anzeige.Datum(am)}" : null;

    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");
}
