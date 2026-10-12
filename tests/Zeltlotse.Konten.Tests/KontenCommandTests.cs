using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Konten.Anmelden;
using Zeltlotse.Konten.Contracts.Sitzungen;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Konten.Passwort;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.Testing;

namespace Zeltlotse.Konten.Tests;

/// <summary>
/// Commands des Moduls Konten gegen PostgreSQL mit künstlicher Uhr (AD-11, AD-27): Anfrage anlegen und verwerfen, Link und
/// Code einlösen, Konto anlegen, Passwort, Sitzungsprüfung. Gespeichert sind nur Hashes und normalisierte Mail-Adressen.
/// </summary>
public sealed class KontenCommandTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = new ZeltlotseApiFactory(_verbindung);
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Anfrage_speichert_normalisierte_Mail_und_nur_Hashes()
    {
        var neu = await SendenAsync(new LoginAnfrageAnlegen("  Paul@Example.ORG "));

        var anfrage = await LesenAsync(db => db.LoginAnfragen.SingleAsync(a => a.Id == neu.AnfrageId, Abbruch));
        Assert.Equal("paul@example.org", anfrage.Mail);
        Assert.Equal("paul@example.org", neu.Mail);
        Assert.Equal(Zugangslink.TokenHash("login", neu.Token), anfrage.TokenHash);
        Assert.Equal(Geheimnisse.CodeHash(anfrage.Id, neu.Code), anfrage.CodeHash);
        Assert.Equal(_factory.Uhr.GetUtcNow() + TimeSpan.FromMinutes(15), anfrage.GueltigBis);
        Assert.Equal(LoginAnfrageStatus.Offen, anfrage.Status);
        Assert.DoesNotContain(neu.Token, neu.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Neue_Anfrage_verwirft_aeltere_offene_Anfragen_derselben_Mail_und_keine_anderen()
    {
        var erste = await SendenAsync(new LoginAnfrageAnlegen("paul@example.org"));
        var andere = await SendenAsync(new LoginAnfrageAnlegen("emma@example.org"));

        var zweite = await SendenAsync(new LoginAnfrageAnlegen("PAUL@example.org"));

        Assert.Equal(LoginAnfrageStatus.Verworfen, await StatusAsync(erste.AnfrageId));
        Assert.Equal(LoginAnfrageStatus.Offen, await StatusAsync(zweite.AnfrageId));
        Assert.Equal(LoginAnfrageStatus.Offen, await StatusAsync(andere.AnfrageId));
        Assert.Null(await SendenAsync(new LoginLinkEinloesenCommand(erste.Token)));
        Assert.NotNull(await SendenAsync(new LoginLinkEinloesenCommand(zweite.Token)));
    }

    [Fact]
    public async Task Link_einloesen_legt_das_Konto_mit_neuer_PersonId_an_und_ein_zweites_Mal_gibt_es_nicht()
    {
        var neu = await SendenAsync(new LoginAnfrageAnlegen("paul@example.org"));

        var sitzung = await SendenAsync(new LoginLinkEinloesenCommand(neu.Token));

        Assert.NotNull(sitzung);
        var konto = await LesenAsync(db => db.Konten.SingleAsync(Abbruch));
        Assert.Equal(sitzung.KontoId, konto.Id);
        Assert.Equal(sitzung.PersonId, konto.PersonId);
        Assert.Equal(sitzung.SitzungsStempel, konto.SitzungsStempel);
        Assert.NotEqual(konto.Id, konto.PersonId);
        Assert.Equal("paul@example.org", konto.Mail);
        Assert.Null(konto.PasswortHash);
        Assert.Equal(LoginAnfrageStatus.Eingeloest, await StatusAsync(neu.AnfrageId));
        Assert.Null(await SendenAsync(new LoginLinkEinloesenCommand(neu.Token)));
        Assert.Null(await SendenAsync(new LoginCodeEinloesenCommand(neu.AnfrageId, neu.Code)));
    }

    [Fact]
    public async Task Abgelaufene_Anfrage_loest_weder_Link_noch_Code_ein()
    {
        var neu = await SendenAsync(new LoginAnfrageAnlegen("paul@example.org"));
        _factory.Uhr.Advance(TimeSpan.FromMinutes(16));

        Assert.Null(await SendenAsync(new LoginLinkEinloesenCommand(neu.Token)));
        Assert.Null(await SendenAsync(new LoginCodeEinloesenCommand(neu.AnfrageId, neu.Code)));
        Assert.Equal(0, await LesenAsync(db => db.Konten.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Fuenf_falsche_Codes_verwerfen_die_Anfrage_in_der_Datenbank()
    {
        var neu = await SendenAsync(new LoginAnfrageAnlegen("paul@example.org"));
        var falsch = neu.Code == "999999" ? "999998" : "999999";

        for (var i = 0; i < 5; i++)
        {
            Assert.Null(await SendenAsync(new LoginCodeEinloesenCommand(neu.AnfrageId, falsch)));
        }

        var anfrage = await LesenAsync(db => db.LoginAnfragen.SingleAsync(a => a.Id == neu.AnfrageId, Abbruch));
        Assert.Equal(5, anfrage.Fehlversuche);
        Assert.Equal(LoginAnfrageStatus.Verworfen, anfrage.Status);
        Assert.Null(await SendenAsync(new LoginCodeEinloesenCommand(neu.AnfrageId, neu.Code)));
        Assert.Null(await SendenAsync(new LoginLinkEinloesenCommand(neu.Token)));
    }

    [Theory]
    [InlineData(4, 4, false)]
    [InlineData(8, 5, true)]
    public async Task Gleichzeitige_falsche_Codes_zaehlen_alle(int versuche, int fehlversuche, bool verworfen)
    {
        var neu = await SendenAsync(new LoginAnfrageAnlegen("paul@example.org"));
        var falsch = neu.Code == "999999" ? "999998" : "999999";

        var ergebnisse = await Task.WhenAll(Enumerable.Range(0, versuche)
            .Select(_ => Task.Run(() => SendenAsync(new LoginCodeEinloesenCommand(neu.AnfrageId, falsch)))));

        Assert.All(ergebnisse, Assert.Null);
        var anfrage = await LesenAsync(db => db.LoginAnfragen.SingleAsync(a => a.Id == neu.AnfrageId, Abbruch));
        Assert.Equal(fehlversuche, anfrage.Fehlversuche);
        Assert.Equal(verworfen ? LoginAnfrageStatus.Verworfen : LoginAnfrageStatus.Offen, anfrage.Status);
        Assert.Equal(versuche, await ProtokollAsync(Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Gleichzeitiges_Einloesen_desselben_Tokens_gelingt_genau_einmal()
    {
        var neu = await SendenAsync(new LoginAnfrageAnlegen("paul@example.org"));

        var ergebnisse = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(() => SendenAsync(new LoginLinkEinloesenCommand(neu.Token))))
                .Append(Task.Run(() => SendenAsync(new LoginCodeEinloesenCommand(neu.AnfrageId, neu.Code)))));

        Assert.Single(ergebnisse, e => e is not null);
        Assert.Equal(1, await LesenAsync(db => db.Konten.CountAsync(Abbruch)));
        Assert.Equal(LoginAnfrageStatus.Eingeloest, await StatusAsync(neu.AnfrageId));
        Assert.Equal(4, await ProtokollAsync(Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Konto_sicherstellen_ist_idempotent_und_Link_findet_das_Konto()
    {
        var erstes = await SendenAsync(new KontoSicherstellen("Andrea.Beispiel@example.org"));
        var zweites = await SendenAsync(new KontoSicherstellen("andrea.beispiel@example.org"));
        var neu = await SendenAsync(new LoginAnfrageAnlegen("andrea.beispiel@example.org"));
        var sitzung = await SendenAsync(new LoginLinkEinloesenCommand(neu.Token));

        Assert.True(erstes.Neu);
        Assert.False(zweites.Neu);
        Assert.Equal(erstes.PersonId, zweites.PersonId);
        Assert.Equal(erstes.PersonId, sitzung!.PersonId);
        Assert.Equal(1, await LesenAsync(db => db.Konten.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Passwort_festlegen_erneuert_den_Stempel_und_die_Pruefung_gelingt_nur_mit_dem_richtigen_Passwort()
    {
        var konto = await SendenAsync(new KontoSicherstellen("paul@example.org"));
        var vorher = await LesenAsync(db => db.Konten.SingleAsync(Abbruch));

        var sitzung = await SendenAsync(new PasswortFestlegenCommand(konto.KontoId, "ein-gutes-passwort"));

        Assert.NotEqual(vorher.SitzungsStempel, sitzung.SitzungsStempel);
        Assert.False(await PruefenAsync(konto.KontoId, konto.PersonId, vorher.SitzungsStempel));
        Assert.True(await PruefenAsync(konto.KontoId, konto.PersonId, sitzung.SitzungsStempel));
        Assert.False(await PruefenAsync(konto.KontoId, Guid.CreateVersion7(), sitzung.SitzungsStempel));
        Assert.NotNull(await SendenAsync(new PasswortAnmeldungPruefen(" PAUL@example.org", "ein-gutes-passwort")));
        Assert.Null(await SendenAsync(new PasswortAnmeldungPruefen("paul@example.org", "ein-gutes-passwort ")));
        Assert.Null(await SendenAsync(new PasswortAnmeldungPruefen("niemand@example.org", "ein-gutes-passwort")));

        // Die Anmeldung mit Passwort ändert den Stempel nicht: andere Sitzungen bleiben.
        Assert.True(await PruefenAsync(konto.KontoId, konto.PersonId, sitzung.SitzungsStempel));
    }

    [Fact]
    public async Task Ein_Pruefwert_in_altem_Format_wird_bei_der_Anmeldung_erneuert()
    {
        var konto = await SendenAsync(new KontoSicherstellen("paul@example.org"));
        await SendenAsync(new PasswortFestlegenCommand(konto.KontoId, "ein-gutes-passwort"));
        var alt = new PasswordHasher<Konto>(Microsoft.Extensions.Options.Options.Create(
            new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 })).HashPassword(null!, "ein-gutes-passwort");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KontenDbContext>().Konten
                .ExecuteUpdateAsync(k => k.SetProperty(x => x.PasswortHash, alt), Abbruch);
        }

        Assert.NotNull(await SendenAsync(new PasswortAnmeldungPruefen("paul@example.org", "ein-gutes-passwort")));

        var neu = (await LesenAsync(db => db.Konten.SingleAsync(Abbruch))).PasswortHash!;
        Assert.NotEqual(alt, neu);
        Assert.Equal(1, Convert.FromBase64String(neu)[0]);
    }

    [Fact]
    public async Task Global_Admins_abgleichen_setzt_das_Kennzeichen_legt_fehlende_Konten_an_und_entfernt_es_bei_allen_anderen()
    {
        var vorhanden = await SendenAsync(new KontoSicherstellen("gerd.betreiber@example.org"));
        var anderes = await SendenAsync(new KontoSicherstellen("andrea.beispiel@example.org"));

        var erster = await SendenAsync(new GlobalAdminsAbgleichen([" Gerd.Betreiber@example.org", "neu@example.org"]));

        Assert.Equal(new GlobalAdminAbgleich(2, 1), erster);
        Assert.True(await GlobalAdminAsync("gerd.betreiber@example.org"));
        Assert.True(await GlobalAdminAsync("neu@example.org"));
        Assert.False(await GlobalAdminAsync("andrea.beispiel@example.org"));
        var neu = await LesenAsync(db => db.Konten.SingleAsync(k => k.Mail == "neu@example.org", Abbruch));
        Assert.NotEqual(neu.Id, neu.PersonId);
        Assert.Equal(3, await LesenAsync(db => db.Konten.CountAsync(Abbruch)));
        // Rollenvergabe je geändertem Konto: Gerd vergeben, neues Konto angelegt.
        Assert.Equal(2, await ProtokollAsync(Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt.Rollenvergabe));

        // Aus der Konfiguration entfernt: Kennzeichen weg, Konto bleibt; ein zweiter Lauf legt nichts doppelt an.
        var zweiter = await SendenAsync(new GlobalAdminsAbgleichen(["andrea.beispiel@example.org"]));

        Assert.Equal(new GlobalAdminAbgleich(1, 0), zweiter);
        Assert.False(await GlobalAdminAsync("gerd.betreiber@example.org"));
        Assert.False(await GlobalAdminAsync("neu@example.org"));
        Assert.True(await GlobalAdminAsync("andrea.beispiel@example.org"));
        Assert.Equal(3, await LesenAsync(db => db.Konten.CountAsync(Abbruch)));
        Assert.Equal(vorhanden.PersonId, await LesenAsync(db => db.Konten.Where(k => k.Id == vorhanden.KontoId).Select(k => k.PersonId).SingleAsync(Abbruch)));
        Assert.NotEqual(anderes.KontoId, vorhanden.KontoId);
        // Andrea vergeben, Gerd und neu entzogen.
        Assert.Equal(5, await ProtokollAsync(Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt.Rollenvergabe));

        // Ohne Änderung kein Eintrag.
        await SendenAsync(new GlobalAdminsAbgleichen(["andrea.beispiel@example.org"]));
        Assert.Equal(5, await ProtokollAsync(Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt.Rollenvergabe));

        // Leere Liste: niemand ist Global Admin.
        await SendenAsync(new GlobalAdminsAbgleichen([]));
        Assert.Equal(0, await LesenAsync(db => db.Konten.CountAsync(k => k.GlobalAdmin, Abbruch)));
        Assert.Equal(6, await ProtokollAsync(Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt.Rollenvergabe));
        Assert.Equal(2L, await WertAsync("SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = 4 AND \"PersonId\" = @p", anderes.PersonId));
    }

    private async Task<long> WertAsync(string sql, Guid personId)
    {
        await using var verbindung = new Npgsql.NpgsqlConnection(_verbindung);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(sql, verbindung);
        befehl.Parameters.AddWithValue("p", personId);
        return (long)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }

    [Fact]
    public async Task Konto_lesen_liefert_Global_Admin_und_Mail_des_Kontos()
    {
        var konto = await SendenAsync(new KontoSicherstellen("Gerd.Betreiber@example.org"));
        await SendenAsync(new GlobalAdminsAbgleichen(["gerd.betreiber@example.org"]));

        await using var scope = _factory.Services.CreateAsyncScope();
        var lesen = scope.ServiceProvider.GetRequiredService<Zeltlotse.Konten.Contracts.Konten.IKontoLesen>();
        Assert.True(await lesen.IstGlobalAdminAsync(konto.KontoId, Abbruch));
        Assert.Equal("gerd.betreiber@example.org", await lesen.MailAsync(konto.KontoId, Abbruch));
        Assert.False(await lesen.IstGlobalAdminAsync(Guid.CreateVersion7(), Abbruch));
        Assert.Null(await lesen.MailAsync(Guid.CreateVersion7(), Abbruch));
    }

    [Fact]
    public async Task Mail_der_Person_liefert_die_Mail_ihres_Kontos_ohne_Konto_null()
    {
        var konto = await SendenAsync(new KontoSicherstellen("Jonas.Huettner@example.org"));

        await using var scope = _factory.Services.CreateAsyncScope();
        var lesen = scope.ServiceProvider.GetRequiredService<Zeltlotse.Konten.Contracts.Konten.IKontoLesen>();
        Assert.Equal("jonas.huettner@example.org", await lesen.MailDerPersonAsync(konto.PersonId, Abbruch));
        Assert.Null(await lesen.MailDerPersonAsync(Guid.CreateVersion7(), Abbruch));
    }

    private Task<bool> GlobalAdminAsync(string mail) =>
        LesenAsync(db => db.Konten.Where(k => k.Mail == mail).Select(k => k.GlobalAdmin).SingleAsync(Abbruch));

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

    private async Task<long> ProtokollAsync(Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt art)
    {
        await using var verbindung = new Npgsql.NpgsqlConnection(_verbindung);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand("SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art", verbindung);
        befehl.Parameters.AddWithValue("art", (short)art);
        return (long)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }

    private Task<LoginAnfrageStatus> StatusAsync(Guid id) =>
        LesenAsync(db => db.LoginAnfragen.Where(a => a.Id == id).Select(a => a.Status).SingleAsync(Abbruch));

    private async Task<bool> PruefenAsync(Guid kontoId, Guid personId, Guid stempel)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IKontoPruefung>().SitzungGueltigAsync(kontoId, personId, stempel, Abbruch);
    }
}
