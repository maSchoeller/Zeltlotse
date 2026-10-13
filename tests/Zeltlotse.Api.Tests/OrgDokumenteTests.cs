using System.Net;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.DokumentHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using OrgRolle = Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-7, Org-Dokumente (FR-43, AD-6, AD-15, AD-30) gegen echtes PostgreSQL und S3: Liste, Hochladen, Datei und Entfernen nur für
/// den Org-Admin; Org-Überblick <c>403</c>, Org-Admin einer anderen Organisation und Personen ohne Org-Rolle <c>404</c>; Titel aus dem
/// Dateinamen, Sortierung, Fehler am Feld <c>datei</c>, Datei unverschlüsselt unter <c>organisationen/&lt;id&gt;</c>, immer als Anhang.
/// </summary>
public sealed class OrgDokumenteTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres, s3);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    [Fact]
    public async Task Andrea_laedt_hoch_Titel_aus_dem_Dateinamen_Liste_nach_Titel_Datei_als_Anhang_unverschluesselt()
    {
        using var hochgeladen = await HochladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung Zeltplatz.pdf");

        Assert.Equal(HttpStatusCode.Created, hochgeladen.StatusCode);
        var json = await JsonAsync(hochgeladen);
        Assert.Equal(["dateiart", "groesse", "hochgeladenAm", "id", "titel"], Namen(json));
        Assert.Equal(("Hausordnung Zeltplatz", "PDF", (long)Pdf.Length), (json.GetProperty("titel").GetString(), json.GetProperty("dateiart").GetString(), json.GetProperty("groesse").GetInt64()));
        var id = json.GetProperty("id").GetGuid();
        await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Png, "aufsicht.png");
        await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "C:\\Ablage\\Leitfaden Aufsichtspflicht.pdf");

        Assert.Equal(["aufsicht", "Hausordnung Zeltplatz", "Leitfaden Aufsichtspflicht"], await TitelAsync(_s.Andrea, OrgPfad(_s.A)));
        Assert.Equal(Pdf, await RohAsync(s3, _s.Bucket!, $"organisationen/{id}"));
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM organisationen.dokument WHERE \"Id\" = @id AND \"OrganisationId\" = @o", ("id", id), ("o", _s.A)));

        using var datei = await _s.Andrea.GetAsync($"{OrgPfad(_s.A)}/{id}/datei", Abbruch);
        await DateiPruefenAsync(datei, Pdf, "application/pdf", "Hausordnung Zeltplatz.pdf");
        var bild = (await DokumenteAsync(_s.Andrea, OrgPfad(_s.A))).Single(d => d.GetProperty("titel").GetString() == "aufsicht");
        Assert.Equal("PNG", bild.GetProperty("dateiart").GetString());
        using var bildDatei = await _s.Andrea.GetAsync($"{OrgPfad(_s.A)}/{bild.GetProperty("id").GetGuid()}/datei", Abbruch);
        await DateiPruefenAsync(bildDatei, Png, "image/png", "aufsicht.png");
    }

    [Theory]
    [InlineData("leer")]
    [InlineData("ohne Datei")]
    [InlineData("Text")]
    [InlineData("zu groß")]
    public async Task Ungueltige_Datei_ergibt_400_am_Feld_datei_mit_den_Texten_der_Nachweise(string fall)
    {
        var (inhalt, text) = fall switch
        {
            "leer" => ([], "Die Datei ist leer."),
            "ohne Datei" => (null, "Datei fehlt."),
            "Text" => ("Kein PDF, nur Text."u8.ToArray(), "Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF."),
            _ => ((byte[]?)[.. Pdf, .. new byte[(10 * 1024 * 1024) + 1 - Pdf.Length]], "Die Datei ist größer als 10 MB."),
        };

        using var antwort = await HochladenAsync(_s.Andrea, OrgPfad(_s.A), inhalt);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(text, (await ProblemAsync(antwort)).GetProperty("errors").GetProperty("datei")[0].GetString());
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM organisationen.dokument"));
    }

    [Theory]
    [InlineData(" .pdf", "Dokument")]
    [InlineData("lang", "lang")]
    public async Task Ohne_Namen_heisst_der_Titel_Dokument_ein_langer_Name_wird_auf_200_Zeichen_gekuerzt(string dateiname, string erwartet)
    {
        var lang = new string('a', 250);
        var titel = (await DokumenteAsync(_s.Andrea, OrgPfad(_s.A))).Count;
        Assert.Equal(0, titel);

        await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, dateiname == "lang" ? $"{lang}.pdf" : dateiname);

        Assert.Equal(erwartet == "lang" ? lang[..200] : erwartet, Assert.Single(await TitelAsync(_s.Andrea, OrgPfad(_s.A))));
    }

    [Fact]
    public async Task Entfernen_loescht_Zeile_und_Datei_danach_404()
    {
        var id = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung Zeltplatz.pdf");

        using var entfernt = await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{id}");
        using var nochmal = await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{id}");

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], [entfernt.StatusCode, nochmal.StatusCode]);
        Assert.Empty(await TitelAsync(_s.Andrea, OrgPfad(_s.A)));
        Assert.Null(await RohAsync(s3, _s.Bucket!, $"organisationen/{id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Andrea.GetAsync($"{OrgPfad(_s.A)}/{id}/datei", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Org_Ueberblick_403_Org_Admin_von_B_und_Personen_ohne_Org_Rolle_404_an_allen_vier_Endpunkten()
    {
        var id = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung Zeltplatz.pdf");
        const string ueberblickMail = "vorstand@example.org";
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, ueberblickMail, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, ueberblickMail);

        foreach (var (client, status) in new[]
                 {
                     (ueberblick, HttpStatusCode.Forbidden), (_s.Lea, HttpStatusCode.NotFound), (_s.Katrin, HttpStatusCode.NotFound),
                     (_s.Miriam, HttpStatusCode.NotFound), (_s.Gerd, HttpStatusCode.NotFound),
                 })
        {
            using var liste = await client.GetAsync(OrgPfad(_s.A), Abbruch);
            using var hochladen = await HochladenAsync(client, OrgPfad(_s.A), Pdf);
            using var datei = await client.GetAsync($"{OrgPfad(_s.A)}/{id}/datei", Abbruch);
            using var entfernen = await SendenAsync(client, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{id}");
            Assert.Equal([status, status, status, status], [liste.StatusCode, hochladen.StatusCode, datei.StatusCode, entfernen.StatusCode]);
        }

        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM organisationen.dokument"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Anonym.GetAsync(OrgPfad(_s.A), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Ein_Dokument_von_A_ist_ueber_die_Route_von_B_nicht_erreichbar()
    {
        var id = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung Zeltplatz.pdf");

        Assert.Empty(await TitelAsync(_s.Lea, OrgPfad(_s.B)));
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Lea.GetAsync($"{OrgPfad(_s.B)}/{id}/datei", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendenAsync(_s.Lea, HttpMethod.Delete, $"{OrgPfad(_s.B)}/{id}")).StatusCode);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM organisationen.dokument"));
    }

    [Fact]
    public async Task Org_Admin_bekommt_den_Aktionsschluessel_org_dokumente_verwalten()
    {
        using var andrea = await _s.Andrea.GetAsync($"/api/organisationen/{_s.A}", Abbruch);

        Assert.Contains(
            Aktionsschluessel.OrgDokumenteVerwalten,
            (await JsonAsync(andrea)).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()));
    }
}
