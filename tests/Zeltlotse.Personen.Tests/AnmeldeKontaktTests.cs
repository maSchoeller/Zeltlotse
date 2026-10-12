using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Gaeste;
using Zeltlotse.Personen.Profil;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Story 2-3 in Personen (AD-8, AD-13): die Empfängerregel der Bestätigungs- und Absage-Mail (<see cref="IAnmeldeKontakt"/>: eigenes
/// Konto, sonst der zuerst eingetragene Elternteil mit Konto, sonst die Mail des Gast-Zugangs, sonst keine) und die Freigabe mit
/// künstlicher Uhr (Stufe Team, Beenden ohne Löschen, ohne Zeile nichts). Die Mail-Adressen der Konten liefert eine nachgebildete
/// <see cref="IKontoLesen"/>. Die Uhr der Factory steht am 01.07.2026.
/// </summary>
public sealed class AnmeldeKontaktTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid Freizeit = Guid.CreateVersion7();

    private readonly NachgebildeteKonten _konten = new();
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

    // ---------- Empfängerregel ----------

    [Fact]
    public async Task Eigenes_Konto_geht_vor_sonst_der_zuerst_eingetragene_Elternteil_mit_Konto()
    {
        var katrin = await ProfilAsync();
        _factory.Uhr.Advance(TimeSpan.FromMinutes(5));
        var thomas = await ProfilAsync();
        var emma = await SendenAsync(new KindAnlegen(katrin, "Emma", "Muster", new DateOnly(2017, 9, 2)));
        await AusfuehrenAsync(
            "INSERT INTO personen.familien_elternteil (\"FamilieId\", \"PersonId\") SELECT \"FamilieId\", @t FROM personen.person WHERE \"Id\" = @e", ("t", thomas), ("e", emma));
        _konten[thomas] = "thomas@example.org";

        // Katrin hat (noch) kein Konto: Thomas ist der einzige Elternteil mit Konto.
        Assert.Equal("thomas@example.org", await MailAsync(emma));

        _konten[katrin] = "katrin@example.org";
        Assert.Equal("katrin@example.org", await MailAsync(emma));
        Assert.Equal("thomas@example.org", await MailAsync(thomas));

        // Ein Kind mit eigenem Konto bekommt die Mail selbst.
        _konten[emma] = "emma@example.org";
        Assert.Equal("emma@example.org", await MailAsync(emma));
    }

    [Fact]
    public async Task Ohne_Konto_in_der_Familie_geht_die_Mail_an_den_Gast_Zugang_sonst_an_niemanden()
    {
        GastAnlage gast;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            gast = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new GastAnlegenCommand("Sabine", "Beispiel", "Gast@Example.org", "Lina", null, new DateOnly(2016, 4, 1)), Abbruch);
        }

        var ohneKonto = await ProfilAsync();
        var kind = await SendenAsync(new KindAnlegen(ohneKonto, "Ben", null, new DateOnly(2018, 1, 5)));

        Assert.Equal("gast@example.org", await MailAsync(gast.KindPersonId));
        Assert.Null(await MailAsync(kind));
        Assert.Null(await MailAsync(ohneKonto));
        Assert.Null(await MailAsync(GeloeschtePerson.Id));
        Assert.Null(await MailAsync(Guid.CreateVersion7()));
    }

    // ---------- Freigabe ----------

    [Fact]
    public void Beenden_setzt_gueltig_bis_auf_jetzt_ein_spaeteres_Beenden_und_ein_abgelaufenes_Ende_bleiben()
    {
        var jetzt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var freigabe = Freigabe.Leitung(Guid.CreateVersion7(), OrgA, Guid.CreateVersion7(), Freizeit, Guid.CreateVersion7(), jetzt.AddDays(60), jetzt);

        freigabe.ZuTeam();
        Assert.Equal(FreigabeStufe.Team, freigabe.Stufe);

        freigabe.Beenden(jetzt.AddDays(1));
        Assert.Equal(jetzt.AddDays(1), freigabe.GueltigBis);
        freigabe.Beenden(jetzt.AddDays(2));
        Assert.Equal(jetzt.AddDays(1), freigabe.GueltigBis);

        var abgelaufen = Freigabe.Leitung(Guid.CreateVersion7(), OrgA, Guid.CreateVersion7(), Freizeit, Guid.CreateVersion7(), jetzt, jetzt.AddDays(-30));
        abgelaufen.Beenden(jetzt.AddDays(5));
        Assert.Equal(jetzt, abgelaufen.GueltigBis);
    }

    [Fact]
    public async Task Empfaenger_setzen_Team_und_beenden_mit_der_Uhr_der_Zustellung_ohne_Freigabe_nichts()
    {
        var kind = await SendenAsync(new KindAnlegen(await ProfilAsync(), "Emma", null, new DateOnly(2017, 9, 2)));
        var anmeldung = Guid.CreateVersion7();
        var bis = _factory.Uhr.GetUtcNow().AddDays(60);
        await ZustellenAsync(FreigabeAnlegen.Aus(Umschlag(), new AnmeldungEingegangen(anmeldung, Freizeit, kind, AnmeldungArt.Teilnehmende, bis)));

        await ZustellenAsync(FreigabeTeamSetzen.Aus(Umschlag(), new AnmeldungBestaetigt(anmeldung, Freizeit, kind, AnmeldungArt.Teilnehmende)));
        var team = await FreigabeAsync();
        Assert.Equal((FreigabeStufe.Team, bis), (team.Stufe, team.GueltigBis));

        _factory.Uhr.Advance(TimeSpan.FromDays(3));
        var ende = _factory.Uhr.GetUtcNow();
        await ZustellenAsync(FreigabeBeenden.Aus(Umschlag(), new AnmeldungAbgesagt(anmeldung, Freizeit, kind, AnmeldungArt.Teilnehmende)));
        Assert.Equal(ende, (await FreigabeAsync()).GueltigBis);

        // Ohne Freigabe-Zeile tun beide nichts.
        await ZustellenAsync(FreigabeTeamSetzen.Aus(Umschlag(), new AnmeldungBestaetigt(Guid.CreateVersion7(), Freizeit, kind, AnmeldungArt.Teilnehmende)));
        await ZustellenAsync(FreigabeBeenden.Aus(Umschlag(), new AnmeldungAbgesagt(Guid.CreateVersion7(), Freizeit, kind, AnmeldungArt.Teilnehmende)));
        Assert.Single(await LesenAsync(db => db.Freigaben.ToListAsync(Abbruch)));
    }

    // ---------- Hilfen ----------

    private async Task<Guid> ProfilAsync()
    {
        var id = Guid.CreateVersion7(_factory.Uhr.GetUtcNow());
        await SendenAsync(new ProfilSicherstellen(id));
        return id;
    }

    private async Task<string?> MailAsync(Guid personId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAnmeldeKontakt>().MailAsync(personId, Abbruch);
    }

    private async Task<FreigabeLesen> FreigabeAsync()
    {
        var freigabe = Assert.Single(await LesenAsync(db => db.Freigaben.ToListAsync(Abbruch)));
        return new FreigabeLesen(freigabe.Stufe, freigabe.GueltigBis);
    }

    private sealed record FreigabeLesen(FreigabeStufe Stufe, DateTimeOffset GueltigBis);

    private Umschlag Umschlag() => new(Guid.CreateVersion7(), _factory.Uhr.GetUtcNow(), OrgA, Guid.CreateVersion7(), 1);

    private async Task<T> SendenAsync<T>(ICommand<T> command)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    /// <summary>Wie der Zustelldienst: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    private async Task ZustellenAsync<T>(T command)
        where T : ICommand
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<PersonenLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        return await abfrage(scope.ServiceProvider.GetRequiredService<PersonenLeseDbContext>());
    }

    private async Task AusfuehrenAsync(string sql, params (string Name, object Wert)[] parameter)
    {
        await using var db = new NpgsqlConnection(_verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        await befehl.ExecuteNonQueryAsync(Abbruch);
    }

    /// <summary>Konten nach Person: nur <see cref="MailDerPersonAsync"/> zählt hier.</summary>
    private sealed class NachgebildeteKonten : Dictionary<Guid, string>, IKontoLesen
    {
        public Task<bool> IstGlobalAdminAsync(Guid kontoId, CancellationToken abbruch = default) => Task.FromResult(false);

        public Task<string?> MailAsync(Guid kontoId, CancellationToken abbruch = default) => Task.FromResult<string?>(null);

        public Task<string?> MailDerPersonAsync(Guid personId, CancellationToken abbruch = default) =>
            Task.FromResult(TryGetValue(personId, out var mail) ? mail : null);
    }
}
