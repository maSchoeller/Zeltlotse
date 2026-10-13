using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Zeltlotse.SharedKernel.Dateien;

/// <summary>Eine geprüfte hochgeladene Datei: Inhalt, am Inhalt erkannter Typ und der Dateiname des Browsers (nur für den Titel).</summary>
public sealed record HochgeladeneDatei(byte[] Inhalt, DateiArt Art, string? Dateiname)
{
    /// <summary>Weder Inhalt noch Dateiname ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(HochgeladeneDatei)}({Art}, {Inhalt.Length} Bytes)";
}

/// <summary>
/// Gemeinsame Regeln für Dokumente (Story 2-7, AD-15), die Freizeiten und Organisationen gleich behandeln: Titel aus dem Dateinamen,
/// Dateiname beim Ausliefern, Bezeichnung der Art („PDF“, „JPEG“, „PNG“, „WebP“) und das Lesen eines Uploads mit denselben Texten wie
/// bei den Nachweisen.
/// </summary>
public static class DokumentDateien
{
    /// <summary>Titel, wenn der Dateiname keinen hergibt.</summary>
    public const string StandardTitel = "Dokument";

    public const int TitelMaximaleLaenge = 200;

    /// <summary>
    /// Der Titel aus dem Dateinamen: ohne Pfad und Endung, ohne Steuerzeichen, getrimmt, auf <see cref="TitelMaximaleLaenge"/> Zeichen
    /// gekürzt (ohne ein Zeichenpaar zu zerschneiden); bleibt nichts übrig, <see cref="StandardTitel"/>.
    /// </summary>
    public static string TitelAusDateiname(string? dateiname)
    {
        var name = (dateiname ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var punkt = name.LastIndexOf('.');
        if (punkt > 0)
        {
            name = name[..punkt];
        }

        var titel = OhneSteuerzeichen(name);
        if (titel.Length > TitelMaximaleLaenge)
        {
            var laenge = char.IsHighSurrogate(titel[TitelMaximaleLaenge - 1]) ? TitelMaximaleLaenge - 1 : TitelMaximaleLaenge;
            titel = titel[..laenge].TrimEnd();
        }

        return titel.Length == 0 ? StandardTitel : titel;
    }

    /// <summary>Der Text ohne Steuerzeichen (auch Zeilenumbrüche), getrimmt; für Titel aus Dateinamen und aus der Bearbeitung.</summary>
    public static string OhneSteuerzeichen(string? text) => new string([.. (text ?? "").Where(z => !char.IsControl(z))]).Trim();

    /// <summary>Der Dateiname beim Ausliefern: der Titel ohne Pfad- und Steuerzeichen plus Endung, z. B. „Packliste.pdf“.</summary>
    public static string Dateiname(string? titel, DateiArt art)
    {
        var name = new string([.. (titel ?? "").Where(z => z is not ('/' or '\\') && !char.IsControl(z))]).Trim();
        return $"{(name.Length == 0 ? StandardTitel : name)}.{Endung(art)}";
    }

    /// <summary>Bezeichnung der Art für Listen und Links.</summary>
    public static string Bezeichnung(DateiArt art) => art switch
    {
        DateiArt.Pdf => "PDF",
        DateiArt.Jpeg => "JPEG",
        DateiArt.Png => "PNG",
        DateiArt.WebP => "WebP",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    public static string Endung(DateiArt art) => art switch
    {
        DateiArt.Pdf => "pdf",
        DateiArt.Jpeg => "jpg",
        DateiArt.Png => "png",
        DateiArt.WebP => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    /// <summary>Die Texte am Feld wie bei den Nachweisen.</summary>
    public static string Fehlertext(Ablehnungsgrund? grund) => grund switch
    {
        Ablehnungsgrund.Leer => "Die Datei ist leer.",
        Ablehnungsgrund.ZuGross => "Die Datei ist größer als 10 MB.",
        _ => "Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF.",
    };

    /// <summary>Text, wenn das Formular keine Datei im Feld trägt.</summary>
    public const string DateiFehlt = "Datei fehlt.";

    /// <summary>
    /// Liest und prüft die Datei im Feld <paramref name="feld"/> (höchstens 10 MB, Typ am Inhalt). Ergebnis ist die Datei oder der
    /// Fehlertext für das Feld.
    /// </summary>
    public static async Task<(HochgeladeneDatei? Datei, string? Fehler)> LesenAsync(IFormCollection? formular, string feld, CancellationToken abbruch)
    {
        if (formular?.Files.GetFile(feld) is not { } datei)
        {
            return (null, DateiFehlt);
        }

        var pruefung = await DateiPruefung.LesenUndPruefenAsync(datei, cancellationToken: abbruch);
        return pruefung.Angenommen
            ? (new HochgeladeneDatei(pruefung.Inhalt!, pruefung.Art!.Value, datei.FileName), null)
            : (null, Fehlertext(pruefung.Grund));
    }

    /// <summary>Vergleich der Titel beim Sortieren: deutsch, ohne Groß- und Kleinschreibung.</summary>
    public static StringComparer TitelVergleich { get; } = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);
}
