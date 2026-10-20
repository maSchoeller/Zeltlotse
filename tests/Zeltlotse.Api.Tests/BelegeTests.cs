using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.DokumentHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 5-2, Belege, Soll-Ist und Erstattung (CAP-33, FR-58, FR-59, NS-8, AD-15, AD-16, AD-17, AD-19, AD-20, AD-30) gegen echtes PostgreSQL und S3: Belege
/// von Hand anlegen mit Pflichtfeldern und „bezahlt von“ aus dem Team, Autosave mit Konfliktregel, Foto oder PDF getrennt hochladen, ausliefern, ersetzen
/// und entfernen, Löschen samt Datei, der Soll-Ist-Vergleich mit Status und Erstattungen, der Erstattungsvermerk auch nach dem Abschluss, die Pflichtfälle
/// der Freizeit-Route für alle neun Routen, der Schalter Finanzen, der Lösch-Lauf mit Dateien, <c>PersonGeloescht</c> und die Datenauskunft. Freizeit X
/// (Organisation A, 02.08.–13.08.2027, 12 Tage; Org-Admin Andrea, Leitung Miriam, Team Tobias) und Z (Organisation B, Leitung Miriam) haben Finanzen an.
/// </summary>
public sealed class BelegeTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres, s3);
        await SchaltenAsync(_s.X, an: true);
        await SchaltenAsync(_s.Z, an: true);
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Finanzen(Guid freizeit) => $"/api/freizeiten/{freizeit}/finanzen";

    private static string BelegePfad(Guid freizeit) => $"{Finanzen(freizeit)}/belege";

    private static string BelegPfad(Guid freizeit, Guid beleg) => $"{BelegePfad(freizeit)}/{beleg}";

    private static string DateiPfad(Guid freizeit, Guid beleg) => $"{BelegPfad(freizeit, beleg)}/datei";

    private static string SollIstPfad(Guid freizeit) => $"{Finanzen(freizeit)}/soll-ist";

    /// <summary>Schaltet Finanzen mit dem aktuellen Stand als altem Wert (Miriam ist Leitung von X und Z).</summary>
    private async Task SchaltenAsync(Guid freizeit, bool an)
    {
        using var stand = await _s.Miriam.GetAsync($"/api/freizeiten/{freizeit}/module", Abbruch);
        var json = await JsonAsync(stand);
        var koerper = new Dictionary<string, object?>
        {
            ["version"] = json.GetProperty("version").GetUInt32(),
            ["alterWert"] = json.GetProperty("finanzen").GetBoolean() ? "true" : "false",
            ["finanzen"] = an ? "true" : "false",
        };
        using var antwort = await SendenAsync(_s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/module", koerper);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private Task<HttpResponseMessage> AnlegenAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, BelegePfad(freizeit ?? _s.X), koerper);

    private async Task<JsonElement> NeuAsync(string kategorie, string betrag, string? bezeichnung = null, HttpClient? client = null, Guid? freizeit = null, Guid? bezahltVon = null)
    {
        using var antwort = await AnlegenAsync(new { kategorie, betrag, bezeichnung, bezahltVon = bezahltVon?.ToString() }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> PatchAsync(Guid beleg, string feld, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, BelegPfad(freizeit ?? _s.X, beleg), Koerper(0, alterWert, feld, wert));

    private Task<HttpResponseMessage> ErstattetAsync(Guid beleg, string alterWert, string wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, $"{BelegPfad(freizeit ?? _s.X, beleg)}/erstattet", Koerper(0, alterWert, "erstattet", wert));

    private async Task<List<JsonElement>> BelegeAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(BelegePfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray().Select(b => b.Clone())];
    }

    private async Task<JsonElement> EinzelnAsync(Guid beleg) => Assert.Single(await BelegeAsync(), b => Id(b) == beleg);

    private async Task<JsonElement> SollIstAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(SollIstPfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private static async Task OkAsync(Task<HttpResponseMessage> aufruf, HttpStatusCode erwartet = HttpStatusCode.OK)
    {
        using var antwort = await aufruf;
        Assert.Equal(erwartet, antwort.StatusCode);
    }

    private Task<long> AnzahlAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM finanzen.beleg WHERE {bedingung}", parameter);

    private Task<byte[]?> RohAsync(Guid beleg) => DokumentHelfer.RohAsync(s3, _s.Bucket!, $"finanzen/{beleg}");

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static async Task FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
    }

    private static string Text(decimal wert) => wert.ToString(CultureInfo.InvariantCulture);

    // ---------- Anlegen ----------

    [Fact]
    public async Task Beleg_von_Hand_201_bezahlt_von_der_angemeldeten_Person_nicht_erstattet_ohne_Datei()
    {
        var beleg = await NeuAsync("Verpflegung", "23,40", client: _s.Tobias);

        Assert.Equal(["betrag", "bezahltVon", "bezeichnung", "datei", "erfasstAm", "erstattet", "id", "kategorie", "version"], Namen(beleg));
        Assert.Equal(
            ("Verpflegung", 23.4m, _s.TobiasId, false, JsonValueKind.Null, JsonValueKind.Null),
            (beleg.GetProperty("kategorie").GetString(), beleg.GetProperty("betrag").GetDecimal(), beleg.GetProperty("bezahltVon").GetGuid(),
                beleg.GetProperty("erstattet").GetBoolean(), beleg.GetProperty("bezeichnung").ValueKind, beleg.GetProperty("datei").ValueKind));
        Assert.Equal(_s.Factory.Uhr.GetUtcNow(), beleg.GetProperty("erfasstAm").GetDateTimeOffset());
        Assert.NotEqual(0u, beleg.GetProperty("version").GetUInt32());
        Assert.Equal(Id(beleg), Id(Assert.Single(await BelegeAsync(_s.Tobias))));
        Assert.Equal(1L, await AnzahlAsync("\"OrganisationId\" = @o AND \"FreizeitId\" = @f AND \"Betrag\" = 23.40", ("o", _s.A), ("f", _s.X)));
    }

    [Fact]
    public async Task Miriam_traegt_einen_Beleg_fuer_Tobias_ein_die_Liste_ist_neueste_zuerst()
    {
        var erster = await NeuAsync("Fahrt", "1950", "  Busfahrt ");
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var zweiter = await NeuAsync("Sonstiges", "12.5", bezahltVon: _s.TobiasId);

        Assert.Equal("Busfahrt", erster.GetProperty("bezeichnung").GetString());
        Assert.Equal((_s.TobiasId, 12.5m), (zweiter.GetProperty("bezahltVon").GetGuid(), zweiter.GetProperty("betrag").GetDecimal()));
        Assert.Equal([Id(zweiter), Id(erster)], (await BelegeAsync(_s.Tobias)).Select(Id));
    }

    [Theory]
    [InlineData(null, "Verpflegung", null, "betrag")]
    [InlineData("1", null, null, "kategorie")]
    [InlineData("1", "xyz", null, "kategorie")]
    [InlineData("1", "2", null, "kategorie")]
    [InlineData("1.250", "Fahrt", null, "betrag")]
    [InlineData("-3", "Fahrt", null, "betrag")]
    [InlineData("1000001", "Fahrt", null, "betrag")]
    [InlineData("1", "Fahrt", "lang", "bezeichnung")]
    public async Task Pflichtfelder_fehlen_oder_sind_ungueltig_400_am_Feld_nichts_gespeichert(string? betrag, string? kategorie, string? bezeichnung, string feld)
    {
        using var antwort = await AnlegenAsync(new { betrag, kategorie, bezeichnung = bezeichnung is null ? null : new string('x', 201) });

        await FeldfehlerAsync(antwort, feld);
        Assert.Equal(0L, await AnzahlAsync());
    }

    [Fact]
    public async Task Fremder_Zahler_oder_keine_Kennung_400_am_Feld_bezahltVon_auch_beim_Aendern()
    {
        foreach (var zahler in new[] { _s.KatrinId.ToString(), Guid.CreateVersion7().ToString(), "Tobias" })
        {
            using var antwort = await AnlegenAsync(new { betrag = "5", kategorie = "Sonstiges", bezahltVon = zahler });
            await FeldfehlerAsync(antwort, "bezahltVon");
        }

        Assert.Equal(0L, await AnzahlAsync());
        var beleg = await NeuAsync("Sonstiges", "5");
        using var patch = await PatchAsync(Id(beleg), "bezahltVon", _s.MiriamId.ToString(), _s.KatrinId.ToString());
        await FeldfehlerAsync(patch, "bezahltVon");
        // Konfliktregel vor Team-Prüfung: ein veralteter alter Wert ist 409, auch mit fremdem Zahler.
        using var veraltet = await PatchAsync(Id(beleg), "bezahltVon", _s.TobiasId.ToString(), _s.KatrinId.ToString());
        Assert.Equal(HttpStatusCode.Conflict, veraltet.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(veraltet)));
        Assert.Equal(_s.MiriamId, (await EinzelnAsync(Id(beleg))).GetProperty("bezahltVon").GetGuid());
    }

    // ---------- Autosave ----------

    [Fact]
    public async Task Autosave_normalisiert_die_Werte_ein_veralteter_alter_Wert_ergibt_409_konflikt()
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40"));

        using var betrag = await PatchAsync(beleg, "betrag", "23.4", "24,50", _s.Tobias);
        var betragJson = await JsonAsync(betrag);
        Assert.Equal(["betrag", "version"], Namen(betragJson));
        Assert.Equal("24,5", betragJson.GetProperty("betrag").GetString());
        using var kategorie = await PatchAsync(beleg, "kategorie", "verpflegung", "fahrt");
        Assert.Equal("Fahrt", (await JsonAsync(kategorie)).GetProperty("kategorie").GetString());
        using var bezeichnung = await PatchAsync(beleg, "bezeichnung", null, " Tanken ");
        Assert.Equal("Tanken", (await JsonAsync(bezeichnung)).GetProperty("bezeichnung").GetString());
        using var zahler = await PatchAsync(beleg, "bezahltVon", _s.MiriamId.ToString().ToUpperInvariant(), _s.TobiasId.ToString("N"));
        Assert.Equal(_s.TobiasId.ToString("D"), (await JsonAsync(zahler)).GetProperty("bezahltVon").GetString());

        foreach (var (feld, alt, neu) in new[] { ("betrag", "23,4", "1"), ("kategorie", "Verpflegung", "Sonstiges"), ("bezeichnung", "Busfahrt", "x"), ("bezahltVon", _s.MiriamId.ToString(), _s.MiriamId.ToString()) })
        {
            using var antwort = await PatchAsync(beleg, feld, alt, neu, _s.Tobias);
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        }

        var stand = await EinzelnAsync(beleg);
        Assert.Equal(("Fahrt", 24.5m, "Tanken", _s.TobiasId), (stand.GetProperty("kategorie").GetString(), stand.GetProperty("betrag").GetDecimal(),
            stand.GetProperty("bezeichnung").GetString(), stand.GetProperty("bezahltVon").GetGuid()));

        using var leer = await PatchAsync(beleg, "betrag", "24,5", "");
        await FeldfehlerAsync(leer, "betrag");
        using var zweiFelder = await SendenAsync(_s.Miriam, HttpMethod.Patch, BelegPfad(_s.X, beleg), new { version = 0, alterWert = "x", betrag = "1", kategorie = "Fahrt" });
        Assert.Equal(HttpStatusCode.BadRequest, zweiFelder.StatusCode);
        using var erstattetHier = await PatchAsync(beleg, "erstattet", "false", "true");
        Assert.Equal(HttpStatusCode.BadRequest, erstattetHier.StatusCode);
    }

    [Fact]
    public async Task Erstattet_setzen_und_zuruecknehmen_Konflikt_409_ungueltiger_Wert_400()
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40", client: _s.Tobias));

        using var gesetzt = await ErstattetAsync(beleg, "false", "true");
        Assert.Equal(HttpStatusCode.OK, gesetzt.StatusCode);
        var json = await JsonAsync(gesetzt);
        Assert.Equal(["erstattet", "version"], Namen(json));
        Assert.Equal("true", json.GetProperty("erstattet").GetString());
        Assert.True((await EinzelnAsync(beleg)).GetProperty("erstattet").GetBoolean());

        using var veraltet = await ErstattetAsync(beleg, "false", "true", _s.Tobias);
        Assert.Equal(HttpStatusCode.Conflict, veraltet.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(veraltet)));
        using var ungueltig = await ErstattetAsync(beleg, "true", "ja");
        await FeldfehlerAsync(ungueltig, "erstattet");

        await OkAsync(ErstattetAsync(beleg, "true", "false", _s.Tobias));
        Assert.False((await EinzelnAsync(beleg)).GetProperty("erstattet").GetBoolean());
    }

    // ---------- Datei ----------

    [Fact]
    public async Task Foto_hochladen_inline_mit_nosniff_durch_ein_PDF_ersetzen_als_Anhang_entfernen_der_Beleg_bleibt()
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40", client: _s.Tobias));

        using var foto = await HochladenAsync(_s.Tobias, DateiPfad(_s.X, beleg), Png, "Kassenbon Bäcker.png");
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        var datei = (await JsonAsync(foto)).GetProperty("datei");
        Assert.Equal(("Kassenbon Bäcker.png", true), (datei.GetProperty("dateiname").GetString(), datei.GetProperty("istBild").GetBoolean()));
        Assert.Equal(Png, await RohAsync(beleg));
        using (var bild = await _s.Miriam.GetAsync(DateiPfad(_s.X, beleg), Abbruch))
        {
            Assert.Equal(HttpStatusCode.OK, bild.StatusCode);
            Assert.Equal(Png, await bild.Content.ReadAsByteArrayAsync(Abbruch));
            Assert.Equal("image/png", bild.Content.Headers.ContentType?.MediaType);
            Assert.Equal("inline", bild.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Contains("nosniff", bild.Headers.GetValues("X-Content-Type-Options"));
        }

        using var pdf = await HochladenAsync(_s.Miriam, DateiPfad(_s.X, beleg), Pdf, "rechnung.pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal(Pdf, await RohAsync(beleg));
        using (var anhang = await _s.Tobias.GetAsync(DateiPfad(_s.X, beleg), Abbruch))
        {
            await DateiPruefenAsync(anhang, Pdf, "application/pdf", "rechnung.pdf");
        }

        Assert.False((await EinzelnAsync(beleg)).GetProperty("datei").GetProperty("istBild").GetBoolean());

        await OkAsync(SendenAsync(_s.Tobias, HttpMethod.Delete, DateiPfad(_s.X, beleg)), HttpStatusCode.NoContent);
        Assert.Null(await RohAsync(beleg));
        Assert.Equal(JsonValueKind.Null, (await EinzelnAsync(beleg)).GetProperty("datei").ValueKind);
        await OkAsync(_s.Miriam.GetAsync(DateiPfad(_s.X, beleg), Abbruch), HttpStatusCode.NotFound);
        await OkAsync(SendenAsync(_s.Tobias, HttpMethod.Delete, DateiPfad(_s.X, beleg)), HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("ohne Datei")]
    [InlineData("Text")]
    [InlineData("leer")]
    [InlineData("zu groß")]
    public async Task Falsche_Datei_400_am_Feld_datei_mit_Text_der_Dokumente_der_Beleg_bleibt_ohne_Datei(string fall)
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40"));
        var (inhalt, text) = fall switch
        {
            "ohne Datei" => ((byte[]?)null, "Datei fehlt."),
            "Text" => ("Kein Foto, nur Text."u8.ToArray(), "Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF."),
            "leer" => ([], "Die Datei ist leer."),
            _ => ([.. Png, .. new byte[(10 * 1024 * 1024) + 1 - Png.Length]], "Die Datei ist größer als 10 MB."),
        };

        using var antwort = await HochladenAsync(_s.Miriam, DateiPfad(_s.X, beleg), inhalt, "kassenbon.png");

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(text, (await ProblemAsync(antwort)).GetProperty("errors").GetProperty("datei")[0].GetString());
        Assert.Equal(JsonValueKind.Null, (await EinzelnAsync(beleg)).GetProperty("datei").ValueKind);
        Assert.Null(await RohAsync(beleg));
    }

    [Fact]
    public async Task Beleg_loeschen_204_loescht_auch_die_Datei_nochmal_404()
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40"));
        var anderer = Id(await NeuAsync("Fahrt", "10"));
        await OkAsync(HochladenAsync(_s.Miriam, DateiPfad(_s.X, beleg), Png, "kassenbon.png"));
        await OkAsync(HochladenAsync(_s.Miriam, DateiPfad(_s.X, anderer), Pdf, "rechnung.pdf"));

        await OkAsync(SendenAsync(_s.Tobias, HttpMethod.Delete, BelegPfad(_s.X, beleg)), HttpStatusCode.NoContent);

        Assert.Null(await RohAsync(beleg));
        Assert.Equal(Pdf, await RohAsync(anderer));
        Assert.Equal([anderer], (await BelegeAsync()).Select(Id));
        await OkAsync(SendenAsync(_s.Miriam, HttpMethod.Delete, BelegPfad(_s.X, beleg)), HttpStatusCode.NotFound);
    }

    // ---------- Soll-Ist ----------

    [Fact]
    public async Task Soll_Ist_Verpflegung_knapp_Fahrt_ueberschritten_leere_Kategorie_im_Plan_Erstattungen_nach_Offen()
    {
        // Soll: 40 TN + 10 MA, 12 Tage; Verpflegung 7 € → 4.200 €, Fahrt 1.800 € fix.
        foreach (var (pfad, feld, wert) in new[]
                 {
                     ($"{Finanzen(_s.X)}/kalkulation", "teilnehmende", "40"), ($"{Finanzen(_s.X)}/kalkulation", "mitarbeitende", "10"),
                     ($"{Finanzen(_s.X)}/kalkulation/kategorien/Verpflegung", "proPersonUndTag", "7"), ($"{Finanzen(_s.X)}/kalkulation/kategorien/Fahrt", "fixkosten", "1800"),
                 })
        {
            await OkAsync(SendenAsync(_s.Miriam, HttpMethod.Patch, pfad, Koerper(0, "0", feld, wert)));
        }

        var leer = await SollIstAsync(_s.Tobias);
        Assert.Equal(["erstattungen", "gesamt", "zeilen"], Namen(leer));
        Assert.Empty(leer.GetProperty("erstattungen").EnumerateArray());

        var grosseinkauf = Id(await NeuAsync("Verpflegung", "3854,20", "Großeinkauf"));
        await NeuAsync("Verpflegung", "23,40", client: _s.Tobias);
        await NeuAsync("Verpflegung", "22,40", client: _s.Tobias);
        var bus = Id(await NeuAsync("Fahrt", "1950"));
        await OkAsync(ErstattetAsync(grosseinkauf, "false", "true"));
        await OkAsync(ErstattetAsync(bus, "false", "true", _s.Tobias));

        var sollIst = await SollIstAsync(_s.Tobias);

        Assert.Equal(
            [
                "Unterkunft 0 0 ImPlan", "Verpflegung 4200 3900 Knapp", "Fahrt 1800 1950 Ueberschritten", "ProgrammUndMaterial 0 0 ImPlan", "Mitarbeitende 0 0 ImPlan",
                "Sonstiges 0 0 ImPlan",
            ],
            sollIst.GetProperty("zeilen").EnumerateArray().Select(z =>
                $"{z.GetProperty("kategorie").GetString()} {Text(z.GetProperty("soll").GetDecimal())} {Text(z.GetProperty("ist").GetDecimal())} {z.GetProperty("status").GetString()}"));
        var gesamt = sollIst.GetProperty("gesamt");
        Assert.Equal((JsonValueKind.Null, 6000m, 5850m, "Knapp"), (gesamt.GetProperty("kategorie").ValueKind, gesamt.GetProperty("soll").GetDecimal(),
            gesamt.GetProperty("ist").GetDecimal(), gesamt.GetProperty("status").GetString()));
        Assert.Equal(
            [$"{_s.TobiasId} 45.8 0", $"{_s.MiriamId} 0 5804.2"],
            sollIst.GetProperty("erstattungen").EnumerateArray().Select(e =>
                $"{e.GetProperty("personId").GetGuid()} {Text(e.GetProperty("offen").GetDecimal())} {Text(e.GetProperty("erstattet").GetDecimal())}"));

        // Ohne Abrechnungsabschluss: ein neuer Beleg steht beim nächsten Aufruf sofort darin.
        await NeuAsync("Fahrt", "10", client: _s.Tobias);
        Assert.Equal(1960m, (await SollIstAsync()).GetProperty("zeilen")[2].GetProperty("ist").GetDecimal());
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufen_Lesen_und_Erstattet_200_jede_andere_Schreibaktion_409_freizeit_abgeschlossen()
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40", client: _s.Tobias));
        await OkAsync(HochladenAsync(_s.Tobias, DateiPfad(_s.X, beleg), Png, "kassenbon.png"));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        Assert.Single(await BelegeAsync(_s.Tobias));
        await SollIstAsync(_s.Tobias);
        await OkAsync(_s.Tobias.GetAsync(DateiPfad(_s.X, beleg), Abbruch));
        await OkAsync(ErstattetAsync(beleg, "false", "true", _s.Tobias));

        var antworten = new List<HttpResponseMessage>
        {
            await AnlegenAsync(new { betrag = "5", kategorie = "Sonstiges" }),
            await PatchAsync(beleg, "betrag", "23,4", "30", _s.Tobias),
            await HochladenAsync(_s.Miriam, DateiPfad(_s.X, beleg), Pdf, "rechnung.pdf"),
            await SendenAsync(_s.Miriam, HttpMethod.Delete, DateiPfad(_s.X, beleg)),
            await SendenAsync(_s.Tobias, HttpMethod.Delete, BelegPfad(_s.X, beleg)),
        };

        foreach (var antwort in antworten)
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
            antwort.Dispose();
        }

        var stand = await EinzelnAsync(beleg);
        Assert.Equal((23.4m, true, true), (stand.GetProperty("betrag").GetDecimal(), stand.GetProperty("erstattet").GetBoolean(), stand.GetProperty("datei").GetProperty("istBild").GetBoolean()));
        Assert.Equal(Png, await RohAsync(beleg));
        Assert.Equal(1L, await AnzahlAsync());
    }

    // ---------- Rechte und Schalter ----------

    /// <summary>Alle neun Routen für einen Client unter einer Freizeit; der Beleg gehört zu X und hat noch keine Datei.</summary>
    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid beleg) =>
    [
        () => client.GetAsync(BelegePfad(freizeit), Abbruch),
        () => AnlegenAsync(new { betrag = "5", kategorie = "Sonstiges" }, client, freizeit),
        () => PatchAsync(beleg, "bezeichnung", null, "Getränke", client, freizeit),
        () => ErstattetAsync(beleg, "false", "true", client, freizeit),
        () => HochladenAsync(client, DateiPfad(freizeit, beleg), Png, "kassenbon.png"),
        () => client.GetAsync(DateiPfad(freizeit, beleg), Abbruch),
        () => SendenAsync(client, HttpMethod.Delete, DateiPfad(freizeit, beleg)),
        () => client.GetAsync(SollIstPfad(freizeit), Abbruch),
        () => SendenAsync(client, HttpMethod.Delete, BelegPfad(freizeit, beleg)),
    ];

    private async Task<List<HttpStatusCode>> StatusAllerRoutenAsync(HttpClient client, Guid freizeit, Guid beleg)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, beleg))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_2xx_auf_allen_neun_Routen()
    {
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            var beleg = Id(await NeuAsync("Verpflegung", "23,40"));

            var status = await StatusAllerRoutenAsync(client, _s.X, beleg);

            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NoContent,
                    HttpStatusCode.OK, HttpStatusCode.NoContent,
                ],
                status);
        }
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_Leitung_von_Z_unbekannte_Freizeit_404_ohne_Sitzung_401_auf_allen_neun_Routen()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kasse@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var beleg = Id(await NeuAsync("Verpflegung", "23,40"));
        var faelle = new (HttpClient Client, Guid Freizeit, HttpStatusCode Erwartet)[]
        {
            (_s.Andrea, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, fremde, HttpStatusCode.NotFound),
            (sara, fremde, HttpStatusCode.NotFound),
            (leitungZ, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
            (_s.Anonym, _s.X, HttpStatusCode.Unauthorized),
        };

        foreach (var (client, freizeit, erwartet) in faelle)
        {
            Assert.All(await StatusAllerRoutenAsync(client, freizeit, beleg), s => Assert.Equal(erwartet, s));
        }

        // Leitung von Z (Organisation B) mit Finanzen an: der Beleg aus X bleibt 404.
        var inZ = (await StatusAllerRoutenAsync(_s.Miriam, _s.Z, beleg)).Where((_, i) => i is not (0 or 1 or 7));
        Assert.All(inZ, s => Assert.Equal(HttpStatusCode.NotFound, s));
        var stand = await EinzelnAsync(beleg);
        Assert.Equal((JsonValueKind.Null, false), (stand.GetProperty("bezeichnung").ValueKind, stand.GetProperty("erstattet").GetBoolean()));
        Assert.Equal(1L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Null(await RohAsync(beleg));
    }

    [Fact]
    public async Task Unbekannter_Beleg_auch_aus_Freizeit_Z_404()
    {
        var inZ = Id(await NeuAsync("Fahrt", "10", freizeit: _s.Z));
        await OkAsync(HochladenAsync(_s.Miriam, DateiPfad(_s.Z, inZ), Png, "kassenbon.png"));

        foreach (var beleg in new[] { inZ, Guid.CreateVersion7() })
        {
            Assert.All((await StatusAllerRoutenAsync(_s.Miriam, _s.X, beleg)).Where((_, i) => i is not (0 or 1 or 7)), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.Equal(Png, await RohAsync(inZ));
        Assert.Single(await BelegeAsync(freizeit: _s.Z));
    }

    [Fact]
    public async Task Schalter_aus_jede_Route_404_fuer_Leitung_und_Team_ohne_Sitzung_401_nach_dem_Einschalten_sind_die_Daten_wieder_da()
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40"));
        await OkAsync(HochladenAsync(_s.Miriam, DateiPfad(_s.X, beleg), Png, "kassenbon.png"));
        await SchaltenAsync(_s.X, an: false);

        foreach (var client in new[] { _s.Miriam, _s.Tobias, _s.Andrea })
        {
            Assert.All(await StatusAllerRoutenAsync(client, _s.X, beleg), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.All(await StatusAllerRoutenAsync(_s.Anonym, _s.X, beleg), s => Assert.Equal(HttpStatusCode.Unauthorized, s));

        await SchaltenAsync(_s.X, an: true);
        var wieder = Assert.Single(await BelegeAsync(_s.Tobias));
        Assert.Equal((beleg, true), (Id(wieder), wieder.GetProperty("datei").GetProperty("istBild").GetBoolean()));
        Assert.Equal(Png, await RohAsync(beleg));
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_Belege_und_Dateien_der_abgelaufenen_Freizeit_die_andere_bleibt()
    {
        var inX = Id(await NeuAsync("Verpflegung", "23,40"));
        var ohneDatei = Id(await NeuAsync("Fahrt", "10"));
        var inZ = Id(await NeuAsync("Verpflegung", "5", freizeit: _s.Z));
        await OkAsync(HochladenAsync(_s.Miriam, DateiPfad(_s.X, inX), Png, "kassenbon.png"));
        await OkAsync(HochladenAsync(_s.Miriam, DateiPfad(_s.Z, inZ), Pdf, "rechnung.pdf"));

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "FinanzenLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Null(await RohAsync(inX));
        Assert.Null(await RohAsync(ohneDatei));
        Assert.Equal(1L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
        Assert.Equal(Pdf, await RohAsync(inZ));
    }

    [Fact]
    public async Task Auskunft_Tobias_sieht_seine_Belege_im_Abschnitt_Belege_Miriam_nicht()
    {
        var heute = FreizeitHelfer.Heute(_s.Factory).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var brot = Id(await NeuAsync("Verpflegung", "23,40", "Brötchen", _s.Tobias));
        _s.Factory.Uhr.Advance(TimeSpan.FromSeconds(1));
        await NeuAsync("Fahrt", "1234,5", bezahltVon: _s.TobiasId);
        await OkAsync(ErstattetAsync(brot, "false", "true"));

        var auskunft = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var miriam = (await _s.Miriam.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        Assert.Equal(
            [
                new DatenauskunftZeile($"Beleg vom {heute}", "Verpflegung, 23,40 €, Brötchen, erstattet: ja"),
                new DatenauskunftZeile($"Beleg vom {heute}", "Fahrt, 1.234,50 €, erstattet: nein"),
            ],
            Assert.Single(auskunft, a => a.Titel == "Belege").Zeilen);
        Assert.DoesNotContain(miriam, a => a.Titel == "Belege");
    }

    [Fact]
    public async Task Person_geloescht_der_Beleg_bleibt_mit_Platzhalter_keine_uuid_Spalte_in_finanzen_traegt_die_Person()
    {
        var beleg = Id(await NeuAsync("Verpflegung", "23,40", "Brötchen", _s.Tobias));
        await OkAsync(HochladenAsync(_s.Tobias, DateiPfad(_s.X, beleg), Png, "kassenbon.png"));
        var anderer = Id(await NeuAsync("Fahrt", "10"));

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        await OkAsync(PostAsync(_s.Tobias, "/api/ich/profil/loeschen"), HttpStatusCode.NoContent);
        await _s.ZustellenAsync();

        var stand = await EinzelnAsync(beleg);
        Assert.Equal((BelegFelderPlatzhalter, 23.4m, "Brötchen", true), (stand.GetProperty("bezahltVon").GetGuid(), stand.GetProperty("betrag").GetDecimal(),
            stand.GetProperty("bezeichnung").GetString(), stand.GetProperty("datei").GetProperty("istBild").GetBoolean()));
        Assert.Equal(GeloeschtePerson.Id, stand.GetProperty("bezahltVon").GetGuid());
        Assert.Equal(_s.MiriamId, (await EinzelnAsync(anderer)).GetProperty("bezahltVon").GetGuid());
        Assert.Equal(Png, await RohAsync(beleg));
        Assert.Empty(await FinanzenSpaltenMitAsync(_s.TobiasId));
        Assert.Equal(GeloeschtePerson.Id, Guid.Parse((await SollIstAsync()).GetProperty("erstattungen").EnumerateArray()
            .Select(e => e.GetProperty("personId").GetString()!).Single(p => p != _s.MiriamId.ToString())));
    }

    private static Guid BelegFelderPlatzhalter => Zeltlotse.Api.Contracts.Finanzen.BelegFelder.GeloeschtePerson;

    /// <summary>Alle Spalten vom Typ uuid im Schema finanzen (außer Outbox und Inbox), die die Kennung noch enthalten.</summary>
    private async Task<List<string>> FinanzenSpaltenMitAsync(Guid kennung)
    {
        var spalten = new List<(string Tabelle, string Spalte)>();
        await using var db = new NpgsqlConnection(_s.Verbindung);
        await db.OpenAsync(Abbruch);
        await using (var befehl = new NpgsqlCommand(
                         "SELECT table_name, column_name FROM information_schema.columns WHERE data_type = 'uuid' AND table_schema = 'finanzen' " +
                         "AND table_name NOT IN ('outbox', 'outbox_zustellung', 'inbox', 'inbox_stand')",
                         db))
        await using (var leser = await befehl.ExecuteReaderAsync(Abbruch))
        {
            while (await leser.ReadAsync(Abbruch))
            {
                spalten.Add((leser.GetString(0), leser.GetString(1)));
            }
        }

        Assert.Contains(spalten, s => s == ("beleg", "PersonId"));
        var treffer = new List<string>();
        foreach (var (tabelle, spalte) in spalten)
        {
            await using var zaehlen = new NpgsqlCommand($"SELECT count(*) FROM finanzen.\"{tabelle}\" WHERE \"{spalte}\" = @k", db);
            zaehlen.Parameters.AddWithValue("k", kennung);
            if ((long)(await zaehlen.ExecuteScalarAsync(Abbruch))! > 0)
            {
                treffer.Add($"{tabelle}.{spalte}");
            }
        }

        return treffer;
    }
}
