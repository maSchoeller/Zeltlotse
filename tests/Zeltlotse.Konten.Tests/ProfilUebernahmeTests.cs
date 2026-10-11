using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Konten.Anmelden;
using Zeltlotse.Konten.Contracts.Sitzungen;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Profil;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Konten.Tests;

/// <summary>
/// Empfänger <c>KontoMitProfilVerbinden</c> (AD-4, AD-7, Tabelle Ereignis-Fluss): Nach <see cref="ProfilUebergabeAngenommen"/>
/// gehört das Konto zum übernommenen Profil, der Sitzungsstempel ist neu (alte Sitzungen enden), und dasselbe Ereignis wirkt
/// nur einmal.
/// </summary>
public sealed class ProfilUebernahmeTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid Paul = Guid.Parse("0199d000-0000-7000-8000-00000000000d");

    private ZeltlotseApiFactory _factory = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public void Empfaenger_ist_ueber_den_Ereignis_Katalog_angemeldet()
    {
        var katalog = _factory.Services.GetRequiredService<EreignisKatalog>();

        var eintrag = Assert.Single(katalog.EmpfaengerFuer(typeof(ProfilUebergabeAngenommen)));
        Assert.Equal(typeof(KontoMitProfilVerbinden), eintrag.CommandTyp);
    }

    [Fact]
    public async Task Verbindet_das_Konto_mit_dem_Profil_mit_neuem_Stempel_und_verarbeitet_dasselbe_Ereignis_nur_einmal()
    {
        var konto = await SendenAsync(new KontoSicherstellen("paul.muster@example.org"));
        var vorher = await LesenAsync(db => db.Konten.SingleAsync(Abbruch));
        var umschlag = new Umschlag(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, Guid.CreateVersion7(), 1);

        await ZustellenAsync(umschlag, new ProfilUebergabeAngenommen(Paul, konto.KontoId));
        var nachher = await LesenAsync(db => db.Konten.SingleAsync(Abbruch));
        await ZustellenAsync(umschlag, new ProfilUebergabeAngenommen(Paul, konto.KontoId));

        Assert.Equal(Paul, nachher.PersonId);
        Assert.NotEqual(vorher.SitzungsStempel, nachher.SitzungsStempel);
        Assert.Equal(nachher.SitzungsStempel, (await LesenAsync(db => db.Konten.SingleAsync(Abbruch))).SitzungsStempel);
        await using var scope = _factory.Services.CreateAsyncScope();
        var pruefung = scope.ServiceProvider.GetRequiredService<IKontoPruefung>();
        Assert.False(await pruefung.SitzungGueltigAsync(konto.KontoId, konto.PersonId, vorher.SitzungsStempel, Abbruch));
        Assert.True(await pruefung.SitzungGueltigAsync(konto.KontoId, Paul, nachher.SitzungsStempel, Abbruch));
    }

    [Fact]
    public async Task Ein_weiteres_Ereignis_fuer_dieselbe_Person_aendert_den_Stempel_nicht()
    {
        var konto = await SendenAsync(new KontoSicherstellen("paul.muster@example.org"));
        await ZustellenAsync(new Umschlag(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, Guid.CreateVersion7(), 1), new ProfilUebergabeAngenommen(Paul, konto.KontoId));
        var stempel = (await LesenAsync(db => db.Konten.SingleAsync(Abbruch))).SitzungsStempel;

        await ZustellenAsync(new Umschlag(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, Guid.CreateVersion7(), 1), new ProfilUebergabeAngenommen(Paul, konto.KontoId));

        var konto2 = await LesenAsync(db => db.Konten.SingleAsync(Abbruch));
        Assert.Equal((Paul, stempel), (konto2.PersonId, konto2.SitzungsStempel));
    }

    /// <summary>Wie der Zustelldienst: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    private async Task ZustellenAsync(Umschlag umschlag, ProfilUebergabeAngenommen ereignis)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(umschlag.OrganisationId);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(KontoMitProfilVerbinden.Aus(umschlag, ereignis), Abbruch);
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<KontenLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await abfrage(scope.ServiceProvider.GetRequiredService<KontenLeseDbContext>());
    }
}
