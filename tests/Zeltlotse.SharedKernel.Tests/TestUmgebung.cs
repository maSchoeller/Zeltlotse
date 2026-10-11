using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Outbox;
using Zeltlotse.SharedKernel.Pipeline;
using Zeltlotse.Testing;
using Zeltlotse.Testmodul;
using Zeltlotse.Testmodul.Daten;
using Zeltlotse.Testmodul.Empfang;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>
/// DI-Aufbau wie im API-Host, aber mit eigenem Mediator-Generator, nur dem Testmodul
/// und einer frischen Datenbank je Test.
/// </summary>
internal sealed class TestUmgebung : IAsyncDisposable
{
    private TestUmgebung(ServiceProvider dienste, FakeTimeProvider uhr)
    {
        Dienste = dienste;
        Uhr = uhr;
    }

    public ServiceProvider Dienste { get; }

    public FakeTimeProvider Uhr { get; }

    public EmpfangsStoerung Stoerung => Dienste.GetRequiredService<EmpfangsStoerung>();

    public static async Task<TestUmgebung> ErstellenAsync(PostgresContainer postgres, Action<IServiceCollection>? vorTestmodul = null)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var uhr = new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:zeltlotse"] = verbindung })
            .Build());
        services.AddSingleton<TimeProvider>(uhr);
        services.AddMediator(options =>
        {
            options.Namespace = "Zeltlotse.SharedKernel.Tests.Mediator";
            options.GenerateTypesAsInternal = true;
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(TestmodulModul)];
            options.PipelineBehaviors =
            [
                typeof(ValidierungsStufe<,>),
                typeof(InboxStufe<,>),
                typeof(TransaktionsStufe<,>),
                typeof(OutboxStufe<,>),
            ];
        });
        vorTestmodul?.Invoke(services);
        services.AddTestmodul();

        var dienste = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using (var scope = dienste.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestmodulDbContext>().Database.MigrateAsync();
        }

        return new TestUmgebung(dienste, uhr);
    }

    /// <summary>Schickt einen Command in eigenem Scope, optional mit Mandanten-Kontext.</summary>
    public async Task<TAntwort> SendenAsync<TAntwort>(ICommand<TAntwort> command, Guid? organisationId = null)
    {
        await using var scope = Dienste.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, TestContext.Current.CancellationToken);
    }

    /// <summary>Liest im Lese-Kontext mit dem angegebenen Mandanten-Kontext.</summary>
    public async Task<T> LesenAsync<T>(Func<TestmodulLeseDbContext, Task<T>> abfrage, Guid? organisationId = null)
    {
        await using var scope = Dienste.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<TestmodulLeseDbContext>());
    }

    /// <summary>Liest im schreibenden Kontext ohne Mandanten-Kontext (nur für Prüfungen im Test).</summary>
    public async Task<T> RohLesenAsync<T>(Func<TestmodulDbContext, Task<T>> abfrage)
    {
        await using var scope = Dienste.CreateAsyncScope();
        return await abfrage(scope.ServiceProvider.GetRequiredService<TestmodulDbContext>());
    }

    public Task<int> ZustellenAsync() =>
        Dienste.GetRequiredService<IOutboxZustellung>().ZustellenAsync(TestContext.Current.CancellationToken);

    public Task AllesZustellenAsync() =>
        Dienste.GetRequiredService<IOutboxZustellung>().AllesZustellenAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => Dienste.DisposeAsync();
}
