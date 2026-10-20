using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Finanzen;

/// <summary>
/// Story 5-2 (CAP-33, FR-58): Ein Beleg einer Freizeit in <c>GET …/finanzen/belege</c> und als Antwort von <c>POST …/finanzen/belege</c> (Leitung und
/// Team, Modul Finanzen). Die Liste ist nach „erfasst am“ sortiert, neueste zuerst.
/// </summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19), auch für <c>PATCH …/erstattet</c>.</param>
/// <param name="Betrag">Euro, 0 bis <see cref="KategorieFelder.BetragMaximum"/>, ohne Null-Nachkommastellen.</param>
/// <param name="Bezeichnung">Optional, höchstens <see cref="BelegFelder.BezeichnungMaximaleLaenge"/> Zeichen; ohne Angabe <c>null</c>.</param>
/// <param name="BezahltVon">
/// Die Person, die bezahlt hat (Leitung oder Team der Freizeit). Den Namen setzt der Client aus Freizeit und Personenliste zusammen (AD-2); nach dem
/// Löschen der Person steht hier <see cref="BelegFelder.GeloeschtePerson"/>.
/// </param>
/// <param name="Erstattet">Ob der Betrag der Person erstattet ist; auch nach dem Abschluss änderbar (AD-17).</param>
/// <param name="ErfasstAm">Wann der Beleg angelegt wurde; ein eigenes Belegdatum gibt es nicht.</param>
/// <param name="Datei">Foto oder PDF des Belegs, falls hochgeladen.</param>
public sealed record Beleg(
    Guid Id,
    uint Version,
    Kostenkategorie Kategorie,
    decimal Betrag,
    string? Bezeichnung,
    Guid BezahltVon,
    bool Erstattet,
    DateTimeOffset ErfasstAm,
    BelegDatei? Datei);

/// <summary>Story 5-2: Die Datei eines Belegs; ausgeliefert über <c>GET …/belege/{id}/datei</c> (Bilder in der Seite, PDF als Anhang).</summary>
/// <param name="Dateiname">Name beim Ausliefern, z. B. „Kassenbon.png“.</param>
/// <param name="IstBild">Foto (JPEG, PNG, WebP); sonst PDF.</param>
public sealed record BelegDatei(string Dateiname, bool IstBild);

/// <summary>
/// Story 5-2: Körper von <c>POST …/finanzen/belege</c>. Pflicht sind nur Kategorie und Betrag (NFR-12); die Datei kommt danach getrennt über
/// <c>POST …/belege/{id}/datei</c>, damit ein gescheiterter Upload den Beleg nicht verliert.
/// </summary>
/// <param name="Kategorie">Name einer <see cref="Kostenkategorie"/>, z. B. <c>Verpflegung</c>.</param>
/// <param name="Betrag">Euro als Text mit Komma oder Punkt (wie im Autosave, AD-19).</param>
/// <param name="Bezeichnung">Optional, z. B. „Großeinkauf Lebensmittel“.</param>
/// <param name="BezahltVon">Kennung einer Person aus Leitung oder Team; leer heißt: die angemeldete Person.</param>
public sealed record BelegNeu(string? Kategorie, string? Betrag, string? Bezeichnung = null, string? BezahltVon = null);

/// <summary>Story 5-2: Stand einer Zeile im Soll-Ist-Vergleich (NFR-16: Zahl und Wort).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SollIstStatus>))]
public enum SollIstStatus
{
    /// <summary>Ist höchstens 90 % vom Soll.</summary>
    ImPlan,

    /// <summary>Ist über 90 % bis 100 % vom Soll.</summary>
    Knapp,

    /// <summary>Ist über dem Soll.</summary>
    Ueberschritten,
}

/// <summary>
/// Story 5-2 (FR-59, NS-8): Antwort von <c>GET …/finanzen/soll-ist</c>, bei jedem Aufruf neu gerechnet: je Kategorie in fester Reihenfolge Soll (Kosten laut
/// Kalkulation) und Ist (Summe der Belege), dazu die Zeile „Gesamt“ und je zahlender Person die Erstattungen.
/// </summary>
/// <param name="Erstattungen">Je Person, die einen Beleg bezahlt hat, nach <see cref="Erstattung.Offen"/> absteigend.</param>
public sealed record SollIst(IReadOnlyList<SollIstZeile> Zeilen, SollIstZeile Gesamt, IReadOnlyList<Erstattung> Erstattungen);

/// <summary>Story 5-2: Eine Zeile des Soll-Ist-Vergleichs.</summary>
/// <param name="Kategorie">Die Kategorie; bei der Zeile „Gesamt“ <c>null</c>.</param>
/// <param name="Soll">Kosten laut Kalkulation in Euro.</param>
/// <param name="Ist">Summe der Belege in Euro.</param>
public sealed record SollIstZeile(Kostenkategorie? Kategorie, decimal Soll, decimal Ist, SollIstStatus Status);

/// <summary>Story 5-2: Was eine Person bezahlt hat, aufgeteilt in noch offen und schon erstattet.</summary>
/// <param name="PersonId">Die zahlende Person (oder <see cref="BelegFelder.GeloeschtePerson"/>).</param>
/// <param name="Offen">Summe ihrer nicht erstatteten Belege.</param>
/// <param name="Erstattet">Summe ihrer erstatteten Belege.</param>
public sealed record Erstattung(Guid PersonId, decimal Offen, decimal Erstattet);

/// <summary>
/// Story 5-2 (FR-59): Die eine Rechnung des Soll-Ist-Vergleichs als reine Funktion. Status: Ist ≤ 90 % vom Soll „Im Plan“, über 90 % bis 100 % „Knapp“,
/// über dem Soll „Überschritten“; bei Soll 0 ist Ist 0 „Im Plan“ und Ist über 0 „Überschritten“.
/// </summary>
public static class SollIstRechnung
{
    /// <summary>Die Grenze zu „Knapp“: 90 % vom Soll.</summary>
    public const decimal KnappAb = 0.9m;

