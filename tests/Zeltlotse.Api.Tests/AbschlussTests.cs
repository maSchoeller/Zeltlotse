using System.Net;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-8, Freizeit abschließen (CAP-27, AD-17, AD-28, AD-30) gegen echtes PostgreSQL: Abschluss durch die Leitung in der Nachbereitung mit
/// Durchlauf <c>FreizeitAbgeschlossen</c> → Vorfall-Notiz im Archiv, zu früh, doppelt, die Rechte-Matrix und der Schreibschutz der Pipeline
/// für Benutzer-Commands der gelaufenen Freizeit (Anmeldung entscheiden, Leitung einladen, Anmeldung über den Anmeldelink) samt Ausnahme
/// Schäden und Verluste. Freizeit X „Zeltlager Kapf 2027“ (02.–13.08.2027, Organisation A, Org-Admin Andrea, Leitung Miriam, Team Tobias),
/// Freizeit Z gehört Organisation B (Org-Admin Lea, Leitung Miriam).
/// </summary>
public sealed class AbschlussTests(PostgresContainer postgres) : IAsyncLifetime
{
    /// <summary>Eine Woche nach dem letzten Tag von X (13.08.2027): Nachbereitung.</summary>
    private static readonly DateTimeOffset NachbereitungX = new(2027, 8, 20, 8, 0, 0, TimeSpan.Zero);

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/abschliessen";

    private Task<HttpResponseMessage> AbschliessenAsync(HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, Pfad(freizeit ?? _s.X));

    private async Task<DateTimeOffset?> AbgeschlossenAmAsync(Guid? freizeit = null) =>
        await WertAsync<object>(_s.Verbindung, "SELECT \"AbgeschlossenAm\" FROM freizeiten.freizeit WHERE \"Id\" = @f", ("f", freizeit ?? _s.X)) is DateTime zeit
            ? new DateTimeOffset(DateTime.SpecifyKind(zeit, DateTimeKind.Utc))
            : null;

    private Task GelaufenPerSqlAsync() =>
        AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

    /// <summary>Stellt die Uhr auf einen Zeitpunkt und meldet die genannten Konten neu an (die Sitzungen laufen mit der Uhr ab).</summary>
    private async Task UhrAsync(DateTimeOffset zeitpunkt, params (HttpClient Client, string Mail)[] konten)
    {
        _s.Factory.Uhr.SetUtcNow(zeitpunkt);
        foreach (var (client, mail) in konten)
        {
            await AnmeldenAsync(_s.Factory, client, mail);
        }
    }

