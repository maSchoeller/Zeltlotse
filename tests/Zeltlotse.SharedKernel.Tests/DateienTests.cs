using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeile „Upload“ und Auslieferung (AD-15).</summary>
public sealed class DateienTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] WebP = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8];
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8, 1, 2, 3];
    private static readonly byte[] Exe = [0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0];

    public static TheoryData<byte[], DateiArt, string> Erlaubte => new()
    {
        { Png, DateiArt.Png, "image/png" },
        { Jpeg, DateiArt.Jpeg, "image/jpeg" },
        { WebP, DateiArt.WebP, "image/webp" },
        { Pdf, DateiArt.Pdf, "application/pdf" },
    };

    [Theory]
    [MemberData(nameof(Erlaubte))]
    public void Erlaubte_Typen_werden_an_den_Anfangsbytes_erkannt(byte[] inhalt, DateiArt art, string contentType)
    {
        var ergebnis = DateiPruefung.Pruefen(inhalt);

        Assert.True(ergebnis.Angenommen);
        Assert.Equal(art, ergebnis.Art);
        Assert.Equal(contentType, ergebnis.ContentType);
        Assert.Null(ergebnis.Grund);
    }

    [Fact]
    public async Task Png_mit_Endung_pdf_wird_als_Png_erkannt()
    {
        var ergebnis = await DateiPruefung.LesenUndPruefenAsync(FormDatei(Png, "ausweis.pdf", "application/pdf"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DateiArt.Png, ergebnis.Art);
        Assert.Equal("image/png", ergebnis.ContentType);
        Assert.Equal(Png, ergebnis.Inhalt);
    }

    [Fact]
    public async Task Exe_mit_Endung_png_wird_abgelehnt()
    {
        var ergebnis = await DateiPruefung.LesenUndPruefenAsync(FormDatei(Exe, "foto.png", "image/png"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(ergebnis.Angenommen);
        Assert.Equal(Ablehnungsgrund.TypNichtErlaubt, ergebnis.Grund);
        Assert.Null(ergebnis.Inhalt);
    }

    [Fact]
    public async Task Pdf_ueber_10_MB_wird_abgelehnt()
    {
        var gross = new byte[11 * 1024 * 1024];
        Pdf.CopyTo(gross, 0);

        var ergebnis = await DateiPruefung.LesenUndPruefenAsync(FormDatei(gross, "programm.pdf", "application/pdf"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(ergebnis.Angenommen);
        Assert.Equal(Ablehnungsgrund.ZuGross, ergebnis.Grund);
    }

    [Fact]
    public async Task Strom_ohne_Laengenangabe_ueber_der_Grenze_wird_abgelehnt()
    {
        await using var strom = new NurLesenderStrom([.. Pdf, .. new byte[200]]);

        var ergebnis = await DateiPruefung.LesenUndPruefenAsync(strom, hoechstgroesse: 100, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Ablehnungsgrund.ZuGross, ergebnis.Grund);
    }

    [Fact]
    public void Genau_die_Grenze_ist_erlaubt()
    {
        var inhalt = new byte[DateiPruefung.VorgabeHoechstgroesse];
        Pdf.CopyTo(inhalt, 0);

        Assert.True(DateiPruefung.Pruefen(inhalt).Angenommen);
        Assert.Equal(10 * 1024 * 1024, DateiPruefung.VorgabeHoechstgroesse);
    }

    [Fact]
    public void Leere_Datei_wird_abgelehnt()
    {
        Assert.Equal(Ablehnungsgrund.Leer, DateiPruefung.Pruefen([]).Grund);
    }

    [Fact]
    public void Nur_RIFF_ohne_WEBP_wird_abgelehnt()
    {
        byte[] wav = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WAVEfmt "u8];

        Assert.Equal(Ablehnungsgrund.TypNichtErlaubt, DateiPruefung.Pruefen(wav).Grund);
    }

    [Fact]
    public void Svg_wird_abgelehnt()
    {
        Assert.Equal(Ablehnungsgrund.TypNichtErlaubt, DateiPruefung.Pruefen("<svg onload=alert(1)>"u8).Grund);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public async Task Bilder_werden_inline_ausgeliefert(string art)
    {
        var inhalt = art switch { "png" => Png, "jpeg" => Jpeg, _ => WebP };

        var (kontext, koerper) = await AusfuehrenAsync(DateiAuslieferung.Ausliefern(inhalt, "foto.pdf"));

        Assert.Equal(StatusCodes.Status200OK, kontext.Response.StatusCode);
        Assert.Equal(DateiPruefung.ContentType(DateiPruefung.ArtErkennen(inhalt)!.Value), kontext.Response.ContentType);
        Assert.StartsWith("inline", kontext.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", kontext.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal(inhalt, koerper);
    }

    [Fact]
    public async Task Pdf_wird_als_Anhang_mit_Dateinamen_ausgeliefert()
    {
        var (kontext, koerper) = await AusfuehrenAsync(DateiAuslieferung.Ausliefern(Pdf, "Programm Sommer.png"));

        Assert.Equal("application/pdf", kontext.Response.ContentType);
        var disposition = kontext.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment", disposition, StringComparison.Ordinal);
        Assert.Contains("Programm", disposition, StringComparison.Ordinal);
        Assert.Equal(Pdf, koerper);
    }

    [Fact]
    public void Auslieferung_eines_unerlaubten_Typs_wirft()
    {
        Assert.Throws<InvalidOperationException>(() => DateiAuslieferung.Ausliefern(Exe, "x.png"));
    }

    [Theory]
    [InlineData("personen", "personen/0199b000-0000-7000-8000-00000000000a")]
    [InlineData("freizeiten", "freizeiten/0199b000-0000-7000-8000-00000000000a")]
    public void Schluessel_besteht_aus_Modul_Praefix_und_Kennung(string praefix, string erwartet)
    {
        Assert.Equal(erwartet, DateiSchluessel.Bilden(praefix, Guid.Parse("0199b000-0000-7000-8000-00000000000a")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../personen")]
    [InlineData("Personen")]
    [InlineData("personen/unter")]
    public void Ungueltiger_Praefix_wird_abgelehnt(string praefix)
    {
        Assert.Throws<ArgumentException>(() => DateiSchluessel.Bilden(praefix, Guid.CreateVersion7()));
    }

    private static FormFile FormDatei(byte[] inhalt, string name, string contentType) =>
        new(new MemoryStream(inhalt), 0, inhalt.Length, "datei", name) { Headers = new HeaderDictionary(), ContentType = contentType };

    private static async Task<(HttpContext Kontext, byte[] Koerper)> AusfuehrenAsync(IResult ergebnis)
    {
        var kontext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        using var koerper = new MemoryStream();
        kontext.Response.Body = koerper;

        await ergebnis.ExecuteAsync(kontext);

        return (kontext, koerper.ToArray());
    }

    /// <summary>Ein Strom ohne Länge und ohne Suchen, wie ein Netzwerk-Upload.</summary>
    private sealed class NurLesenderStrom(byte[] inhalt) : MemoryStream(inhalt)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