    public static SollIstStatus Status(decimal soll, decimal ist)
    {
        if (ist > soll)
        {
            return SollIstStatus.Ueberschritten;
        }

        return soll > 0 && ist > soll * KnappAb ? SollIstStatus.Knapp : SollIstStatus.ImPlan;
    }

    /// <summary>Eine Zeile mit ihrem Status; Beträge ohne Null-Nachkommastellen.</summary>
    public static SollIstZeile Zeile(Kostenkategorie? kategorie, decimal soll, decimal ist) => new(kategorie, OhneNullen(soll), OhneNullen(ist), Status(soll, ist));

    /// <summary>
    /// Der Vergleich: je Kategorie in fester Reihenfolge das Soll aus <paramref name="soll"/> (fehlt eine Kategorie, 0) und die Summe der Belege, dazu
    /// „Gesamt“ und die Erstattungen.
    /// </summary>
    public static SollIst Rechnen(IReadOnlyDictionary<Kostenkategorie, decimal> soll, IEnumerable<BelegBetrag> belege)
    {
        ArgumentNullException.ThrowIfNull(soll);
        ArgumentNullException.ThrowIfNull(belege);
        var liste = belege.ToList();
        var zeilen = Kostenkategorien.Alle
            .Select(k => Zeile(k, soll.GetValueOrDefault(k), liste.Where(b => b.Kategorie == k).Sum(b => b.Betrag)))
            .ToList();
        return new SollIst(zeilen, Zeile(null, zeilen.Sum(z => z.Soll), zeilen.Sum(z => z.Ist)), Erstattungen(liste));
    }

    /// <summary>Je zahlender Person offen und erstattet, nach Offen absteigend; bei Gleichstand nach Erstattet absteigend, dann nach Kennung.</summary>
    public static IReadOnlyList<Erstattung> Erstattungen(IEnumerable<BelegBetrag> belege)
    {
        ArgumentNullException.ThrowIfNull(belege);
        return
        [
            .. belege
                .GroupBy(b => b.PersonId)
                .Select(g => new Erstattung(g.Key, OhneNullen(g.Where(b => !b.Erstattet).Sum(b => b.Betrag)), OhneNullen(g.Where(b => b.Erstattet).Sum(b => b.Betrag))))
                .OrderByDescending(e => e.Offen)
                .ThenByDescending(e => e.Erstattet)
                .ThenBy(e => e.PersonId),
        ];
    }

    /// <summary>Dieselbe Zahl ohne Null-Nachkommastellen (3.900,00 → 3.900), damit die Summen als JSON-Zahl schlicht bleiben.</summary>
    private static decimal OhneNullen(decimal zahl) => zahl / 1.000000000000000000000000000000000m;
}

/// <summary>Story 5-2: Die Angaben eines Belegs, die der Soll-Ist-Vergleich braucht.</summary>
public sealed record BelegBetrag(Kostenkategorie Kategorie, decimal Betrag, Guid PersonId, bool Erstattet);

/// <summary>Story 5-2: Bearbeitbare Felder eines Belegs für <c>PATCH …/finanzen/belege/{id}</c> (AD-19). Der Erstattungsvermerk hat eine eigene Route.</summary>
public enum BelegFeld
{
    Bezeichnung,
    Betrag,
    Kategorie,
    BezahltVon,
}

/// <summary>Story 5-2: Das Feld von <c>PATCH …/finanzen/belege/{id}/erstattet</c> (AD-19, auch nach dem Abschluss erlaubt, AD-17).</summary>
public enum BelegErstattetFeld
{
    Erstattet,
}

/// <summary>
/// Story 5-2: JSON-Namen und Grenzen der Felder eines Belegs. <c>PATCH …/belege/{id}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"betrag"|"kategorie"|"bezahltVon": neu }</c> mit genau einem Feld als Text; Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>: der Betrag wie <see cref="KategorieFelder.BetragText"/>, die Kategorie als Name, „bezahlt von“ als
/// Kennung. <c>PATCH …/belege/{id}/erstattet</c>: <c>{ "version": n, "alterWert": "false", "erstattet": "true" }</c>. Upload als Multipart im Feld
/// <see cref="Datei"/>.
/// </summary>
public static class BelegFelder
{
    public const string Bezeichnung = "bezeichnung";

    public const string Betrag = "betrag";

    public const string Kategorie = "kategorie";

    public const string BezahltVon = "bezahltVon";

    public const string Erstattet = "erstattet";

    /// <summary>Multipart-Feld der Datei.</summary>
    public const string Datei = "datei";

    public const int BezeichnungMaximaleLaenge = 200;

    /// <summary>
    /// Platzhalter für eine gelöschte Person in <see cref="Beleg.BezahltVon"/> und <see cref="Erstattung.PersonId"/> (AD-16): dieselbe feste Kennung wie im
    /// Server; zu ihr gibt es nie ein Profil. Der Client zeigt dafür „Gelöschte Person“.
    /// </summary>
    public static readonly Guid GeloeschtePerson = Guid.Parse("00000000-0000-7000-8000-0000000de1e7");

    public static string JsonName(BelegFeld feld) => feld switch
    {
        BelegFeld.Bezeichnung => Bezeichnung,
        BelegFeld.Betrag => Betrag,
        BelegFeld.Kategorie => Kategorie,
        BelegFeld.BezahltVon => BezahltVon,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    public static string JsonName(BelegErstattetFeld feld) => feld switch
    {
        BelegErstattetFeld.Erstattet => Erstattet,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
