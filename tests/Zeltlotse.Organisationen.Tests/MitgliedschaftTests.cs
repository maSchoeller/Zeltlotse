using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Mitglieder;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Organisationen.Tests;

/// <summary>
/// Story 2-3, FR-7: Der Empfänger <see cref="MitgliedschaftAnlegen"/> macht bestätigte Mitarbeitende zu Mitarbeitenden der
/// Organisation aus dem Umschlag, einmal je Person; Teilnehmende und gelöschte Personen ignoriert er. Der Mandantenfilter zeigt die
/// Mitgliedschaft nur der eigenen Organisation (AD-6).
/// </summary>
public sealed class MitgliedschaftTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();
    private static readonly Guid Jonas = Guid.CreateVersion7();
    private static readonly Guid Emma = Guid.CreateVersion7();

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

        Assert.Contains(katalog.EmpfaengerFuer(typeof(AnmeldungBestaetigt)), e => e.CommandTyp == typeof(MitgliedschaftAnlegen));
    }

    [Fact]
    public async Task Nur_Mitarbeitende_werden_einmal_Mitglied_mit_der_Organisation_aus_dem_Umschlag()
    {
        var seit = _factory.Uhr.GetUtcNow();

        await ZustellenAsync(Jonas, AnmeldungArt.Mitarbeitende);
        _factory.Uhr.Advance(TimeSpan.FromDays(3));
        await ZustellenAsync(Jonas, AnmeldungArt.Mitarbeitende);
        await ZustellenAsync(Emma, AnmeldungArt.Teilnehmende);
        await ZustellenAsync(GeloeschtePerson.Id, AnmeldungArt.Mitarbeitende);

        var zeile = Assert.Single(await LesenAsync(OrgA));
        Assert.Equal((OrgA, Jonas, seit), (zeile.OrganisationId, zeile.PersonId, zeile.SeitAm));
    }

    [Fact]
    public async Task Mandantenfilter_die_Mitgliedschaft_nur_im_eigenen_Kontext()
    {
        await ZustellenAsync(Jonas, AnmeldungArt.Mitarbeitende);

        Assert.Single(await LesenAsync(OrgA));
        Assert.Empty(await LesenAsync(OrgB));
        Assert.Empty(await LesenAsync(null));
    }

    [Fact]
    public async Task PersonGeloescht_loescht_die_Mitgliedschaft_der_Person()
    {
        await ZustellenAsync(Jonas, AnmeldungArt.Mitarbeitende);
        var andere = Guid.CreateVersion7();
        await ZustellenAsync(andere, AnmeldungArt.Mitarbeitende);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var umschlag = new Umschlag(Guid.CreateVersion7(), _factory.Uhr.GetUtcNow(), null, Jonas, 1);
            await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(Datenschutz.OrgPersonLoeschen.Aus(umschlag, new Zeltlotse.Personen.Contracts.Events.PersonGeloescht(Jonas)), Abbruch);
        }

        Assert.Equal(andere, Assert.Single(await LesenAsync(OrgA)).PersonId);
    }

    /// <summary>Wie der Zustelldienst: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    private async Task ZustellenAsync(Guid person, AnmeldungArt art)
    {
        var umschlag = new Umschlag(Guid.CreateVersion7(), _factory.Uhr.GetUtcNow(), OrgA, Guid.CreateVersion7(), 1);
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(MitgliedschaftAnlegen.Aus(umschlag, new AnmeldungBestaetigt(Guid.CreateVersion7(), Guid.CreateVersion7(), person, art)), Abbruch);
    }

    private async Task<List<Domaene.Mitgliedschaft>> LesenAsync(Guid? organisationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<OrganisationenLeseDbContext>().Mitgliedschaften.ToListAsync(Abbruch);
    }
}
