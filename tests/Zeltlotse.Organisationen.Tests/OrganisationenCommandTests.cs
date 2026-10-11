using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Domaene;
using Zeltlotse.Organisationen.Einladungen;
using Zeltlotse.Organisationen.Verwaltung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Tests;

/// <summary>
/// Commands des Moduls Organisationen gegen PostgreSQL mit künstlicher Uhr (FR-5, AD-11, AD-27): Organisation anlegen,
/// einladen, annehmen (Ablauf, einmal, Verwerfen, Bindung an die Mail-Adresse, doppelte Rolle, eingerichtet). Die
/// Mail-Adressen der Konten liefert eine nachgebildete <see cref="IKontoLesen"/>.
/// </summary>
public sealed class OrganisationenCommandTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid Gerd = Guid.CreateVersion7();
    private static readonly Guid AndreaKonto = Guid.CreateVersion7();
    private static readonly Guid AndreaPerson = Guid.CreateVersion7();
    private static readonly Guid JonasKonto = Guid.CreateVersion7();
    private static readonly Guid JonasPerson = Guid.CreateVersion7();

    private readonly NachgebildeteKonten _konten = new()
    {
        [AndreaKonto] = "andrea.beispiel@example.org",
        [JonasKonto] = "jonas.huettner@example.org",
    };

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = new ZeltlotseApiFactory(_verbindung);
        _factory.ZusatzDienste.Add(s => s.AddScoped<IKontoLesen>(_ => _konten));
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Anlegen_speichert_Organisation_und_Einladung_mit_normalisierter_Mail_und_nur_dem_Hash()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("  Jugendwerk Musterstadt ", " Andrea.Beispiel@Example.ORG ", Gerd));

        var organisation = await LesenAsync(db => db.Organisationen.SingleAsync(Abbruch));
        Assert.Equal("Jugendwerk Musterstadt", organisation.Name);
        Assert.Null(organisation.EingerichtetAm);
        var einladung = await LesenAsync(db => db.Einladungen.SingleAsync(Abbruch), neu.OrganisationId);
        Assert.Equal(neu.EinladungId, einladung.Id);
        Assert.Equal("andrea.beispiel@example.org", einladung.Mail);
        Assert.Equal(OrgRolleArt.OrgAdmin, einladung.Rolle);
        Assert.Equal(Gerd, einladung.EingeladenVon);
        Assert.Equal(OrgEinladungStatus.Offen, einladung.Status);
        Assert.Equal(_factory.Uhr.GetUtcNow() + TimeSpan.FromDays(7), einladung.GueltigBis);
        Assert.Equal(Zugangslink.TokenHash("org-einladung", neu.Token), einladung.TokenHash);
        Assert.True(Zugangslink.KannTokenSein(neu.Token));
        Assert.DoesNotContain(neu.Token, neu.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("andrea", neu.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "andrea@example.org", "name", "Name fehlt.")]
    [InlineData("   ", "andrea@example.org", "name", "Name fehlt.")]
    [InlineData(null, "andrea@example.org", "name", "Name fehlt.")]
    [InlineData("Jugendwerk", "", "mail", "Mail-Adresse fehlt.")]
    [InlineData("Jugendwerk", "keine-mail", "mail", "Das ist keine gültige Mail-Adresse.")]
    [InlineData("Jugendwerk", "Andrea <andrea@example.org>", "mail", "Das ist keine gültige Mail-Adresse.")]
    public async Task Anlegen_prueft_Name_und_Mail_je_Feld(string? name, string? mail, string feld, string meldung)
    {
        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new OrganisationAnlegenCommand(name, mail, Gerd)));

        Assert.Equal([feld], fehler.Fehler.Keys);
        Assert.Equal([meldung], fehler.Fehler[feld]);
        Assert.Equal(0, await LesenAsync(db => db.Organisationen.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Name_mit_100_Zeichen_geht_mit_101_nicht()
    {
        await SendenAsync(new OrganisationAnlegenCommand(new string('a', 100), "andrea@example.org", Gerd));

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new OrganisationAnlegenCommand(new string('a', 101), "andrea@example.org", Gerd)));

        Assert.Equal(["name"], fehler.Fehler.Keys);
    }

    [Fact]
    public async Task Annehmen_vergibt_die_Rolle_setzt_den_Status_und_richtet_die_Organisation_ein()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));

        var angenommen = await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson));

        Assert.NotNull(angenommen);
        Assert.Equal(neu.OrganisationId, angenommen.OrganisationId);
        Assert.Equal(neu.EinladungId, angenommen.EinladungId);
        var rolle = await LesenAsync(db => db.Rollen.SingleAsync(Abbruch), neu.OrganisationId);
        Assert.Equal((neu.OrganisationId, AndreaPerson, OrgRolleArt.OrgAdmin), (rolle.OrganisationId, rolle.PersonId, rolle.Rolle));
        Assert.Equal(_factory.Uhr.GetUtcNow(), rolle.VergebenAm);
        Assert.Equal(OrgEinladungStatus.Angenommen, await StatusAsync(neu));
        Assert.Equal(_factory.Uhr.GetUtcNow(), (await LesenAsync(db => db.Organisationen.SingleAsync(Abbruch))).EingerichtetAm);
    }

    [Fact]
    public async Task Ein_zweites_Annehmen_gibt_es_nicht()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));
        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)));

        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)));

        Assert.Equal(1, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), neu.OrganisationId));
        Assert.Equal(1L, await ProtokollAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Abgelaufen_nach_7_Tagen_und_1_Minute_bleibt_die_Einladung_offen_und_vergibt_nichts()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));
        _factory.Uhr.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));

        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)));

        Assert.Equal(OrgEinladungStatus.Offen, await StatusAsync(neu));
        Assert.Equal(0, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), neu.OrganisationId));
        Assert.Null((await LesenAsync(db => db.Organisationen.SingleAsync(Abbruch))).EingerichtetAm);
    }

    [Fact]
    public async Task Kurz_vor_Ablauf_geht_es_noch()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));
        _factory.Uhr.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1));

        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)));
    }

    [Fact]
    public async Task Neue_Einladung_verwirft_aeltere_offene_derselben_Mail_Organisation_und_Rolle_und_keine_anderen()
    {
        var a = await SendenAsync(new OrganisationAnlegenCommand("A", "andrea.beispiel@example.org", Gerd));
        var b = await SendenAsync(new OrganisationAnlegenCommand("B", "andrea.beispiel@example.org", Gerd));
        var andereRolle = await SendenAsync(new EinladungAnlegenCommand(a.OrganisationId, "andrea.beispiel@example.org", OrgRolleArt.OrgUeberblick, Gerd), a.OrganisationId);
        var andereMail = await SendenAsync(new EinladungAnlegenCommand(a.OrganisationId, "jonas.huettner@example.org", OrgRolleArt.OrgAdmin, Gerd), a.OrganisationId);

        var neu = await SendenAsync(new EinladungAnlegenCommand(a.OrganisationId, " ANDREA.beispiel@example.org", OrgRolleArt.OrgAdmin, Gerd), a.OrganisationId);

        Assert.Equal(OrgEinladungStatus.Verworfen, await StatusAsync(a));
        Assert.Equal(OrgEinladungStatus.Offen, await StatusAsync(neu));
        Assert.Equal(OrgEinladungStatus.Offen, await StatusAsync(b));
        Assert.Equal(OrgEinladungStatus.Offen, await StatusAsync(andereRolle));
        Assert.Equal(OrgEinladungStatus.Offen, await StatusAsync(andereMail));
        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(a.Token, AndreaKonto, AndreaPerson)));
        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)));
    }

    [Fact]
    public async Task Andere_Mail_nimmt_nicht_an_die_Einladung_bleibt_unveraendert_und_die_richtige_Person_nimmt_danach_an()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));

        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, JonasKonto, JonasPerson)));
        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, Guid.CreateVersion7(), Guid.CreateVersion7())));

        Assert.Equal(OrgEinladungStatus.Offen, await StatusAsync(neu));
        Assert.Equal(0, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), neu.OrganisationId));
        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)));
        Assert.Equal(AndreaPerson, (await LesenAsync(db => db.Rollen.SingleAsync(Abbruch), neu.OrganisationId)).PersonId);
    }

    [Theory]
    [InlineData("login")]
    [InlineData(null)]
    public async Task Fremdes_unbekanntes_oder_kaputtes_Token_nimmt_nichts_an(string? zweck)
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));
        var fremd = Zugangslink.NeuesToken();
        if (zweck is not null)
        {
            // Ein Token mit dem Hash eines anderen Zwecks in der Tabelle: Der Zweck im Hash verhindert den Treffer.
            Assert.NotEqual(Zugangslink.TokenHash(zweck, fremd), Zugangslink.TokenHash("org-einladung", fremd));
        }

        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(fremd, AndreaKonto, AndreaPerson)));
        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(neu.Token + "x", AndreaKonto, AndreaPerson)));
        Assert.Null(await SendenAsync(new EinladungAnnehmenCommand(null, AndreaKonto, AndreaPerson)));

        Assert.Equal(OrgEinladungStatus.Offen, await StatusAsync(neu));
        Assert.Equal(3L, await ProtokollAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Zweite_Einladung_gleicher_Rolle_ergibt_eine_Rollen_Zeile()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));
        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)));
        var zweite = await SendenAsync(new EinladungAnlegenCommand(neu.OrganisationId, "andrea.beispiel@example.org", OrgRolleArt.OrgAdmin, AndreaPerson), neu.OrganisationId);

        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(zweite.Token, AndreaKonto, AndreaPerson)));

        Assert.Equal(1, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), neu.OrganisationId));
        Assert.Equal(OrgEinladungStatus.Angenommen, await StatusAsync(zweite));
    }

    [Fact]
    public async Task Ueberblick_annehmen_vergibt_Org_Ueberblick_und_richtet_nicht_ein_beide_Rollen_sind_moeglich()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));
        var ueberblick = await SendenAsync(new EinladungAnlegenCommand(neu.OrganisationId, "jonas.huettner@example.org", OrgRolleArt.OrgUeberblick, Gerd), neu.OrganisationId);

        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(ueberblick.Token, JonasKonto, JonasPerson)));

        Assert.Equal(OrgRolleArt.OrgUeberblick, (await LesenAsync(db => db.Rollen.SingleAsync(Abbruch), neu.OrganisationId)).Rolle);
        Assert.Null((await LesenAsync(db => db.Organisationen.SingleAsync(Abbruch))).EingerichtetAm);

        var admin = await SendenAsync(new EinladungAnlegenCommand(neu.OrganisationId, "jonas.huettner@example.org", OrgRolleArt.OrgAdmin, Gerd), neu.OrganisationId);
        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(admin.Token, JonasKonto, JonasPerson)));
        Assert.Equal(
            [OrgRolleArt.OrgAdmin, OrgRolleArt.OrgUeberblick],
            await LesenAsync(db => db.Rollen.Where(r => r.PersonId == JonasPerson).Select(r => r.Rolle).OrderBy(r => r).ToListAsync(Abbruch), neu.OrganisationId));
        Assert.NotNull((await LesenAsync(db => db.Organisationen.SingleAsync(Abbruch))).EingerichtetAm);
    }

    [Fact]
    public async Task Einladung_fuer_X_wirkt_nur_in_X()
    {
        var x = await SendenAsync(new OrganisationAnlegenCommand("X", "andrea.beispiel@example.org", Gerd));
        var y = await SendenAsync(new OrganisationAnlegenCommand("Y", "jonas.huettner@example.org", Gerd));

        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(x.Token, AndreaKonto, AndreaPerson)));

        Assert.Single(await LesenAsync(db => db.Rollen.ToListAsync(Abbruch), x.OrganisationId));
        Assert.Empty(await LesenAsync(db => db.Rollen.ToListAsync(Abbruch), y.OrganisationId));
        Assert.Null(await LesenAsync(db => db.Organisationen.Where(o => o.Id == y.OrganisationId).Select(o => o.EingerichtetAm).SingleAsync(Abbruch)));
    }

    [Fact]
    public async Task Einladung_anlegen_verlangt_den_Mandanten_Kontext_der_Organisation()
    {
        var a = await SendenAsync(new OrganisationAnlegenCommand("A", "andrea.beispiel@example.org", Gerd));
        var b = await SendenAsync(new OrganisationAnlegenCommand("B", "andrea.beispiel@example.org", Gerd));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendenAsync(new EinladungAnlegenCommand(a.OrganisationId, "x@example.org", OrgRolleArt.OrgAdmin, Gerd)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendenAsync(new EinladungAnlegenCommand(a.OrganisationId, "x@example.org", OrgRolleArt.OrgAdmin, Gerd), b.OrganisationId));
    }

    [Fact]
    public async Task Einladung_anlegen_prueft_Mail_und_Rolle_je_Feld()
    {
        var a = await SendenAsync(new OrganisationAnlegenCommand("A", "andrea.beispiel@example.org", Gerd));

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new EinladungAnlegenCommand(a.OrganisationId, "keine", null, Gerd), a.OrganisationId));

        Assert.Equal(["mail", "rolle"], fehler.Fehler.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Rolle fehlt."], fehler.Fehler["rolle"]);
    }

    [Fact]
    public async Task Gleichzeitiges_Annehmen_derselben_Einladung_gelingt_genau_einmal()
    {
        var neu = await SendenAsync(new OrganisationAnlegenCommand("Jugendwerk", "andrea.beispiel@example.org", Gerd));

        var ergebnisse = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => SendenAsync(new EinladungAnnehmenCommand(neu.Token, AndreaKonto, AndreaPerson)))));

        Assert.Single(ergebnisse, e => e is not null);
        Assert.Equal(1, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), neu.OrganisationId));
    }

    [Fact]
    public async Task Mandantenfilter_Rollen_und_Einladungen_nur_im_eigenen_Kontext()
    {
        var a = await SendenAsync(new OrganisationAnlegenCommand("A", "andrea.beispiel@example.org", Gerd));
        var b = await SendenAsync(new OrganisationAnlegenCommand("B", "jonas.huettner@example.org", Gerd));
        Assert.NotNull(await SendenAsync(new EinladungAnnehmenCommand(a.Token, AndreaKonto, AndreaPerson)));
        await SendenAsync(new EinladungAnlegenCommand(a.OrganisationId, "neu@example.org", OrgRolleArt.OrgUeberblick, AndreaPerson), a.OrganisationId);

        // Kontext A sieht die eigenen Zeilen.
        Assert.Single(await LesenAsync(db => db.Rollen.ToListAsync(Abbruch), a.OrganisationId));
        Assert.Equal(2, (await LesenAsync(db => db.Einladungen.ToListAsync(Abbruch), a.OrganisationId)).Count);

        // Kontext B sieht nichts von A, ohne Kontext nichts.
        Assert.Empty(await LesenAsync(db => db.Rollen.Where(r => r.OrganisationId == a.OrganisationId).ToListAsync(Abbruch), b.OrganisationId));
        Assert.Empty(await LesenAsync(db => db.Einladungen.Where(e => e.OrganisationId == a.OrganisationId).ToListAsync(Abbruch), b.OrganisationId));
        Assert.Empty(await LesenAsync(db => db.Rollen.ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(db => db.Einladungen.ToListAsync(Abbruch)));

        // Auch der schreibende Kontext filtert.
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(b.OrganisationId);
        var schreiben = scope.ServiceProvider.GetRequiredService<OrganisationenDbContext>();
        Assert.Empty(await schreiben.Rollen.ToListAsync(Abbruch));
        Assert.Single(await schreiben.Einladungen.ToListAsync(Abbruch));
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<OrganisationenLeseDbContext, Task<T>> abfrage, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<OrganisationenLeseDbContext>());
    }

    private Task<OrgEinladungStatus> StatusAsync(NeueEinladung einladung) =>
        LesenAsync(db => db.Einladungen.Where(e => e.Id == einladung.EinladungId).Select(e => e.Status).SingleAsync(Abbruch), einladung.OrganisationId);

    private async Task<long> ProtokollAsync(SicherheitsEreignisArt art)
    {
        await using var verbindung = new Npgsql.NpgsqlConnection(_verbindung);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand("SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art", verbindung);
        befehl.Parameters.AddWithValue("art", (short)art);
        return (long)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }

    /// <summary>Mail-Adressen der Konten ohne das Modul Konten.</summary>
    private sealed class NachgebildeteKonten : Dictionary<Guid, string>, IKontoLesen
    {
        public Task<bool> IstGlobalAdminAsync(Guid kontoId, CancellationToken abbruch = default) => Task.FromResult(false);

        public Task<string?> MailAsync(Guid kontoId, CancellationToken abbruch = default) =>
            Task.FromResult(TryGetValue(kontoId, out var mail) ? mail : null);
    }
}
