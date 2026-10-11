using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Personen besitzt die Tabelle <c>personen.personen_schluessel</c> (AD-14, Datenbesitz). Anlegen geschieht ab Story 1-1
/// im Command „Person anlegen“, Vernichten im Lösch-Ablauf (1-7); bis dahin legen die Tests die Zeilen direkt an.
/// </summary>
public sealed class PersonenSchluesselTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid PersonA = Guid.Parse("0199b000-0000-7000-8000-00000000000a");
    private static readonly Guid PersonB = Guid.Parse("0199b000-0000-7000-8000-00000000000b");

    private ServiceProvider _dienste = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:zeltlotse"] = await postgres.NeueDatenbankAsync(),
                [Hauptschluessel.Konfigurationsschluessel] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            })
            .Build());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero)));
        services.AddPersonenModul();
        _dienste = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = _dienste.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PersonenDbContext>().Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _dienste.DisposeAsync();

    [Fact]
    public async Task Schluessel_anlegen_lesen_und_loeschen()
    {
        var umhuellt = await AnlegenAsync(PersonA);

        Assert.Equal(umhuellt, await LesenAsync(PersonA));
        Assert.Null(await LesenAsync(PersonB));

        await LoeschenAsync(PersonA);

        Assert.Null(await LesenAsync(PersonA));
    }

    [Fact]
    public async Task Tabelle_liegt_im_Schema_personen_und_speichert_nur_den_umhuellten_Schluessel()
    {
        var umhuellt = await AnlegenAsync(PersonA);

        await using var scope = _dienste.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonenDbContext>();
        var spalten = await db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'personen' AND table_name = 'personen_schluessel' ORDER BY column_name")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["ErstelltAm", "PersonId", "Umhuellt"], spalten);

        // 32 Bytes Schlüssel + Kopf, Nonce und Tag der Umhüllung: nicht der rohe Schlüssel.
        Assert.True(umhuellt.Length > 32);
    }

    [Fact]
    public async Task Verschluesseln_und_Entschluesseln_ueber_die_Tabelle()
    {
        await AnlegenAsync(PersonA);
        await using var scope = _dienste.CreateAsyncScope();
        var verschluesselung = scope.ServiceProvider.GetRequiredService<IVerschluesselung>();

        var geschuetzt = await verschluesselung.VerschluesselnAsync(PersonA, "Bienenstich-Allergie", TestContext.Current.CancellationToken);

        Assert.Equal("Bienenstich-Allergie", await verschluesselung.EntschluesselnAsync(geschuetzt, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entschluesseln_nach_Loeschen_des_Schluessels_scheitert()
    {
        await AnlegenAsync(PersonA);
        Geschuetzt geschuetzt;
        await using (var scope = _dienste.CreateAsyncScope())
        {
            geschuetzt = await scope.ServiceProvider.GetRequiredService<IVerschluesselung>()
                .VerschluesselnAsync(PersonA, "Medikament: Ritalin", TestContext.Current.CancellationToken);
        }

        await LoeschenAsync(PersonA);

        await using var neuerScope = _dienste.CreateAsyncScope();
        var fehler = await Assert.ThrowsAsync<PersonenSchluesselFehltAusnahme>(() => neuerScope.ServiceProvider
            .GetRequiredService<IVerschluesselung>()
            .EntschluesselnAsync(geschuetzt, TestContext.Current.CancellationToken));
        Assert.Equal(PersonA, fehler.PersonId);
    }

    [Fact]
    public async Task Schluesselquelle_ist_registriert_und_liest_ueber_den_Lese_Kontext()
    {
        await using var scope = _dienste.CreateAsyncScope();

        var quelle = scope.ServiceProvider.GetRequiredService<IPersonenSchluesselQuelle>();

        Assert.IsType<PersonenSchluesselQuelle>(quelle);
    }

    private async Task<byte[]> AnlegenAsync(Guid personId)
    {
        await using var scope = _dienste.CreateAsyncScope();
        var hauptschluessel = scope.ServiceProvider.GetRequiredService<Hauptschluessel>();
        var db = scope.ServiceProvider.GetRequiredService<PersonenDbContext>();
        var umhuellt = hauptschluessel.NeuenPersonenSchluesselUmhuellen(personId);
        db.PersonenSchluessel.Add(PersonenSchluessel.Anlegen(personId, umhuellt, scope.ServiceProvider.GetRequiredService<TimeProvider>()));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return umhuellt;
    }

    private async Task<byte[]?> LesenAsync(Guid personId)
    {
        await using var scope = _dienste.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPersonenSchluesselQuelle>()
            .UmhuelltenSchluesselLesenAsync(personId, TestContext.Current.CancellationToken);
    }

    private async Task LoeschenAsync(Guid personId)
    {
        await using var scope = _dienste.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PersonenDbContext>().PersonenSchluessel
            .Where(s => s.PersonId == personId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }
}