    private static async Task<string?> PhaseAsync(HttpClient client, Guid freizeit)
    {
        using var antwort = await client.GetAsync($"/api/freizeiten/{freizeit}/kontext", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("phase").GetString();
    }

    private static async Task AbgeschlossenProblemAsync(HttpResponseMessage antwort)
    {
        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(problem));
        Assert.Equal("Diese Freizeit ist abgeschlossen und nur lesbar.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Leitung_schliesst_in_der_Nachbereitung_ab_das_Ereignis_archiviert_die_Vorfall_Notiz_in_der_Organisation()
    {
        var vorfall = Guid.CreateVersion7();
        using (var eintrag = await PostAsync(
                   _s.Tobias, $"/api/freizeiten/{_s.X}/sicherheit/vorfaelle", new VorfallEintragNeu(vorfall, _s.TobiasId, VorfallArt.Geschehen, "Beim Abbau am Finger geklemmt.")))
        {
            Assert.Equal(HttpStatusCode.Created, eintrag.StatusCode);
        }

        await UhrAsync(NachbereitungX, (_s.Miriam, FreizeitHelfer.Miriam), (_s.Tobias, FreizeitHelfer.Tobias), (_s.Andrea, OrgHelfer.Andrea));
        Assert.Equal("Nachbereitung", await PhaseAsync(_s.Tobias, _s.X));

        using var antwort = await AbschliessenAsync();

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Equal(NachbereitungX, await AbgeschlossenAmAsync());
        Assert.Equal("Gelaufen", await PhaseAsync(_s.Miriam, _s.X));
        Assert.Equal("Gelaufen", await PhaseAsync(_s.Tobias, _s.X));
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitAbgeschlossen' AND \"Inhalt\"->>'freizeitId' = @f", ("f", _s.X.ToString())));

        await _s.ZustellenAsync();

        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.vorfall_eintrag WHERE \"Id\" = @v AND \"ArchiviertAm\" IS NOT NULL", ("v", vorfall)));
        using var archiv = await _s.Andrea.GetAsync($"/api/organisationen/{_s.A}/sicherheit/vorfall-archiv", Abbruch);
        Assert.Equal(HttpStatusCode.OK, archiv.StatusCode);
        var freizeit = Assert.Single((await JsonAsync(archiv)).EnumerateArray());
        Assert.Equal((_s.X, FreizeitpassSzenario.Kapf), (freizeit.GetProperty("freizeitId").GetGuid(), freizeit.GetProperty("name").GetString()));
        var notiz = Assert.Single(freizeit.GetProperty("eintraege").EnumerateArray());
        Assert.Equal((vorfall, "Beim Abbau am Finger geklemmt."), (notiz.GetProperty("id").GetGuid(), notiz.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Vor_dem_Freizeitende_400_am_Feld_phase_und_nichts_gespeichert()
    {
        // In Vorbereitung (Uhr vor dem 02.08.2027).
        using var vorbereitung = await AbschliessenAsync();
        await UhrAsync(new DateTimeOffset(2027, 8, 13, 20, 0, 0, TimeSpan.Zero), (_s.Miriam, FreizeitHelfer.Miriam));
        Assert.Equal("Laeuft", await PhaseAsync(_s.Miriam, _s.X));
        using var laeuft = await AbschliessenAsync();

        foreach (var antwort in new[] { vorbereitung, laeuft })
        {
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            var problem = await ProblemAsync(antwort);
            Assert.Equal(Problemcodes.Validierung, Code(problem));
            Assert.Equal("Abschließen geht erst nach dem letzten Freizeittag.", problem.GetProperty("errors").GetProperty("phase")[0].GetString());
        }

        Assert.Null(await AbgeschlossenAmAsync());
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitAbgeschlossen'"));
    }

    [Fact]
    public async Task Ein_zweiter_Abschluss_409_freizeit_abgeschlossen_und_der_Zeitpunkt_bleibt()
    {
        await UhrAsync(NachbereitungX, (_s.Miriam, FreizeitHelfer.Miriam));
        using (var erster = await AbschliessenAsync())
        {
            Assert.Equal(HttpStatusCode.NoContent, erster.StatusCode);
        }

        _s.Factory.Uhr.Advance(TimeSpan.FromDays(1));
        using var zweiter = await AbschliessenAsync();

        await AbgeschlossenProblemAsync(zweiter);
        Assert.Equal(NachbereitungX, await AbgeschlossenAmAsync());
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitAbgeschlossen'"));
    }

    [Fact]
    public async Task Rechte_Team_403_Org_Rollen_fremde_Organisation_und_unbekannte_Freizeit_404_ohne_Sitzung_401()
    {
        const string ueberblickMail = "vorstand@example.org";
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, ueberblickMail, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, ueberblickMail);
        await UhrAsync(
            NachbereitungX, (_s.Tobias, FreizeitHelfer.Tobias), (_s.Andrea, OrgHelfer.Andrea), (_s.Lea, OrgHelfer.Lea), (_s.Miriam, FreizeitHelfer.Miriam),
            (ueberblick, ueberblickMail));

        using var team = await AbschliessenAsync(_s.Tobias);
        using var orgAdmin = await AbschliessenAsync(_s.Andrea);
        using var orgUeberblick = await AbschliessenAsync(ueberblick);
        using var fremd = await AbschliessenAsync(_s.Lea);
        using var unbekannt = await AbschliessenAsync(_s.Miriam, Guid.CreateVersion7());
        using var anonym = await _s.Anonym.PostAsync(Pfad(_s.X), content: null, Abbruch);

        Assert.Equal(
            [HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized],
            [team.StatusCode, orgAdmin.StatusCode, orgUeberblick.StatusCode, fremd.StatusCode, unbekannt.StatusCode, anonym.StatusCode]);
        Assert.Null(await AbgeschlossenAmAsync());
    }

    [Fact]
    public async Task Schreibschutz_Anmeldung_entscheiden_und_Leitung_einladen_409_nach_dem_Abschluss()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        await GelaufenPerSqlAsync();

        using var bestaetigen = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/anmeldungen/{emma}/bestaetigen");
        using var einladen = await PostAsync(_s.Andrea, $"/api/organisationen/{_s.A}/freizeiten/{_s.X}/leitung/einladungen", new LeitungEinladen("neu@example.org"));

        await AbgeschlossenProblemAsync(bestaetigen);
        await AbgeschlossenProblemAsync(einladen);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @a AND \"Status\" = 1", ("a", emma)));
        Assert.Empty(_s.Factory.Mails.An("neu@example.org"));
    }

    [Fact]
    public async Task Schreibschutz_eine_offene_Leitungs_Einladung_laesst_sich_nach_dem_Abschluss_nicht_annehmen()
    {
        const string neu = "neu@example.org";
        await FreizeitHelfer.EinladenAsync(_s.Andrea, _s.A, _s.X, neu);
        using var client = await AngemeldeterClientAsync(_s.Factory, neu);
        await GelaufenPerSqlAsync();
        var rollenVorher = await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"FreizeitId\" = @f", ("f", _s.X));

        using var annehmen = await FreizeitHelfer.AnnehmenAsync(client, FreizeitHelfer.LeitungsToken(_s.Factory, neu));

        await AbgeschlossenProblemAsync(annehmen);
        Assert.Equal(rollenVorher, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung WHERE \"Status\" = 2 AND \"FreizeitId\" = @f AND \"Mail\" = @m", ("f", _s.X), ("m", neu)));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitRolleGeaendert' AND \"Inhalt\"->>'personId' = @p", ("p", (await PersonIdAsync(_s.Verbindung, neu)).ToString())));
    }

    [Fact]
    public async Task Schreibschutz_Anmeldung_ueber_den_Anmeldelink_409_mit_Konto_und_als_Gast()
    {
        await GelaufenPerSqlAsync();

        using var mitKonto = await PostAsync(_s.Katrin, "/api/ich/anmeldungen", new AnmeldungAbsenden(_s.TeilnehmendeX, _s.Mia, true));
        using var gast = await PostAsync(_s.Anonym, "/api/personen/oeffentlich/gaeste", new GastAnlegen("Sabine", "Beispiel", "sabine@example.org", "Ben", "Beispiel", new DateOnly(2016, 4, 1)));
        Assert.Equal(HttpStatusCode.Created, gast.StatusCode);
        var json = await JsonAsync(gast);
        using var alsGast = await PostAsync(
            _s.Anonym, $"/api/freizeiten/oeffentlich/{_s.TeilnehmendeX}/anmeldungen",
            new GastAnmeldungAbsenden(json.GetProperty("gastToken").GetString(), json.GetProperty("kindPersonId").GetGuid(), true));

        await AbgeschlossenProblemAsync(mitKonto);
        await AbgeschlossenProblemAsync(alsGast);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"FreizeitId\" = @f", ("f", _s.X)));
    }

    [Fact]
    public async Task Ausnahme_Schaden_eintragen_bleibt_nach_dem_Abschluss_erlaubt()
    {
        await UhrAsync(NachbereitungX, (_s.Miriam, FreizeitHelfer.Miriam), (_s.Tobias, FreizeitHelfer.Tobias));
        using (var abschluss = await AbschliessenAsync())
        {
            Assert.Equal(HttpStatusCode.NoContent, abschluss.StatusCode);
        }

        using var schaden = await PostAsync(_s.Tobias, $"/api/freizeiten/{_s.X}/inventar/schaeden", new SchadenNeu(SchadenArt.Verlust, "Taschenlampe"));
        using var vorfall = await PostAsync(
            _s.Tobias, $"/api/freizeiten/{_s.X}/sicherheit/vorfaelle", new VorfallEintragNeu(Guid.CreateVersion7(), _s.TobiasId, VorfallArt.Geschehen, "Nachtrag"));

        Assert.Equal(HttpStatusCode.Created, schaden.StatusCode);
        await AbgeschlossenProblemAsync(vorfall);
    }
}
