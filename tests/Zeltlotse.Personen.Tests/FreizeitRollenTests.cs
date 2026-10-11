using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Empfänger <c>FreizeitRolleUebernehmen</c> (AD-4, AD-8): Personen pflegt die eigene Liste der Freizeit-Rollen nur aus dem
/// Ereignis <see cref="FreizeitRolleGeaendert"/>. Legt die Zeile an oder setzt die Rolle, verarbeitet ein Ereignis nur
/// einmal und nimmt den Mandanten aus dem Umschlag.
/// </summary>
public sealed class FreizeitRollenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();
    private static readonly Guid Freizeit = Guid.CreateVersion7();
    private static readonly Guid Person = Guid.CreateVersion7();

    private ZeltlotseApiFactory _factory = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public void Empfaenger_ist_angemeldet()
    {
        var katalog = _factory.Services.GetRequiredService<EreignisKatalog>();

        var eintrag = Assert.Single(katalog.EmpfaengerFuer(typeof(FreizeitRolleGeaendert)));
        Assert.Equal(typeof(FreizeitRolleUebernehmen), eintrag.CommandTyp);
    }

    [Fact]
    public async Task Legt_die_Zeile_mit_Organisation_aus_dem_Umschlag_an_und_verarbeitet_dasselbe_Ereignis_nur_einmal()
    {
        var umschlag = Umschlag(OrgA, 1);

        await ZustellenAsync(umschlag, FreizeitRolleArt.Leitung);
        await ZustellenAsync(umschlag, FreizeitRolleArt.Leitung);

        var zeile = Assert.Single(await LesenAsync(db => db.FreizeitRollen.ToListAsync(Abbruch), OrgA));
        Assert.Equal((OrgA, Freizeit, Person, FreizeitRolleArt.Leitung), (zeile.OrganisationId, zeile.FreizeitId, zeile.PersonId, zeile.Rolle));
    }

    [Fact]
    public async Task Ein_spaeteres_Ereignis_setzt_die_Rolle_ein_aelteres_nicht()
    {
        await ZustellenAsync(Umschlag(OrgA, 1), FreizeitRolleArt.Leitung);
        await ZustellenAsync(Umschlag(OrgA, 3), FreizeitRolleArt.Team);
        await ZustellenAsync(Umschlag(OrgA, 2), FreizeitRolleArt.Leitung);

        var zeile = Assert.Single(await LesenAsync(db => db.FreizeitRollen.ToListAsync(Abbruch), OrgA));
        Assert.Equal(FreizeitRolleArt.Team, zeile.Rolle);
    }

    [Fact]
    public async Task Ohne_Organisation_im_Umschlag_gibt_es_keine_Zeile()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ZustellenAsync(Umschlag(null, 1), FreizeitRolleArt.Leitung));
    }

    [Fact]
    public async Task Mandantenfilter_die_Zeile_nur_im_eigenen_Kontext()
    {
        await ZustellenAsync(Umschlag(OrgA, 1), FreizeitRolleArt.Leitung);

        Assert.Single(await LesenAsync(db => db.FreizeitRollen.ToListAsync(Abbruch), OrgA));
        Assert.Empty(await LesenAsync(db => db.FreizeitRollen.ToListAsync(Abbruch), OrgB));
        Assert.Empty(await LesenAsync(db => db.FreizeitRollen.ToListAsync(Abbruch), null));
    }

    private static Umschlag Umschlag(Guid? organisation, long folgenummer) =>
        new(Guid.CreateVersion7(), DateTimeOffset.UtcNow, organisation, Freizeit, folgenummer);

    /// <summary>Wie der Zustelldienst: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    private async Task ZustellenAsync(Umschlag umschlag, FreizeitRolleArt rolle)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(umschlag.OrganisationId);
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(FreizeitRolleUebernehmen.Aus(umschlag, new FreizeitRolleGeaendert(Freizeit, Person, rolle)), Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<PersonenLeseDbContext, Task<T>> abfrage, Guid? organisationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<PersonenLeseDbContext>());
    }
}
