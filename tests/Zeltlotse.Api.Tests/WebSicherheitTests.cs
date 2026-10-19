using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Sicherheit;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>AD-25: Header, Cookies, Antiforgery-Zwang, Dienst-Kennung und Zugriffsbegrenzung.</summary>
public sealed class WebSicherheitTests(PostgresContainer postgres)
{
    private const string Csp =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private async Task<ZeltlotseApiFactory> FactoryAsync(int? grenze = null, params (string Schluessel, string Wert)[] einstellungen)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueMigrierteDatenbankAsync());
        factory.ZusatzDienste.Add(TestEndpunkte.Registrieren);
        if (grenze is not null)
        {
            factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = grenze.Value.ToString();
        }

        foreach (var (schluessel, wert) in einstellungen)
        {
            factory.Einstellungen[schluessel] = wert;
        }

        return factory;
    }

    [Theory]
    [InlineData("/health", HttpStatusCode.OK, false)]
    [InlineData("/alive", HttpStatusCode.OK, false)]
    [InlineData("/gibt-es-nicht", HttpStatusCode.NotFound, false)]
    [InlineData("/api/betrieb/antiforgery", HttpStatusCode.OK, true)]
    [InlineData("/api/test/ok", HttpStatusCode.OK, true)]
    [InlineData("/api/gibt-es-nicht", HttpStatusCode.NotFound, true)]
    [InlineData("/api/test/ausnahme", HttpStatusCode.InternalServerError, true)]
    public async Task Jede_Antwort_traegt_die_Sicherheits_Header(string pfad, HttpStatusCode status, bool unterApi)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync(pfad, Abbruch);

        Assert.Equal(status, antwort.StatusCode);
        PruefeHeader(antwort, unterApi);
    }

    [Fact]
    public async Task Auch_400_und_429_tragen_die_Sicherheits_Header()
    {
        await using var factory = await FactoryAsync(grenze: 1);
        using var client = factory.CreateClient();

        using var ohneToken = await client.SendAsync(FehlerMeldung(GueltigeMeldung()), Abbruch);
        Assert.Equal(HttpStatusCode.BadRequest, ohneToken.StatusCode);
        PruefeHeader(ohneToken, unterApi: true);

        using var erste = await client.GetAsync("/api/betrieb/antiforgery", Abbruch);
        using var zweite = await client.GetAsync("/api/betrieb/antiforgery", Abbruch);
        Assert.Equal((HttpStatusCode)429, zweite.StatusCode);
        PruefeHeader(zweite, unterApi: true);
    }

    [Fact]
    public async Task Antiforgery_Cookie_hat_die_vorgeschriebenen_Attribute_und_das_Token_macht_den_POST_gueltig()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/api/betrieb/antiforgery", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var cookies = antwort.Headers.GetValues("Set-Cookie").ToList();
        Assert.NotEmpty(cookies);
        Assert.Contains(cookies, c => c.StartsWith("__Host-Zeltlotse-Antiforgery=", StringComparison.Ordinal));
        foreach (var cookie in cookies)
        {
            var teile = cookie.Split(';').Select(t => t.Trim().ToLowerInvariant()).ToList();
            Assert.StartsWith("__host-", teile[0], StringComparison.Ordinal);
            Assert.Contains("secure", teile);
            Assert.Contains("httponly", teile);
            Assert.Contains("samesite=lax", teile);
            Assert.Contains("path=/", teile);
            Assert.DoesNotContain(teile, t => t.StartsWith("domain=", StringComparison.Ordinal));
        }

        var token = (await ProblemlosesJsonAsync(antwort)).GetProperty("token").GetString();
        using var post = await client.SendAsync(FehlerMeldung(GueltigeMeldung(), antiforgery: token), Abbruch);
        Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);
    }

    [Fact]
    public async Task Cookie_Richtlinie_erzwingt_Secure_HttpOnly_Lax_und_Pfad()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/api/test/keks", Abbruch);

        var teile = Assert.Single(antwort.Headers.GetValues("Set-Cookie")).Split(';').Select(t => t.Trim().ToLowerInvariant()).ToList();
        Assert.Equal("__host-test=1", teile[0]);
        Assert.Contains("secure", teile);
        Assert.Contains("httponly", teile);
        Assert.Contains("samesite=lax", teile);
        Assert.Contains("path=/", teile);
    }

    [Fact]
    public async Task Cookie_ohne_Host_Praefix_wird_nicht_gesetzt()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/api/test/keks-ohne-praefix", Abbruch);

        Assert.Equal(HttpStatusCode.InternalServerError, antwort.StatusCode);
        Assert.False(antwort.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("falsch")]
    public async Task Aendern_ohne_oder_mit_falschem_Antiforgery_Header_ergibt_400(string? header)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        _ = await AntiforgeryTokenAsync(client);

        using var antwort = await client.SendAsync(FehlerMeldung(GueltigeMeldung(), antiforgery: header), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(problem));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Formular_Upload_mit_gueltigem_Token_bindet_die_Datei()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/test/upload") { Content = Upload() };
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);

        using var antwort = await client.SendAsync(anfrage, Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("Pdf", (await ProblemlosesJsonAsync(antwort)).GetProperty("art").GetString());
    }

    [Fact]
    public async Task Formular_Upload_ohne_Token_ergibt_400()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.PostAsync("/api/test/upload", Upload(), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task Gueltiges_Token_nur_als_Formularfeld_ohne_Header_ergibt_400()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);
        var inhalt = Upload();
        inhalt.Add(new StringContent(token), "__RequestVerificationToken");

        using var antwort = await client.PostAsync("/api/test/upload", inhalt, Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task Gross_geschriebenes_API_unterliegt_Zwang_und_no_store()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var post = await client.PostAsync("/API/test/aendern", null, Abbruch);
        using var get = await client.GetAsync("/API/test/ok", Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(await ProblemAsync(post)));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        PruefeHeader(get, unterApi: true);
    }

    private static MultipartFormDataContent Upload()
    {
        var datei = new ByteArrayContent([.. "%PDF-1.7\n"u8, 1, 2, 3]);
        datei.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { datei, "datei", "programm.pdf" } };
    }

    [Fact]
    public async Task Token_ohne_passendes_Cookie_ergibt_400()
    {
        await using var factory = await FactoryAsync();
        using var mitCookie = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(mitCookie);
        using var ohneCookie = factory.CreateClient();

        using var antwort = await ohneCookie.SendAsync(FehlerMeldung(GueltigeMeldung(), antiforgery: token), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task Zwang_gilt_fuer_alle_aendernden_Methoden_unter_api(string methode)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.SendAsync(new HttpRequestMessage(new HttpMethod(methode), "/api/test/aendern"), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task Aendern_mit_gueltiger_Dienst_Kennung_ohne_Antiforgery_ergibt_204()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var kennung = factory.Services.GetRequiredService<PublicSeitenDienstKennung>();

        using var antwort = await client.SendAsync(FehlerMeldung(GueltigeMeldung(), dienst: kennung.Geheimnis), Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("falsch")]
    public async Task Falsche_Dienst_Kennung_ergibt_400(string geheimnis)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.SendAsync(FehlerMeldung(GueltigeMeldung(), dienst: geheimnis), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task Falsche_Dienst_Kennung_faellt_nicht_auf_ein_gueltiges_Antiforgery_Token_zurueck()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);

        using var antwort = await client.SendAsync(FehlerMeldung(GueltigeMeldung(), antiforgery: token, dienst: "falsch"), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    [Fact]
    public void Dienst_Kennung_ist_zufaellig_je_Prozessstart_und_256_Bit_lang()
    {
        var eins = new PublicSeitenDienstKennung();
        var zwei = new PublicSeitenDienstKennung();

        Assert.NotEqual(eins.Geheimnis, zwei.Geheimnis);
        Assert.Equal(32, Convert.FromBase64String(eins.Geheimnis).Length);
        Assert.True(eins.Pruefen(eins.Geheimnis));
        Assert.False(eins.Pruefen(zwei.Geheimnis));
        Assert.False(eins.Pruefen(null));
        Assert.False(eins.Pruefen(eins.Geheimnis[..^1]));
    }

    [Fact]
    public async Task Vierte_Anfrage_derselben_IP_ergibt_429_mit_Retry_After_andere_durchgereichte_IP_nicht()
    {
        await using var factory = await FactoryAsync(grenze: 3);
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var erlaubt = await AnfrageVonAsync(client, "198.51.100.7");
            Assert.Equal(HttpStatusCode.OK, erlaubt.StatusCode);
        }

        using var vierte = await AnfrageVonAsync(client, "198.51.100.7");
        Assert.Equal((HttpStatusCode)429, vierte.StatusCode);
        Assert.True(vierte.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After fehlt.");
        Assert.Equal(Problemcodes.Zugriffsbegrenzt, Code(await ProblemAsync(vierte)));

        // Public reicht die Besucher-IP mit der Dienst-Kennung durch: eigene Partition.
        var kennung = factory.Services.GetRequiredService<PublicSeitenDienstKennung>().Geheimnis;
        using var durchgereicht = await AnfrageVonAsync(client, "198.51.100.7", dienst: kennung, besucher: "203.0.113.9");
        Assert.Equal(HttpStatusCode.OK, durchgereicht.StatusCode);

        // Ohne gültige Dienst-Kennung zählt die durchgereichte IP nicht.
        using var gefaelscht = await AnfrageVonAsync(client, "198.51.100.7", dienst: "falsch", besucher: "203.0.113.10");
        Assert.Equal((HttpStatusCode)429, gefaelscht.StatusCode);
    }

    [Fact]
    public async Task Besucher_IP_mit_Dienst_Kennung_wird_selbst_begrenzt()
    {
        await using var factory = await FactoryAsync(grenze: 2);
        using var client = factory.CreateClient();
        var kennung = factory.Services.GetRequiredService<PublicSeitenDienstKennung>().Geheimnis;

        using var eins = await AnfrageVonAsync(client, "127.0.0.1", dienst: kennung, besucher: "203.0.113.20");
        using var zwei = await AnfrageVonAsync(client, "127.0.0.1", dienst: kennung, besucher: "203.0.113.20");
        using var drei = await AnfrageVonAsync(client, "127.0.0.1", dienst: kennung, besucher: "203.0.113.20");
        using var andere = await AnfrageVonAsync(client, "127.0.0.1", dienst: kennung, besucher: "203.0.113.21");

        Assert.Equal(HttpStatusCode.OK, zwei.StatusCode);
        Assert.Equal((HttpStatusCode)429, drei.StatusCode);
        Assert.Equal(HttpStatusCode.OK, andere.StatusCode);
    }

    [Fact]
    public async Task Vorgabe_der_Grenze_ist_20_Anfragen_je_Minute()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        for (var i = 0; i < 20; i++)
        {
            using var erlaubt = await AnfrageVonAsync(client, "198.51.100.30");
            Assert.Equal(HttpStatusCode.OK, erlaubt.StatusCode);
        }

        using var einundzwanzig = await AnfrageVonAsync(client, "198.51.100.30");
        Assert.Equal((HttpStatusCode)429, einundzwanzig.StatusCode);
    }

    [Fact]
    public async Task Weitergeleitete_Header_gelten_nur_von_konfigurierten_Proxy_Adressen()
    {
        await using var ohneProxy = await FactoryAsync(grenze: 1);
        using var client = ohneProxy.CreateClient();
        using var a = await AnfrageVonAsync(client, "10.0.0.1", weitergeleitet: "203.0.113.30");
        using var b = await AnfrageVonAsync(client, "10.0.0.1", weitergeleitet: "203.0.113.31");
        // Ohne konfigurierten Proxy zählt die Verbindungs-IP: beide Anfragen teilen eine Partition.
        Assert.Equal((HttpStatusCode)429, b.StatusCode);

        await using var mitProxy = await FactoryAsync(grenze: 1, ("Zeltlotse:ReverseProxy:Adressen:0", "10.0.0.1"));
        using var proxyClient = mitProxy.CreateClient();
        using var c = await AnfrageVonAsync(proxyClient, "10.0.0.1", weitergeleitet: "203.0.113.30");
        using var d = await AnfrageVonAsync(proxyClient, "10.0.0.1", weitergeleitet: "203.0.113.31");
        Assert.Equal(HttpStatusCode.OK, d.StatusCode);
    }

    [Fact]
    public async Task Jeder_aendernde_Endpunkt_liegt_unter_api_oder_verlangt_Antiforgery_und_keiner_ist_ausgenommen()
    {
        await using var factory = await FactoryAsync();
        _ = factory.CreateClient();
        var endpunkte = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        string[] aendernd = ["POST", "PUT", "PATCH", "DELETE"];

        Assert.Contains(endpunkte, e => e.RoutePattern.RawText == "/api/betrieb/fehler");
        foreach (var endpunkt in endpunkte)
        {
            var antiforgery = endpunkt.Metadata.GetOrderedMetadata<IAntiforgeryMetadata>();
            Assert.DoesNotContain(antiforgery, m => !m.RequiresValidation);

            var methoden = endpunkt.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            if (methoden.Count == 0 || methoden.Any(m => aendernd.Contains(m, StringComparer.OrdinalIgnoreCase)))
            {
                var pfad = "/" + (endpunkt.RoutePattern.RawText ?? "").TrimStart('/');
                var unterApi = pfad == "/api" || pfad.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
                Assert.True(
                    unterApi || antiforgery.Any(m => m.RequiresValidation),
                    $"{endpunkt.DisplayName} ändert, liegt nicht unter /api und verlangt kein Antiforgery-Token.");
            }
        }
    }

    [Fact]
    public async Task Zugriffsbegrenzung_haengt_an_beiden_Betriebs_Endpunkten()
    {
        await using var factory = await FactoryAsync();
        _ = factory.CreateClient();
        var endpunkte = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/betrieb/", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, endpunkte.Count);
        Assert.All(endpunkte, e => Assert.Contains(
            e.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
            r => r.PolicyName == SharedKernel.Web.Zugriffsbegrenzung.Richtlinie));
    }

    private static void PruefeHeader(HttpResponseMessage antwort, bool unterApi)
    {
        Assert.Equal(Csp, Kopf(antwort, "Content-Security-Policy"));
        Assert.Equal("no-referrer", Kopf(antwort, "Referrer-Policy"));
        Assert.Equal("nosniff", Kopf(antwort, "X-Content-Type-Options"));
        if (unterApi)
        {
            Assert.Equal("no-store", Kopf(antwort, "Cache-Control"));
        }
    }

    private static string? Kopf(HttpResponseMessage antwort, string name) =>
        antwort.Headers.TryGetValues(name, out var werte) || antwort.Content.Headers.TryGetValues(name, out werte)
            ? string.Join(", ", werte)
            : null;

    private static Task<HttpResponseMessage> AnfrageVonAsync(
        HttpClient client, string absender, string? dienst = null, string? besucher = null, string? weitergeleitet = null)
    {
        var anfrage = new HttpRequestMessage(HttpMethod.Get, "/api/betrieb/antiforgery");
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, absender);
        if (dienst is not null)
        {
            anfrage.Headers.Add(Kopfzeilen.Dienst, dienst);
        }

        if (besucher is not null)
        {
            anfrage.Headers.Add(Kopfzeilen.BesucherIp, besucher);
        }

        if (weitergeleitet is not null)
        {
            anfrage.Headers.Add("X-Forwarded-For", weitergeleitet);
        }

        return client.SendAsync(anfrage, Abbruch);
    }

    private static async Task<System.Text.Json.JsonElement> ProblemlosesJsonAsync(HttpResponseMessage antwort) =>
        await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(antwort.Content, Abbruch);
}
