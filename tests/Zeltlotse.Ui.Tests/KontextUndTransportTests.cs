using System.Net;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Kontext;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>Freizeit-Kontext (Matrix-Zeile „Kontext“) und die drei Transport-Handler der Web-App.</summary>
public sealed class KontextUndTransportTests
{
    private static readonly Guid FreizeitId = Guid.Parse("0190a0f0-0000-7000-8000-0000000000aa");
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    // ---------- Freizeit-Kontext ----------

    [Fact]
    public async Task Kontext_bei_200_wird_geliefert()
    {
        var server = new NachgebildeterHandler().Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => NachgebildeterHandler.Json(HttpStatusCode.OK,
            $$"""{"freizeitId":"{{FreizeitId}}","name":"Zeltlager 2027","phase":"Laeuft","rollen":["Team"],"schalter":["Verpflegung","TeilnehmerZugang"],"erlaubt":["schaden-melden"]}"""));
        var dienst = new FreizeitKontextDienst(new FreizeitenApiClient(server.Client()));

        var kontext = await dienst.LadenAsync(FreizeitId, Abbruch);

        Assert.NotNull(kontext);
        Assert.Equal(FreizeitId, kontext.FreizeitId);
        Assert.Equal("Zeltlager 2027", kontext.Name);
        Assert.Equal(Phase.Laeuft, kontext.Phase);
        Assert.Equal([FreizeitRolle.Team], kontext.Rollen);
        Assert.Equal([FunktionsSchalter.Verpflegung, FunktionsSchalter.TeilnehmerZugang], kontext.Schalter);
        Assert.Equal(["schaden-melden"], kontext.Erlaubt);
        var anfrage = Assert.Single(server.Anfragen);
        Assert.Equal(HttpMethod.Get, anfrage.Methode);
        Assert.Equal($"/api/freizeiten/{FreizeitId}/kontext", anfrage.Pfad);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Kontext_bei_403_oder_404_ist_nicht_verfuegbar(HttpStatusCode status)
    {
        var server = new NachgebildeterHandler().Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => NachgebildeterHandler.Problem(status, "x"));
        var dienst = new FreizeitKontextDienst(new FreizeitenApiClient(server.Client()));

