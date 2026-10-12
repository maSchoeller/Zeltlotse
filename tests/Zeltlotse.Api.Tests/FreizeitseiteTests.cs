using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-1, Freizeitseite und Vertrauensseite (FR-15, FR-16, FR-66, FR-67, AD-11, AD-19, AD-30): Einstellungen der Freizeitseite
/// (Leitung), Anmeldelinks erneuern, die anonymen Token-Endpunkte und die statischen öffentlichen Seiten.
/// </summary>
public sealed class FreizeitseiteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Besucher = "203.0.113.7";

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _lea = null!;
    private HttpClient _miriam = null!;
    private HttpClient _jonas = null!;
    private HttpClient _ohneRolle = null!;
    private HttpClient _anonym = null!;
    private Guid _a;
    private Guid _x;
    private Guid _z;

    /// <summary>
    /// Organisation A (Andrea) mit Freizeit X: Miriam Leitung (Herkunft Musterstadt), Jonas Team (per SQL). Organisation B (Lea)
    /// mit Freizeit Z, Leitung Miriam. Tobias ohne Rolle.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = OrgHelfer.Factory(_verbindung);
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        (_a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "CVJM Musterstadt", Andrea);
        Guid b;
        (b, _lea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "EJW Beispielhausen", Lea);
        _x = await AnlegenAsync(_andrea, _a, "Zeltlager Kapf 2027");
        _z = await AnlegenAsync(_lea, b, "Z", new DateOnly(2027, 9, 1), new DateOnly(2027, 9, 3));
        _miriam = await LeitungAsync(_factory, _andrea, _a, _x, Miriam);
        await FreizeitHelfer.EinladenAsync(_lea, b, _z, Miriam);
        await AnnehmenMitAsync(_factory, _miriam, Miriam);
        await NameSetzenAsync(_miriam, "Miriam", "Zeltner");
        await HerkunftSetzenAsync(_miriam, "Musterstadt");
        _jonas = await AngemeldeterClientAsync(_factory, Jonas);
        await NameSetzenAsync(_jonas, "Jonas", "Hüttner");
        await AusfuehrenAsync(
            _verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"OrganisationId\", \"FreizeitId\", \"PersonId\", \"Rolle\", \"VergebenAm\") VALUES (@o, @f, @p, 2, now())",
            ("o", _a), ("f", _x), ("p", await PersonIdAsync(_verbindung, Jonas)));
        _ohneRolle = await AngemeldeterClientAsync(_factory, Tobias);
        _anonym = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _lea, _miriam, _jonas, _ohneRolle, _anonym })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    // ---------- Einstellungen (Leitung) ----------

    [Fact]
    public async Task Leitung_laedt_die_Einstellungen_mit_Version_leeren_Feldern_und_beiden_Anmeldelinks()
    {
        using var antwort = await _miriam.GetAsync(EinstellungenPfad(_x), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["anmeldelinks", "beschreibung", "hoechstzahlTeilnehmende", "ort", "version"], Namen(json));
        Assert.True(json.GetProperty("version").GetUInt32() > 0);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("beschreibung").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("ort").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("hoechstzahlTeilnehmende").ValueKind);
        var links = json.GetProperty("anmeldelinks").EnumerateArray().ToList();
        Assert.Equal(["Teilnehmende", "Mitarbeitende"], links.Select(l => l.GetProperty("art").GetString()));
        Assert.All(links, l => Assert.Matches("^https://localhost/freizeit/[A-Za-z0-9_-]{43}$", l.GetProperty("adresse").GetString()));
        Assert.NotEqual(links[0].GetProperty("adresse").GetString(), links[1].GetProperty("adresse").GetString());

        // Wiederholt geladen: dieselben Links (verschlüsselt gespeichert, AD-11).
        var zweites = await AnmeldelinksAsync(_miriam, _x);
        Assert.Equal(links.Select(l => l.GetProperty("adresse").GetString()), zweites.Values);
        // Nur der Hash und der Chiffretext liegen in der Datenbank, nie das Token.
        var token = Token(zweites["Teilnehmende"]);
        Assert.Equal(0L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldelink WHERE encode(\"TokenHash\", 'escape') LIKE @t OR encode(\"TokenGeschuetzt\", 'escape') LIKE @t", ("t", $"%{token}%")));
        Assert.Equal(2L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldelink WHERE \"FreizeitId\" = @f", ("f", _x)));
    }

    [Fact]
    public async Task Kontext_erlaubt_der_Leitung_die_Einstellungen_dem_Team_nicht()
    {
        var leitung = await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/kontext", Abbruch));
        var team = await JsonAsync(await _jonas.GetAsync($"/api/freizeiten/{_x}/kontext", Abbruch));

        Assert.Contains(Aktionsschluessel.FreizeitEinstellungen, leitung.GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["Team"], team.GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
        Assert.Empty(team.GetProperty("erlaubt").EnumerateArray());
    }

    [Fact]
    public async Task Team_bekommt_403_ohne_Rolle_fremde_Organisation_und_unbekannte_Freizeit_404()
    {
        var version = await VersionAsync(_x);
        var patch = $$"""{"version":{{version}},"alterWert":null,"ort":"Kapf"}""";

        foreach (var (client, freizeit, status) in new (HttpClient, Guid, HttpStatusCode)[]
                 {
                     (_jonas, _x, HttpStatusCode.Forbidden),
                     (_andrea, _x, HttpStatusCode.NotFound),
                     (_lea, _x, HttpStatusCode.NotFound),
                     (_gerd, _x, HttpStatusCode.NotFound),
                     (_ohneRolle, _x, HttpStatusCode.NotFound),
                     (_miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
                 })
        {
            using var laden = await client.GetAsync(EinstellungenPfad(freizeit), Abbruch);
            using var aendern = await SendenAsync(client, HttpMethod.Patch, EinstellungenPfad(freizeit), patch);
            using var erneuern = await PostAsync(client, $"/api/freizeiten/{freizeit}/anmeldelinks/teilnehmende/erneuern");

            Assert.Equal(status, laden.StatusCode);
            Assert.Equal(status, aendern.StatusCode);
            Assert.Equal(status, erneuern.StatusCode);
        }

        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await _miriam.GetAsync(EinstellungenPfad(_x), Abbruch))).GetProperty("ort").ValueKind);
    }

    [Fact]
    public async Task Autosave_speichert_je_Feld_liefert_neue_Version_und_meldet_Konflikt_nur_fuer_dasselbe_Feld()
    {
        var version = await VersionAsync(_x);

        using var beschreibung = await PatchAsync(_x, $$"""{"version":{{version}},"alterWert":null,"beschreibung":"  Eine Woche Zeltlager.  "}""");
        Assert.Equal(HttpStatusCode.OK, beschreibung.StatusCode);
        var json = await JsonAsync(beschreibung);
        Assert.Equal(["beschreibung", "version"], Namen(json));
        Assert.Equal("Eine Woche Zeltlager.", json.GetProperty("beschreibung").GetString());
        Assert.NotEqual(version, json.GetProperty("version").GetUInt32());

        // Anderes Feld mit der alten Version: wird angewendet (AD-19).
        using var ort = await PatchAsync(_x, $$"""{"version":{{version}},"alterWert":null,"ort":"Kapf"}""");
        Assert.Equal(HttpStatusCode.OK, ort.StatusCode);
        using var zahl = await PatchAsync(_x, $$"""{"version":{{version}},"alterWert":null,"hoechstzahlTeilnehmende":40}""");
        Assert.Equal(HttpStatusCode.OK, zahl.StatusCode);
        Assert.Equal("40", (await JsonAsync(zahl)).GetProperty("hoechstzahlTeilnehmende").GetString());

        // Dasselbe Feld mit abweichendem alten Wert: 409.
        using var konflikt = await PatchAsync(_x, $$"""{"version":{{version}},"alterWert":null,"beschreibung":"Anders"}""");
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        // Leer heißt: keine Höchstzahl.
        using var leer = await PatchAsync(_x, $$"""{"version":{{version}},"alterWert":"40","hoechstzahlTeilnehmende":""}""");
        Assert.Equal(HttpStatusCode.OK, leer.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(leer)).GetProperty("hoechstzahlTeilnehmende").ValueKind);

        var geladen = await JsonAsync(await _miriam.GetAsync(EinstellungenPfad(_x), Abbruch));
        Assert.Equal(("Eine Woche Zeltlager.", "Kapf", JsonValueKind.Null), (
            geladen.GetProperty("beschreibung").GetString(),
            geladen.GetProperty("ort").GetString(),
            geladen.GetProperty("hoechstzahlTeilnehmende").ValueKind));
    }

    [Theory]
    [InlineData("beschreibung", "\"x\"", 2001, "beschreibung")]
    [InlineData("ort", "\"x\"", 101, "ort")]
    [InlineData("hoechstzahlTeilnehmende", "\"0\"", 0, "hoechstzahlTeilnehmende")]
    [InlineData("hoechstzahlTeilnehmende", "\"1001\"", 0, "hoechstzahlTeilnehmende")]
    [InlineData("hoechstzahlTeilnehmende", "\"viele\"", 0, "hoechstzahlTeilnehmende")]
    [InlineData("hoechstzahlTeilnehmende", "-3", 0, "hoechstzahlTeilnehmende")]
    public async Task Ungueltige_Werte_ergeben_400_mit_Fehler_am_Feld(string feld, string wert, int laenge, string fehlerFeld)
    {
        var version = await VersionAsync(_x);
        var neu = laenge > 0 ? JsonSerializer.Serialize(new string('x', laenge)) : wert;

        using var antwort = await PatchAsync(_x, $$"""{"version":{{version}},"alterWert":null,"{{feld}}":{{neu}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(fehlerFeld, out _));
    }

    [Fact]
    public async Task Erneuern_zeigt_einen_neuen_Link_der_alte_gibt_404_der_andere_bleibt()
    {
        var vorher = await AnmeldelinksAsync(_miriam, _x);
        var alt = Token(vorher["Teilnehmende"]);
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(OeffentlichPfad(alt), Abbruch)).StatusCode);

        using var antwort = await PostAsync(_miriam, $"/api/freizeiten/{_x}/anmeldelinks/teilnehmende/erneuern");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["adresse", "art"], Namen(json));
        Assert.Equal("Teilnehmende", json.GetProperty("art").GetString());
        var neu = Token(json.GetProperty("adresse").GetString()!);
        Assert.NotEqual(alt, neu);
        var nachher = await AnmeldelinksAsync(_miriam, _x);
        Assert.Equal(json.GetProperty("adresse").GetString(), nachher["Teilnehmende"]);
        Assert.Equal(vorher["Mitarbeitende"], nachher["Mitarbeitende"]);
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(OeffentlichPfad(alt), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(OeffentlichPfad(alt) + "/vertrauensseite", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(OeffentlichPfad(neu), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(OeffentlichPfad(Token(vorher["Mitarbeitende"])), Abbruch)).StatusCode);

        using var unbekannt = await PostAsync(_miriam, $"/api/freizeiten/{_x}/anmeldelinks/eltern/erneuern");
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
    }

    [Fact]
    public async Task Abgeschlossene_Freizeit_laesst_sich_nicht_mehr_aendern_und_keine_Links_erneuern()
    {
        var version = await VersionAsync(_x);
        await AusfuehrenAsync(_verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _x));

        using var aendern = await PatchAsync(_x, $$"""{"version":{{version}},"alterWert":null,"ort":"Kapf"}""");
        using var erneuern = await PostAsync(_miriam, $"/api/freizeiten/{_x}/anmeldelinks/teilnehmende/erneuern");

        Assert.Equal(HttpStatusCode.Conflict, aendern.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(aendern)));
        Assert.Equal(HttpStatusCode.Conflict, erneuern.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(erneuern)));
    }

    // ---------- Öffentliche API (Token) ----------

    [Fact]
    public async Task Freizeitseite_liefert_Organisation_Freizeit_und_Team_nur_mit_Vorname_Initialen_Rolle_und_Herkunft()
    {
        await FeldAsync("beschreibung", "Eine Woche Zeltlager auf dem Kapf.");
        await FeldAsync("ort", "Zeltplatz Kapf");
        var token = Token((await AnmeldelinksAsync(_miriam, _x))["Teilnehmende"]);

        using var antwort = await _anonym.GetAsync(OeffentlichPfad(token), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var roh = await antwort.Content.ReadAsStringAsync(Abbruch);
        var json = await JsonAsync(antwort);
        Assert.Equal(["art", "ausgebucht", "beschreibung", "ersterTag", "letzterTag", "name", "organisation", "ort", "team"], Namen(json));
        Assert.Equal(("CVJM Musterstadt", "Zeltlager Kapf 2027", "2027-08-02", "2027-08-13", "Zeltplatz Kapf", "Eine Woche Zeltlager auf dem Kapf.", "Teilnehmende", false), (
            json.GetProperty("organisation").GetString(),
            json.GetProperty("name").GetString(),
            json.GetProperty("ersterTag").GetString(),
            json.GetProperty("letzterTag").GetString(),
            json.GetProperty("ort").GetString(),
            json.GetProperty("beschreibung").GetString(),
            json.GetProperty("art").GetString(),
            json.GetProperty("ausgebucht").GetBoolean()));
        var team = json.GetProperty("team").EnumerateArray().ToList();
        Assert.All(team, p => Assert.Equal(["herkunft", "initialen", "rolle", "vorname"], Namen(p)));
        Assert.Equal(
            ["Miriam MZ Leitung Musterstadt", "Jonas JH Team "],
            team.Select(p => $"{p.GetProperty("vorname").GetString()} {p.GetProperty("initialen").GetString()} {p.GetProperty("rolle").GetString()} {p.GetProperty("herkunft").GetString()}"));
        // Kein Nachname, keine Mail-Adresse, keine Kennung einer Person.
        Assert.DoesNotContain("Zeltner", roh, StringComparison.Ordinal);
        Assert.DoesNotContain("Hüttner", roh, StringComparison.Ordinal);
        Assert.DoesNotContain("@", roh, StringComparison.Ordinal);
        Assert.DoesNotContain((await PersonIdAsync(_verbindung, Miriam)).ToString(), roh, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Der_Link_fuer_Mitarbeitende_oeffnet_dieselbe_Freizeitseite()
    {
        var token = Token((await AnmeldelinksAsync(_miriam, _x))["Mitarbeitende"]);

        var json = await JsonAsync(await _anonym.GetAsync(OeffentlichPfad(token), Abbruch));

        Assert.Equal(("Zeltlager Kapf 2027", "Mitarbeitende"), (json.GetProperty("name").GetString(), json.GetProperty("art").GetString()));
    }

    [Fact]
    public async Task Token_von_X_liest_nur_X_und_Token_von_Z_nur_Z()
    {
        var x = Token((await AnmeldelinksAsync(_miriam, _x))["Teilnehmende"]);
        var z = Token((await AnmeldelinksAsync(_miriam, _z))["Teilnehmende"]);

        var freizeitX = await JsonAsync(await _anonym.GetAsync(OeffentlichPfad(x), Abbruch));
        var freizeitZ = await JsonAsync(await _anonym.GetAsync(OeffentlichPfad(z), Abbruch));
        var vertrauenZ = await JsonAsync(await _anonym.GetAsync(OeffentlichPfad(z) + "/vertrauensseite", Abbruch));

        Assert.Equal(("Zeltlager Kapf 2027", "CVJM Musterstadt"), (freizeitX.GetProperty("name").GetString(), freizeitX.GetProperty("organisation").GetString()));
        Assert.Equal(("Z", "EJW Beispielhausen"), (freizeitZ.GetProperty("name").GetString(), freizeitZ.GetProperty("organisation").GetString()));
        Assert.Equal(("Z", "EJW Beispielhausen"), (vertrauenZ.GetProperty("name").GetString(), vertrauenZ.GetProperty("organisation").GetString()));
    }

    [Fact]
    public async Task Unbekanntes_falsch_geformtes_und_Token_mit_anderem_Zweck_ergeben_404_ohne_Sicherheitsprotokoll()
    {
        var leitungsToken = LeitungsToken(_factory, Miriam);
        var vorher = await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);

        foreach (var token in new[] { Zeltlotse.SharedKernel.Zugangslinks.Zugangslink.NeuesToken(), "kurz", leitungsToken })
        {
            using var freizeit = await _anonym.GetAsync(OeffentlichPfad(token), Abbruch);
            using var vertrauen = await _anonym.GetAsync(OeffentlichPfad(token) + "/vertrauensseite", Abbruch);

            Assert.Equal(HttpStatusCode.NotFound, freizeit.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, vertrauen.StatusCode);
        }

        Assert.Equal(vorher, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Nach_dem_Freizeitende_gibt_der_Link_404()
    {
        var token = Token((await AnmeldelinksAsync(_miriam, _x))["Teilnehmende"]);

        // Freizeitende: 14.08.2027 00:00 Europe/Berlin = 13.08.2027 22:00 UTC.
        _factory.Uhr.SetUtcNow(new DateTimeOffset(2027, 8, 13, 21, 59, 0, TimeSpan.Zero));
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(OeffentlichPfad(token), Abbruch)).StatusCode);

        _factory.Uhr.SetUtcNow(new DateTimeOffset(2027, 8, 13, 22, 0, 0, TimeSpan.Zero));
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(OeffentlichPfad(token), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(OeffentlichPfad(token) + "/vertrauensseite", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Vertrauensseite_nennt_die_gespeicherten_Einstellungen_und_folgt_ihren_Aenderungen()
    {
        var token = Token((await AnmeldelinksAsync(_miriam, _x))["Teilnehmende"]);

        var vorher = await JsonAsync(await _anonym.GetAsync(OeffentlichPfad(token) + "/vertrauensseite", Abbruch));

        Assert.Equal(
            ["ersterTag", "freigabeBis", "freigabeIntervallTage", "freizeitDatenMonate", "letzterTag", "nachweisBestaetigungMonate", "name", "organisation", "pflichtNachweise"],
            Namen(vorher));
        Assert.Equal(("CVJM Musterstadt", "Zeltlager Kapf 2027", 30, "2027-09-12", 12, 12), (
            vorher.GetProperty("organisation").GetString(),
            vorher.GetProperty("name").GetString(),
            vorher.GetProperty("freigabeIntervallTage").GetInt32(),
            vorher.GetProperty("freigabeBis").GetString(),
            vorher.GetProperty("freizeitDatenMonate").GetInt32(),
            vorher.GetProperty("nachweisBestaetigungMonate").GetInt32()));
        Assert.Equal(["Führungszeugnis"], vorher.GetProperty("pflichtNachweise").EnumerateArray().Select(p => p.GetString()));

        // Andrea ändert Löschfrist und Nachweis-Richtlinie.
        var fristen = await JsonAsync(await _andrea.GetAsync($"/api/organisationen/{_a}/loeschfristen", Abbruch));
        using (var frist = await SendenAsync(
                   _andrea, HttpMethod.Patch, $"/api/organisationen/{_a}/loeschfristen",
                   $$"""{"version":{{fristen.GetProperty("version").GetUInt32()}},"alterWert":"12","freizeitDatenMonate":"24"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, frist.StatusCode);
        }

        var richtlinie = await JsonAsync(await _andrea.GetAsync($"/api/organisationen/{_a}/nachweis-richtlinie", Abbruch));
        var ersteHilfe = richtlinie.EnumerateArray().Single(z => z.GetProperty("art").GetString() == "ersteHilfe");
        using (var pflicht = await SendenAsync(
                   _andrea, HttpMethod.Patch, $"/api/organisationen/{_a}/nachweis-richtlinie/ersteHilfe",
                   $$"""{"version":{{ersteHilfe.GetProperty("version").GetUInt32()}},"alterWert":"false","pflicht":"true"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, pflicht.StatusCode);
        }

        var nachher = await JsonAsync(await _anonym.GetAsync(OeffentlichPfad(token) + "/vertrauensseite", Abbruch));
        Assert.Equal(24, nachher.GetProperty("freizeitDatenMonate").GetInt32());
        Assert.Equal(["Führungszeugnis", "Erste Hilfe"], nachher.GetProperty("pflichtNachweise").EnumerateArray().Select(p => p.GetString()));

        // Die Seite selbst zeigt die neuen Werte.
        using var seite = await SeiteAsync($"/freizeit/{token}/vertrauensseite");
        var html = WebUtility.HtmlDecode(await seite.Content.ReadAsStringAsync(Abbruch));
        Assert.Contains("24 Monate nach dem", html, StringComparison.Ordinal);
        Assert.DoesNotContain("12 Monate nach dem", html, StringComparison.Ordinal);
        Assert.Contains("Erste Hilfe", html, StringComparison.Ordinal);
    }

    // ---------- Öffentliche Seiten (statisch) ----------

    [Fact]
    public async Task Freizeitseite_ist_statisches_HTML_mit_Organisation_Zeitraum_Team_und_Link_zur_Vertrauensseite()
    {
        await FeldAsync("beschreibung", "Eine Woche Zeltlager auf dem Kapf.\n\n  Mit Lagerfeuer.  ");
        await FeldAsync("ort", "Zeltplatz Kapf");
        var token = Token((await AnmeldelinksAsync(_miriam, _x))["Teilnehmende"]);

        using var antwort = await SeiteAsync($"/freizeit/{token}");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("text/html", antwort.Content.Headers.ContentType?.MediaType);
        var html = WebUtility.HtmlDecode(await antwort.Content.ReadAsStringAsync(Abbruch));
        Assert.Contains("CVJM Musterstadt", html, StringComparison.Ordinal);
        Assert.Contains("Freizeit · 2. bis 13. August 2027", html, StringComparison.Ordinal);
        Assert.Matches("<h1[^>]*>Zeltlager Kapf 2027</h1>", html);
        Assert.Contains("Zeltplatz Kapf", html, StringComparison.Ordinal);
        // Jede Zeile der Beschreibung ist ein Absatz, leere Zeilen fallen weg.
        Assert.Contains("<p>Eine Woche Zeltlager auf dem Kapf.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Mit Lagerfeuer.</p>", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"<p>\s*</p>", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html, StringComparison.Ordinal);
        Assert.Contains("Das Team", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"elkw-avatar\" aria-hidden=\"true\">MZ</span>", html, StringComparison.Ordinal);
        Assert.Contains("Miriam", html, StringComparison.Ordinal);
        Assert.Contains("Leitung · aus Musterstadt", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/freizeit/{token}/vertrauensseite\"", html, StringComparison.Ordinal);
        Assert.Contains("<title>Zeltlager Kapf 2027", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Ausgebucht", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Zeltner", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Vertrauensseite_ist_statisches_HTML_mit_den_drei_Abschnitten_und_den_Werten()
    {
        var token = Token((await AnmeldelinksAsync(_miriam, _x))["Teilnehmende"]);

        using var antwort = await SeiteAsync($"/freizeit/{token}/vertrauensseite");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var html = WebUtility.HtmlDecode(await antwort.Content.ReadAsStringAsync(Abbruch));
        Assert.Contains("CVJM Musterstadt", html, StringComparison.Ordinal);
        Assert.Contains("Welche Daten erhoben werden", html, StringComparison.Ordinal);
        Assert.Contains("Wer die Daten sieht", html, StringComparison.Ordinal);
        Assert.Contains("Wann die Daten verschwinden", html, StringComparison.Ordinal);
        Assert.Contains("Führungszeugnis", html, StringComparison.Ordinal);
        Assert.Contains("12.09.2027", html, StringComparison.Ordinal);
        Assert.Contains("30 Tage", html, StringComparison.Ordinal);
        Assert.Contains("12 Monate", html, StringComparison.Ordinal);
        Assert.Contains("Freigabefenster", html, StringComparison.Ordinal);
        Assert.Contains("Freizeitpass", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/freizeit/{token}\"", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/freizeit/{0}")]
    [InlineData("/freizeit/{0}/vertrauensseite")]
    public async Task Ungueltiger_Link_zeigt_eine_404_Seite_mit_dem_Hinweis(string muster)
    {
        using var antwort = await SeiteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, muster, Zeltlotse.SharedKernel.Zugangslinks.Zugangslink.NeuesToken()));

        var html = await antwort.Content.ReadAsStringAsync(Abbruch);
        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal("text/html", antwort.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Dieser Anmeldelink ist nicht mehr gültig.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Fehlende oder unlesbare Links, Team, Zugriffsbegrenzung ----------

    [Fact]
    public async Task Fehlen_die_Links_zeigen_die_Einstellungen_keine_und_Erneuern_legt_einen_an_der_die_Freizeitseite_oeffnet()
    {
        await AusfuehrenAsync(_verbindung, "DELETE FROM freizeiten.anmeldelink WHERE \"FreizeitId\" = @f", ("f", _x));

        Assert.Empty(await AnmeldelinksAsync(_miriam, _x));

        using var antwort = await PostAsync(_miriam, $"/api/freizeiten/{_x}/anmeldelinks/teilnehmende/erneuern");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var adresse = (await JsonAsync(antwort)).GetProperty("adresse").GetString()!;
        Assert.Matches("^https://localhost/freizeit/[A-Za-z0-9_-]{43}$", adresse);
        Assert.Equal(["Teilnehmende"], (await AnmeldelinksAsync(_miriam, _x)).Keys);
        using var seite = await _anonym.GetAsync(OeffentlichPfad(Token(adresse)), Abbruch);
        Assert.Equal(HttpStatusCode.OK, seite.StatusCode);
        Assert.Equal("Zeltlager Kapf 2027", (await JsonAsync(seite)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ein_unlesbarer_Link_fehlt_in_den_Einstellungen_und_Erneuern_stellt_ihn_wieder_her()
    {
        var vorher = await AnmeldelinksAsync(_miriam, _x);
        await AusfuehrenAsync(
            _verbindung,
            "UPDATE freizeiten.anmeldelink SET \"TokenGeschuetzt\" = decode('01020304', 'hex') WHERE \"FreizeitId\" = @f AND \"Art\" = 1",
            ("f", _x));

        using var laden = await _miriam.GetAsync(EinstellungenPfad(_x), Abbruch);
        Assert.Equal(HttpStatusCode.OK, laden.StatusCode);
        var links = (await JsonAsync(laden)).GetProperty("anmeldelinks").EnumerateArray().ToList();
        Assert.Equal(["Mitarbeitende"], links.Select(l => l.GetProperty("art").GetString()));
        Assert.Equal(vorher["Mitarbeitende"], links[0].GetProperty("adresse").GetString());

        using var erneuern = await PostAsync(_miriam, $"/api/freizeiten/{_x}/anmeldelinks/teilnehmende/erneuern");

        Assert.Equal(HttpStatusCode.OK, erneuern.StatusCode);
        var nachher = await AnmeldelinksAsync(_miriam, _x);
        Assert.Equal(["Teilnehmende", "Mitarbeitende"], nachher.Keys);
        Assert.Equal((await JsonAsync(erneuern)).GetProperty("adresse").GetString(), nachher["Teilnehmende"]);
    }

    /// <summary>
    /// Eine Person ohne Vornamen im Profil (Tobias, Team per SQL) erscheint nicht im öffentlichen Team. Zwei Rollen derselben Person
    /// in einer Freizeit gibt es nicht: Der Schlüssel von <c>freizeit_rolle</c> ist (FreizeitId, PersonId).
    /// </summary>
    [Fact]
    public async Task Personen_ohne_Vorname_fehlen_im_oeffentlichen_Team()
    {
        using (var profil = await PostAsync(_ohneRolle, "/api/ich/profil"))
        {
            Assert.Equal(HttpStatusCode.OK, profil.StatusCode);
        }

        await AusfuehrenAsync(
            _verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"OrganisationId\", \"FreizeitId\", \"PersonId\", \"Rolle\", \"VergebenAm\") VALUES (@o, @f, @p, 2, now())",
            ("o", _a), ("f", _x), ("p", await PersonIdAsync(_verbindung, Tobias)));
        var token = Token((await AnmeldelinksAsync(_miriam, _x))["Teilnehmende"]);

        using var antwort = await _anonym.GetAsync(OeffentlichPfad(token), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(
            ["Miriam Leitung", "Jonas Team"],
            (await JsonAsync(antwort)).GetProperty("team").EnumerateArray().Select(p => $"{p.GetProperty("vorname").GetString()} {p.GetProperty("rolle").GetString()}"));
    }

    [Theory]
    [InlineData("/freizeit/{0}")]
    [InlineData("/freizeit/{0}/vertrauensseite")]
    public async Task Bei_Zugriffsbegrenzung_antwortet_die_Seite_mit_429_und_einem_Hinweis_statt_500(string muster)
    {
        await using var factory = AnmeldeHelfer.Factory(await postgres.NeueDatenbankAsync(), mitZugriffsbegrenzung: true);
        factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "1";
        using var client = factory.CreateClient();
        var pfad = string.Format(System.Globalization.CultureInfo.InvariantCulture, muster, Zeltlotse.SharedKernel.Zugangslinks.Zugangslink.NeuesToken());

        using var erste = await SeiteAsync(client, pfad);
        using var zweite = await SeiteAsync(client, pfad);

        Assert.Equal(HttpStatusCode.NotFound, erste.StatusCode);
        Assert.Equal((HttpStatusCode)429, zweite.StatusCode);
        Assert.Equal("text/html", zweite.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            "Gerade kommen sehr viele Aufrufe an. Bitte in einer Minute erneut versuchen.",
            WebUtility.HtmlDecode(await zweite.Content.ReadAsStringAsync(Abbruch)),
            StringComparison.Ordinal);
    }

    // ---------- Hilfen ----------

    private static string EinstellungenPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/freizeitseite";

    private static string OeffentlichPfad(string token) => $"/api/freizeiten/oeffentlich/{token}";

    private static string Token(string adresse) => adresse[(adresse.LastIndexOf('/') + 1)..];

    private Task<HttpResponseMessage> PatchAsync(Guid freizeit, string koerper) => SendenAsync(_miriam, HttpMethod.Patch, EinstellungenPfad(freizeit), koerper);

    private async Task<uint> VersionAsync(Guid freizeit) =>
        (await JsonAsync(await _miriam.GetAsync(EinstellungenPfad(freizeit), Abbruch))).GetProperty("version").GetUInt32();

    /// <summary>Setzt ein Feld der Freizeitseite von X über den echten PATCH (alter Wert leer).</summary>
    private async Task FeldAsync(string feld, string wert)
    {
        using var antwort = await PatchAsync(_x, $$"""{"version":{{await VersionAsync(_x)}},"alterWert":null,"{{feld}}":{{JsonSerializer.Serialize(wert)}}}""");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private static async Task<Dictionary<string, string>> AnmeldelinksAsync(HttpClient client, Guid freizeit) =>
        (await JsonAsync(await client.GetAsync(EinstellungenPfad(freizeit), Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
        .ToDictionary(l => l.GetProperty("art").GetString()!, l => l.GetProperty("adresse").GetString()!);

    private Task<HttpResponseMessage> SeiteAsync(string pfad) => SeiteAsync(_anonym, pfad);

    private static async Task<HttpResponseMessage> SeiteAsync(HttpClient client, string pfad)
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, pfad);
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, Besucher);
        return await client.SendAsync(anfrage, Abbruch);
    }

    private static async Task HerkunftSetzenAsync(HttpClient client, string herkunft)
    {
        var profil = await JsonAsync(await client.GetAsync("/api/ich/profil", Abbruch));
        using var antwort = await SendenAsync(
            client, HttpMethod.Patch, "/api/ich/profil", $$"""{"version":{{profil.GetProperty("version").GetUInt32()}},"alterWert":null,"herkunft":"{{herkunft}}"}""");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }
}
