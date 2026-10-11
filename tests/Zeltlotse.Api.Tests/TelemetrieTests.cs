using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>AD-22: Spans tragen nur die Routen-Vorlage, nie Rohpfad oder Abfrage, und keine Ausnahme-Nachrichten.</summary>
public sealed class TelemetrieTests(PostgresContainer postgres)
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz012345";
    private static readonly string[] VerboteneTags = ["url.path", "url.query", "url.full", "http.target", "http.url"];

    [Fact]
    public async Task Eingehende_Spans_tragen_http_route_aber_weder_Pfad_noch_Abfrage()
    {
        var spans = new List<Activity>();
        await using var factory = await FactoryAsync(spans);
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync($"/api/betrieb/antiforgery?token={Token}", Abbruch);
        using var unbekannt = await client.GetAsync($"/api/gast/{Token}/pass?code=1", Abbruch);

        var eingehend = await WartenAufAsync(spans, s => s.Kind == ActivityKind.Server, anzahl: 2);
        Assert.Contains(eingehend, s => (string?)s.GetTagItem("http.route") == "/api/betrieb/antiforgery");
        PruefeOhneRohdaten(eingehend);
    }

    [Fact]
    public async Task Ausgehende_Spans_tragen_keine_URL()
    {
        var spans = new List<Activity>();
        await using var factory = await FactoryAsync(spans);
        _ = factory.CreateClient();

        using (var ausgehend = new HttpClient())
        {
            // Port 9 (discard) ist geschlossen: der Aufruf scheitert schnell, der Span entsteht trotzdem.
            await Assert.ThrowsAnyAsync<Exception>(() => ausgehend.GetAsync($"http://127.0.0.1:9/gast/{Token}?code=1", Abbruch));
        }

        var client = await WartenAufAsync(spans, s => s.Kind == ActivityKind.Client, anzahl: 1);
        PruefeOhneRohdaten(client);
        // Die Laufzeit legt das Ereignis selbst an; es darf nur den Typ tragen, keine Nachricht und keinen Stacktrace.
        var ereignisse = client.SelectMany(s => s.Events).Where(e => e.Name == "exception").ToList();
        Assert.NotEmpty(ereignisse);
        Assert.All(ereignisse, e =>
        {
            var tags = e.Tags.ToDictionary(t => t.Key, t => t.Value?.ToString());
            Assert.DoesNotContain("refused", tags.GetValueOrDefault("exception.message") ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal("", tags.GetValueOrDefault("exception.stacktrace") ?? "");
            Assert.DoesNotContain(e.Tags, t => t.Value?.ToString()?.Contains(Token, StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    public async Task Ausnahmen_erscheinen_in_Spans_nicht_mit_Nachricht()
    {
        var spans = new List<Activity>();
        await using var factory = await FactoryAsync(spans);
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync("/api/test/ausnahme", Abbruch);

        var span = Assert.Single(await WartenAufAsync(spans, s => s.Kind == ActivityKind.Server && (string?)s.GetTagItem("http.route") == "/api/test/ausnahme", anzahl: 1));
        Assert.DoesNotContain(span.Events, e => e.Name == "exception");
        Assert.DoesNotContain(span.Events.SelectMany(e => e.Tags), t => t.Value?.ToString()?.Contains("Erika", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(span.TagObjects, t => t.Value?.ToString()?.Contains("Erika", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("Erika", span.StatusDescription ?? "", StringComparison.Ordinal);
    }

    private async Task<ZeltlotseApiFactory> FactoryAsync(List<Activity> spans)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.ZusatzDienste.Add(TestEndpunkte.Registrieren);
        factory.ZusatzDienste.Add(s => s.AddOpenTelemetry().WithTracing(t => t.AddInMemoryExporter(spans)));
        return factory;
    }

    private static void PruefeOhneRohdaten(IEnumerable<Activity> spans)
    {
        foreach (var span in spans)
        {
            foreach (var tag in VerboteneTags)
            {
                Assert.Null(span.GetTagItem(tag));
            }

            Assert.DoesNotContain(span.TagObjects, t => t.Value?.ToString()?.Contains(Token, StringComparison.Ordinal) == true);
            Assert.DoesNotContain(Token, span.DisplayName, StringComparison.Ordinal);
        }
    }

    private static async Task<List<Activity>> WartenAufAsync(List<Activity> spans, Func<Activity, bool> bedingung, int anzahl)
    {
        var bis = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            List<Activity> treffer;
            lock (spans)
            {
                treffer = spans.Where(bedingung).ToList();
            }

            if (treffer.Count >= anzahl || DateTime.UtcNow > bis)
            {
                Assert.True(treffer.Count >= anzahl, $"Nur {treffer.Count} von {anzahl} Spans exportiert.");
                return treffer;
            }

            await Task.Delay(20, Abbruch);
        }
    }
}
