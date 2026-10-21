using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 6-4, Teilnehmer-Zugang per QR-Code (CAP-38, FR-64, NS-10, AD-11, AD-12, AD-20, AD-21, AD-30) gegen echtes PostgreSQL: QR-Link erzeugen, lesen und
/// erneuern (Leitung, nur mit dem Modul), das Einlösen gegen das Gast-Cookie, die Lese-Routen des Gasts (Freizeit, Infos, Wochenübersicht, Kanal „Alle“), das
/// eigene Push-Abo, der Gast-Push über die Outbox, der Abschluss als Durchlauf, die Token-Fälle (erneuert, anderer Zweck, Freizeitende, Schalter aus, fremde
/// Freizeit, ohne Cookie) und die Grenze zu den Team-Routen (401). Freizeit X (Organisation A; Org-Admin Andrea, Leitung Miriam, Team Tobias) und Z
/// (Organisation B, Leitung Miriam).
/// </summary>
public sealed class TeilnehmerZugangTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private static readonly string P256dh = Base64Url([0x04, .. Enumerable.Range(1, 64).Select(i => (byte)i)]);
    private static readonly string Auth = Base64Url([.. Enumerable.Range(1, 16).Select(i => (byte)i)]);

    /// <summary>Freizeitende von X: 14.08.2027 00:00 Europe/Berlin = 13.08.2027 22:00 UTC.</summary>
    private static readonly DateTimeOffset FreizeitendeX = new(2027, 8, 13, 22, 0, 0, TimeSpan.Zero);

    private readonly PushAufzeichnung _push = new();
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() =>
        _s = await FreizeitpassSzenario.AufbauenAsync(
            postgres, s3, mitTestEndpunkten: true, zusatzDienste: s => s.Replace(ServiceDescriptor.Singleton<IPushVersand>(_push)));

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string QrPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/qr-zugang";

    private static string ErneuernPfad(Guid freizeit) => $"{QrPfad(freizeit)}/erneuern";

    private const string EinloesenPfad = "/api/teilnehmer/einloesen";

    /// <summary>Die Lese-Routen des Gasts (#4 bis #8 ohne die POSTs).</summary>
    private static readonly string[] LeseRouten =
        ["/api/teilnehmer", "/api/teilnehmer/infos", "/api/teilnehmer/programm", "/api/teilnehmer/kanal", "/api/teilnehmer/push/schluessel"];

    private static string Endpunkt(string name) => $"https://fcm.googleapis.com/fcm/send/{name}";

    private static string TokenAus(string adresse) => adresse[(adresse.LastIndexOf('/') + 1)..];

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM {tabelle} WHERE {bedingung}", parameter);

    /// <summary>Miriam schaltet den Teilnehmer-Zugang einer Freizeit an oder aus (Autosave-PATCH mit dem aktuellen Stand als altem Wert).</summary>
    private async Task SchaltenAsync(bool an, Guid? freizeit = null)
    {
        var pfad = $"/api/freizeiten/{freizeit ?? _s.X}/module";
        var stand = await JsonAsync(await _s.Miriam.GetAsync(pfad, Abbruch));
        var alt = stand.GetProperty("teilnehmerZugang").GetBoolean() ? "true" : "false";
        using var antwort = await SendenAsync(
            _s.Miriam, HttpMethod.Patch, pfad,
            $$"""{"version":{{stand.GetProperty("version").GetUInt32()}},"alterWert":"{{alt}}","teilnehmerZugang":"{{(an ? "true" : "false")}}"}""");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    /// <summary>Miriam erzeugt (oder erneuert) den QR-Link und liefert die Antwort.</summary>
    private async Task<JsonElement> ErneuernAsync(Guid? freizeit = null)
    {
        using var antwort = await PostAsync(_s.Miriam, ErneuernPfad(freizeit ?? _s.X));
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    /// <summary>Teilnehmer-Zugang an, Link erzeugt; liefert das Token.</summary>
    private async Task<string> ZugangAsync(Guid? freizeit = null)
    {
        await SchaltenAsync(true, freizeit);
        return TokenAus((await ErneuernAsync(freizeit)).GetProperty("adresse").GetString()!);
    }

    private Task<HttpResponseMessage> EinloesenAsync(HttpClient client, string? token) => PostAsync(client, EinloesenPfad, new { token });

    /// <summary>Ein neuer Browser ohne Konto, der das Token eingelöst hat.</summary>
    private async Task<HttpClient> GastAsync(string token)
    {
        var gast = _s.Factory.CreateClient();
        using var antwort = await EinloesenAsync(gast, token);
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        return gast;
    }

    private static async Task<List<HttpStatusCode>> LeseStatusAsync(HttpClient client)
    {
        var status = new List<HttpStatusCode>();
        foreach (var pfad in LeseRouten)
        {
            using var antwort = await client.GetAsync(pfad, Abbruch);
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string pfad)
    {
        using var antwort = await client.GetAsync(pfad, Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task NachrichtAsync(HttpClient client, string text, string kanal = "alle", Guid? freizeit = null)
    {
        using var antwort = await PostAsync(client, $"/api/freizeiten/{freizeit ?? _s.X}/kanaele/{kanal}/nachrichten", new { text, bestaetigt = true });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
    }

    private static async Task AbonnierenAsync(HttpClient gast, string endpunkt)
    {
        using var antwort = await PostAsync(gast, "/api/teilnehmer/push/abo", new { endpunkt, p256dh = P256dh, auth = Auth });
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    // ---------- QR-Link (Leitung) ----------

    [Fact]
    public async Task Erzeugen_liefert_Adresse_und_QR_Matrix_mit_Ruhezone_GET_liefert_dasselbe_vorher_ohne_Link_null()
    {
        await SchaltenAsync(true);
        var leer = await GetJsonAsync(_s.Miriam, QrPfad(_s.X));
        Assert.Equal(["adresse", "zeilen"], Namen(leer));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (leer.GetProperty("adresse").ValueKind, leer.GetProperty("zeilen").ValueKind));

        var erzeugt = await ErneuernAsync();

        var adresse = erzeugt.GetProperty("adresse").GetString()!;
        Assert.Matches(@"^https://localhost/app/teilnehmer/[A-Za-z0-9_-]{43}$", adresse);
        var zeilen = erzeugt.GetProperty("zeilen").EnumerateArray().Select(z => z.GetString()!).ToList();
        // Quadratisch, nur '0'/'1', Version 1 bis 40 (21 + 4k Module) plus vier Module Ruhezone je Seite.
        Assert.All(zeilen, z => Assert.Equal(zeilen.Count, z.Length));
        Assert.All(zeilen, z => Assert.Matches("^[01]+$", z));
        Assert.Equal(0, (zeilen.Count - 8 - 21) % 4);
        Assert.All(zeilen.Take(4).Concat(zeilen.TakeLast(4)), z => Assert.DoesNotContain('1', z));
        Assert.All(zeilen, z => Assert.Equal("00000000", z[..4] + z[^4..]));
        // Das Suchmuster oben links (7 dunkle Module) beginnt nach der Ruhezone.
        Assert.Equal("1111111", zeilen[4].Substring(4, 7));

        var gelesen = await GetJsonAsync(_s.Miriam, QrPfad(_s.X));
        Assert.Equal(adresse, gelesen.GetProperty("adresse").GetString());
        Assert.Equal(zeilen, gelesen.GetProperty("zeilen").EnumerateArray().Select(z => z.GetString()!));
        // Gespeichert sind nur Hash und Chiffretext, nie das Token im Klartext.
        Assert.Equal(1L, await AnzahlAsync("freizeiten.qr_zugang", "\"FreizeitId\" = @f AND position(convert_to(@t, 'UTF8') in \"TokenGeschuetzt\") = 0", ("f", _s.X), ("t", TokenAus(adresse))));
    }

    [Fact]
    public async Task Rechte_Leitung_2xx_Team_403_ohne_Rolle_und_fremde_Organisation_404_Schalter_aus_404_ohne_Sitzung_401()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(QrPfad(_s.X), Abbruch)).StatusCode);
        using (var aus = await PostAsync(_s.Miriam, ErneuernPfad(_s.X)))
        {
            Assert.Equal(HttpStatusCode.NotFound, aus.StatusCode);
        }

        await SchaltenAsync(true);
        var faelle = new (HttpClient Client, HttpStatusCode Erwartet)[]
        {
            (_s.Tobias, HttpStatusCode.Forbidden),
            (_s.Andrea, HttpStatusCode.NotFound),
            (_s.Lea, HttpStatusCode.NotFound),
            (_s.Anonym, HttpStatusCode.Unauthorized),
            (_s.Miriam, HttpStatusCode.OK),
        };
        foreach (var (client, erwartet) in faelle)
        {
            using var lesen = await client.GetAsync(QrPfad(_s.X), Abbruch);
            using var erneuern = await PostAsync(client, ErneuernPfad(_s.X));
            Assert.Equal((erwartet, erwartet), (lesen.StatusCode, erneuern.StatusCode));
        }

        Assert.Equal(1L, await AnzahlAsync("freizeiten.qr_zugang"));
    }

    [Fact]
    public async Task Gelaufen_Lesen_200_Erneuern_409_freizeit_abgeschlossen()
    {
        await ZugangAsync();
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        Assert.Equal(HttpStatusCode.OK, (await _s.Miriam.GetAsync(QrPfad(_s.X), Abbruch)).StatusCode);
        using var erneuern = await PostAsync(_s.Miriam, ErneuernPfad(_s.X));
        Assert.Equal(HttpStatusCode.Conflict, erneuern.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(erneuern)));
    }

    // ---------- Einlösen und Lesen (Gast) ----------

    [Fact]
    public async Task Einloesen_204_setzt_das_Gast_Cookie_bis_Freizeitende_ohne_Token_danach_lesen_alle_Gast_Routen_200()
    {
        var token = await ZugangAsync();
        using var gast = _s.Factory.CreateClient();

        using var antwort = await EinloesenAsync(gast, token);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        var cookie = Assert.Single(antwort.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Zeltlotse-Gast=", StringComparison.Ordinal));
        var teile = cookie.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(t => t.ToLowerInvariant()).ToList();
        // Freizeitende: 14.08.2027 00:00 Europe/Berlin.
        Assert.Contains("expires=fri, 13 aug 2027 22:00:00 gmt", teile);
        Assert.Contains("path=/", teile);
        Assert.Contains("secure", teile);
        Assert.Contains("samesite=lax", teile);
        Assert.Contains("httponly", teile);
        Assert.DoesNotContain(teile, t => t.StartsWith("domain", StringComparison.Ordinal));
        Assert.DoesNotContain(token, cookie, StringComparison.Ordinal);
        // Keine normale Sitzung.
        Assert.DoesNotContain(antwort.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Zeltlotse=", StringComparison.Ordinal));

        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.OK, s));
        var freizeit = await GetJsonAsync(gast, "/api/teilnehmer");
        Assert.Equal(["ersterTag", "letzterTag", "name"], Namen(freizeit));
        Assert.Equal((FreizeitpassSzenario.Kapf, "2027-08-02", "2027-08-13"), (
            freizeit.GetProperty("name").GetString(), freizeit.GetProperty("ersterTag").GetString(), freizeit.GetProperty("letzterTag").GetString()));
        Assert.Equal(["oeffentlicherSchluessel"], Namen(await GetJsonAsync(gast, "/api/teilnehmer/push/schluessel")));
    }

    [Fact]
    public async Task Erneuert_altes_Token_und_altes_Cookie_404_das_neue_Token_wirkt()
    {
        var alt = await ZugangAsync();
        using var gast = await GastAsync(alt);

        var neu = TokenAus((await ErneuernAsync()).GetProperty("adresse").GetString()!);

        Assert.NotEqual(alt, neu);
        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.NotFound, s));
        using var andererGast = _s.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await EinloesenAsync(andererGast, alt)).StatusCode);
        using var neuerGast = await GastAsync(neu);
        Assert.All(await LeseStatusAsync(neuerGast), s => Assert.Equal(HttpStatusCode.OK, s));
        // Auch das alte Gerät kommt mit einem neuen Scan wieder hinein.
        Assert.Equal(HttpStatusCode.NoContent, (await EinloesenAsync(gast, neu)).StatusCode);
        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.OK, s));
    }

    [Fact]
    public async Task Token_mit_anderem_Zweck_unbekannt_und_falsch_geformt_404_mit_Sicherheitsprotokoll_ohne_Token()
    {
        await ZugangAsync();
        using var gast = _s.Factory.CreateClient();
        var vorher = await ProtokollAsync(_s.Verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);

        // Der Anmeldelink für Teilnehmende ist ein gültiges Token, aber mit dem Zweck „anmeldelink-teilnehmende“.
        var tokens = new[] { _s.TeilnehmendeX, Zeltlotse.SharedKernel.Zugangslinks.Zugangslink.NeuesToken(), "kurz", null };
        foreach (var token in tokens)
        {
            using var antwort = await EinloesenAsync(gast, token);
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
            Assert.False(antwort.Headers.Contains("Set-Cookie") && antwort.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith("__Host-Zeltlotse-Gast=", StringComparison.Ordinal)));
        }

        Assert.Equal(vorher + tokens.Length, await ProtokollAsync(_s.Verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(0L, await AnzahlAsync("betrieb.sicherheitsprotokoll", "\"BezugId\" IS NOT NULL AND \"Art\" = @a", ("a", (short)SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen)));
        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.NotFound, s));
    }

    [Fact]
    public async Task Ab_dem_Freizeitende_404_beim_Einloesen_und_auf_allen_Gast_Routen()
    {
        var token = await ZugangAsync();
        using var gast = await GastAsync(token);

        _s.Factory.Uhr.SetUtcNow(FreizeitendeX.AddMinutes(-1));
        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.OK, s));

        _s.Factory.Uhr.SetUtcNow(FreizeitendeX);
        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.NotFound, s));
        using var abo = await PostAsync(gast, "/api/teilnehmer/push/abo", new { endpunkt = Endpunkt("gast"), p256dh = P256dh, auth = Auth });
        Assert.Equal(HttpStatusCode.NotFound, abo.StatusCode);
        using var neu = _s.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await EinloesenAsync(neu, token)).StatusCode);
    }

    [Fact]
    public async Task Schalter_aus_404_beim_Einloesen_und_auf_allen_Gast_Routen_wieder_an_wirkt_das_Cookie_wieder()
    {
        var token = await ZugangAsync();
        using var gast = await GastAsync(token);

        await SchaltenAsync(false);

        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.NotFound, s));
        using var neu = _s.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await EinloesenAsync(neu, token)).StatusCode);

        await SchaltenAsync(true);
        Assert.All(await LeseStatusAsync(gast), s => Assert.Equal(HttpStatusCode.OK, s));
    }

    [Fact]
    public async Task Cookie_fuer_X_liest_nur_X_Cookie_fuer_Z_nur_Z()
    {
        using var gastX = await GastAsync(await ZugangAsync(_s.X));
        using var gastZ = await GastAsync(await ZugangAsync(_s.Z));
        await NachrichtAsync(_s.Miriam, "Nur in X", freizeit: _s.X);
        await NachrichtAsync(_s.Miriam, "Nur in Z", freizeit: _s.Z);

        Assert.Equal(FreizeitpassSzenario.Kapf, (await GetJsonAsync(gastX, "/api/teilnehmer")).GetProperty("name").GetString());
        Assert.Equal("Z", (await GetJsonAsync(gastZ, "/api/teilnehmer")).GetProperty("name").GetString());
        Assert.Equal(["Nur in X"], (await GetJsonAsync(gastX, "/api/teilnehmer/kanal")).GetProperty("nachrichten").EnumerateArray().Select(n => n.GetProperty("text").GetString()));
        Assert.Equal(["Nur in Z"], (await GetJsonAsync(gastZ, "/api/teilnehmer/kanal")).GetProperty("nachrichten").EnumerateArray().Select(n => n.GetProperty("text").GetString()));
        Assert.Equal(("2027-09-01", "2027-09-03"), (
            (await GetJsonAsync(gastZ, "/api/teilnehmer/programm")).GetProperty("ersterTag").GetString(),
            (await GetJsonAsync(gastZ, "/api/teilnehmer/programm")).GetProperty("letzterTag").GetString()));

        // Ein Abo des Gasts von X liegt in X (Organisation A), keines in Z.
        await AbonnierenAsync(gastX, Endpunkt("gast-x"));
        Assert.Equal(1L, await AnzahlAsync("kanaele.gast_push_abo", "\"FreizeitId\" = @f AND \"OrganisationId\" = @o", ("f", _s.X), ("o", _s.A)));
        Assert.Equal(0L, await AnzahlAsync("kanaele.gast_push_abo", "\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    [Fact]
    public async Task Ohne_Gast_Cookie_404_auch_mit_Team_Sitzung_ein_Gast_an_Team_Routen_401()
    {
        var token = await ZugangAsync();
        using var gast = await GastAsync(token);

        Assert.All(await LeseStatusAsync(_s.Anonym), s => Assert.Equal(HttpStatusCode.NotFound, s));
        Assert.All(await LeseStatusAsync(_s.Miriam), s => Assert.Equal(HttpStatusCode.NotFound, s));
        using (var abo = await PostAsync(_s.Miriam, "/api/teilnehmer/push/abo", new { endpunkt = Endpunkt("miriam"), p256dh = P256dh, auth = Auth }))
        {
            Assert.Equal(HttpStatusCode.NotFound, abo.StatusCode);
        }

        foreach (var pfad in new[]
                 {
                     $"/api/freizeiten/{_s.X}/kanaele/alle/nachrichten", $"/api/freizeiten/{_s.X}/kanaele/team/nachrichten", $"/api/freizeiten/{_s.X}/programm",
                     $"/api/freizeiten/{_s.X}/kontext", $"/api/freizeiten/{_s.X}/dokumente", "/api/ich/profil",
                 })
        {
            using var antwort = await gast.GetAsync(pfad, Abbruch);
            Assert.True(antwort.StatusCode == HttpStatusCode.Unauthorized, $"{pfad}: {antwort.StatusCode}");
        }

        // Schreiben an einer anderen Route: keine normale Sitzung.
        using var senden = await PostAsync(gast, $"/api/freizeiten/{_s.X}/kanaele/alle/nachrichten", new { text = "Hallo", bestaetigt = true });
        Assert.Equal(HttpStatusCode.Unauthorized, senden.StatusCode);
        Assert.Equal(0L, await AnzahlAsync("kanaele.nachricht"));
    }

    [Fact]
    public async Task Datenumfang_Woche_nur_Tag_Beginn_Titel_Kanal_nur_Alle_ohne_Autor_Infos_nur_Eltern_und_Teilnehmende()
    {
        var token = await ZugangAsync();
        using var gast = await GastAsync(token);
        using (var punkt = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/programm/punkte", new { titel = "Geländespiel", tag = "2027-08-03", beginn = "14:30" }))
        {
            Assert.Equal(HttpStatusCode.Created, punkt.StatusCode);
            var id = (await JsonAsync(punkt)).GetProperty("id").GetGuid();
            using var uebernehmen = await PostAsync(_s.Tobias, $"/api/freizeiten/{_s.X}/programm/punkte/{id}/uebernehmen");
            Assert.True(uebernehmen.IsSuccessStatusCode);
        }

        await NachrichtAsync(_s.Tobias, "Essen 13:30 statt 13:00");
        await NachrichtAsync(_s.Miriam, "Nur fürs Team", "team");

        using var wocheAntwort = await gast.GetAsync("/api/teilnehmer/programm", Abbruch);
        var wocheRoh = await wocheAntwort.Content.ReadAsStringAsync(Abbruch);
        var woche = JsonDocument.Parse(wocheRoh).RootElement;
        Assert.Equal(["ersterTag", "letzterTag", "punkte"], Namen(woche));
        var punkte = woche.GetProperty("punkte").EnumerateArray().ToList();
        Assert.All(punkte, p => Assert.Equal(["beginn", "tag", "titel"], Namen(p)));
        Assert.Contains(punkte, p => (p.GetProperty("tag").GetString(), p.GetProperty("beginn").GetString(), p.GetProperty("titel").GetString()) == ("2027-08-03", "14:30", "Geländespiel"));
        Assert.DoesNotContain(_s.TobiasId.ToString(), wocheRoh, StringComparison.OrdinalIgnoreCase);
        // Liest nur: Die Mahlzeiten legt erst der Wochenraster des Teams an.
        Assert.Equal(1L, await AnzahlAsync("programm.programmpunkt", "\"FreizeitId\" = @f", ("f", _s.X)));

        using var kanalAntwort = await gast.GetAsync("/api/teilnehmer/kanal", Abbruch);
        var kanalRoh = await kanalAntwort.Content.ReadAsStringAsync(Abbruch);
        var nachricht = Assert.Single(JsonDocument.Parse(kanalRoh).RootElement.GetProperty("nachrichten").EnumerateArray());
        Assert.Equal(["gesendetAm", "id", "text"], Namen(nachricht));
        Assert.Equal("Essen 13:30 statt 13:00", nachricht.GetProperty("text").GetString());
        Assert.DoesNotContain("autorPersonId", kanalRoh, StringComparison.Ordinal);
        Assert.DoesNotContain(_s.TobiasId.ToString(), kanalRoh, StringComparison.OrdinalIgnoreCase);

        var infos = await GetJsonAsync(gast, "/api/teilnehmer/infos");
        Assert.Equal(["dokumente"], Namen(infos));
        Assert.Empty(infos.GetProperty("dokumente").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await gast.GetAsync($"/api/teilnehmer/infos/{Guid.CreateVersion7()}", Abbruch)).StatusCode);
    }

    // ---------- Gast-Push ----------

    [Fact]
    public async Task Gast_Abo_bekommt_den_Push_aus_Alle_mit_der_Adresse_des_Teilnehmer_Zugangs_aus_Team_keinen()
    {
        using var gast = await GastAsync(await ZugangAsync());
        await AbonnierenAsync(gast, Endpunkt("gast"));
        using (var abo = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/kanaele/push/abo", new { endpunkt = Endpunkt("miriam"), p256dh = P256dh, auth = Auth }))
        {
            Assert.Equal(HttpStatusCode.NoContent, abo.StatusCode);
        }

        await NachrichtAsync(_s.Tobias, "Nur fürs Team", "team");
        await NachrichtAsync(_s.Tobias, "Essen 13:30 statt 13:00");
        await _s.ZustellenAsync();

        var anGast = Assert.Single(_push.Gesendet, p => p.Ziel.Endpunkt == Endpunkt("gast"));
        Assert.Equal(new PushInhalt($"{FreizeitpassSzenario.Kapf} · Alle", "Essen 13:30 statt 13:00", "/app/teilnehmer"), anGast.Inhalt);
        // Das Team bekommt beide Nachrichten wie bisher, mit der Adresse der Kanal-Seite.
        Assert.Equal(
            ["Nur fürs Team", "Essen 13:30 statt 13:00"],
            _push.Gesendet.Where(p => p.Ziel.Endpunkt == Endpunkt("miriam")).Select(p => p.Inhalt.Text));
        // In der Outbox steht nur die Kennung der Nachricht.
        Assert.DoesNotContain("Essen", await WertAsync<string>(_s.Verbindung, "SELECT string_agg(\"Inhalt\"::text, ' ') FROM kanaele.outbox"), StringComparison.Ordinal);

        // Erneut zustellen schickt nichts doppelt.
        await _s.ZustellenAsync();
        Assert.Single(_push.Gesendet, p => p.Ziel.Endpunkt == Endpunkt("gast"));
    }

    [Fact]
    public async Task Kein_Gast_Push_bei_ausgeschaltetem_Schalter_oder_nach_dem_Freizeitende_abgelaufene_Gast_Abos_werden_abgeraeumt()
    {
        using var gast = await GastAsync(await ZugangAsync());
        await AbonnierenAsync(gast, Endpunkt("gast"));
        await AbonnierenAsync(gast, Endpunkt("abgelaufen"));

        await SchaltenAsync(false);
        await NachrichtAsync(_s.Tobias, "Während der Zugang aus ist");
        await _s.ZustellenAsync();
        Assert.Empty(_push.Gesendet);

        await SchaltenAsync(true);
        await NachrichtAsync(_s.Tobias, "Wieder an");
        await _s.ZustellenAsync();
        Assert.Equal(["Wieder an"], _push.Gesendet.Select(p => p.Inhalt.Text));
        Assert.Equal(0L, await AnzahlAsync("kanaele.gast_push_abo", "\"Endpunkt\" = @e", ("e", Endpunkt("abgelaufen"))));
        Assert.Equal(1L, await AnzahlAsync("kanaele.gast_push_abo"));

        // Nach dem Freizeitende geht nichts mehr an Gäste: Tobias schreibt kurz vor dem Ende (die Sitzung läuft mit der Uhr ab), zugestellt wird danach.
        _s.Factory.Uhr.SetUtcNow(FreizeitendeX.AddMinutes(-1));
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        await NachrichtAsync(_s.Tobias, "Kurz vor dem Ende");
        _s.Factory.Uhr.SetUtcNow(FreizeitendeX);
        await _s.ZustellenAsync();
        Assert.DoesNotContain(_push.Gesendet, p => p.Inhalt.Text == "Kurz vor dem Ende");
    }

    [Fact]
    public async Task Nach_Link_erneuern_bekommt_ein_altes_Gast_Abo_keinen_Push_mehr_nach_neuem_Scan_wieder()
    {
        using var gast = await GastAsync(await ZugangAsync());
        await AbonnierenAsync(gast, Endpunkt("gast"));

        var neu = TokenAus((await ErneuernAsync()).GetProperty("adresse").GetString()!);
        await NachrichtAsync(_s.Tobias, "Nach dem Erneuern");
        await _s.ZustellenAsync();

        Assert.Empty(_push.Gesendet);

        // Ein neuer Scan mit dem neuen Link: das Gerät sendet sein Abo erneut, es bekommt wieder Pushes.
        Assert.Equal(HttpStatusCode.NoContent, (await EinloesenAsync(gast, neu)).StatusCode);
        await AbonnierenAsync(gast, Endpunkt("gast"));
        Assert.Equal(1L, await AnzahlAsync("kanaele.gast_push_abo"));
        await NachrichtAsync(_s.Tobias, "Nach dem neuen Scan");
        await _s.ZustellenAsync();
        Assert.Equal(["Nach dem neuen Scan"], _push.Gesendet.Select(p => p.Inhalt.Text));
    }

    [Fact]
    public async Task Hat_dasselbe_Geraet_ein_Team_Abo_und_ein_Gast_Abo_der_Freizeit_kommt_eine_Nachricht_an_alle_nur_einmal()
    {
        using var gast = await GastAsync(await ZugangAsync());
        await AbonnierenAsync(gast, Endpunkt("handy-miriam"));
        using (var abo = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/kanaele/push/abo", new { endpunkt = Endpunkt("handy-miriam"), p256dh = P256dh, auth = Auth }))
        {
            Assert.Equal(HttpStatusCode.NoContent, abo.StatusCode);
        }

        await NachrichtAsync(_s.Tobias, "Essen 13:30 statt 13:00");
        await _s.ZustellenAsync();

        var push = Assert.Single(_push.Gesendet);
        Assert.Equal((Endpunkt("handy-miriam"), $"/app/freizeiten/{_s.X}/kanaele?kanal=alle"), (push.Ziel.Endpunkt, push.Inhalt.Url));
    }

    [Fact]
    public async Task Infos_listen_nur_Dokumente_fuer_Eltern_und_Teilnehmende_der_eigenen_Freizeit_die_Datei_kommt_mit_den_Bytes()
    {
        var infobrief = await DokumentHelfer.HochgeladenAsync(_s.Miriam, DokumentHelfer.FreizeitPfad(_s.X), DokumentHelfer.Pdf, "Infobrief.pdf", "ElternUndTeilnehmende");
        var teamDokument = await DokumentHelfer.HochgeladenAsync(_s.Miriam, DokumentHelfer.FreizeitPfad(_s.X), DokumentHelfer.Pdf, "Nachtwache.pdf", "Team");
        var dokumentZ = await DokumentHelfer.HochgeladenAsync(_s.Miriam, DokumentHelfer.FreizeitPfad(_s.Z), DokumentHelfer.Pdf, "Infobrief Z.pdf", "ElternUndTeilnehmende");
        using var gast = await GastAsync(await ZugangAsync());

        var infos = await GetJsonAsync(gast, "/api/teilnehmer/infos");

        var dokument = Assert.Single(infos.GetProperty("dokumente").EnumerateArray());
        Assert.Equal(["dateiart", "id", "titel"], Namen(dokument));
        Assert.Equal((infobrief, "Infobrief", "PDF"), (dokument.GetProperty("id").GetGuid(), dokument.GetProperty("titel").GetString(), dokument.GetProperty("dateiart").GetString()));
        using (var datei = await gast.GetAsync($"/api/teilnehmer/infos/{infobrief}", Abbruch))
        {
            Assert.Equal(HttpStatusCode.OK, datei.StatusCode);
            Assert.Equal(DokumentHelfer.Pdf, await datei.Content.ReadAsByteArrayAsync(Abbruch));
        }

        foreach (var fremd in new[] { teamDokument, dokumentZ })
        {
            using var antwort = await gast.GetAsync($"/api/teilnehmer/infos/{fremd}", Abbruch);
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }
    }

    [Fact]
    public async Task Gast_Abo_nur_bekannte_Push_Dienste_400_am_Feld_endpunkt_Abmelden_loescht_nur_das_eigene()
    {
        var token = await ZugangAsync();
        using var gast = await GastAsync(token);
        using var andererGast = await GastAsync(token);

        foreach (var endpunkt in new[] { "http://fcm.googleapis.com/fcm/send/x", "https://127.0.0.1/push", "https://example.org/push", "" })
        {
            using var antwort = await PostAsync(gast, "/api/teilnehmer/push/abo", new { endpunkt, p256dh = P256dh, auth = Auth });
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty("endpunkt", out _));
        }

        using (var schluessel = await PostAsync(gast, "/api/teilnehmer/push/abo", new { endpunkt = Endpunkt("gast"), p256dh = "kurz!", auth = Auth }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, schluessel.StatusCode);
            Assert.True((await ProblemAsync(schluessel)).GetProperty("errors").TryGetProperty("p256dh", out _));
        }

        Assert.Equal(0L, await AnzahlAsync("kanaele.gast_push_abo"));
        await AbonnierenAsync(gast, Endpunkt("gast"));
        await AbonnierenAsync(gast, Endpunkt("gast"));
        await AbonnierenAsync(andererGast, Endpunkt("anderer"));
        Assert.Equal(2L, await AnzahlAsync("kanaele.gast_push_abo"));
        // Gast-Abos sind keine Team-Abos.
        Assert.Equal(0L, await AnzahlAsync("kanaele.push_abo"));

        foreach (var (client, endpunkt) in new[] { (andererGast, Endpunkt("gast")), (gast, Endpunkt("unbekannt")), (gast, Endpunkt("gast")) })
        {
            using var antwort = await PostAsync(client, "/api/teilnehmer/push/abo/abmelden", new { endpunkt });
            Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        }

        Assert.Equal(1L, await AnzahlAsync("kanaele.gast_push_abo"));
        Assert.Equal(1L, await AnzahlAsync("kanaele.gast_push_abo", "\"Endpunkt\" = @e", ("e", Endpunkt("anderer"))));
    }

    // ---------- Abschluss und Löschlauf ----------

    [Fact]
    public async Task Abschluss_als_Durchlauf_loescht_Gast_und_Team_Abos_der_Freizeit_die_von_Z_bleiben()
    {
        using var gastX = await GastAsync(await ZugangAsync(_s.X));
        using var gastZ = await GastAsync(await ZugangAsync(_s.Z));
        await AbonnierenAsync(gastX, Endpunkt("gast-x"));
        await AbonnierenAsync(gastZ, Endpunkt("gast-z"));
        foreach (var (freizeit, name) in new[] { (_s.X, "miriam-x"), (_s.Z, "miriam-z") })
        {
            using var abo = await PostAsync(_s.Miriam, $"/api/freizeiten/{freizeit}/kanaele/push/abo", new { endpunkt = Endpunkt(name), p256dh = P256dh, auth = Auth });
            Assert.Equal(HttpStatusCode.NoContent, abo.StatusCode);
        }

        // Nachbereitung von X: eine Woche nach dem letzten Tag; die Sitzung läuft mit der Uhr ab.
        _s.Factory.Uhr.SetUtcNow(new DateTimeOffset(2027, 8, 20, 8, 0, 0, TimeSpan.Zero));
        await AnmeldenAsync(_s.Factory, _s.Miriam, FreizeitHelfer.Miriam);
        using (var abschliessen = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/abschliessen"))
        {
            Assert.Equal(HttpStatusCode.NoContent, abschliessen.StatusCode);
        }

        await _s.ZustellenAsync();

        Assert.Equal(0L, await AnzahlAsync("kanaele.gast_push_abo", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(0L, await AnzahlAsync("kanaele.push_abo", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await AnzahlAsync("kanaele.gast_push_abo", "\"FreizeitId\" = @f", ("f", _s.Z)));
        Assert.Equal(1L, await AnzahlAsync("kanaele.push_abo", "\"FreizeitId\" = @f", ("f", _s.Z)));
        Assert.Equal(1L, await AnzahlAsync("kanaele.inbox", "\"Empfaenger\" LIKE '%GastAbosBeenden%'"));
    }

    [Fact]
    public async Task Der_Loeschlauf_loescht_die_Gast_Abos_der_abgelaufenen_Freizeit_die_von_Z_bleiben()
    {
        using var gastX = await GastAsync(await ZugangAsync(_s.X));
        using var gastZ = await GastAsync(await ZugangAsync(_s.Z));
        await AbonnierenAsync(gastX, Endpunkt("gast-x"));
        await AbonnierenAsync(gastZ, Endpunkt("gast-z"));

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "KanaeleLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("kanaele.gast_push_abo", "\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await AnzahlAsync("kanaele.gast_push_abo", "\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    // ---------- Konfiguration ----------

    [Fact]
    public async Task NurQrGast_mit_einem_weiteren_Rechtemerkmal_ist_ein_Konfigurationsfehler()
    {
        using var antwort = await _s.Miriam.GetAsync($"/api/test/qr-gast-mit-rolle/{_s.X}", Abbruch);

        Assert.Equal(HttpStatusCode.InternalServerError, antwort.StatusCode);
        Assert.Equal(nameof(InvalidOperationException), (await ProblemAsync(antwort)).GetProperty("ausnahmeTyp").GetString());
    }
}