        Assert.Null(await dienst.LadenAsync(FreizeitId, Abbruch));
    }

    [Fact]
    public async Task Kontext_wird_nicht_zwischengespeichert()
    {
        var server = new NachgebildeterHandler().Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => NachgebildeterHandler.Problem(HttpStatusCode.NotFound, "x"));
        var dienst = new FreizeitKontextDienst(new FreizeitenApiClient(server.Client()));

        await dienst.LadenAsync(FreizeitId, Abbruch);
        await dienst.LadenAsync(FreizeitId, Abbruch);

        Assert.Equal(2, server.Anfragen.Count);
    }

    [Fact]
    public void Darf_prueft_nur_Erlaubt_und_nie_die_Rollen()
    {
        var leitungOhneErlaubnis = new FreizeitKontext(FreizeitId, "Zeltlager", Phase.InVorbereitung, [FreizeitRolle.Leitung], [], []);
        var teamMitErlaubnis = new FreizeitKontext(FreizeitId, "Zeltlager", Phase.InVorbereitung, [FreizeitRolle.Team], [], ["anmeldung-bestaetigen"]);

        Assert.False(FreizeitKontextDienst.Darf(leitungOhneErlaubnis, "anmeldung-bestaetigen"));
        Assert.True(FreizeitKontextDienst.Darf(teamMitErlaubnis, "anmeldung-bestaetigen"));
        Assert.False(FreizeitKontextDienst.Darf(teamMitErlaubnis, "Anmeldung-Bestaetigen"));
    }

    // ---------- Antiforgery-Handler ----------

    private static (HttpClient Client, NachgebildeterHandler Server) MitAntiforgery(NachgebildeterHandler server)
    {
        var tokens = 0;
        server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++tokens}}"}"""));
        var client = new HttpClient(new AntiforgeryHandler { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/app/") };
        return (client, server);
    }

    private static int TokenAbrufe(NachgebildeterHandler server) => server.Anfragen.Count(a => a.Pfad == "/api/betrieb/antiforgery");

    [Fact]
    public async Task Antiforgery_Token_wird_beim_ersten_aendernden_Aufruf_einmal_geholt_und_gesetzt()
    {
        var (client, server) = MitAntiforgery(new NachgebildeterHandler()
            .Bei(r => r.Method != HttpMethod.Get, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))));

        using var lesen = await client.GetAsync("/api/test/ok", Abbruch);
        Assert.Equal(0, TokenAbrufe(server));
        Assert.False(server.Anfragen[0].Kopfzeilen.ContainsKey(Kopfzeilen.Antiforgery));

        using var erste = await client.PatchAsync("/api/eins", new StringContent("{}"), Abbruch);
        using var zweite = await client.PostAsync("/api/zwei", new StringContent("{}"), Abbruch);
        using var dritte = await client.DeleteAsync("/api/drei", Abbruch);

        Assert.Equal(1, TokenAbrufe(server));
        var aendernd = server.Anfragen.Where(a => a.Methode != HttpMethod.Get).ToList();
        Assert.Equal(3, aendernd.Count);
        Assert.All(aendernd, a => Assert.Equal("token-1", a.Kopfzeilen[Kopfzeilen.Antiforgery]));
    }

    [Fact]
    public async Task Antiforgery_Token_wird_bei_gleichzeitigen_ersten_Aufrufen_nur_einmal_geholt()
    {
        var (client, server) = MitAntiforgery(new NachgebildeterHandler()
            .Bei(r => r.Method != HttpMethod.Get, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))));

        await Task.WhenAll(Enumerable.Range(0, 5).Select(i => client.PostAsync($"/api/{i}", new StringContent("{}"), Abbruch)));

        Assert.Equal(1, TokenAbrufe(server));
    }

    [Fact]
    public async Task Abgelaufenes_Token_wird_einmal_neu_geholt_und_die_Anfrage_mit_demselben_Koerper_wiederholt()
    {
        var versuche = 0;
        var (client, server) = MitAntiforgery(new NachgebildeterHandler()
            .Bei(r => r.Method == HttpMethod.Patch, _ => Task.FromResult(++versuche == 1
                ? NachgebildeterHandler.Problem(HttpStatusCode.BadRequest, Problemcodes.AntiforgeryUngueltig)
                : new HttpResponseMessage(HttpStatusCode.OK))));

        using var antwort = await client.PatchAsync("/api/punkt", new StringContent("""{"version":1}"""), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(2, TokenAbrufe(server));
        var patches = server.Anfragen.Where(a => a.Methode == HttpMethod.Patch).ToList();
        Assert.Equal(["token-1", "token-2"], patches.Select(p => p.Kopfzeilen[Kopfzeilen.Antiforgery]));
        Assert.All(patches, p => Assert.Equal("""{"version":1}""", p.Koerper));
    }

    [Fact]
    public async Task Zweites_400_antiforgery_ungueltig_geht_an_den_Aufrufer()
    {
        var (client, server) = MitAntiforgery(new NachgebildeterHandler()
            .Bei(r => r.Method == HttpMethod.Post, _ => Task.FromResult(NachgebildeterHandler.Problem(HttpStatusCode.BadRequest, Problemcodes.AntiforgeryUngueltig))));

        using var antwort = await client.PostAsync("/api/x", new StringContent("{}"), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Contains(Problemcodes.AntiforgeryUngueltig, await antwort.Content.ReadAsStringAsync(Abbruch), StringComparison.Ordinal);
        Assert.Equal(2, TokenAbrufe(server));
        Assert.Equal(2, server.Anfragen.Count(a => a.Methode == HttpMethod.Post));
    }

    [Fact]
    public async Task Anderes_400_wird_nicht_wiederholt()
    {
        var (client, server) = MitAntiforgery(new NachgebildeterHandler()
            .Bei(r => r.Method == HttpMethod.Post, _ => Task.FromResult(NachgebildeterHandler.Problem(HttpStatusCode.BadRequest, Problemcodes.Validierung))));

        using var antwort = await client.PostAsync("/api/x", new StringContent("{}"), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(1, TokenAbrufe(server));
        Assert.Single(server.Anfragen, a => a.Methode == HttpMethod.Post);
    }

    // ---------- Versions-Handler ----------

    private static async Task<NeueVersion> MitVersionAsync(string? version)
    {
        var neueVersion = new NeueVersion();
        var server = new NachgebildeterHandler().Bei(r => true, _ =>
        {
            var antwort = new HttpResponseMessage(HttpStatusCode.OK);
            if (version is not null)
            {
                antwort.Headers.Add(Kopfzeilen.Version, version);
            }

            return Task.FromResult(antwort);
        });
        using var client = new HttpClient(new VersionsHandler(neueVersion, "1.0.0+abc") { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/") };
        using var _ = await client.GetAsync("/api/x", Abbruch);
        return neueVersion;
    }

    [Fact]
    public async Task Andere_Server_Version_meldet_eine_neue_Version()
    {
        var neueVersion = await MitVersionAsync("1.0.0+def");

        Assert.True(neueVersion.Da);
    }

    [Theory]
    [InlineData("1.0.0+abc")]
    [InlineData(null)]
    public async Task Gleiche_oder_fehlende_Version_meldet_nichts(string? version)
    {
        Assert.False((await MitVersionAsync(version)).Da);
    }

    [Fact]
    public void Neue_Version_meldet_das_Ereignis_nur_einmal()
    {
        var neueVersion = new NeueVersion();
        var gemeldet = 0;
        neueVersion.Gemeldet += () => gemeldet++;

        neueVersion.Melden();
        neueVersion.Melden();

        Assert.Equal(1, gemeldet);
    }

    [Fact]
    public async Task Versions_Handler_vergleicht_ohne_Angabe_mit_ZeltlotseVersion_Aktuell()
    {
        var neueVersion = new NeueVersion();
        var server = new NachgebildeterHandler().Bei(r => true, _ =>
        {
            var antwort = new HttpResponseMessage(HttpStatusCode.OK);
            antwort.Headers.Add(Kopfzeilen.Version, ZeltlotseVersion.Aktuell);
            return Task.FromResult(antwort);
        });
        using var client = new HttpClient(new VersionsHandler(neueVersion) { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/") };

        using var _ = await client.GetAsync("/api/x", Abbruch);

        Assert.False(neueVersion.Da);
    }

    // ---------- Verbindungs-Handler ----------

    private sealed class AufgezeichneteVerbindung : IVerbindung
    {
        public List<string> Meldungen { get; } = [];

        public ValueTask NetzfehlerMeldenAsync()
        {
            Meldungen.Add("Netzfehler");
            return ValueTask.CompletedTask;
        }

        public ValueTask ErfolgMeldenAsync()
        {
            Meldungen.Add("Erfolg");
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Verbindungs_Handler_meldet_Netzfehler_und_reicht_die_Ausnahme_weiter()
    {
        var verbindung = new AufgezeichneteVerbindung();
        var server = new NachgebildeterHandler().Bei(r => true, _ => throw new HttpRequestException("Kein Netz"));
        using var client = new HttpClient(new VerbindungsHandler(verbindung) { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/") };

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/api/x", Abbruch));

        Assert.Equal(["Netzfehler"], verbindung.Meldungen);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Verbindungs_Handler_meldet_jede_Antwort_des_Servers_als_Erfolg(HttpStatusCode status)
    {
        var verbindung = new AufgezeichneteVerbindung();
        var server = new NachgebildeterHandler().Bei(r => true, _ => Task.FromResult(new HttpResponseMessage(status)));
        using var client = new HttpClient(new VerbindungsHandler(verbindung) { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/") };

        using var antwort = await client.GetAsync("/api/x", Abbruch);

        Assert.Equal(status, antwort.StatusCode);
        Assert.Equal(["Erfolg"], verbindung.Meldungen);
    }

    [Fact]
    public async Task Verbindungs_Handler_meldet_eine_Zeitueberschreitung_als_Netzfehler()
    {
        var verbindung = new AufgezeichneteVerbindung();
        using var client = new HttpClient(new VerbindungsHandler(verbindung, TimeSpan.FromMilliseconds(50)) { InnerHandler = new Verzoegert() })
        {
            BaseAddress = new Uri("http://localhost/"),
        };

        var fehler = await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync("/api/x", Abbruch));

        Assert.IsType<TimeoutException>(fehler.InnerException);
        Assert.Equal(["Netzfehler"], verbindung.Meldungen);
    }

    [Fact]
    public async Task Verbindungs_Handler_meldet_einen_Abbruch_durch_den_Aufrufer_nicht()
    {
        var verbindung = new AufgezeichneteVerbindung();
        using var client = new HttpClient(new VerbindungsHandler(verbindung) { InnerHandler = new Verzoegert() }) { BaseAddress = new Uri("http://localhost/") };
        using var abbruch = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("/api/x", abbruch.Token));

        Assert.Empty(verbindung.Meldungen);
    }

    [Fact]
    public async Task Verbindungs_Handler_wertet_HttpRequestException_mit_Statuscode_als_Antwort_des_Servers()
    {
        var verbindung = new AufgezeichneteVerbindung();
        var server = new NachgebildeterHandler().Bei(r => true,
            _ => throw new HttpRequestException("Token nicht geholt", null, HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(new VerbindungsHandler(verbindung) { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/") };

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync("/api/x", null, Abbruch));

        Assert.Equal(["Erfolg"], verbindung.Meldungen);
    }

    /// <summary>Antwortet erst, wenn die Anfrage abgebrochen wird.</summary>
    private sealed class Verzoegert : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // ---------- Betriebs-Client ----------

    [Fact]
    public async Task Betriebs_Client_holt_das_Token_und_meldet_Browser_Fehler()
    {
        var server = new NachgebildeterHandler()
            .Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"token":"abc"}"""))
            .Bei(HttpMethod.Post, "/api/betrieb/fehler", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var betrieb = new BetriebApiClient(server.Client());

        Assert.Equal("abc", await betrieb.AntiforgeryTokenHolenAsync(Abbruch));
        await betrieb.FehlerMeldenAsync(new global::Zeltlotse.Api.Contracts.Betrieb.BrowserFehler("TypeError", "/app/start", "0af7651916cd43dd8448eb211c80319c"), Abbruch);

        Assert.Equal("""{"fehlerart":"TypeError","seite":"/app/start","traceId":"0af7651916cd43dd8448eb211c80319c"}""", server.Anfragen[1].Koerper);
    }
}
