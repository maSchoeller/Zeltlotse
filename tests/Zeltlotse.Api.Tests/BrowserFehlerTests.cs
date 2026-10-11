using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Matrix-Zeile „Browser-Fehler“: <c>POST /api/betrieb/fehler</c> (Konvention „Logging“, AD-22).</summary>
public sealed class BrowserFehlerTests(PostgresContainer postgres)
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz012345";

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.ZusatzDienste.Add(s => s.AddLogging(b => b.AddFakeLogging()));
        return factory;
    }

    [Fact]
    public async Task Meldung_wird_mit_bereinigter_Seite_geloggt_und_mit_204_beantwortet()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);
        var meldung = new BrowserFehler("TypeError", $"/app/gast/{Token}/pass?code=1#x", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

        using var antwort = await client.SendAsync(FehlerMeldung(meldung, antiforgery: token), Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        var eintraege = factory.Services.GetFakeLogCollector().GetSnapshot();
        var eintrag = Assert.Single(eintraege, e => e.Message.Contains("TypeError", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, eintrag.Level);
        Assert.Contains("/app/gast/***/pass", eintrag.Message, StringComparison.Ordinal);
        Assert.Contains(meldung.TraceId!, eintrag.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(eintraege, e => e.Message.Contains(Token, StringComparison.Ordinal) || e.Message.Contains("code=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ungueltiger_Koerper_ergibt_400_mit_Fehlern_je_Feld()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);

        using var antwort = await client.SendAsync(FehlerMeldung(new BrowserFehler("Fehler mit Name Erika", "", null), antiforgery: token), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal(Problemcodes.Validierung, Code(problem));
        var fehler = problem.GetProperty("errors");
        Assert.True(fehler.TryGetProperty("fehlerart", out _));
        Assert.True(fehler.TryGetProperty("seite", out _));
        Assert.True(fehler.TryGetProperty("traceId", out _));
        Assert.DoesNotContain(factory.Services.GetFakeLogCollector().GetSnapshot(), e => e.Message.Contains("Erika", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/app/Erika Muster")]
    [InlineData("/app/start\r\nGefaelschter Eintrag")]
    [InlineData("/app/erika@muster.de")]
    public async Task Seite_mit_Freitext_Zeichen_ergibt_400_am_Feld_seite(string seite)
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);

        using var antwort = await client.SendAsync(FehlerMeldung(new BrowserFehler("TypeError", seite, "0HN7:00000001"), antiforgery: token), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var fehler = (await ProblemAsync(antwort)).GetProperty("errors");
        Assert.True(fehler.TryGetProperty("seite", out _));
        Assert.False(fehler.TryGetProperty("traceId", out _), "Eine TraceIdentifier mit : ist gültig.");
        Assert.DoesNotContain(factory.Services.GetFakeLogCollector().GetSnapshot(), e => e.Message.Contains("Erika", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Leerer_Koerper_ergibt_400_mit_Fehlern_je_Feld()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);

        using var antwort = await client.SendAsync(FehlerMeldung(new { }, antiforgery: token), Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var fehler = (await ProblemAsync(antwort)).GetProperty("errors");
        Assert.True(fehler.TryGetProperty("fehlerart", out _));
        Assert.True(fehler.TryGetProperty("seite", out _));
        Assert.True(fehler.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Kein_JSON_ergibt_400_ProblemDetails()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/betrieb/fehler")
        {
            Content = new StringContent("{ kaputt", System.Text.Encoding.UTF8, "application/json"),
        };
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);

        using var antwort = await client.SendAsync(anfrage, Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal(Problemcodes.Validierung, Code(problem));
    }
}
