using System.Net;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.DokumentHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-7, Elterninfo (CAP-10, FR-68, AD-6, AD-11, AD-26, AD-30) gegen echtes PostgreSQL und S3: je Anmeldung die Dokumente ihrer
/// Freizeit mit Sichtbarkeit „Eltern und Teilnehmende“ (auch eingebundene Org-Dokumente), im Konto für die Person selbst und die Eltern
/// ihrer Familie, über den Gast-Link für die Gast-Familie; fremde, abgesagte oder unbekannte Anmeldungen, Dokumente anderer Freizeiten
/// und ungültige Tokens <c>404</c>. Eine Anmeldung bei einer anderen Organisation liefert die Dokumente dieser Freizeit (Mandant aus
/// der geprüften Anmeldung).
/// </summary>
public sealed class ElterninfoTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private static readonly DateTimeOffset NachFreigabeEnde = new(2027, 9, 13, 0, 0, 0, TimeSpan.Zero);

    private FreizeitpassSzenario _s = null!;
    private Guid _t;
    private Guid _e;
    private Guid _o;
    private Guid _h;

    /// <summary>
    /// X hat je ein Dokument Team (T), Eltern und Teilnehmende (E, „Infobrief“), öffentlich (O) und die eingebundene Hausordnung von A
    /// mit Eltern und Teilnehmende (H).
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres, s3);
        var x = FreizeitPfad(_s.X);
        _t = await HochgeladenAsync(_s.Miriam, x, Pdf, "Programmideen.pdf", "Team");
        _e = await HochgeladenAsync(_s.Miriam, x, Pdf, "Infobrief.pdf", "ElternUndTeilnehmende");
        _o = await HochgeladenAsync(_s.Miriam, x, Pdf, "Anfahrt.pdf", "Oeffentlich");
        var hausordnung = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Png, "Hausordnung Zeltplatz.png");
        _h = (await JsonAsync(await PostAsync(_s.Miriam, $"{x}/einbindungen", new DokumentEinbinden(hausordnung)))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(
            _s.Miriam, HttpMethod.Patch, $"{x}/{_h}", """{"version":0,"alterWert":"Team","sichtbarkeit":"ElternUndTeilnehmende"}""")).StatusCode);
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    // ---------- Konto ----------

    [Fact]
    public async Task Katrin_sieht_zu_Emmas_Anmeldung_genau_die_Dokumente_fuer_Eltern_sortiert_und_laedt_sie()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);

        using var antwort = await _s.Katrin.GetAsync(KontoPfad(emma), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["dokumente", "freizeitName"], Namen(json));
        Assert.Equal(FreizeitpassSzenario.Kapf, json.GetProperty("freizeitName").GetString());
        var dokumente = json.GetProperty("dokumente").EnumerateArray().ToList();
        Assert.Equal(
            [(_h, "Hausordnung Zeltplatz", "PNG"), (_e, "Infobrief", "PDF")],
            dokumente.Select(d => (d.GetProperty("id").GetGuid(), d.GetProperty("titel").GetString(), d.GetProperty("dateiart").GetString())));
        using var infobrief = await _s.Katrin.GetAsync($"{KontoPfad(emma)}/{_e}", Abbruch);
        await DateiPruefenAsync(infobrief, Pdf, "application/pdf", "Infobrief.pdf");
        using var hausordnung = await _s.Katrin.GetAsync($"{KontoPfad(emma)}/{_h}", Abbruch);
        await DateiPruefenAsync(hausordnung, Png, "image/png", "Hausordnung Zeltplatz.png");
        foreach (var andere in new[] { _t, _o, Guid.CreateVersion7() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _s.Katrin.GetAsync($"{KontoPfad(emma)}/{andere}", Abbruch)).StatusCode);
        }
    }

    [Fact]
    public async Task Die_Person_selbst_sieht_die_Elterninfo_ihrer_eigenen_Anmeldung()
    {
        using var antwort = await _s.Tobias.GetAsync(KontoPfad(_s.AnmeldungTobias), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(2, (await JsonAsync(antwort)).GetProperty("dokumente").GetArrayLength());
    }

    [Fact]
    public async Task Eine_Anmeldung_bei_einer_anderen_Organisation_liefert_die_Dokumente_dieser_Freizeit()
    {
        var inZ = await _s.KindAnmeldenAsync(_s.Emma, _s.TeilnehmendeZ);
        var z = FreizeitPfad(_s.Z);
        var packliste = await HochgeladenAsync(_s.Miriam, z, Pdf, "Packliste Z.pdf", "ElternUndTeilnehmende");
        await HochgeladenAsync(_s.Miriam, z, Pdf, "Intern Z.pdf", "Team");
        var vonB = await HochgeladenAsync(_s.Lea, OrgPfad(_s.B), Pdf, "Regeln B.pdf");
        var eingebunden = (await JsonAsync(await PostAsync(_s.Miriam, $"{z}/einbindungen", new DokumentEinbinden(vonB)))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(
            _s.Miriam, HttpMethod.Patch, $"{z}/{eingebunden}", """{"version":0,"alterWert":"Team","sichtbarkeit":"ElternUndTeilnehmende"}""")).StatusCode);

        var json = await JsonAsync(await _s.Katrin.GetAsync(KontoPfad(inZ), Abbruch));

        Assert.Equal("Z", json.GetProperty("freizeitName").GetString());
        Assert.Equal(["Packliste Z", "Regeln B"], json.GetProperty("dokumente").EnumerateArray().Select(d => d.GetProperty("titel").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await _s.Katrin.GetAsync($"{KontoPfad(inZ)}/{eingebunden}", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _s.Katrin.GetAsync($"{KontoPfad(inZ)}/{packliste}", Abbruch)).StatusCode);
        // Ein Dokument aus Z über Emmas Anmeldung in X und umgekehrt: 404.
        var inX = await _s.KindAnmeldenAsync(_s.Emma);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Katrin.GetAsync($"{KontoPfad(inX)}/{packliste}", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Katrin.GetAsync($"{KontoPfad(inZ)}/{_e}", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Eine_andere_Familie_eine_abgesagte_oder_unbekannte_Anmeldung_ergibt_404()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        using var daniel = await AngemeldeterClientAsync(_s.Factory, FamilienHelfer.Daniel);
        (await PostAsync(daniel, "/api/ich/profil")).Dispose();

        foreach (var (client, anmeldung) in new[] { (daniel, emma), (_s.Miriam, emma), (_s.Katrin, Guid.CreateVersion7()), (_s.Katrin, _s.AnmeldungTobias) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(KontoPfad(anmeldung), Abbruch)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{KontoPfad(anmeldung)}/{_e}", Abbruch)).StatusCode);
        }

        await _s.AbsagenAsync(emma);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Katrin.GetAsync(KontoPfad(emma), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Katrin.GetAsync($"{KontoPfad(emma)}/{_e}", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Anonym.GetAsync(KontoPfad(emma), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Loescht_die_Organisation_die_Hausordnung_fehlt_sie_in_der_Elterninfo()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var orgDokument = await WertAsync<Guid>(_s.Verbindung, "SELECT \"OrgDokumentId\" FROM freizeiten.dokument WHERE \"Id\" = @h", ("h", _h));

        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{orgDokument}")).StatusCode);

        var json = await JsonAsync(await _s.Katrin.GetAsync(KontoPfad(emma), Abbruch));
        Assert.Equal(["Infobrief"], json.GetProperty("dokumente").EnumerateArray().Select(d => d.GetProperty("titel").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Katrin.GetAsync($"{KontoPfad(emma)}/{_h}", Abbruch)).StatusCode);
    }

    // ---------- Gast-Link ----------

    [Fact]
    public async Task Der_Gast_sieht_die_Elterninfo_seiner_Anmeldung_ohne_Konto_nach_dem_Freigabefenster_nicht_mehr()
    {
        var (token, _, anmeldung) = await _s.GastAnmeldenAsync("lina.eltern@example.org", "Lina");

        using var antwort = await _s.Anonym.GetAsync(GastPfad(token, anmeldung), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(FreizeitpassSzenario.Kapf, json.GetProperty("freizeitName").GetString());
        Assert.Equal(["Hausordnung Zeltplatz", "Infobrief"], json.GetProperty("dokumente").EnumerateArray().Select(d => d.GetProperty("titel").GetString()));
        using var datei = await _s.Anonym.GetAsync($"{GastPfad(token, anmeldung)}/{_e}", Abbruch);
        await DateiPruefenAsync(datei, Pdf, "application/pdf", "Infobrief.pdf");
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync($"{GastPfad(token, anmeldung)}/{_t}", Abbruch)).StatusCode);

        _s.Factory.Uhr.SetUtcNow(NachFreigabeEnde);

        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync(GastPfad(token, anmeldung), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync($"{GastPfad(token, anmeldung)}/{_e}", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Anmeldelink_statt_Gast_Token_unbekanntes_Token_fremde_Familie_und_Dokument_von_Z_ergeben_404()
    {
        var (token, _, anmeldung) = await _s.GastAnmeldenAsync("lina.eltern@example.org", "Lina");
        var (andererToken, _, _) = await _s.GastAnmeldenAsync("anna.anders@example.org", "Ben");
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var inZ = await HochgeladenAsync(_s.Miriam, FreizeitPfad(_s.Z), Pdf, "Z.pdf", "ElternUndTeilnehmende");

        foreach (var (gast, fuer) in new[] { (_s.TeilnehmendeX, anmeldung), (Zugangslink.NeuesToken(), anmeldung), (andererToken, anmeldung), (token, emma) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync(GastPfad(gast, fuer), Abbruch)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync($"{GastPfad(gast, fuer)}/{_e}", Abbruch)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync($"{GastPfad(token, anmeldung)}/{inZ}", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _s.Anonym.GetAsync(GastPfad(token, anmeldung), Abbruch)).StatusCode);
    }

    private static string KontoPfad(Guid anmeldung) => $"/api/ich/anmeldungen/{anmeldung}/elterninfo";

    private static string GastPfad(string token, Guid anmeldung) => $"/api/freizeiten/gast/{token}/anmeldungen/{anmeldung}/elterninfo";
}
