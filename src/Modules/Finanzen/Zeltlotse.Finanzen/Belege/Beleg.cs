using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// Story 5-2 (CAP-33, FR-58): Ein Beleg einer Freizeit: Kategorie und Betrag (Pflicht), optional eine Bezeichnung, wer bezahlt hat (<see cref="PersonId"/>,
/// AD-7) und ob erstattet ist, dazu optional ein Foto oder PDF im Dateispeicher unter <c>finanzen/&lt;Id&gt;</c> (unverschlüsselt, AD-15). Felder ändern
/// sich nach AD-19 nur über <see cref="FeldAendern"/> und <see cref="ErstattetAendern"/>.
/// </summary>
/// <remarks>
/// Lösch-Klasse „aufbewahrungspflichtig für die Organisation“ (AD-16): Bei <c>PersonGeloescht</c> wird <see cref="PersonId"/> zum Platzhalter, der Rest
/// bleibt; nach der Frist der Organisation löscht der Lösch-Lauf Beleg und Datei. Kein besonders schützenswertes Datum.
/// </remarks>
internal sealed class Beleg : IOrganisationsEigen
{
    /// <summary>Präfix im Dateispeicher: das Schema des Moduls.</summary>
    public const string DateiPraefix = FinanzenModul.Schema;

    private Beleg()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Wann der Beleg angelegt wurde (Uhr des Servers).</summary>
    public DateTimeOffset ErfasstAm { get; private set; }

    public Kostenkategorie Kategorie { get; private set; }

    /// <summary>Betrag in Euro, 0 bis <see cref="KategorieFelder.BetragMaximum"/>.</summary>
    public decimal Betrag { get; private set; }

    /// <summary>Getrimmt, höchstens <see cref="BelegFelder.BezeichnungMaximaleLaenge"/> Zeichen; ohne Angabe <c>null</c>.</summary>
    public string? Bezeichnung { get; private set; }

    /// <summary>Wer bezahlt hat; nach dem Löschen der Person der Platzhalter <c>GeloeschtePerson.Id</c>.</summary>
    public Guid PersonId { get; private set; }

    public bool Erstattet { get; private set; }

    /// <summary>Am Inhalt erkannte Art der Datei; ohne Datei <c>null</c>.</summary>
    public DateiArt? DateiArt { get; private set; }

    /// <summary>Name der Datei beim Ausliefern, z. B. „Kassenbon.png“; ohne Datei <c>null</c>.</summary>
    public string? Dateiname { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Beleg Anlegen(
        Guid id, Guid organisationId, Guid freizeitId, DateTimeOffset jetzt, Kostenkategorie kategorie, decimal betrag, string? bezeichnung, Guid personId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        ErfasstAm = jetzt,
        Kategorie = kategorie,
        Betrag = betrag,
        Bezeichnung = FinanzenWerte.TextOderNull(bezeichnung),
        PersonId = personId,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert: Betrag „7,5“, Kategorie als Name, „bezahlt von“ als Kennung.</summary>
    public string? Wert(BelegFeld feld) => feld switch
    {
        BelegFeld.Bezeichnung => Bezeichnung,
        BelegFeld.Betrag => FinanzenWerte.BetragText(Betrag),
        BelegFeld.Kategorie => Kategorie.ToString(),
        BelegFeld.BezahltVon => PersonText(PersonId),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Der Erstattungsvermerk als Text: <c>"true"</c> oder <c>"false"</c>.</summary>
    public string ErstattetText => Erstattet ? "true" : "false";

    /// <summary>Die Kennung einer Person als Text, wie die API sie im Autosave liefert.</summary>
    public static string PersonText(Guid personId) => personId.ToString("D");

    /// <summary>„bezahlt von“ aus dem Text: eine Kennung oder <c>null</c>.</summary>
    public static Guid? PersonLesen(string? text) => Guid.TryParse(FinanzenWerte.TextOderNull(text), out var person) ? person : null;

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab (beide normalisiert), bleibt alles, wie
    /// es ist, und das Ergebnis ist <c>false</c>. Die Form des neuen Werts prüft der Validator, die Zugehörigkeit von „bezahlt von“ zum Team der Handler.
    /// </summary>
    public bool FeldAendern(BelegFeld feld, string? alterWert, string? neuerWert)
    {
        if (!AlterWertPasst(feld, alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case BelegFeld.Bezeichnung:
                Bezeichnung = FinanzenWerte.TextOderNull(neuerWert);
                break;
            case BelegFeld.Betrag:
                Betrag = FinanzenWerte.BetragLesen(neuerWert) ?? throw new ArgumentException(FinanzenWerte.BetragFehlerText, nameof(neuerWert));
                break;
            case BelegFeld.Kategorie:
                Kategorie = Kostenkategorien.Lesen(neuerWert) ?? throw new ArgumentException("Unbekannte Kategorie.", nameof(neuerWert));
                break;
            case BelegFeld.BezahltVon:
                PersonId = PersonLesen(neuerWert) ?? throw new ArgumentException("Keine Kennung.", nameof(neuerWert));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Ob der mitgeschickte alte Wert (normalisiert) dem aktuellen Wert des Felds entspricht (AD-19).</summary>
    public bool AlterWertPasst(BelegFeld feld, string? alterWert) => Wert(feld) == Vergleichswert(feld, alterWert);

    /// <summary>Setzt den Erstattungsvermerk nach der Konfliktregel aus AD-19 (<c>"true"</c>/<c>"false"</c>); weicht der alte Wert ab, <c>false</c>.</summary>
    public bool ErstattetAendern(string? alterWert, string? neuerWert)
    {
        if (ErstattetText != JaNeinVergleichswert(alterWert))
        {
            return false;
        }

        Erstattet = JaNeinLesen(neuerWert) ?? throw new ArgumentException("Nur true oder false.", nameof(neuerWert));
        return true;
    }

    /// <summary>„true“ oder „false“ (Groß- und Kleinschreibung egal), sonst <c>null</c>.</summary>
    public static bool? JaNeinLesen(string? text) => FinanzenWerte.TextOderNull(text)?.ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <summary>Eine neue Datei (ersetzt eine vorhandene; der Inhalt liegt unter derselben Kennung im Dateispeicher).</summary>
    public void DateiSetzen(DateiArt art, string dateiname) => (DateiArt, Dateiname) = (art, dateiname);

    public void DateiEntfernen() => (DateiArt, Dateiname) = (null, null);

    private static string? Vergleichswert(BelegFeld feld, string? alterWert) => feld switch
    {
        BelegFeld.Betrag => FinanzenWerte.BetragVergleichswert(alterWert),
        BelegFeld.Kategorie => Kostenkategorien.Lesen(alterWert)?.ToString() ?? FinanzenWerte.TextOderNull(alterWert),
        BelegFeld.BezahltVon => PersonLesen(alterWert) is { } person ? PersonText(person) : FinanzenWerte.TextOderNull(alterWert),
        _ => FinanzenWerte.TextOderNull(alterWert),
    };

    private static string? JaNeinVergleichswert(string? alterWert) => JaNeinLesen(alterWert) is { } wert ? (wert ? "true" : "false") : FinanzenWerte.TextOderNull(alterWert);
}
