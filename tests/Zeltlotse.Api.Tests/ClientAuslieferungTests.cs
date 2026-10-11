using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using Zeltlotse.Ui.Api;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 0-3, Auslieferung der Clients unter derselben Adresse (AD-5): öffentliche Seiten unter <c>/</c>, Web-App unter
/// <c>/app</c>, HTML-404 außerhalb von <c>/api</c>, Versions-Kopfzeile, Public als API-Client und Formular-Token
/// außerhalb von <c>/api</c>.
/// </summary>
public sealed partial class ClientAuslieferungTests(PostgresContainer postgres)
{
    private const string Csp =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private async Task<ZeltlotseApiFactory> FactoryAsync(int? grenze = null)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.ZusatzDienste.Add(TestEndpunkte.Registrieren);
        if (grenze is not null)
        {
            factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = grenze.Value.ToString();
        }

        return factory;
    }

    [Fact]
    public async Task Startseite_ist_statisches_HTML_mit_ELKW_Attributen_Wortmarke_und_Sprunglink_ohne_Skript()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("text/html", antwort.Content.Headers.ContentType?.MediaType);
        var html = await antwort.Content.ReadAsStringAsync(Abbruch);
        Assert.Contains("<html lang=\"de\" class=\"elkw\" data-farbwelt=\"09\" data-stil=\"nahbar\" data-dichte=\"arbeiten\">", html, StringComparison.Ordinal);
        Assert.Contains("Zeltlotse", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#inhalt\">Zum Inhalt springen</a>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"inhalt\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        PruefeSicherheitsHeader(antwort);
    }

    [Fact]
    public async Task Stylesheets_der_Startseite_liegen_unter_content_der_Komponenten_und_sind_text_css()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/", Abbruch);

        var links = StylesheetMuster().Matches(html).Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(6, links.Count);
        foreach (var link in links)
        {
            Assert.StartsWith("/_content/Zeltlotse.Ui.Komponenten/", link, StringComparison.Ordinal);
            using var css = await client.GetAsync(link, Abbruch);
            Assert.Equal(HttpStatusCode.OK, css.StatusCode);
            Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        }

        using var schrift = await client.GetAsync("/_content/Zeltlotse.Ui.Komponenten/fonts/Sarabun-Regular.ttf", Abbruch);
        Assert.Equal(HttpStatusCode.OK, schrift.StatusCode);
        using var pfeil = await client.GetAsync("/_content/Zeltlotse.Ui.Komponenten/css/pfeil.svg", Abbruch);
        Assert.Equal(HttpStatusCode.OK, pfeil.StatusCode);
    }

    [Theory]
    [InlineData("/gibt-es-nicht")]
    [InlineData("/tief/gibt/es/nicht")]
    [InlineData("/app/fehlt.js")]
    public async Task Unbekannte_Seite_ausserhalb_von_api_ergibt_404_als_HTML_Seite(string pfad)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync(pfad, Abbruch);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal("text/html", antwort.Content.Headers.ContentType?.MediaType);
        var html = await antwort.Content.ReadAsStringAsync(Abbruch);
        Assert.Contains("Diese Seite ist nicht verfügbar.", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"de\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        PruefeSicherheitsHeader(antwort);
    }

    [Fact]
    public async Task Anderer_Status_ausserhalb_von_api_bleibt_beim_Status_und_wird_ProblemDetails()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/test/status/405", Abbruch);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, antwort.StatusCode);
        Assert.Equal(405, (await ProblemAsync(antwort)).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Unbekannte_Adresse_unter_api_bleibt_ProblemDetails()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/api/gibt-es-nicht", Abbruch);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).TryGetProperty("traceId", out _));
    }

    [Theory]
    [InlineData("/app/")]
    [InlineData("/app/tief/unten")]
    [InlineData("/app/start?x=1")]
    public async Task Web_App_liefert_index_html_mit_Basispfad_Manifest_und_nur_externen_Skripten(string pfad)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync(pfad, Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("text/html", antwort.Content.Headers.ContentType?.MediaType);
        var html = await antwort.Content.ReadAsStringAsync(Abbruch);
        Assert.Contains("<base href=\"/app/\" />", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"manifest\"", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"de\" class=\"elkw\" data-farbwelt=\"09\" data-stil=\"nahbar\" data-dichte=\"arbeiten\">", html, StringComparison.Ordinal);
        var skripte = SkriptMuster().Matches(html).Select(m => m.Value).ToList();
        Assert.NotEmpty(skripte);
        Assert.All(skripte, s => Assert.Matches("^<script src=\"[^\"]+\"></script>$", s));
        Assert.DoesNotContain("importmap", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        PruefeSicherheitsHeader(antwort);
    }

    [Fact]
    public async Task App_ohne_Schraegstrich_leitet_auf_app_mit_Schraegstrich()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var antwort = await client.GetAsync("/app", Abbruch);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal("/app/", antwort.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Alle_Dateien_aus_index_html_werden_ausgeliefert()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/app/", Abbruch);

        var verweise = VerweisMuster().Matches(html).Select(m => m.Groups[1].Value).Where(v => v != "/app/").ToList();

        Assert.Contains("_framework/blazor.webassembly.js", verweise);
        Assert.Contains("js/sw-registrierung.js", verweise);
        foreach (var verweis in verweise)
        {
            using var datei = await client.GetAsync("/app/" + verweis, Abbruch);
            Assert.True(datei.StatusCode == HttpStatusCode.OK, $"{verweis}: {datei.StatusCode}");
        }
    }

    [Fact]
    public async Task PWA_Dateien_werden_ausgeliefert_und_das_Manifest_hat_start_url_und_scope_app()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var manifest = await client.GetAsync("/app/manifest.webmanifest", Abbruch);
        using var serviceWorker = await client.GetAsync("/app/service-worker.js", Abbruch);
        using var registrierung = await client.GetAsync("/app/js/sw-registrierung.js", Abbruch);
        using var interop = await client.GetAsync("/app/js/zeltlotse.js", Abbruch);
        using var blazor = await client.GetAsync("/app/_framework/blazor.webassembly.js", Abbruch);
        using var dotnet = await client.GetAsync("/app/_framework/dotnet.js", Abbruch);

        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, serviceWorker.StatusCode);
        Assert.Equal(HttpStatusCode.OK, registrierung.StatusCode);
        Assert.Equal(HttpStatusCode.OK, interop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, blazor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dotnet.StatusCode);
        using var json = JsonDocument.Parse(await manifest.Content.ReadAsStringAsync(Abbruch));
        Assert.Equal("/app/", json.RootElement.GetProperty("start_url").GetString());
        Assert.Equal("/app/", json.RootElement.GetProperty("scope").GetString());
        Assert.Equal("de", json.RootElement.GetProperty("lang").GetString());
    }

    [Theory]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/app/", HttpStatusCode.OK)]
    [InlineData("/app/_framework/blazor.webassembly.js", HttpStatusCode.OK)]
    [InlineData("/_content/Zeltlotse.Ui.Komponenten/css/zeltlotse.css", HttpStatusCode.OK)]
    [InlineData("/api/betrieb/antiforgery", HttpStatusCode.OK)]
    [InlineData("/gibt-es-nicht", HttpStatusCode.NotFound)]
    [InlineData("/api/gibt-es-nicht", HttpStatusCode.NotFound)]
    [InlineData("/api/test/ausnahme", HttpStatusCode.InternalServerError)]
    public async Task Jede_Antwort_traegt_die_Version(string pfad, HttpStatusCode status)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync(pfad, Abbruch);

        Assert.Equal(status, antwort.StatusCode);
        Assert.Equal(ZeltlotseVersion.Aktuell, Assert.Single(antwort.Headers.GetValues(Kopfzeilen.Version)));
    }

    [Fact]
    public void Version_ist_die_InformationalVersion_von_Api_Contracts()
    {
        var erwartet = typeof(Kopfzeilen).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Single().InformationalVersion;

        Assert.Equal(erwartet, ZeltlotseVersion.Aktuell);
        Assert.False(string.IsNullOrWhiteSpace(ZeltlotseVersion.Aktuell));
    }

    [Fact]
    public async Task Public_Client_meldet_ohne_Antiforgery_und_die_Zugriffsbegrenzung_zaehlt_fuer_die_Besucher_IP()
    {
        await using var factory = await FactoryAsync(grenze: 2);
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var anfrage = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            anfrage.HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.50") } };
            var betrieb = scope.ServiceProvider.GetRequiredService<BetriebApiClient>();

            // 204 ohne Antiforgery-Token: die Dienst-Kennung gilt als Schutz (AD-25).
            await betrieb.FehlerMeldenAsync(GueltigeMeldung(), Abbruch);
            await betrieb.FehlerMeldenAsync(GueltigeMeldung(), Abbruch);
            anfrage.HttpContext = null;
        }

        // Beide Aufrufe zählten für 203.0.113.50: die nächste Anfrage dieser IP ist zu viel, eine andere IP nicht.
        using var dieselbe = await AnfrageVonAsync(client, "203.0.113.50");
        using var andere = await AnfrageVonAsync(client, "203.0.113.51");
        Assert.Equal((HttpStatusCode)429, dieselbe.StatusCode);
        Assert.Equal(HttpStatusCode.OK, andere.StatusCode);
    }

    [Fact]
    public async Task Public_Client_ohne_laufende_Anfrage_wirft_und_ruft_nicht_auf()
    {
        await using var factory = await FactoryAsync(grenze: 1);
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
            var betrieb = scope.ServiceProvider.GetRequiredService<BetriebApiClient>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => betrieb.FehlerMeldenAsync(GueltigeMeldung(), Abbruch));
        }

        // Kein Aufruf ohne Besucher-IP: die Partition des Testservers (ohne Absender-IP) ist noch frei.
        using var antwort = await client.GetAsync("/api/betrieb/antiforgery", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    [Fact]
    public async Task Formular_ausserhalb_von_api_mit_Formular_Token_ergibt_200()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);

        using var antwort = await client.PostAsync("/test/formular", Formular(("wert", "Zelt"), ("__RequestVerificationToken", token)), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("Zelt", (await antwort.Content.ReadFromJsonAsync<JsonElement>(Abbruch)).GetProperty("wert").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("falsch")]
    public async Task Formular_ausserhalb_von_api_ohne_gueltiges_Token_ergibt_400(string? token)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        _ = await AntiforgeryTokenAsync(client);
        var felder = token is null ? [("wert", "Zelt")] : new[] { ("wert", "Zelt"), ("__RequestVerificationToken", token) };

        using var antwort = await client.PostAsync("/test/formular", Formular(felder), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task Formular_Token_ohne_Header_unter_api_ergibt_weiter_400()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);

        using var antwort = await client.PostAsync("/api/test/aendern", Formular(("__RequestVerificationToken", token)), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(Problemcodes.AntiforgeryUngueltig, Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task POST_an_eine_oeffentliche_Seite_ohne_Token_ergibt_400()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.PostAsync("/", Formular(("wert", "x")), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    private static FormUrlEncodedContent Formular(params (string Name, string Wert)[] felder) =>
        new(felder.Select(f => new KeyValuePair<string, string>(f.Name, f.Wert)));

    private static async Task<HttpResponseMessage> AnfrageVonAsync(HttpClient client, string ip)
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/api/betrieb/antiforgery");
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, ip);
        return await client.SendAsync(anfrage, Abbruch);
    }

    private static void PruefeSicherheitsHeader(HttpResponseMessage antwort)
    {
        Assert.Equal(Csp, Assert.Single(antwort.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-referrer", Assert.Single(antwort.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("nosniff", Assert.Single(antwort.Headers.GetValues("X-Content-Type-Options")));
    }

    [GeneratedRegex("<link rel=\"stylesheet\" href=\"([^\"]+)\"")]
    private static partial Regex StylesheetMuster();

    [GeneratedRegex("<script[^>]*>[^<]*</script>", RegexOptions.IgnoreCase)]
    private static partial Regex SkriptMuster();

    [GeneratedRegex("(?:src|href)=\"([^\"#:]+)\"")]
    private static partial Regex VerweisMuster();
}
