using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.Personen.Profil;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Gemeinsamer Aufbau der Nachweis-Tests: Host gegen eine frische Datenbank mit künstlicher Uhr (01.07.2026, 08:00 UTC) und
/// einem Dateispeicher im Arbeitsspeicher; Commands in eigenem Scope, optional mit Mandanten-Kontext.
/// </summary>
public abstract class NachweisTestBasis(PostgresContainer postgres) : IAsyncLifetime
{
    protected static readonly Guid Jonas = Guid.Parse("0199d000-0000-7000-8000-0000000000a1");
    protected static readonly Guid Lea = Guid.Parse("0199d000-0000-7000-8000-0000000000a2");
    protected static readonly Guid Miriam = Guid.Parse("0199d000-0000-7000-8000-0000000000b1");
    protected static readonly Guid Tobias = Guid.Parse("0199d000-0000-7000-8000-0000000000b2");
    protected static readonly Guid Andrea = Guid.Parse("0199d000-0000-7000-8000-0000000000c1");

    protected static readonly byte[] Pdf = [.. "%PDF-1.7\nNachweis\n"u8, 1, 2, 3];
    protected static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    protected ZeltlotseApiFactory Factory { get; private set; } = null!;

    protected string Verbindung { get; private set; } = null!;

    protected DateiSpeicherImSpeicher Speicher { get; } = new();

    protected static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Verbindung = await postgres.NeueDatenbankAsync();
        Factory = new ZeltlotseApiFactory(Verbindung);
        Factory.ZusatzDienste.Add(services =>
        {
            services.RemoveAll<IDateiSpeicher>();
            services.AddSingleton<IDateiSpeicher>(Speicher);
        });
        _ = Factory.CreateClient();
        foreach (var person in new[] { Jonas, Lea, Miriam, Tobias, Andrea })
        {
            await SendenAsync(new ProfilSicherstellen(person));
        }
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();

    protected async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private protected async Task<T> LesenAsync<T>(Func<PersonenLeseDbContext, Task<T>> abfrage, Guid? organisationId = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<PersonenLeseDbContext>());
    }

    /// <summary>Stellt ein Ereignis wie der Zustelldienst zu: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    internal async Task RolleZustellenAsync(Umschlag umschlag, FreizeitRolleGeaendert ereignis)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(umschlag.OrganisationId);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(FreizeitRolleUebernehmen.Aus(umschlag, ereignis), Abbruch);
    }

    protected async Task<byte[]?> DateiLesenAsync(Guid nachweisId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IGeschuetzteDateien>().LesenAsync("personen", nachweisId, Abbruch);
    }

    protected async Task<T> WertAsync<T>(string sql)
    {
        await using var db = new NpgsqlConnection(Verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        return (T)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }
}
