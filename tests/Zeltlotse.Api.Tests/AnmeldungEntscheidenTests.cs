using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Api.Datenschutz;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using FamilienHelfer = Zeltlotse.Api.Tests.FamilienHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-3, Anmeldungen bestätigen und absagen (FR-7, FR-20, FR-21, FR-67, AD-8, AD-13, AD-28, AD-30) gegen echtes PostgreSQL: die
/// Liste der Leitung mit Rechte-Matrix, Bestätigen und Absagen mit Zustandsregeln, Mails über die Outbox an Konto, Elternteil oder
/// Gast, Freigabe Stufe Team und Beenden, Team-Rolle und Mitgliedschaft für Mitarbeitende, „ausgebucht“, der Kontakt für Absagen in
/// Einstellungen, eigenen Anmeldungen und Gast-Link sowie die Auskunft von Freizeiten und Organisationen.
/// </summary>
public sealed class AnmeldungEntscheidenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Lina = "lina.eltern@example.org";
    private const string Kapf = "Zeltlager Kapf 2027";
    private const string KontaktMail = "zeltlager@example.org";
    private const string KontaktTelefon = "0123 456789";

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _lea = null!;
    private HttpClient _miriam = null!;
    private HttpClient _katrin = null!;
    private HttpClient _jonas = null!;
    private HttpClient _tobias = null!;
    private HttpClient _anonym = null!;
    private Guid _a;
    private Guid _x;
    private Guid _z;
    private Guid _emma;
    private string _tnX = null!;
    private string _maX = null!;
    private string _tnZ = null!;

    /// <summary>
    /// Organisation A (Andrea) mit Freizeit X, Organisation B (Lea) mit Freizeit Z; Miriam ist Leitung beider. Tobias ist Team in X
    /// (per SQL). Katrin hat Emma im Familienkonto; Jonas hat ein Konto. Die Uhr steht auf der echten Zeit, vor beiden Freizeiten.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = OrgHelfer.Factory(_verbindung);
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        (_a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "CVJM Musterstadt", Andrea);
        Guid b;
        (b, _lea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "EJW Beispielhausen", Lea);
        _x = await AnlegenAsync(_andrea, _a, Kapf);
        _z = await AnlegenAsync(_lea, b, "Z", new DateOnly(2027, 9, 1), new DateOnly(2027, 9, 3));
        _miriam = await LeitungAsync(_factory, _andrea, _a, _x, Miriam);
        await FreizeitHelfer.EinladenAsync(_lea, b, _z, Miriam);
        await AnnehmenMitAsync(_factory, _miriam, Miriam);
        var links = await AnmeldelinksAsync(_x);
        (_tnX, _maX) = (TokenAus(links["Teilnehmende"]), TokenAus(links["Mitarbeitende"]));
        _tnZ = TokenAus((await AnmeldelinksAsync(_z))["Teilnehmende"]);
        _katrin = await AngemeldeterClientAsync(_factory, FamilienHelfer.Katrin);
        await NameSetzenAsync(_katrin, "Katrin", "Muster");
        _emma = await FamilienHelfer.KindAsync(_katrin, "Emma");
        _jonas = await AngemeldeterClientAsync(_factory, Jonas);
        await NameSetzenAsync(_jonas, "Jonas", "Hüttner");
        _tobias = await AngemeldeterClientAsync(_factory, Tobias);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_tobias, "/api/ich/profil")).StatusCode);
        await AusfuehrenAsync(
            _verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"OrganisationId\", \"FreizeitId\", \"PersonId\", \"Rolle\", \"VergebenAm\") VALUES (@o, @f, @p, 2, now())",
            ("o", _a), ("f", _x), ("p", await PersonIdAsync(_verbindung, Tobias)));
        _anonym = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _lea, _miriam, _katrin, _jonas, _tobias, _anonym })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Leitung_ist_berechtigt_Team_bekommt_403_ohne_Rolle_fremde_Organisation_und_unbekannte_Freizeit_404()
    {
        var emma = await EmmaAnmeldenAsync();

        foreach (var (client, freizeit, status) in new (HttpClient, Guid, HttpStatusCode)[]
                 {
                     (_tobias, _x, HttpStatusCode.Forbidden),
                     (_andrea, _x, HttpStatusCode.NotFound),
                     (_lea, _x, HttpStatusCode.NotFound),
                     (_gerd, _x, HttpStatusCode.NotFound),
                     (_miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
                 })
        {
            using var liste = await client.GetAsync(ListePfad(freizeit), Abbruch);
            using var bestaetigen = await PostAsync(client, $"{ListePfad(freizeit)}/{emma}/bestaetigen");
            using var absagen = await PostAsync(client, $"{ListePfad(freizeit)}/{emma}/absagen");
            using var kontakt = await SendenAsync(
                client, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/freizeitseite", $$"""{"version":1,"alterWert":null,"kontaktMail":"{{KontaktMail}}"}""");

            Assert.Equal([status, status, status, status], [liste.StatusCode, bestaetigen.StatusCode, absagen.StatusCode, kontakt.StatusCode]);
        }

        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Status\" = 1"));
        Assert.Equal(HttpStatusCode.OK, (await _miriam.GetAsync(ListePfad(_x), Abbruch)).StatusCode);

        // Der Kontext erlaubt die Anmeldungen nur der Leitung; die App zeigt dem Team keinen Knopf.
        var leitung = await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/kontext", Abbruch));
        var team = await JsonAsync(await _tobias.GetAsync($"/api/freizeiten/{_x}/kontext", Abbruch));
        Assert.Contains(Aktionsschluessel.AnmeldungenBearbeiten, leitung.GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(Aktionsschluessel.AnmeldungenBearbeiten, team.GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()));
    }

    // ---------- Liste ----------

    [Fact]
    public async Task Die_Liste_zeigt_alle_Anmeldungen_nach_Eingang_mit_Namen_Art_und_Status_eine_geloeschte_Person_als_Platzhalter()
    {
        var emma = await EmmaAnmeldenAsync();
        _factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var jonas = await MitarbeitAsync(_jonas);
        _factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var (gastToken, kind) = await GastAsync();
        var lina = await GastAnmeldenAsync(_tnX, gastToken, kind);
        // Eine Anmeldung in Z gehört nicht in die Liste von X.
        await AnmeldenAsync(_katrin, _tnZ, _emma);
        await AusfuehrenAsync(_verbindung, "UPDATE freizeiten.anmeldung SET \"PersonId\" = @p WHERE \"Id\" = @a", ("p", GeloeschtePerson.Id), ("a", lina));

        using var antwort = await _miriam.GetAsync(ListePfad(_x), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["anmeldungen", "bestaetigteTeilnehmende", "hoechstzahlTeilnehmende"], Namen(json));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("hoechstzahlTeilnehmende").ValueKind);
        Assert.Equal(0, json.GetProperty("bestaetigteTeilnehmende").GetInt32());
        var zeilen = json.GetProperty("anmeldungen").EnumerateArray().ToList();
        Assert.All(zeilen, z => Assert.Equal(
            ["anmeldungId", "art", "eingegangenAm", "entschiedenAm", "nachname", "personId", "status", "vorname"], Namen(z)));
        Assert.Equal([emma, jonas, lina], zeilen.Select(z => z.GetProperty("anmeldungId").GetGuid()));
        Assert.Equal(
            ["Emma Muster Teilnehmende Eingegangen", "Jonas Hüttner Mitarbeitende Eingegangen", "Gelöschte Person  Teilnehmende Eingegangen"],
            zeilen.Select(z => $"{z.GetProperty("vorname").GetString()} {z.GetProperty("nachname").GetString()} {z.GetProperty("art").GetString()} {z.GetProperty("status").GetString()}"));
        Assert.All(zeilen, z => Assert.Equal(JsonValueKind.Null, z.GetProperty("entschiedenAm").ValueKind));
    }

    // ---------- Bestätigen ----------

    [Fact]
    public async Task Bestaetigen_einer_Teilnehmenden_ergibt_Freigabe_Stufe_Team_und_genau_eine_Mail_an_den_Elternteil_ohne_Mitgliedschaft()
    {
        var anmeldung = await EmmaAnmeldenAsync();
        await KontaktSetzenAsync();
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        var mailsVorher = _factory.Mails.An(FamilienHelfer.Katrin).Count;
        var jetzt = _factory.Uhr.GetUtcNow();

        using var antwort = await BestaetigenAsync(anmeldung);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @a AND \"Status\" = 2 AND \"EntschiedenAm\" = @j", ("a", anmeldung), ("j", Mikro(jetzt))));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.AnmeldungBestaetigt'"));
        // Der Mail-Auftrag trägt nur Kennung und Entscheidung (AD-13): keine Adresse, keine Namen.
        var inhalt = await WertAsync<string>(_verbindung, "SELECT \"Inhalt\"::text FROM freizeiten.outbox WHERE \"Typ\" LIKE '%AnmeldungEntschiedenMail'");
        Assert.DoesNotContain("@", inhalt, StringComparison.Ordinal);
        Assert.DoesNotContain("Emma", inhalt, StringComparison.Ordinal);
        Assert.DoesNotContain("Katrin", inhalt, StringComparison.Ordinal);
        Assert.Equal(mailsVorher, _factory.Mails.An(FamilienHelfer.Katrin).Count);

        await _factory.OutboxJetztZustellenAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"AnmeldungId\" = @a AND \"Stufe\" = 2", ("a", anmeldung)));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM organisationen.mitgliedschaft"));
        var mails = _factory.Mails.An(FamilienHelfer.Katrin);
        Assert.Equal(mailsVorher + 1, mails.Count);
        Assert.Equal($"Anmeldung bestätigt: {Kapf}", mails[^1].Betreff);
        Assert.Contains($"Emma ist bei {Kapf} dabei.", mails[^1].Text, StringComparison.Ordinal);
        Assert.Contains($"Eine Absage geht nur über die Leitung: {KontaktMail} · {KontaktTelefon}.", mails[^1].Text, StringComparison.Ordinal);

        // Die Familienübersicht liest Status und Kontakt der Leitung.
        var zeile = Assert.Single(await MeineAnmeldungenAsync(_katrin));
        Assert.Equal(["anmeldungId", "art", "ersterTag", "freizeitName", "kontaktMail", "kontaktTelefon", "letzterTag", "personId", "status"], Namen(zeile));
        Assert.Equal(("Bestaetigt", KontaktMail, KontaktTelefon), (
            zeile.GetProperty("status").GetString(), zeile.GetProperty("kontaktMail").GetString(), zeile.GetProperty("kontaktTelefon").GetString()));
        var liste = await JsonAsync(await _miriam.GetAsync(ListePfad(_x), Abbruch));
        Assert.Equal(1, liste.GetProperty("bestaetigteTeilnehmende").GetInt32());
        Assert.Equal(Mikro(jetzt), liste.GetProperty("anmeldungen")[0].GetProperty("entschiedenAm").GetDateTimeOffset());
    }

    [Fact]
    public async Task Bestaetigen_und_Absagen_von_Mitarbeitenden_vergibt_und_entzieht_die_Team_Rolle_die_Mitgliedschaft_bleibt()
    {
        var anmeldung = await MitarbeitAsync(_jonas);
        var jonas = await PersonIdAsync(_verbindung, Jonas);
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(HttpStatusCode.NotFound, (await _jonas.GetAsync($"/api/freizeiten/{_x}", Abbruch)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(anmeldung)).StatusCode);

        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"Rolle\" = 2", ("f", _x), ("p", jonas)));
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM personen.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"Rolle\" = 2", ("f", _x), ("p", jonas)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.nachweis_einsicht WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM organisationen.mitgliedschaft WHERE \"OrganisationId\" = @o AND \"PersonId\" = @p", ("o", _a), ("p", jonas)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"AnmeldungId\" = @a AND \"Stufe\" = 2", ("a", anmeldung)));
        var mail = _factory.Mails.LetzteAn(Jonas);
        Assert.Equal($"Anmeldung bestätigt: {Kapf}", mail.Betreff);
        Assert.Contains($"Jonas arbeitet bei {Kapf} mit.", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Eine Absage geht nur über die Leitung der Freizeit.", mail.Text, StringComparison.Ordinal);
        var meine = await _jonas.GetFromJsonAsync<List<MeineFreizeit>>("/api/ich/freizeiten", Abbruch);
        Assert.Equal((_x, FreizeitRolle.Team), (Assert.Single(meine!).FreizeitId, Assert.Single(meine![0].Rollen)));
        Assert.Equal(HttpStatusCode.OK, (await _jonas.GetAsync($"/api/freizeiten/{_x}", Abbruch)).StatusCode);
        // Als Team sieht Jonas die Anmeldungen nicht.
        Assert.Equal(HttpStatusCode.Forbidden, (await _jonas.GetAsync(ListePfad(_x), Abbruch)).StatusCode);

        var zustellung = await ZustellzeitAsync();
        using var absage = await AbsagenAsync(anmeldung);

        Assert.Equal(HttpStatusCode.NoContent, absage.StatusCode);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitRolleGeaendert' AND \"Inhalt\"->>'rolle' = '3'"));
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeit_rolle WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"AnmeldungId\" = @a AND \"GueltigBis\" = @z", ("a", anmeldung), ("z", zustellung)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM organisationen.mitgliedschaft WHERE \"PersonId\" = @p", ("p", jonas)));
        Assert.Empty((await _jonas.GetFromJsonAsync<List<MeineFreizeit>>("/api/ich/freizeiten", Abbruch))!);
        Assert.Equal(HttpStatusCode.NotFound, (await _jonas.GetAsync($"/api/freizeiten/{_x}", Abbruch)).StatusCode);
        Assert.Equal($"Anmeldung abgesagt: {Kapf}", _factory.Mails.LetzteAn(Jonas).Betreff);

        // Auskunft: Freizeiten nennt Status und „entschieden am“, Organisationen die Mitgliedschaft.
        var auskunft = (await _jonas.GetFromJsonAsync<Zeltlotse.Api.Contracts.Betrieb.DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var anmeldungen = Assert.Single(auskunft, a => a.Titel == "Anmeldungen");
        Assert.Contains(anmeldungen.Zeilen, z => z.Feld == Kapf && z.Wert.Contains("Mitarbeitende", StringComparison.Ordinal)
                                                 && z.Wert.Contains("Status Abgesagt, entschieden am", StringComparison.Ordinal));
        var mitgliedschaften = Assert.Single(auskunft, a => a.Titel == "Mitgliedschaften");
        Assert.Contains(mitgliedschaften.Zeilen, z => z.Wert.StartsWith("Mitarbeitende von CVJM Musterstadt seit ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mitarbeit_einer_Leitung_bestaetigen_legt_die_Mitgliedschaft_an_die_Rolle_bleibt_Leitung()
    {
        var anmeldung = await MitarbeitAsync(_miriam);
        var miriam = await PersonIdAsync(_verbindung, Miriam);
        var rollenVorher = await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitRolleGeaendert'");

        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(anmeldung)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(rollenVorher, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitRolleGeaendert'"));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"Rolle\" = 1", ("f", _x), ("p", miriam)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM organisationen.mitgliedschaft WHERE \"PersonId\" = @p AND \"OrganisationId\" = @o", ("p", miriam), ("o", _a)));

        // Auch eine Absage lässt die Leitung unberührt.
        Assert.Equal(HttpStatusCode.NoContent, (await AbsagenAsync(anmeldung)).StatusCode);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"Rolle\" = 1", ("f", _x), ("p", miriam)));
        Assert.Equal(rollenVorher, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.FreizeitRolleGeaendert'"));
    }

    // ---------- Absagen und Übergänge ----------

    [Fact]
    public async Task Absage_einer_eingegangenen_Anmeldung_beendet_die_Freigabe_bei_der_Zustellung_die_Mail_nennt_den_Kontakt()
    {
        var anmeldung = await EmmaAnmeldenAsync();
        await KontaktSetzenAsync();
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        var mailsVorher = _factory.Mails.An(FamilienHelfer.Katrin).Count;

        using var antwort = await AbsagenAsync(anmeldung);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @a AND \"Status\" = 3 AND \"EntschiedenAm\" IS NOT NULL", ("a", anmeldung)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.AnmeldungAbgesagt'"));
        var zustellung = await ZustellzeitAsync();
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"AnmeldungId\" = @a AND \"Stufe\" = 1 AND \"GueltigBis\" = @z", ("a", anmeldung), ("z", zustellung)));
        var mails = _factory.Mails.An(FamilienHelfer.Katrin);
        Assert.Equal(mailsVorher + 1, mails.Count);
        Assert.Equal($"Anmeldung abgesagt: {Kapf}", mails[^1].Betreff);
        Assert.Contains("Emma", mails[^1].Text, StringComparison.Ordinal);
        Assert.Contains($"{KontaktMail} · {KontaktTelefon}", mails[^1].Text, StringComparison.Ordinal);
        Assert.Equal("Abgesagt", Assert.Single(await MeineAnmeldungenAsync(_katrin)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ungueltige_Uebergaenge_ergeben_409_konflikt_ohne_Aenderung_Ereignis_oder_Mail()
    {
        var bestaetigt = await EmmaAnmeldenAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(bestaetigt)).StatusCode);
        var abgesagt = await MitarbeitAsync(_jonas);
        Assert.Equal(HttpStatusCode.NoContent, (await AbsagenAsync(abgesagt)).StatusCode);
        var outboxVorher = await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox");

        foreach (var (pfad, anmeldung) in new[] { ("bestaetigen", bestaetigt), ("bestaetigen", abgesagt), ("absagen", abgesagt) })
        {
            using var antwort = await PostAsync(_miriam, $"{ListePfad(_x)}/{anmeldung}/{pfad}");
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        }

        Assert.Equal(outboxVorher, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox"));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @a AND \"Status\" = 2", ("a", bestaetigt)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @a AND \"Status\" = 3", ("a", abgesagt)));

        // Eine bestätigte Anmeldung lässt sich absagen; danach ist Schluss.
        Assert.Equal(HttpStatusCode.NoContent, (await AbsagenAsync(bestaetigt)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await BestaetigenAsync(bestaetigt)).StatusCode);
    }

    [Fact]
    public async Task Gleichzeitiges_Bestaetigen_erzeugt_nur_ein_Ereignis()
    {
        var anmeldung = await EmmaAnmeldenAsync();

        var antworten = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => BestaetigenAsync(anmeldung))));

        Assert.Single(antworten, a => a.StatusCode == HttpStatusCode.NoContent);
        Assert.All(antworten.Where(a => a.StatusCode != HttpStatusCode.NoContent), a => Assert.Equal(HttpStatusCode.Conflict, a.StatusCode));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%.AnmeldungBestaetigt'"));
    }

    [Fact]
    public async Task Eine_Anmeldung_einer_anderen_Freizeit_derselben_Leitung_und_eine_unbekannte_ergeben_404()
    {
        var inZ = await AnmeldenAsync(_katrin, _tnZ, _emma);

        foreach (var anmeldung in new[] { inZ, Guid.CreateVersion7() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await BestaetigenAsync(anmeldung)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await AbsagenAsync(anmeldung)).StatusCode);
        }

        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @a AND \"Status\" = 1", ("a", inZ)));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_miriam, $"{ListePfad(_z)}/{inZ}/bestaetigen")).StatusCode);
    }

    // ---------- Ausgebucht ----------

    [Fact]
    public async Task Ausgebucht_zaehlt_bestaetigte_Teilnehmende_Mitarbeitende_nie_und_Bestaetigen_bleibt_moeglich()
    {
        var version = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch))).GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(
            _miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite", $$"""{"version":{{version}},"alterWert":null,"hoechstzahlTeilnehmende":1}""")).StatusCode);
        var jonas = await MitarbeitAsync(_jonas);
        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(jonas)).StatusCode);
        Assert.False(await AusgebuchtAsync(_tnX));

        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(await EmmaAnmeldenAsync())).StatusCode);

        Assert.True(await AusgebuchtAsync(_tnX));
        Assert.False(await AusgebuchtAsync(_maX));
        var weitere = await AnmeldenAsync(_katrin, _tnX, null);
        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(weitere)).StatusCode);
        var liste = await JsonAsync(await _miriam.GetAsync(ListePfad(_x), Abbruch));
        Assert.Equal((1, 2), (liste.GetProperty("hoechstzahlTeilnehmende").GetInt32(), liste.GetProperty("bestaetigteTeilnehmende").GetInt32()));
    }

    // ---------- Gast ----------

    [Fact]
    public async Task Gast_nach_Bestaetigung_bekommt_die_Mail_an_die_Gast_Adresse_und_der_Gast_Link_zeigt_Status_und_Kontakt()
    {
        await KontaktSetzenAsync();
        var (token, kind) = await GastAsync();
        var anmeldung = await GastAnmeldenAsync(_tnX, token, kind);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(anmeldung)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        var mail = _factory.Mails.LetzteAn(Lina);
        Assert.Equal($"Anmeldung bestätigt: {Kapf}", mail.Betreff);
        Assert.Contains($"Lina ist bei {Kapf} dabei.", mail.Text, StringComparison.Ordinal);
        using var ansicht = await _anonym.GetAsync(GastPfad(token), Abbruch);
        Assert.Equal(HttpStatusCode.OK, ansicht.StatusCode);
        var zeile = Assert.Single((await JsonAsync(ansicht)).GetProperty("anmeldungen").EnumerateArray());
        Assert.Equal(["ersterTag", "freizeitName", "kontaktMail", "kontaktTelefon", "letzterTag", "status", "vorname"], Namen(zeile));
        Assert.Equal(("Bestaetigt", KontaktMail, KontaktTelefon), (
            zeile.GetProperty("status").GetString(), zeile.GetProperty("kontaktMail").GetString(), zeile.GetProperty("kontaktTelefon").GetString()));
    }

    [Fact]
    public async Task Gast_nach_Absage_der_einzigen_Anmeldung_bekommt_die_Mail_der_Gast_Link_endet_und_der_Loeschlauf_loescht_die_Familie()
    {
        var (token, kind) = await GastAsync();
        var anmeldung = await GastAnmeldenAsync(_tnX, token, kind);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, (await AbsagenAsync(anmeldung)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal($"Anmeldung abgesagt: {Kapf}", _factory.Mails.LetzteAn(Lina).Betreff);
        // Ohne Kontakt nennt die Mail die Leitung der Freizeit allgemein.
        Assert.Contains("Leitung der Freizeit", _factory.Mails.LetzteAn(Lina).Text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(GastPfad(token), Abbruch)).StatusCode);

        _factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        await _factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @k", ("k", kind)));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang"));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @a AND \"PersonId\" = @p AND \"Status\" = 3", ("a", anmeldung), ("p", GeloeschtePerson.Id)));
    }

    [Fact]
    public async Task Die_Gast_Link_Seite_zeigt_unter_einer_abgesagten_Anmeldung_keinen_Kontakt_unter_der_anderen_schon()
    {
        await KontaktSetzenAsync();
        var (token, lina) = await GastAsync();
        var linaAnmeldung = await GastAnmeldenAsync(_tnX, token, lina);
        using var weiteres = await PostAsync(_anonym, $"/api/personen/oeffentlich/gaeste/{token}/kinder", new GastKindHinzufuegen("Ben", null, new DateOnly(2018, 1, 5)));
        Assert.Equal(HttpStatusCode.Created, weiteres.StatusCode);
        await GastAnmeldenAsync(_tnX, token, (await JsonAsync(weiteres)).GetProperty("kindPersonId").GetGuid());
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, (await AbsagenAsync(linaAnmeldung)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        using var anfrage = new HttpRequestMessage(HttpMethod.Get, $"/gast/{token}");
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, "203.0.113.23");
        using var seite = await _anonym.SendAsync(anfrage, Abbruch);
        Assert.Equal(HttpStatusCode.OK, seite.StatusCode);
        var html = System.Net.WebUtility.HtmlDecode(await seite.Content.ReadAsStringAsync(Abbruch));
        string Karte(string vorname)
        {
            var anfang = html.IndexOf($">{vorname}</h2>", StringComparison.Ordinal);
            Assert.True(anfang >= 0, $"Card {vorname} fehlt.");
            var ende = new[] { html.IndexOf("</h2>", anfang + vorname.Length + 6, StringComparison.Ordinal), html.IndexOf("Gast-Link gilt bis", anfang, StringComparison.Ordinal) }
                .Where(i => i > anfang).Min();
            return html[anfang..ende];
        }

        Assert.Contains(">Abgesagt</span>", Karte("Lina"), StringComparison.Ordinal);
        Assert.DoesNotContain("Absage nur über die Leitung", Karte("Lina"), StringComparison.Ordinal);
        Assert.Contains(">Eingegangen</span>", Karte("Ben"), StringComparison.Ordinal);
        Assert.Contains("Absage nur über die Leitung: ", Karte("Ben"), StringComparison.Ordinal);
        Assert.Contains($"href=\"mailto:{KontaktMail}\"", Karte("Ben"), StringComparison.Ordinal);
    }

    // ---------- Kontakt für Absagen ----------

    [Fact]
    public async Task Kontakt_fuer_Absagen_pflegt_die_Leitung_per_Autosave_mit_Formpruefung_ein_leerer_Wert_loescht()
    {
        var version = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch))).GetProperty("version").GetUInt32();

        foreach (var (feld, wert) in new[]
                 {
                     ("kontaktMail", "ohne-at-zeichen"),
                     ("kontaktMail", new string('a', 250) + "@x.de"),
                     ("kontaktTelefon", new string('1', 41)),
                     ("kontaktMail", "a b@example.org"),
                     ("kontaktTelefon", "keine Nummer"),
                 })
        {
            using var falsch = await SendenAsync(_miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite", $$"""{"version":{{version}},"alterWert":null,"{{feld}}":"{{wert}}"}""");
            Assert.Equal(HttpStatusCode.BadRequest, falsch.StatusCode);
            Assert.True((await ProblemAsync(falsch)).GetProperty("errors").TryGetProperty(feld, out _));
        }

        await KontaktSetzenAsync();
        var einstellungen = await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch));
        Assert.Equal((KontaktMail, KontaktTelefon), (einstellungen.GetProperty("kontaktMail").GetString(), einstellungen.GetProperty("kontaktTelefon").GetString()));

        using var leeren = await SendenAsync(
            _miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite",
            $$"""{"version":{{einstellungen.GetProperty("version").GetUInt32()}},"alterWert":"{{KontaktTelefon}}","kontaktTelefon":""}""");
        Assert.Equal(HttpStatusCode.OK, leeren.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(leeren)).GetProperty("kontaktTelefon").ValueKind);
    }

    [Fact]
    public async Task Meine_Anmeldungen_tragen_den_Kontakt_der_jeweiligen_Freizeit_ohne_Kontakt_leer()
    {
        await KontaktSetzenAsync();
        await EmmaAnmeldenAsync();
        await AnmeldenAsync(_katrin, _tnZ, _emma);

        var zeilen = await MeineAnmeldungenAsync(_katrin);

        Assert.Equal(
            [("Z", (string?)null), (Kapf, KontaktMail)],
            zeilen.Select(z => (z.GetProperty("freizeitName").GetString()!, z.GetProperty("kontaktMail").GetString())));
    }

    // ---------- Hilfen ----------

    private string ListePfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/anmeldungen";

    private Task<HttpResponseMessage> BestaetigenAsync(Guid anmeldung) => PostAsync(_miriam, $"{ListePfad(_x)}/{anmeldung}/bestaetigen");

    private Task<HttpResponseMessage> AbsagenAsync(Guid anmeldung) => PostAsync(_miriam, $"{ListePfad(_x)}/{anmeldung}/absagen");

    private Task<Guid> EmmaAnmeldenAsync() => AnmeldenAsync(_katrin, _tnX, _emma);

    private Task<Guid> MitarbeitAsync(HttpClient client) => AnmeldenAsync(client, _maX, null);

    private static async Task<Guid> AnmeldenAsync(HttpClient client, string anmeldelink, Guid? person)
    {
        using var antwort = await PostAsync(client, "/api/ich/anmeldungen", new AnmeldungAbsenden(anmeldelink, person, true));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("anmeldungId").GetGuid();
    }

    private async Task<(string Token, Guid Kind)> GastAsync()
    {
        using var antwort = await PostAsync(_anonym, "/api/personen/oeffentlich/gaeste", new GastAnlegen("Sabine", "Beispiel", Lina, "Lina", "Beispiel", new DateOnly(2016, 4, 1)));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        return (json.GetProperty("gastToken").GetString()!, json.GetProperty("kindPersonId").GetGuid());
    }

    private async Task<Guid> GastAnmeldenAsync(string anmeldelink, string gastToken, Guid kind)
    {
        using var antwort = await PostAsync(_anonym, $"/api/freizeiten/oeffentlich/{anmeldelink}/anmeldungen", new GastAnmeldungAbsenden(gastToken, kind, true));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("anmeldungId").GetGuid();
    }

    private async Task KontaktSetzenAsync()
    {
        foreach (var (feld, wert) in new[] { ("kontaktMail", KontaktMail), ("kontaktTelefon", KontaktTelefon) })
        {
            var version = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch))).GetProperty("version").GetUInt32();
            using var antwort = await SendenAsync(_miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite", $$"""{"version":{{version}},"alterWert":null,"{{feld}}":"{{wert}}"}""");
            Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            Assert.Equal(wert, (await JsonAsync(antwort)).GetProperty(feld).GetString());
        }
    }

    private static async Task<List<JsonElement>> MeineAnmeldungenAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/ich/anmeldungen", Abbruch))).EnumerateArray().ToList();

    private async Task<bool> AusgebuchtAsync(string token) =>
        (await JsonAsync(await _anonym.GetAsync($"/api/freizeiten/oeffentlich/{token}", Abbruch))).GetProperty("ausgebucht").GetBoolean();

    /// <summary>Stellt die Uhr auf die nächste volle Sekunde (die Datenbank speichert Mikrosekunden) und liefert diesen Zeitpunkt.</summary>
    private Task<DateTimeOffset> ZustellzeitAsync()
    {
        var jetzt = _factory.Uhr.GetUtcNow();
        var ziel = new DateTimeOffset(jetzt.Ticks - (jetzt.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero).AddSeconds(1);
        _factory.Uhr.SetUtcNow(ziel);
        return Task.FromResult(ziel);
    }

    private static DateTimeOffset Mikro(DateTimeOffset zeit) => new(zeit.Ticks - (zeit.Ticks % 10), zeit.Offset);

    private static string GastPfad(string token) => $"/api/freizeiten/gast/{token}";

    private static string TokenAus(string adresse) => adresse[(adresse.LastIndexOf('/') + 1)..];

    private async Task<Dictionary<string, string>> AnmeldelinksAsync(Guid freizeit) =>
        (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{freizeit}/freizeitseite", Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
        .ToDictionary(l => l.GetProperty("art").GetString()!, l => l.GetProperty("adresse").GetString()!);
}
