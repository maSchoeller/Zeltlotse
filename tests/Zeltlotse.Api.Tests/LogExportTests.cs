using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>AD-22: Exportierte Logs tragen Ausnahmen nur mit Typ und keine Scopes mit Rohpfad.</summary>
public sealed class LogExportTests(PostgresContainer postgres)
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz012345";

    [Fact]
    public async Task Ausnahme_im_Log_wird_nur_mit_ihrem_Typ_exportiert()
    {
        var eintraege = new List<LogRecord>();
        await using var factory = await FactoryAsync(eintraege);
        _ = factory.CreateClient();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.EntityFrameworkCore.Update");

        // Wie ein Framework-Logger (EF Core, Npgsql): Ausnahme mit Personendaten in der Nachricht angehängt.
        logger.Log(LogLevel.Error, new EventId(1), "Speichern fehlgeschlagen.", new InvalidOperationException("Erika Muster"), (s, _) => s);

        var eintrag = Assert.Single(Momentaufnahme(eintraege), e => e.FormattedMessage == "Speichern fehlgeschlagen.");
        Assert.Null(eintrag.Exception);
        Assert.Contains(eintrag.Attributes!, a => a.Key == LogBereinigung.TypAttribut && (string?)a.Value == typeof(InvalidOperationException).FullName);
        Assert.DoesNotContain(eintrag.Attributes!, a => a.Value?.ToString()?.Contains("Erika", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Kein_exportierter_Eintrag_traegt_ein_Token_aus_dem_Pfad()
    {
        var eintraege = new List<LogRecord>();
        await using var factory = await FactoryAsync(eintraege);
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync($"/api/test/ausnahme-mit-token/{Token}", Abbruch);

        var exportiert = Momentaufnahme(eintraege);
        Assert.Contains(exportiert, e => e.LogLevel == LogLevel.Error);
        foreach (var eintrag in exportiert)
        {
            Assert.DoesNotContain(Token, eintrag.FormattedMessage ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain(eintrag.Attributes ?? [], a => a.Value?.ToString()?.Contains(Token, StringComparison.Ordinal) == true);
            var scopes = new List<object?>();
            eintrag.ForEachScope(
                (scope, liste) =>
                {
                    foreach (var wert in scope)
                    {
                        liste.Add(wert.Value);
                    }
                },
                scopes);
            Assert.DoesNotContain(scopes, s => s?.ToString()?.Contains(Token, StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public async Task Host_gibt_Logs_nur_ueber_OpenTelemetry_aus()
    {
        // Konsole, Debug, EventSource und EventLog umgingen die Bereinigung und schrieben Ausnahmen mit Nachricht und Stacktrace.
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueMigrierteDatenbankAsync());
        _ = factory.CreateClient();

        Assert.All(factory.Services.GetServices<ILoggerProvider>(), p => Assert.IsType<OpenTelemetryLoggerProvider>(p));
    }

    private async Task<ZeltlotseApiFactory> FactoryAsync(List<LogRecord> eintraege)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueMigrierteDatenbankAsync());
        factory.ZusatzDienste.Add(TestEndpunkte.Registrieren);
        factory.ZusatzDienste.Add(s => s.Configure<OpenTelemetryLoggerOptions>(o => o.AddInMemoryExporter(eintraege)));
        return factory;
    }

    private static List<LogRecord> Momentaufnahme(List<LogRecord> eintraege)
    {
        lock (eintraege)
        {
            return [.. eintraege];
        }
    }
}
