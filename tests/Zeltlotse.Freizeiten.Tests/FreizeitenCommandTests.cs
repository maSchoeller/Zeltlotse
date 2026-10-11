using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Leitung;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Commands und Lese-Schnittstellen des Moduls Freizeiten gegen PostgreSQL mit künstlicher Uhr (FR-6, FR-45, AD-11, AD-17,
/// AD-27): Freizeit anlegen (Validierung, Intervall), Leitung einladen (Ablauf, Verwerfen), annehmen (einmal, Mail- und
/// Freizeit-Bindung, doppelte Leitung, Ereignis in der Outbox) und der Mandantenfilter je Entität.
/// </summary>
public sealed class FreizeitenCommandTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string JonasMail = "jonas.huettner@example.org";
    private const string MiriamMail = "miriam.zeltner@example.org";

    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();
    private static readonly Guid Andrea = Guid.CreateVersion7();
    private static readonly Guid Jonas = Guid.CreateVersion7();
    private static readonly Guid Miriam = Guid.CreateVersion7();
    private static readonly DateOnly Erster = new(2027, 8, 2);
    private static readonly DateOnly Letzter = new(2027, 8, 13);

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

    // ---------- Anlegen ----------

    [Fact]
    public async Task Anlegen_speichert_Name_getrimmt_Zeitraum_Intervall_30_und_wer_wann()
    {
        var id = await AnlegenAsync(OrgA, "  Zeltlager Sommer 2027 ");

        var freizeit = await LesenAsync(db => db.Freizeiten.SingleAsync(Abbruch), OrgA);
        Assert.Equal(id, freizeit.Id);
        Assert.Equal(OrgA, freizeit.OrganisationId);
        Assert.Equal("Zeltlager Sommer 2027", freizeit.Name);
        Assert.Equal((Erster, Letzter), (freizeit.ErsterTag, freizeit.LetzterTag));
        Assert.Equal(30, freizeit.FreigabeIntervallTage);
        Assert.Equal((Andrea, _factory.Uhr.GetUtcNow()), (freizeit.AngelegtVon, freizeit.AngelegtAm));
        Assert.Null(freizeit.AbgeschlossenAm);
        Assert.Empty(await LesenAsync(db => db.Rollen.ToListAsync(Abbruch), OrgA));
    }

    public static TheoryData<string?, string?, string?, string, string> Ungueltig => new()
    {
        { "", "2027-08-02", "2027-08-13", "name", "Name fehlt." },
        { "   ", "2027-08-02", "2027-08-13", "name", "Name fehlt." },
        { null, "2027-08-02", "2027-08-13", "name", "Name fehlt." },
        { new string('a', 101), "2027-08-02", "2027-08-13", "name", "Der Name ist zu lang (höchstens 100 Zeichen)." },
        { "Zeltlager", null, "2027-08-13", "ersterTag", "Erster Tag fehlt." },
        { "Zeltlager", "2027-08-02", null, "letzterTag", "Letzter Tag fehlt." },
        { "Zeltlager", "2027-08-13", "2027-08-12", "letzterTag", "Der letzte Tag liegt vor dem ersten Tag." },
        { "Zeltlager", "1999-12-31", "2000-01-02", "ersterTag", "Das Jahr muss zwischen 2000 und 2100 liegen." },
        { "Zeltlager", "2100-12-30", "2101-01-01", "letzterTag", "Das Jahr muss zwischen 2000 und 2100 liegen." },
    };

    [Theory]
    [MemberData(nameof(Ungueltig))]
    public async Task Anlegen_prueft_jedes_Feld(string? name, string? erster, string? letzter, string feld, string meldung)
    {
        var command = new FreizeitAnlegenCommand(OrgA, name, Tag(erster), Tag(letzter), Andrea);

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(command, OrgA));

        Assert.Equal([feld], fehler.Fehler.Keys);
        Assert.Equal([meldung], fehler.Fehler[feld]);
        Assert.Equal(0, await LesenAsync(db => db.Freizeiten.CountAsync(Abbruch), OrgA));
    }

    [Fact]
    public async Task Name_mit_100_Zeichen_und_ein_einzelner_Tag_an_den_Jahresgrenzen_gehen()
    {
        await SendenAsync(new FreizeitAnlegenCommand(OrgA, new string('a', 100), new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 1), Andrea), OrgA);
        await SendenAsync(new FreizeitAnlegenCommand(OrgA, "Ende", new DateOnly(2100, 12, 31), new DateOnly(2100, 12, 31), Andrea), OrgA);

        Assert.Equal(2, await LesenAsync(db => db.Freizeiten.CountAsync(Abbruch), OrgA));
    }

    [Fact]
    public async Task Anlegen_nur_im_Mandanten_Kontext_der_Organisation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendenAsync(new FreizeitAnlegenCommand(OrgA, "Zeltlager", Erster, Letzter, Andrea), OrgB));
    }

    // ---------- Einladen ----------

    [Fact]
    public async Task Einladen_speichert_normalisierte_Mail_nur_den_Hash_und_7_Tage_Gueltigkeit()
    {
        var freizeit = await AnlegenAsync(OrgA);

        var neu = await EinladenAsync(OrgA, freizeit, " Jonas.Huettner@Example.ORG ");

        var einladung = await LesenAsync(db => db.Einladungen.SingleAsync(Abbruch), OrgA);
        Assert.Equal(neu.EinladungId, einladung.Id);
        Assert.Equal((OrgA, freizeit, JonasMail, Andrea), (einladung.OrganisationId, einladung.FreizeitId, einladung.Mail, einladung.EingeladenVon));
        Assert.Equal(LeitungsEinladungStatus.Offen, einladung.Status);
        Assert.Equal(_factory.Uhr.GetUtcNow() + TimeSpan.FromDays(7), einladung.GueltigBis);
        Assert.Equal(Zugangslink.TokenHash("leitung-einladung", neu.Token), einladung.TokenHash);
        Assert.Equal((JonasMail, "Zeltlager"), (neu.Mail, neu.FreizeitName));
        Assert.True(Zugangslink.KannTokenSein(neu.Token));
        Assert.DoesNotContain(neu.Token, neu.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("jonas", neu.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, "Mail-Adresse fehlt.")]
    [InlineData("keine-mail", "Das ist keine gültige Mail-Adresse.")]
    [InlineData("Jonas <jonas@example.org>", "Das ist keine gültige Mail-Adresse.")]
    public async Task Einladen_prueft_die_Mail(string? mail, string meldung)
    {
        var freizeit = await AnlegenAsync(OrgA);

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new LeitungEinladenCommand(OrgA, freizeit, mail, Andrea), OrgA));

        Assert.Equal(["mail"], fehler.Fehler.Keys);
        Assert.Equal([meldung], fehler.Fehler["mail"]);
    }

    [Fact]
    public async Task Neue_Einladung_verwirft_aeltere_offene_derselben_Mail_fuer_dieselbe_Freizeit_aber_nicht_fuer_andere()
    {
        var x = await AnlegenAsync(OrgA, "X");
        var y = await AnlegenAsync(OrgA, "Y");
        var alt = await EinladenAsync(OrgA, x, JonasMail);
        var andereFreizeit = await EinladenAsync(OrgA, y, JonasMail);
        var andereMail = await EinladenAsync(OrgA, x, MiriamMail);

        var neu = await EinladenAsync(OrgA, x, JonasMail);

        Assert.Equal(LeitungsEinladungStatus.Verworfen, await StatusAsync(alt));
        Assert.Equal(LeitungsEinladungStatus.Offen, await StatusAsync(andereFreizeit));
        Assert.Equal(LeitungsEinladungStatus.Offen, await StatusAsync(andereMail));
        Assert.Equal(LeitungsEinladungStatus.Offen, await StatusAsync(neu));
        Assert.Null(await AnnehmenAsync(alt.Token, JonasMail, Jonas));
    }

    // ---------- Annehmen ----------

    [Fact]
    public async Task Annehmen_setzt_die_Leitung_den_Status_und_legt_das_Ereignis_in_die_Outbox()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var neu = await EinladenAsync(OrgA, freizeit, JonasMail);

        var angenommen = await AnnehmenAsync(neu.Token, "JONAS.huettner@example.org", Jonas);

        Assert.Equal(new AngenommeneLeitung(neu.EinladungId, freizeit, OrgA), angenommen);
        var rolle = await LesenAsync(db => db.Rollen.SingleAsync(Abbruch), OrgA);
        Assert.Equal((OrgA, freizeit, Jonas, RolleArt.Leitung, _factory.Uhr.GetUtcNow()), (rolle.OrganisationId, rolle.FreizeitId, rolle.PersonId, rolle.Rolle, rolle.VergebenAm));
        Assert.Equal(LeitungsEinladungStatus.Angenommen, await StatusAsync(neu));
        Assert.Equal(1L, await OutboxAsync(freizeit));
        Assert.Equal(1L, await WertAsync<long>("SELECT \"Folgenummer\" FROM freizeiten.outbox"));
        Assert.Equal(OrgA, await WertAsync<Guid>("SELECT \"OrganisationId\" FROM freizeiten.outbox"));
        Assert.Contains(Jonas.ToString(), await WertAsync<string>("SELECT \"Inhalt\"::text FROM freizeiten.outbox"), StringComparison.Ordinal);
    }

    public static TheoryData<string> Ungueltige => ["zweites Annehmen", "abgelaufen", "andere Mail", "ohne Mail", "unbekannt", "Formfehler", "kein Token"];

    [Theory]
    [MemberData(nameof(Ungueltige))]
    public async Task Ungueltiges_Annehmen_aendert_nichts_und_steht_im_Protokoll(string fall)
    {
        var freizeit = await AnlegenAsync(OrgA);
        var neu = await EinladenAsync(OrgA, freizeit, JonasMail);
        var token = neu.Token;
        string? mail = JonasMail;
        var rollen = 0;
        var status = LeitungsEinladungStatus.Offen;
        switch (fall)
        {
            case "zweites Annehmen":
                Assert.NotNull(await AnnehmenAsync(token, mail, Jonas));
                (rollen, status) = (1, LeitungsEinladungStatus.Angenommen);
                break;
            case "abgelaufen":
                _factory.Uhr.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
                break;
            case "andere Mail":
                mail = MiriamMail;
                break;
            case "ohne Mail":
                mail = null;
                break;
            case "unbekannt":
                token = Zugangslink.NeuesToken();
                break;
            case "Formfehler":
                token = token[..20] + "!";
                break;
            case "kein Token":
                token = null!;
                break;
        }

        var vorher = await ProtokollAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);
        Assert.Null(await AnnehmenAsync(token, mail, Jonas));

        Assert.Equal(vorher + 1, await ProtokollAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(rollen, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), OrgA));
        Assert.Equal(status, await StatusAsync(neu));
        Assert.Equal(rollen, await OutboxAsync(freizeit));
    }

    [Fact]
    public async Task Ein_Token_mit_dem_Hash_eines_anderen_Zwecks_passt_nicht()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var neu = await EinladenAsync(OrgA, freizeit, JonasMail);
        await AusfuehrenAsync("UPDATE freizeiten.leitungs_einladung SET \"TokenHash\" = @hash", ("hash", Zugangslink.TokenHash("org-einladung", neu.Token)));

        Assert.Null(await AnnehmenAsync(neu.Token, JonasMail, Jonas));

        Assert.Equal(0, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), OrgA));
    }

    [Fact]
    public async Task Einladung_fuer_X_macht_nicht_zur_Leitung_von_Y()
    {
        var x = await AnlegenAsync(OrgA, "X");
        var y = await AnlegenAsync(OrgA, "Y");
        var neu = await EinladenAsync(OrgA, x, JonasMail);

        Assert.NotNull(await AnnehmenAsync(neu.Token, JonasMail, Jonas));

        Assert.Equal([RolleArt.Leitung], await RollenAsync(x, Jonas, OrgA));
        Assert.Empty(await RollenAsync(y, Jonas, OrgA));
    }

    [Fact]
    public async Task Zweite_Einladung_derselben_Person_ergibt_eine_Rolle_und_ein_Ereignis()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var erste = await EinladenAsync(OrgA, freizeit, JonasMail);
        Assert.NotNull(await AnnehmenAsync(erste.Token, JonasMail, Jonas));
        var zweite = await EinladenAsync(OrgA, freizeit, JonasMail);

        Assert.NotNull(await AnnehmenAsync(zweite.Token, JonasMail, Jonas));

        Assert.Equal(1, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), OrgA));
        Assert.Equal(1L, await OutboxAsync(freizeit));
        Assert.Equal(LeitungsEinladungStatus.Angenommen, await StatusAsync(zweite));
    }

    [Fact]
    public async Task Zwei_Personen_werden_Leitung_mit_fortlaufender_Folgenummer()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var jonas = await EinladenAsync(OrgA, freizeit, JonasMail);
        var miriam = await EinladenAsync(OrgA, freizeit, MiriamMail);

        Assert.NotNull(await AnnehmenAsync(jonas.Token, JonasMail, Jonas));
        Assert.NotNull(await AnnehmenAsync(miriam.Token, MiriamMail, Miriam));

        Assert.Equal(2, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), OrgA));
        Assert.Equal("1,2", await WertAsync<string>("SELECT string_agg(\"Folgenummer\"::text, ',' ORDER BY \"Folgenummer\") FROM freizeiten.outbox"));
    }

    [Fact]
    public async Task Gleichzeitiges_Annehmen_derselben_Einladung_gelingt_genau_einmal()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var neu = await EinladenAsync(OrgA, freizeit, JonasMail);

        var ergebnisse = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => AnnehmenAsync(neu.Token, JonasMail, Jonas))));

        Assert.Single(ergebnisse, e => e is not null);
        Assert.Equal(1, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), OrgA));
        Assert.Equal(1L, await OutboxAsync(freizeit));
    }

    [Fact]
    public async Task Gleichzeitiges_Annehmen_zweier_Personen_fuer_dieselbe_Freizeit_ergibt_zwei_Rollen_mit_Folgenummern_1_und_2()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var jonas = await EinladenAsync(OrgA, freizeit, JonasMail);
        var miriam = await EinladenAsync(OrgA, freizeit, MiriamMail);

        var ergebnisse = await Task.WhenAll(
            Task.Run(() => AnnehmenAsync(jonas.Token, JonasMail, Jonas)),
            Task.Run(() => AnnehmenAsync(miriam.Token, MiriamMail, Miriam)));

        Assert.All(ergebnisse, e => Assert.NotNull(e));
        Assert.Equal(2, await LesenAsync(db => db.Rollen.CountAsync(Abbruch), OrgA));
        Assert.Equal("1,2", await WertAsync<string>("SELECT string_agg(\"Folgenummer\"::text, ',' ORDER BY \"Folgenummer\") FROM freizeiten.outbox"));
    }

    // ---------- Lese-Schnittstellen ----------

    [Fact]
    public async Task Zeitpunkte_und_Rollen_lesen_unter_dem_Mandantenfilter_die_Organisation_ohne()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var neu = await EinladenAsync(OrgA, freizeit, JonasMail);
        Assert.NotNull(await AnnehmenAsync(neu.Token, JonasMail, Jonas));
        _factory.Uhr.SetUtcNow(new DateTimeOffset(2027, 8, 13, 21, 59, 0, TimeSpan.Zero));

        await using var scope = _factory.Services.CreateAsyncScope();
        var mandant = scope.ServiceProvider.GetRequiredService<MandantenKontext>();
        var zeitpunkte = scope.ServiceProvider.GetRequiredService<IFreizeitZeitpunkte>();
        var rollen = scope.ServiceProvider.GetRequiredService<IFreizeitRollenLesen>();

        Assert.Equal(OrgA, await rollen.OrganisationAsync(freizeit, Abbruch));
        Assert.Null(await rollen.OrganisationAsync(Guid.CreateVersion7(), Abbruch));
        Assert.Empty(await zeitpunkte.ZeitpunkteAsync([freizeit], Abbruch));

        mandant.Setzen(OrgA);
        var ergebnis = Assert.Single(await zeitpunkte.ZeitpunkteAsync([freizeit, Guid.CreateVersion7()], Abbruch));
        Assert.Equal(freizeit, ergebnis.Key);
        Assert.Equal(
            new FreizeitZeitpunkte(FreizeitPhase.Laeuft, new DateTimeOffset(2027, 8, 13, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 9, 12, 22, 0, 0, TimeSpan.Zero)),
            ergebnis.Value);
        Assert.Equal([RolleArt.Leitung], await rollen.RollenAsync(freizeit, Jonas, Abbruch));
        Assert.Empty(await rollen.RollenAsync(freizeit, Andrea, Abbruch));

        mandant.Setzen(OrgB);
        Assert.Empty(await rollen.RollenAsync(freizeit, Jonas, Abbruch));
    }

    // ---------- Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_Freizeit_Rolle_und_Einladung_nur_im_eigenen_Kontext()
    {
        var freizeit = await AnlegenAsync(OrgA);
        var neu = await EinladenAsync(OrgA, freizeit, JonasMail);
        Assert.NotNull(await AnnehmenAsync(neu.Token, JonasMail, Jonas));
        await EinladenAsync(OrgA, freizeit, MiriamMail);

        Assert.Single(await LesenAsync(db => db.Freizeiten.ToListAsync(Abbruch), OrgA));
        Assert.Single(await LesenAsync(db => db.Rollen.ToListAsync(Abbruch), OrgA));
        Assert.Equal(2, (await LesenAsync(db => db.Einladungen.ToListAsync(Abbruch), OrgA)).Count);

        foreach (var kontext in new Guid?[] { OrgB, null })
        {
            Assert.Empty(await LesenAsync(db => db.Freizeiten.ToListAsync(Abbruch), kontext));
            Assert.Empty(await LesenAsync(db => db.Rollen.ToListAsync(Abbruch), kontext));
            Assert.Empty(await LesenAsync(db => db.Einladungen.ToListAsync(Abbruch), kontext));
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgB);
        var schreiben = scope.ServiceProvider.GetRequiredService<FreizeitenDbContext>();
        Assert.Empty(await schreiben.Freizeiten.ToListAsync(Abbruch));
        Assert.Empty(await schreiben.Rollen.ToListAsync(Abbruch));
        Assert.Empty(await schreiben.Einladungen.ToListAsync(Abbruch));
    }

    // ---------- Hilfen ----------

    private static DateOnly? Tag(string? text) => text is null ? null : DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private Task<Guid> AnlegenAsync(Guid organisation, string name = "Zeltlager") =>
        SendenAsync(new FreizeitAnlegenCommand(organisation, name, Erster, Letzter, Andrea), organisation);

    private Task<NeueLeitungsEinladung> EinladenAsync(Guid organisation, Guid freizeit, string mail) =>
        SendenAsync(new LeitungEinladenCommand(organisation, freizeit, mail, Andrea), organisation);

    private Task<AngenommeneLeitung?> AnnehmenAsync(string? token, string? mail, Guid person) =>
        SendenAsync(new LeitungAnnehmenCommand(token, mail, person));

    private async Task<IReadOnlyCollection<RolleArt>> RollenAsync(Guid freizeit, Guid person, Guid organisation)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await scope.ServiceProvider.GetRequiredService<IFreizeitRollenLesen>().RollenAsync(freizeit, person, Abbruch);
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<FreizeitenLeseDbContext, Task<T>> abfrage, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<FreizeitenLeseDbContext>());
    }

    private Task<LeitungsEinladungStatus> StatusAsync(NeueLeitungsEinladung einladung) =>
        LesenAsync(db => db.Einladungen.Where(e => e.Id == einladung.EinladungId).Select(e => e.Status).SingleAsync(Abbruch), einladung.OrganisationId);

    private Task<long> OutboxAsync(Guid freizeit) =>
        WertAsync<long>(
            "SELECT count(*) FROM freizeiten.outbox WHERE \"DatensatzId\" = @id AND \"Typ\" = @typ",
            ("id", freizeit),
            ("typ", typeof(Contracts.Events.FreizeitRolleGeaendert).FullName!));

    private Task<long> ProtokollAsync(SicherheitsEreignisArt art) =>
        WertAsync<long>("SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art", ("art", (short)art));

    private async Task<T> WertAsync<T>(string sql, params (string Name, object Wert)[] parameter)
    {
        await using var verbindung = new Npgsql.NpgsqlConnection(_verbindung);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(sql, verbindung);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        return (T)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }

    private async Task AusfuehrenAsync(string sql, params (string Name, object Wert)[] parameter)
    {
        await using var verbindung = new Npgsql.NpgsqlConnection(_verbindung);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(sql, verbindung);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        await befehl.ExecuteNonQueryAsync(Abbruch);
    }
}
