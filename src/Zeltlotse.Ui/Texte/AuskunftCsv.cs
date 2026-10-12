using System.Text;
using Zeltlotse.Api.Contracts.Betrieb;

namespace Zeltlotse.Ui.Texte;

/// <summary>
/// CSV-Export der Datenauskunft (FR-14), im Client aus der geladenen Auskunft erzeugt: Spalten <c>Bereich;Feld;Wert</c>, UTF-8 mit
/// BOM (damit Tabellenprogramme Umlaute richtig lesen), jeder Wert in Anführungszeichen mit verdoppelten <c>"</c>. Werte, die mit
/// <c>= + - @</c>, Tabulator oder Wagenrücklauf beginnen, bekommen ein <c>'</c> davor, damit kein Tabellenprogramm sie als Formel ausführt.
/// </summary>
public static class AuskunftCsv
{
    /// <summary>Medientyp der Datei.</summary>
    public const string Medientyp = "text/csv";

    private static readonly UTF8Encoding Utf8MitBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Die Datei als Bytes, mit BOM am Anfang.</summary>
    public static byte[] Erzeugen(DatenauskunftAntwort auskunft)
    {
        ArgumentNullException.ThrowIfNull(auskunft);
        var text = new StringBuilder();
        Zeile(text, "Bereich", "Feld", "Wert");
        foreach (var abschnitt in auskunft.Abschnitte)
        {
            foreach (var zeile in abschnitt.Zeilen)
            {
                Zeile(text, abschnitt.Titel, zeile.Feld, zeile.Wert);
            }
        }

        return [.. Utf8MitBom.GetPreamble(), .. Utf8MitBom.GetBytes(text.ToString())];
    }

    /// <summary><c>zeltlotse-meine-daten-JJJJ-MM-TT.csv</c> mit dem Tag der Auskunft in Europe/Berlin.</summary>
    public static string Dateiname(DateTimeOffset erstelltAm) =>
        $"zeltlotse-meine-daten-{Anzeige.Tag(erstelltAm).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.csv";

    /// <summary>Ein Wert in Anführungszeichen, mit Formel-Schutz.</summary>
    public static string Wert(string? wert)
    {
        var text = wert ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void Zeile(StringBuilder text, string bereich, string feld, string wert) =>
        text.Append(Wert(bereich)).Append(';').Append(Wert(feld)).Append(';').Append(Wert(wert)).Append("\r\n");
}
