using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.SharedKernel.Outbox;
using Zeltlotse.Testing;

namespace Zeltlotse.Api.Tests;

public sealed class ZustellDienstTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Host_registriert_den_Zustell_Dienst()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync(), mitZustellDienst: true);
        _ = factory.CreateClient();

        Assert.Contains(factory.Services.GetServices<IHostedService>(), d => d is ZustellDienst);
    }

    [Fact]
    public async Task Fehlgeschlagener_Durchgang_wird_geloggt_und_der_naechste_Takt_laeuft()
    {
        var uhr = new FakeTimeProvider();
        var zustellung = new ErstMalFehlerhafteZustellung();
        var logger = new MerkLogger();
        using var dienst = new ZustellDienst(zustellung, uhr, logger);

        await dienst.StartAsync(TestContext.Current.CancellationToken);
        await WartenBisAsync(() => zustellung.Aufrufe >= 1);
        uhr.Advance(TimeSpan.FromSeconds(2));
        await WartenBisAsync(() => zustellung.Aufrufe >= 2);
        await dienst.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains(logger.Eintraege, e => e.Stufe == LogLevel.Error && e.Text.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    private static async Task WartenBisAsync(Func<bool> bedingung)
    {
        var bis = DateTime.UtcNow.AddSeconds(10);
        while (!bedingung())
        {
            Assert.True(DateTime.UtcNow < bis, "Bedingung nicht rechtzeitig erfüllt.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ErstMalFehlerhafteZustellung : IOutboxZustellung
    {
        private int _aufrufe;

        public int Aufrufe => Volatile.Read(ref _aufrufe);

        public Task<int> ZustellenAsync(CancellationToken abbruch = default) => Task.FromResult(0);

        public Task AllesZustellenAsync(CancellationToken abbruch = default) =>
            Interlocked.Increment(ref _aufrufe) == 1
                ? Task.FromException(new InvalidOperationException("erster Durchgang scheitert"))
                : Task.CompletedTask;
    }

    private sealed class MerkLogger : ILogger<ZustellDienst>
    {
        public List<(LogLevel Stufe, string Text)> Eintraege { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Eintraege)
            {
                Eintraege.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
