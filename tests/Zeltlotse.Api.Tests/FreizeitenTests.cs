using System.Net;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Freizeiten auf den Organisations-Routen (FR-6, AD-12, AD-30), Matrix-Zeilen „Freizeit anlegen“, „Rollen Org-Routen“ und
/// „Fremde Freizeit“: Org-Admin legt an, verwaltet und lädt ein; Org-Überblick sieht nur die Liste (<c>403</c> sonst);
/// ohne Rolle, Org-Admin einer anderen Organisation, Global Admin ohne Rolle und unbekannte Organisation → <c>404</c>.
/// </summary>
public sealed class FreizeitenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _jonas = null!;
    private HttpClient _lea = null!;
    private HttpClient _ohneRolle = null!;
    private Guid _a;
    private Guid _b;

    /// <summary>
    /// Organisation A: Andrea Org-Admin, Jonas Org-Überblick. Organisation B: Lea Org-Admin. Tobias ohne Rolle, Gerd Global
    /// Admin ohne eigene Rolle.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = OrgHelfer.Factory(_verbindung);
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        (_a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "A", Andrea);
        (_b, _lea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "B", Lea);
        await OrgHelfer.EinladenAsync(_andrea, _a, Jonas, OrgRolle.OrgUeberblick);
        _jonas = await AnnehmenAlsAsync(_factory, Jonas);
        _ohneRolle = await AngemeldeterClientAsync(_factory, Tobias);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _jonas, _lea, _ohneRolle })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Org_Admin_legt_an_die_Liste_zeigt_sie_mit_Phase_und_erlaubten_Aktionen_Intervall_30_gespeichert()
    {
        using var antwort = await PostAsync(_andrea, $"/api/organisationen/{_a}/freizeiten", new FreizeitAnlegen(" Zeltlager Sommer 2027 ", Erster, Letzter));

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["id"], Namen(json));
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal(30, await WertAsync<int>(_verbindung, "SELECT \"FreigabeIntervallTage\" FROM freizeiten.freizeit WHERE \"Id\" = @id", ("id", id)));
        Assert.Equal(_a, await WertAsync<Guid>(_verbindung, "SELECT \"OrganisationId\" FROM freizeiten.freizeit WHERE \"Id\" = @id", ("id", id)));

        using var liste = await _andrea.GetAsync($"/api/organisationen/{_a}/freizeiten", Abbruch);
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);
        var listeJson = await JsonAsync(liste);
        Assert.Equal(["erlaubt", "freizeiten"], Namen(listeJson));
        Assert.Equal([Aktionsschluessel.FreizeitenAnlegen, Aktionsschluessel.LeitungEinsetzen], listeJson.GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()));
        var zeile = Assert.Single(listeJson.GetProperty("freizeiten").EnumerateArray());
        Assert.Equal(["ersterTag", "id", "letzterTag", "name", "phase"], Namen(zeile));
        Assert.Equal((id, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", "InVorbereitung"), (
            zeile.GetProperty("id").GetGuid(),
            zeile.GetProperty("name").GetString(),
            zeile.GetProperty("ersterTag").GetString(),
            zeile.GetProperty("letzterTag").GetString(),
            zeile.GetProperty("phase").GetString()));
    }

    [Fact]
    public async Task Liste_nach_erstem_Tag_absteigend_mit_der_Phase_nach_der_Uhr()
    {
        var heute = Heute(_factory);
        var vorbei = await AnlegenAsync(_andrea, _a, "Vorbei", heute.AddDays(-20), heute.AddDays(-10));
        var laeuft = await AnlegenAsync(_andrea, _a, "Läuft", heute.AddDays(-1), heute.AddDays(1));
        var kommt = await AnlegenAsync(_andrea, _a, "Kommt", heute.AddDays(10), heute.AddDays(20));

        var zeilen = (await JsonAsync(await _andrea.GetAsync($"/api/organisationen/{_a}/freizeiten", Abbruch))).GetProperty("freizeiten").EnumerateArray().ToList();

        Assert.Equal([kommt, laeuft, vorbei], zeilen.Select(z => z.GetProperty("id").GetGuid()));
        Assert.Equal(["InVorbereitung", "Laeuft", "Nachbereitung"], zeilen.Select(z => z.GetProperty("phase").GetString()));
    }

    public static TheoryData<string, string> Ungueltig => new()
    {
        { """{"name":"","ersterTag":"2027-08-02","letzterTag":"2027-08-13"}""", "name" },
        { $$"""{"name":"{{new string('a', 101)}}","ersterTag":"2027-08-02","letzterTag":"2027-08-13"}""", "name" },
        { """{"name":"Zeltlager","letzterTag":"2027-08-13"}""", "ersterTag" },
        { """{"name":"Zeltlager","ersterTag":"2027-08-02"}""", "letzterTag" },
        { """{"name":"Zeltlager","ersterTag":"2027-08-13","letzterTag":"2027-08-12"}""", "letzterTag" },
        { """{"name":"Zeltlager","ersterTag":"1999-08-02","letzterTag":"2027-08-13"}""", "ersterTag" },
    };

    [Theory]
    [MemberData(nameof(Ungueltig))]
    public async Task Anlegen_mit_ungueltigem_Feld_ergibt_400_am_Feld(string koerper, string feld)
    {
        using var antwort = await PostAsync(_andrea, $"/api/organisationen/{_a}/freizeiten", koerper);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var fehler = (await ProblemAsync(antwort)).GetProperty("errors");
        Assert.Equal([feld], Namen(fehler));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.freizeit"));
    }

    [Fact]
    public async Task Org_Ueberblick_sieht_die_Liste_ohne_erlaubte_Aktion_und_bekommt_sonst_403()
    {
        var freizeit = await AnlegenAsync(_andrea, _a);

        using var liste = await _jonas.GetAsync($"/api/organisationen/{_a}/freizeiten", Abbruch);
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);
        var json = await JsonAsync(liste);
        Assert.Empty(json.GetProperty("erlaubt").EnumerateArray());
        Assert.Single(json.GetProperty("freizeiten").EnumerateArray());

        using var anlegen = await PostAsync(_jonas, $"/api/organisationen/{_a}/freizeiten", new FreizeitAnlegen("X", Erster, Letzter));
        using var kaputt = await PostAsync(_jonas, $"/api/organisationen/{_a}/freizeiten", "kein json");
        using var verwalten = await _jonas.GetAsync($"/api/organisationen/{_a}/freizeiten/{freizeit}", Abbruch);
        using var einladen = await PostAsync(_jonas, $"/api/organisationen/{_a}/freizeiten/{freizeit}/leitung/einladungen", new LeitungEinladen("x@example.org"));
        foreach (var antwort in new[] { anlegen, kaputt, verwalten, einladen })
        {
            Assert.Equal(HttpStatusCode.Forbidden, antwort.StatusCode);
            Assert.Equal(403, (await ProblemAsync(antwort)).GetProperty("status").GetInt32());
        }

        Assert.Empty(_factory.Mails.An("x@example.org"));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.freizeit"));
    }

    [Fact]
    public async Task Ohne_Rolle_Org_Admin_von_B_Global_Admin_ohne_Rolle_und_unbekannte_Organisation_ergeben_404()
    {
        var freizeit = await AnlegenAsync(_andrea, _a);
        var unbekannt = Guid.CreateVersion7();
        var faelle = new (HttpClient Client, Guid OrgId)[] { (_ohneRolle, _a), (_lea, _a), (_gerd, _a), (_andrea, unbekannt), (_gerd, unbekannt) };

        foreach (var (client, orgId) in faelle)
        {
            using var liste = await client.GetAsync($"/api/organisationen/{orgId}/freizeiten", Abbruch);
            using var anlegen = await PostAsync(client, $"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen("X", Erster, Letzter));
            using var verwalten = await client.GetAsync($"/api/organisationen/{orgId}/freizeiten/{freizeit}", Abbruch);
            using var einladen = await PostAsync(client, $"/api/organisationen/{orgId}/freizeiten/{freizeit}/leitung/einladungen", new LeitungEinladen("x@example.org"));
            foreach (var antwort in new[] { liste, anlegen, verwalten, einladen })
            {
                Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
                Assert.Equal(404, (await ProblemAsync(antwort)).GetProperty("status").GetInt32());
            }
        }

        Assert.Empty(_factory.Mails.An("x@example.org"));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.freizeit"));
    }

    [Fact]
    public async Task Freizeit_einer_anderen_Organisation_gibt_es_auf_der_eigenen_Route_nicht()
    {
        var vonB = await AnlegenAsync(_lea, _b, "Freizeit von B");

        using var verwalten = await _andrea.GetAsync($"/api/organisationen/{_a}/freizeiten/{vonB}", Abbruch);
        using var einladen = await PostAsync(_andrea, $"/api/organisationen/{_a}/freizeiten/{vonB}/leitung/einladungen", new LeitungEinladen("x@example.org"));
        using var liste = await _andrea.GetAsync($"/api/organisationen/{_a}/freizeiten", Abbruch);

        Assert.Equal(HttpStatusCode.NotFound, verwalten.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, einladen.StatusCode);
        Assert.Empty((await JsonAsync(liste)).GetProperty("freizeiten").EnumerateArray());
        Assert.Empty(_factory.Mails.An("x@example.org"));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung"));
    }

    [Fact]
    public async Task Verwaltung_zeigt_die_Leitung_nur_mit_Namen_und_die_offenen_Einladungen()
    {
        var freizeit = await AnlegenAsync(_andrea, _a);
        using var miriam = await LeitungAsync(_factory, _andrea, _a, freizeit, Miriam);
        await NameSetzenAsync(miriam, "Miriam", "Zeltner");
        await FreizeitHelfer.EinladenAsync(_andrea, _a, freizeit, "lea.feuerstein@example.org");
        await FreizeitHelfer.EinladenAsync(_andrea, _a, freizeit, "abgelaufen@example.org");
        await AusfuehrenAsync(_verbindung, "UPDATE freizeiten.leitungs_einladung SET \"GueltigBis\" = '2000-01-01T00:00:00Z' WHERE \"Mail\" = 'abgelaufen@example.org'");

        using var antwort = await _andrea.GetAsync($"/api/organisationen/{_a}/freizeiten/{freizeit}", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["einladungen", "ersterTag", "id", "leitung", "letzterTag", "name", "phase"], Namen(json));
        Assert.Equal(("Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", "InVorbereitung"), (
            json.GetProperty("name").GetString(), json.GetProperty("ersterTag").GetString(), json.GetProperty("letzterTag").GetString(), json.GetProperty("phase").GetString()));
        var leitung = Assert.Single(json.GetProperty("leitung").EnumerateArray());
        Assert.Equal(["nachname", "personId", "vorname"], Namen(leitung));
        Assert.Equal((await PersonIdAsync(_verbindung, Miriam), "Miriam", "Zeltner"), (
            leitung.GetProperty("personId").GetGuid(), leitung.GetProperty("vorname").GetString(), leitung.GetProperty("nachname").GetString()));
        var einladung = Assert.Single(json.GetProperty("einladungen").EnumerateArray());
        Assert.Equal(["gueltigBis", "id", "mail"], Namen(einladung));
        Assert.Equal("lea.feuerstein@example.org", einladung.GetProperty("mail").GetString());
        Assert.DoesNotContain(Miriam, json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Einladen_mit_ungueltiger_Mail_ergibt_400_ohne_Mail_und_ohne_Einladung()
    {
        var freizeit = await AnlegenAsync(_andrea, _a);

        using var antwort = await PostAsync(_andrea, $"/api/organisationen/{_a}/freizeiten/{freizeit}/leitung/einladungen", new LeitungEinladen("keine-mail"));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(["mail"], Namen((await ProblemAsync(antwort)).GetProperty("errors")));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.leitungs_einladung"));
    }

    [Fact]
    public async Task Einladen_ergibt_202_mit_Kennung_und_eine_Mail_ohne_Anrede_mit_Freizeit_Rolle_Link_und_Gueltigkeit()
    {
        var freizeit = await AnlegenAsync(_andrea, _a);

        using var antwort = await PostAsync(_andrea, $"/api/organisationen/{_a}/freizeiten/{freizeit}/leitung/einladungen", new LeitungEinladen(" Miriam.Zeltner@Example.org "));

        Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["id"], Namen(json));
        var mail = Assert.Single(_factory.Mails.An(Miriam));
        Assert.Equal(Betreff, mail.Betreff);
        Assert.Contains("„Zeltlager Sommer 2027“", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Rolle „Leitung“", mail.Text, StringComparison.Ordinal);
        Assert.Matches(@"^https://localhost/app/leitung/[A-Za-z0-9_-]{43}$", mail.Link);
        Assert.Contains("Der Link gilt 7 Tage", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Zum Annehmen mit dieser Mail-Adresse anmelden.", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(du|dich|dir|dein\w*|Sie|Ihnen|Ihr\w*)\b", mail.Text);
        Assert.Equal(json.GetProperty("id").GetGuid(), await WertAsync<Guid>(_verbindung, "SELECT \"Id\" FROM freizeiten.leitungs_einladung"));
    }
}
