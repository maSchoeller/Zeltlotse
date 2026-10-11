using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Matrix-Zeile „Unerwartete Ausnahme“ und die Fehlerform ProblemDetails mit <c>traceId</c> (AD-22, AD-27).</summary>
public sealed class FehlerTests(PostgresContainer postgres)
{
    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.ZusatzDienste.Add(TestEndpunkte.Registrieren);
        factory.ZusatzDienste.Add(s => s.AddLogging(b => b.AddFakeLogging().SetMinimumLevel(LogLevel.Trace)));
        return factory;
    }

    [Fact]
    public async Task Unerwartete_Ausnahme_ergibt_500_mit_Typ_und_traceId_ohne_Nachricht_in_Antwort_und_Log()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/api/test/ausnahme", Abbruch);

        Assert.Equal(HttpStatusCode.InternalServerError, antwort.StatusCode);
        var text = await antwort.Content.ReadAsStringAsync(Abbruch);
        Assert.DoesNotContain("Erika", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Erdnuss", text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);

        var problem = await ProblemAsync(antwort);
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal(Problemcodes.UnerwarteterFehler, Code(problem));
        Assert.Equal(nameof(InvalidOperationException), problem.GetProperty("ausnahmeTyp").GetString());
        var traceId = problem.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrEmpty(traceId));

        var eintraege = factory.Services.GetFakeLogCollector().GetSnapshot();
        Assert.DoesNotContain(eintraege, e => e.Message.Contains("Erika", StringComparison.Ordinal));
        Assert.DoesNotContain(eintraege, e => e.Exception is not null);
        Assert.DoesNotContain(eintraege, e => e.StructuredState?.Any(kv => kv.Value?.Contains("Erika", StringComparison.Ordinal) == true) == true);
        Assert.Contains(eintraege, e =>
            e.Level == LogLevel.Error
            && e.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal)
            && e.Message.Contains(traceId!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidierungsAusnahme_ergibt_400_mit_Fehlern_je_JSON_Feldname()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var token = await AntiforgeryTokenAsync(client);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/test/validierung");
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);

        using var antwort = await client.SendAsync(anfrage, Abbruch);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal(Problemcodes.Validierung, Code(problem));
        Assert.True(problem.TryGetProperty("traceId", out _));
        var fehler = problem.GetProperty("errors");
        Assert.Equal("Pflichtfeld.", fehler.GetProperty("vorname")[0].GetString());
        Assert.Equal("Fünf Ziffern.", fehler.GetProperty("adresse.plz")[0].GetString());
    }

    [Fact]
    public async Task Unbekannte_Adresse_unter_api_ergibt_404_ProblemDetails_mit_traceId()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/api/gibt-es-nicht", Abbruch);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Test_Endpunkte_werden_ausserhalb_von_Testing_nicht_abgebildet()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = "Production" };
        factory.ZusatzDienste.Add(TestEndpunkte.Registrieren);
        await using (factory)
        {
            using var client = factory.CreateClient();

            using var antwort = await client.GetAsync("/api/test/ok", Abbruch);

            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }
    }
}
