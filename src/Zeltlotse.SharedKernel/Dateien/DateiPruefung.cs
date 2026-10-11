using Microsoft.AspNetCore.Http;

namespace Zeltlotse.SharedKernel.Dateien;

/// <summary>Erlaubte Dateitypen (AD-15): nur Bilder und PDF.</summary>
public enum DateiArt
{
    Jpeg,
    Png,
    WebP,
    Pdf,
}

/// <summary>Warum ein Upload abgelehnt wurde.</summary>
public enum Ablehnungsgrund
{
    Leer,
    ZuGross,
    TypNichtErlaubt,
}

/// <summary>Ergebnis einer Prüfung; abgelehnt ist keine Ausnahme, sondern ein Ergebnis mit Grund.</summary>
/// <param name="Art">Erkannter Typ, wenn angenommen.</param>
/// <param name="Grund">Ablehnungsgrund, wenn abgelehnt.</param>
/// <param name="Inhalt">Der gelesene Inhalt (nur beim Prüfen eines Stroms und nur, wenn angenommen).</param>
public sealed record DateiPruefErgebnis(DateiArt? Art, Ablehnungsgrund? Grund, byte[]? Inhalt = null)
{
    public bool Angenommen => Art is not null;

    /// <summary>Der Content-Type aus der Prüfung, nie der vom Client genannte.</summary>
    public string? ContentType => Art is { } art ? DateiPruefung.ContentType(art) : null;
}

/// <summary>
/// Prüft Uploads am Inhalt (AD-15): Der Typ wird an den Anfangsbytes erkannt (JPEG, PNG, WebP, PDF);
/// Dateiendung und vom Client genannter Typ zählen nicht. Vorgabe-Grenze 10 MB.
/// </summary>
public static class DateiPruefung
{
    public const long VorgabeHoechstgroesse = 10 * 1024 * 1024;

    private static ReadOnlySpan<byte> PngSignatur => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignatur => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PdfSignatur => "%PDF-"u8;

    private static ReadOnlySpan<byte> Riff => "RIFF"u8;

    private static ReadOnlySpan<byte> Webp => "WEBP"u8;

    public static DateiPruefErgebnis Pruefen(ReadOnlySpan<byte> inhalt, long hoechstgroesse = VorgabeHoechstgroesse)
    {
        if (inhalt.IsEmpty)
        {
            return new(null, Ablehnungsgrund.Leer);
        }

        if (inhalt.Length > hoechstgroesse)
        {
            return new(null, Ablehnungsgrund.ZuGross);
        }

        return ArtErkennen(inhalt) is { } art ? new(art, null) : new(null, Ablehnungsgrund.TypNichtErlaubt);
    }

    /// <summary>Liest höchstens <paramref name="hoechstgroesse"/> + 1 Bytes und prüft sie; der Inhalt steht im Ergebnis.</summary>
    public static async Task<DateiPruefErgebnis> LesenUndPruefenAsync(
        Stream quelle, long hoechstgroesse = VorgabeHoechstgroesse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        using var puffer = new MemoryStream();
        var block = new byte[81920];
        int gelesen;
        while ((gelesen = await quelle.ReadAsync(block, cancellationToken)) > 0)
        {
            puffer.Write(block, 0, gelesen);
            if (puffer.Length > hoechstgroesse)
            {
                return new(null, Ablehnungsgrund.ZuGross);
            }
        }

        var inhalt = puffer.ToArray();
        var ergebnis = Pruefen(inhalt, hoechstgroesse);
        return ergebnis.Angenommen ? ergebnis with { Inhalt = inhalt } : ergebnis;
    }

    /// <summary>Prüft ein hochgeladenes Formularfeld; Name und Content-Type der Datei werden nicht beachtet.</summary>
    public static async Task<DateiPruefErgebnis> LesenUndPruefenAsync(
        IFormFile datei, long hoechstgroesse = VorgabeHoechstgroesse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datei);
        if (datei.Length > hoechstgroesse)
        {
            return new(null, Ablehnungsgrund.ZuGross);
        }

        await using var strom = datei.OpenReadStream();
        return await LesenUndPruefenAsync(strom, hoechstgroesse, cancellationToken);
    }

    /// <summary>Erkennt den Typ an den Anfangsbytes; <c>null</c> heißt: nicht erlaubt.</summary>
    public static DateiArt? ArtErkennen(ReadOnlySpan<byte> inhalt)
    {
        if (inhalt.StartsWith(PngSignatur))
        {
            return DateiArt.Png;
        }

        if (inhalt.StartsWith(JpegSignatur))
        {
            return DateiArt.Jpeg;
        }

        if (inhalt.StartsWith(PdfSignatur))
        {
            return DateiArt.Pdf;
        }

        if (inhalt.Length >= 12 && inhalt.StartsWith(Riff) && inhalt.Slice(8, 4).SequenceEqual(Webp))
        {
            return DateiArt.WebP;
        }

        return null;
    }

    public static string ContentType(DateiArt art) => art switch
    {
        DateiArt.Jpeg => "image/jpeg",
        DateiArt.Png => "image/png",
        DateiArt.WebP => "image/webp",
        DateiArt.Pdf => "application/pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(art)),
    };

    /// <summary>Bilder werden in der Seite angezeigt; alles andere wird heruntergeladen.</summary>
    public static bool IstBild(DateiArt art) => art is DateiArt.Jpeg or DateiArt.Png or DateiArt.WebP;
}
