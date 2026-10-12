using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Gaeste;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Gast-Zugang und Freigabe (AD-7, AD-8, AD-11, FR-17) mit künstlicher Uhr: Der Gast-Link gilt vor der ersten Freigabe 1 Tag, danach
/// bis zum spätesten Ende der Freigaben der Familie, nach der Übernahme nicht mehr. Der Empfänger <c>FreigabeAnlegen</c> legt die
/// Freigabe der Stufe Leitung einmal je Anmeldung an, nimmt den Mandanten aus dem Umschlag, und der Mandantenfilter zeigt sie nur
/// der eigenen Organisation. Die Uhr der Factory steht am 01.07.2026.
/// </summary>
public sealed class GastZugangTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();
    private static readonly Guid Freizeit = Guid.CreateVersion7();

    private ZeltlotseApiFactory _factory = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    // ---------- Gültigkeitsregel (ohne Datenbank) ----------

    [Fact]
    public void Ohne_Freigabe_gilt_der_Gast_Link_einen_Tag_mit_Freigaben_bis_zur_spaetesten()
    {
        var angelegt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var zugang = GastZugang.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), "gast@example.org", [1, 2, 3], angelegt);
        var frueh = angelegt.AddDays(10);
        var spaet = angelegt.AddDays(40);

        Assert.Equal(angelegt.AddDays(1), zugang.GueltigBis([]));
        Assert.True(zugang.IstGueltig(angelegt.AddDays(1).AddTicks(-1), []));
        Assert.False(zugang.IstGueltig(angelegt.AddDays(1), []));
        Assert.Equal(spaet, zugang.GueltigBis([frueh, spaet]));
        Assert.True(zugang.IstGueltig(spaet.AddTicks(-1), [frueh, spaet]));
        Assert.False(zugang.IstGueltig(spaet, [frueh, spaet]));
        // Auch eine schon abgelaufene Freigabe ersetzt den einen Tag.
        Assert.False(zugang.IstGueltig(angelegt.AddHours(1), [angelegt]));
    }

    [Fact]
    public void Nach_der_Uebernahme_gilt_der_Gast_Link_nicht_mehr_und_das_Ereignis_liegt_einmal_vor()
    {
        var angelegt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var eltern = Guid.CreateVersion7();
        var konto = Guid.CreateVersion7();
        var zugang = GastZugang.Anlegen(Guid.CreateVersion7(), eltern, "gast@example.org", [1], angelegt);

        zugang.Uebernehmen(konto, angelegt.AddMinutes(5));

        Assert.False(zugang.IstGueltig(angelegt.AddMinutes(6), []));
        Assert.Equal(new Zeltlotse.Personen.Contracts.Events.ProfilUebergabeAngenommen(eltern, konto), Assert.Single(zugang.Ereignisse));
        Assert.Throws<InvalidOperationException>(() => zugang.Uebernehmen(konto, angelegt.AddMinutes(7)));
    }

    // ---------- Gast-Zugang mit künstlicher Uhr ----------

    [Fact]
    public async Task PruefenAsync_folgt_der_Uhr_vor_und_nach_der_ersten_Freigabe()
    {
        var anlage = await GastAsync();
        var start = _factory.Uhr.GetUtcNow();

        var gueltig = await PruefenAsync(anlage.Token);
        Assert.NotNull(gueltig);
        Assert.Equal((anlage.ElternPersonId, "gast@example.org", start.AddDays(1)), (gueltig.ElternPersonId, gueltig.Mail, gueltig.GueltigBis));
        Assert.Equal([anlage.KindPersonId], gueltig.KindPersonIds);
        Assert.Null(await PruefenAsync(Zugangslink.NeuesToken()));
        Assert.Null(await PruefenAsync("kein-token"));

        _factory.Uhr.Advance(TimeSpan.FromDays(1));
        Assert.Null(await PruefenAsync(anlage.Token));

        // Eine Freigabe der Familie verlängert den Gast-Link bis zu ihrem Ende.
        await ZustellenAsync(Umschlag(OrgA), Guid.CreateVersion7(), anlage.KindPersonId, start.AddDays(30));
        Assert.Equal(start.AddDays(30), (await PruefenAsync(anlage.Token))!.GueltigBis);
        _factory.Uhr.Advance(TimeSpan.FromDays(29));
        Assert.Null(await PruefenAsync(anlage.Token));
    }

    // ---------- Freigabe ----------

    [Fact]
    public void Empfaenger_ist_angemeldet()
    {
        var katalog = _factory.Services.GetRequiredService<EreignisKatalog>();

        var eintrag = Assert.Single(katalog.EmpfaengerFuer(typeof(AnmeldungEingegangen)));
        Assert.Equal(typeof(FreigabeAnlegen), eintrag.CommandTyp);
    }

    [Fact]
    public async Task Freigabe_entsteht_einmal_je_Anmeldung_mit_Organisation_aus_dem_Umschlag_und_nur_fuer_vorhandene_Personen()
    {
        var anlage = await GastAsync();
        var anmeldung = Guid.CreateVersion7();
        var bis = _factory.Uhr.GetUtcNow().AddDays(60);

        await ZustellenAsync(Umschlag(OrgA), anmeldung, anlage.KindPersonId, bis);
        await ZustellenAsync(Umschlag(OrgA), anmeldung, anlage.KindPersonId, bis);
        await ZustellenAsync(Umschlag(OrgA), Guid.CreateVersion7(), Guid.CreateVersion7(), bis);

        var zeile = Assert.Single(await LesenAsync(db => db.Freigaben.ToListAsync(Abbruch), OrgA));
        Assert.Equal((OrgA, anlage.KindPersonId, Freizeit, anmeldung, FreigabeStufe.Leitung, bis), (zeile.OrganisationId, zeile.PersonId, zeile.FreizeitId, zeile.AnmeldungId, zeile.Stufe, zeile.GueltigBis));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ZustellenAsync(Umschlag(null), Guid.CreateVersion7(), anlage.KindPersonId, bis));
    }

    [Fact]
    public async Task Mandantenfilter_die_Freigabe_nur_im_eigenen_Kontext()
    {
        var anlage = await GastAsync();
        await ZustellenAsync(Umschlag(OrgA), Guid.CreateVersion7(), anlage.KindPersonId, _factory.Uhr.GetUtcNow().AddDays(60));

        Assert.Single(await LesenAsync(db => db.Freigaben.ToListAsync(Abbruch), OrgA));
        Assert.Empty(await LesenAsync(db => db.Freigaben.ToListAsync(Abbruch), OrgB));
        Assert.Empty(await LesenAsync(db => db.Freigaben.ToListAsync(Abbruch), null));
    }

    // ---------- Hilfen ----------

    private async Task<GastAnlage> GastAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GastAnlegenCommand("Sabine", "Beispiel", "Gast@Example.org", "Lina", null, new DateOnly(2016, 4, 1)), Abbruch);
    }

    private async Task<GastZugangInfo?> PruefenAsync(string token)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IGastZugaenge>().PruefenAsync(token, Abbruch);
    }

    private Umschlag Umschlag(Guid? organisation) => new(Guid.CreateVersion7(), _factory.Uhr.GetUtcNow(), organisation, Guid.CreateVersion7(), 1);

    /// <summary>Wie der Zustelldienst: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    private async Task ZustellenAsync(Umschlag umschlag, Guid anmeldung, Guid person, DateTimeOffset bis)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(umschlag.OrganisationId);
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(FreigabeAnlegen.Aus(umschlag, new AnmeldungEingegangen(anmeldung, Freizeit, person, AnmeldungArt.Teilnehmende, bis)), Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<PersonenLeseDbContext, Task<T>> abfrage, Guid? organisationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<PersonenLeseDbContext>());
    }
}
