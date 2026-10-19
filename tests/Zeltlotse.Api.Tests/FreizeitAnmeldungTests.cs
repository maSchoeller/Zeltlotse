using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Api.Datenschutz;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using FamilienHelfer = Zeltlotse.Api.Tests.FamilienHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-2, Anmeldung über Anmeldelinks (FR-16 bis FR-19, AD-7, AD-11, AD-26, AD-30) gegen echtes PostgreSQL: Gast-Anmeldung
/// (Personen legt die Gast-Familie an, Freizeiten die Anmeldung, Eingangsmail mit Gast-Link), weiteres Kind, Gast-Link mit seiner
/// Gültigkeit, „Konto aus diesen Daten erstellen“, Anmeldung mit Konto (Teilnehmende und Mitarbeitende), die eigenen Anmeldungen,
/// das Feld Teilnahmebedingungen sowie Löschen und Auskunft. Die Datei heißt nicht <c>AnmeldungTests</c>, weil es diese Datei für
/// den Login schon gibt.
/// </summary>
public sealed class FreizeitAnmeldungTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Lina = "lina.eltern@example.org";
    private const string Kapf = "Zeltlager Kapf 2027";

    /// <summary>Ende des Freigabefensters von X: 13.09.2027 00:00 Europe/Berlin (letzter Tag 13.08.2027 plus 30 Tage).</summary>
    private static readonly DateTimeOffset FreigabeEndeX = new(2027, 9, 12, 22, 0, 0, TimeSpan.Zero);

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _lea = null!;
    private HttpClient _miriam = null!;
    private HttpClient _katrin = null!;
    private HttpClient _anonym = null!;
    private Guid _a;
    private Guid _x;
    private Guid _z;
    private Guid _emma;
    private Guid _paul;
    private string _tnX = null!;
    private string _maX = null!;
    private string _tnZ = null!;

    /// <summary>
    /// Organisation A (Andrea) mit Freizeit X, Organisation B (Lea) mit Freizeit Z; Miriam ist Leitung beider. Katrin hat Emma (unter
    /// 18) und Paul (volljährig) im Familienkonto. Die Uhr steht auf der echten Zeit, also vor beiden Freizeiten.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueMigrierteDatenbankAsync();
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
        _paul = await FamilienHelfer.VolljaehrigesKindAsync(_katrin, "Paul");
        _anonym = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _lea, _miriam, _katrin, _anonym })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    // ---------- Gast-Anmeldung (Personen + Freizeiten) ----------

    [Fact]
    public async Task Gast_Anmeldung_legt_Gast_Familie_und_Anmeldung_an_und_schickt_die_Eingangsmail_mit_dem_Gast_Link()
    {
        var personenVorher = await AnzahlAsync("personen.person");

        using var gast = await GastAnlegenAsync(new GastAnlegen("  Sabine ", "Beispiel", " Lina.Eltern@Example.org ", " Lina ", "Beispiel", new DateOnly(2016, 4, 1)));

        Assert.Equal(HttpStatusCode.Created, gast.StatusCode);
        var json = await JsonAsync(gast);
        Assert.Equal(["gastToken", "kindPersonId"], Namen(json));
        var token = json.GetProperty("gastToken").GetString()!;
        var kind = json.GetProperty("kindPersonId").GetGuid();
        Assert.True(Zugangslink.KannTokenSein(token));
        Assert.Equal(personenVorher + 2, await AnzahlAsync("personen.person"));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang WHERE \"Mail\" = @m AND \"UebernommenAm\" IS NULL", ("m", Lina)));
        // Nur der Hash liegt in der Datenbank.
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang WHERE encode(\"TokenHash\", 'escape') LIKE @t", ("t", $"%{token}%")));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung,
            "SELECT count(*) FROM personen.person k JOIN personen.familien_elternteil e ON e.\"FamilieId\" = k.\"FamilieId\" JOIN personen.person p ON p.\"Id\" = e.\"PersonId\" " +
            "WHERE k.\"Id\" = @k AND k.\"Vorname\" = 'Lina' AND p.\"Vorname\" = 'Sabine' AND p.\"FamilieId\" IS NULL",
            ("k", kind)));
        Assert.Equal(2L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM personen.personen_schluessel WHERE \"PersonId\" IN (SELECT \"Id\" FROM personen.person WHERE \"Vorname\" IN ('Sabine', 'Lina'))"));

        using var anmeldung = await GastAnmeldenAsync(_tnX, token, kind);

        Assert.Equal(HttpStatusCode.Created, anmeldung.StatusCode);
        var anmeldungId = (await JsonAsync(anmeldung)).GetProperty("anmeldungId").GetGuid();
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung,
            "SELECT count(*) FROM freizeiten.anmeldung WHERE \"Id\" = @id AND \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"OrganisationId\" = @o " +
            "AND \"Art\" = 1 AND \"Status\" = 1 AND \"TeilnahmebedingungenBestaetigtAm\" IS NOT NULL",
            ("id", anmeldungId), ("f", _x), ("p", kind), ("o", _a)));

        var mail = Assert.Single(_factory.Mails.An(Lina));
        Assert.Equal($"Anmeldung eingegangen: {Kapf}", mail.Betreff);
        Assert.Equal($"https://localhost/gast/{token}", mail.Link);
        Assert.Contains("Anmeldung eingegangen: Die Leitung meldet sich in Kürze.", mail.Text, StringComparison.Ordinal);
        Assert.Contains("für Lina", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Der Gast-Link gilt bis 12.09.2027.", mail.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fehlende_Pflichtangaben_ergeben_400_je_Feld_und_es_entsteht_nichts()
    {
        var personenVorher = await AnzahlAsync("personen.person");

        using var antwort = await GastAnlegenAsync(new GastAnlegen(null, null, " ", "", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var fehler = (await ProblemAsync(antwort)).GetProperty("errors");
        Assert.Equal(["elternVorname", "kindGeburtsdatum", "kindVorname", "mail"], Namen(fehler));
        Assert.Equal("Geburtsdatum fehlt.", fehler.GetProperty("kindGeburtsdatum")[0].GetString());
        Assert.Equal(personenVorher, await AnzahlAsync("personen.person"));
        Assert.Equal(0L, await AnzahlAsync("personen.gast_zugang"));
    }

    [Fact]
    public async Task Ein_volljaehriges_Kind_ergibt_400_am_Geburtsdatum()
    {
        using var antwort = await GastAnlegenAsync(new GastAnlegen("Sabine", null, Lina, "Lina", null, FamilienHelfer.Volljaehrig));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal("Volljährige melden sich mit Konto an.", (await ProblemAsync(antwort)).GetProperty("errors").GetProperty("kindGeburtsdatum")[0].GetString());
        Assert.Equal(0L, await AnzahlAsync("personen.gast_zugang"));
    }

    [Fact]
    public async Task Ohne_Haken_ergibt_die_Anmeldung_400_und_es_entsteht_keine_Anmeldung()
    {
        var (token, kind) = await GastAsync();

        using var antwort = await GastAnmeldenAsync(_tnX, token, kind, bestaetigt: false);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal("Teilnahmebedingungen nicht bestätigt.", (await ProblemAsync(antwort)).GetProperty("errors").GetProperty("teilnahmebedingungenBestaetigt")[0].GetString());
        Assert.Equal(0L, await AnzahlAsync("freizeiten.anmeldung"));
        Assert.Empty(_factory.Mails.An(Lina));
    }

    [Fact]
    public async Task Weiteres_Kind_gehoert_zur_selben_Gast_Familie_und_steht_unter_demselben_Gast_Link()
    {
        var (token, lina) = await GastAsync();
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, lina)).StatusCode);

        using var weiteres = await PostAsync(_anonym, $"/api/personen/oeffentlich/gaeste/{token}/kinder", new GastKindHinzufuegen(" Ben ", null, new DateOnly(2018, 1, 5)));

        Assert.Equal(HttpStatusCode.Created, weiteres.StatusCode);
        var ben = (await JsonAsync(weiteres)).GetProperty("kindPersonId").GetGuid();
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(DISTINCT \"FamilieId\") FROM personen.person WHERE \"Id\" IN (@a, @b)", ("a", lina), ("b", ben)));
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, ben)).StatusCode);

        var mails = _factory.Mails.An(Lina);
        Assert.Equal(2, mails.Count);
        Assert.All(mails, m => Assert.Equal($"https://localhost/gast/{token}", m.Link));
        var ansicht = await JsonAsync(await _anonym.GetAsync(GastPfad(token), Abbruch));
        Assert.Equal(["Ben", "Lina"], ansicht.GetProperty("anmeldungen").EnumerateArray().Select(a => a.GetProperty("vorname").GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Die_Eingangsmail_nennt_das_spaetere_Ende_wenn_die_Gast_Familie_schon_ein_laengeres_Freigabefenster_hat()
    {
        var (token, kind) = await GastAsync();
        // Z endet am 03.09.2027, sein Freigabefenster also nach dem von X.
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnZ, token, kind)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, kind)).StatusCode);

        var mails = _factory.Mails.An(Lina);
        Assert.Equal(2, mails.Count);
        Assert.Contains("Der Gast-Link gilt bis 03.10.2027.", mails[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weiteres_Kind_mit_ungueltigem_Gast_Token_ergibt_404_und_einen_Protokolleintrag()
    {
        var vorher = await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);

        using var antwort = await PostAsync(_anonym, $"/api/personen/oeffentlich/gaeste/{Zugangslink.NeuesToken()}/kinder", new GastKindHinzufuegen("Ben", null, new DateOnly(2018, 1, 5)));

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(vorher + 1, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Doppelt_angemeldet_ergibt_409_konflikt()
    {
        var (token, kind) = await GastAsync();
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, kind)).StatusCode);

        using var antwort = await GastAnmeldenAsync(_tnX, token, kind);

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        Assert.Equal(1L, await AnzahlAsync("freizeiten.anmeldung"));
    }

    [Fact]
    public async Task Ungueltige_Anmeldelinks_und_Gast_Tokens_ergeben_404_mit_Protokolleintrag()
    {
        var (token, kind) = await GastAsync();
        var (fremdToken, _) = await GastAsync("andere.eltern@example.org");
        var alt = _tnX;
        using (var erneuern = await PostAsync(_miriam, $"/api/freizeiten/{_x}/anmeldelinks/teilnehmende/erneuern"))
        {
            Assert.Equal(HttpStatusCode.OK, erneuern.StatusCode);
            _tnX = TokenAus((await JsonAsync(erneuern)).GetProperty("adresse").GetString()!);
        }

        var vorher = await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);
        var faelle = new (string Anmeldelink, string? GastToken, Guid Kind)[]
        {
            (Zugangslink.NeuesToken(), token, kind),
            (alt, token, kind),
            // Anderer Zweck: Link für Mitarbeitende, Leitungs-Einladung, Gast-Token als Anmeldelink.
            (_maX, token, kind),
            (LeitungsToken(_factory, Miriam), token, kind),
            (token, token, kind),
            // Gast-Token ungültig, Anmeldelink als Gast-Token, Kind einer anderen Familie.
            (_tnX, Zugangslink.NeuesToken(), kind),
            (_tnX, _tnX, kind),
            (_tnX, fremdToken, kind),
            (_tnX, token, _emma),
        };
        foreach (var (anmeldelink, gastToken, person) in faelle)
        {
            using var antwort = await GastAnmeldenAsync(anmeldelink, gastToken, person);
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }

        Assert.Equal(vorher + faelle.Length, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(0L, await AnzahlAsync("freizeiten.anmeldung"));
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, kind)).StatusCode);
    }

    [Fact]
    public async Task Der_Link_von_X_meldet_nur_fuer_X_an_und_der_von_Z_nur_fuer_Z()
    {
        var (token, kind) = await GastAsync();

        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnZ, token, kind)).StatusCode);

        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"FreizeitId\" = @z", ("z", _z)));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"FreizeitId\" = @x", ("x", _x)));
        Assert.Equal("Z", (await JsonAsync(await _anonym.GetAsync(GastPfad(token), Abbruch))).GetProperty("anmeldungen")[0].GetProperty("freizeitName").GetString());
    }

    [Fact]
    public async Task Nach_dem_Freizeitende_ergibt_das_Absenden_404_mit_Protokolleintrag()
    {
        var (token, kind) = await GastAsync();
        // Ende von Z: 04.09.2027 00:00 Europe/Berlin; der Gast-Link gilt dann nicht mehr, darum über X: Freizeitende 14.08.2027.
        _factory.Uhr.SetUtcNow(new DateTimeOffset(2027, 8, 13, 22, 0, 0, TimeSpan.Zero));
        var vorher = await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);

        using var antwort = await GastAnmeldenAsync(_tnX, token, kind);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(vorher + 1, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    // ---------- Gast-Link ----------

    [Fact]
    public async Task Gast_Link_zeigt_nur_Uebersichtsfelder_und_schreibt_nichts_ins_Protokoll()
    {
        var (token, kind) = await GastAsync();
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, kind)).StatusCode);
        var vorher = await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);

        using var antwort = await _anonym.GetAsync(GastPfad(token), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var roh = await antwort.Content.ReadAsStringAsync(Abbruch);
        var json = JsonDocument.Parse(roh).RootElement;
        Assert.Equal(["anmeldungen", "gueltigBis"], Namen(json));
        var zeile = Assert.Single(json.GetProperty("anmeldungen").EnumerateArray());
        Assert.Equal(["anmeldungId", "einsichtBis", "ersterTag", "fragen", "freizeitName", "kontaktMail", "kontaktTelefon", "letzterTag", "personId", "status", "vorname"], Namen(zeile));
        Assert.Equal(("Lina", Kapf, "2027-08-02", "2027-08-13", "Eingegangen"), (
            zeile.GetProperty("vorname").GetString(),
            zeile.GetProperty("freizeitName").GetString(),
            zeile.GetProperty("ersterTag").GetString(),
            zeile.GetProperty("letzterTag").GetString(),
            zeile.GetProperty("status").GetString()));
        Assert.DoesNotContain("@", roh, StringComparison.Ordinal);
        // Story 2-4: Kennung des Kindes für den Freizeitpass über den Gast-Link (nur Kinder der eigenen Gast-Familie).
        Assert.Equal(kind, zeile.GetProperty("personId").GetGuid());

        foreach (var falsch in new[] { Zugangslink.NeuesToken(), "kurz", _tnX, LeitungsToken(_factory, Miriam) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(GastPfad(falsch), Abbruch)).StatusCode);
        }

        Assert.Equal(vorher, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Gast_Link_gilt_ohne_Freigabe_einen_Tag_mit_Freigabe_bis_zum_Ende_des_Freigabefensters()
    {
        var start = _factory.Uhr.GetUtcNow();
        var (ohne, _) = await GastAsync("ohne.freigabe@example.org");
        var (token, kind) = await GastAsync();
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, kind)).StatusCode);

        // Vor der ersten Freigabe: 1 Tag ab dem Anlegen.
        var vorFreigabe = await JsonAsync(await _anonym.GetAsync(GastPfad(token), Abbruch));
        Assert.True(vorFreigabe.GetProperty("gueltigBis").GetDateTimeOffset() <= start + TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));

        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"PersonId\" = @p AND \"FreizeitId\" = @f AND \"Stufe\" = 1 AND \"GueltigBis\" = @bis AND \"OrganisationId\" = @o",
            ("p", kind), ("f", _x), ("bis", FreigabeEndeX), ("o", _a)));

        _factory.Uhr.SetUtcNow(start + TimeSpan.FromDays(1) + TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(GastPfad(ohne), Abbruch)).StatusCode);
        using (var mitFreigabe = await _anonym.GetAsync(GastPfad(token), Abbruch))
        {
            Assert.Equal(HttpStatusCode.OK, mitFreigabe.StatusCode);
            Assert.Equal(FreigabeEndeX, (await JsonAsync(mitFreigabe)).GetProperty("gueltigBis").GetDateTimeOffset());
        }

        _factory.Uhr.SetUtcNow(FreigabeEndeX - TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(GastPfad(token), Abbruch)).StatusCode);
        _factory.Uhr.SetUtcNow(FreigabeEndeX);
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(GastPfad(token), Abbruch)).StatusCode);
    }

    // ---------- Konto aus diesen Daten erstellen ----------

    [Fact]
    public async Task Ein_leeres_Konto_uebernimmt_das_Gast_Profil_danach_zeigt_die_Familie_die_Kinder_und_der_Gast_Link_gibt_404()
    {
        var (token, kind) = await GastAsync();
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, kind)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        using var konto = await AngemeldeterClientAsync(_factory, Lina);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(konto, "/api/ich/profil")).StatusCode);
        var eigene = await PersonIdAsync(_verbindung, Lina);

        using var antwort = await PostAsync(konto, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(token));

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var eltern = (await JsonAsync(antwort)).GetProperty("personId").GetGuid();
        Assert.NotEqual(eigene, eltern);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", eigene)));
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(GastPfad(token), Abbruch)).StatusCode);
        Assert.Equal(1L, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.ProfilUebergabe, eltern));

        // Konten verbindet das Konto über ProfilUebergabeAngenommen; danach neu anmelden.
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(eltern, await PersonIdAsync(_verbindung, Lina));
        using var neu = await AngemeldeterClientAsync(_factory, Lina);
        var familie = await FamilienHelfer.FamilieAsync(neu);
        Assert.Equal(kind, Assert.Single(familie.Kinder).PersonId);
        var anmeldungen = await neu.GetFromJsonAsync<List<AnmeldungZeile>>("/api/ich/anmeldungen", Abbruch);
        Assert.Equal((kind, AnmeldungStatus.Eingegangen, Kapf), (Assert.Single(anmeldungen!).PersonId, anmeldungen![0].Status, anmeldungen[0].FreizeitName));

        // Ein zweites Mal ist der Gast-Link verbraucht.
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(neu, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(token))).StatusCode);

        // Die Auskunft nennt den Gast-Link mit Mail-Adresse und Übernahme, nie das Token.
        var auskunft = (await neu.GetFromJsonAsync<Zeltlotse.Api.Contracts.Betrieb.DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var gastLink = Assert.Single(auskunft, a => a.Titel == "Gast-Link");
        Assert.Contains(gastLink.Zeilen, z => z.Feld == "Mail-Adresse" && z.Wert == Lina);
        Assert.Contains(gastLink.Zeilen, z => z.Feld == "In ein Konto übernommen am");
        Assert.DoesNotContain(gastLink.Zeilen, z => z.Wert.Contains(token, StringComparison.Ordinal));

        // Nach dem Ende des Freigabefensters löscht der Löschlauf das übernommene Profil nicht.
        _factory.Uhr.SetUtcNow(FreigabeEndeX);
        await _factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(2L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" IN (@e, @k)", ("e", eltern), ("k", kind)));
        Assert.Equal(1L, await AnzahlAsync("personen.gast_zugang"));
    }

    [Fact]
    public async Task Ein_Konto_mit_eigenen_Angaben_bekommt_409_ein_ungueltiges_Token_404_mit_Protokoll()
    {
        var (token, _) = await GastAsync();
        var vorher = await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);

        using var eigeneAngaben = await PostAsync(_katrin, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(token));
        using var ungueltig = await PostAsync(_katrin, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(_tnX));

        Assert.Equal(HttpStatusCode.Conflict, eigeneAngaben.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(eigeneAngaben)));
        Assert.Equal(HttpStatusCode.NotFound, ungueltig.StatusCode);
        Assert.Equal(vorher + 1, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(GastPfad(token), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Ein_Konto_ohne_Namen_das_schon_Elternteil_einer_Familie_ist_bekommt_409()
    {
        var (token, _) = await GastAsync();
        using var thomas = await AngemeldeterClientAsync(_factory, FamilienHelfer.Thomas);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(thomas, "/api/ich/profil")).StatusCode);
        // Die Einladung eines zweiten Elternteils legt das Familienkonto mit Thomas als erstem Elternteil an; Namen hat er keine.
        Assert.Equal(HttpStatusCode.Accepted, (await FamilienHelfer.EinladenAsync(thomas, "partnerin@example.org")).StatusCode);
        var thomasId = await PersonIdAsync(_verbindung, FamilienHelfer.Thomas);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p AND \"Vorname\" IS NULL AND \"Nachname\" IS NULL", ("p", thomasId)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.familien_elternteil WHERE \"PersonId\" = @p", ("p", thomasId)));

        using var antwort = await PostAsync(thomas, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(token));

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", thomasId)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang WHERE \"UebernommenAm\" IS NULL"));
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(GastPfad(token), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Ein_Konto_ohne_Namen_mit_eigener_Anmeldung_oder_Freizeit_Rolle_bekommt_409_und_bleibt_bestehen()
    {
        var (token, _) = await GastAsync();
        // Ohne Namen über den Link für Mitarbeitende selbst angemeldet: Nach der Zustellung besteht eine Freigabe.
        using var ohneNamen = await AngemeldeterClientAsync(_factory, "ohne.namen@example.org");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(ohneNamen, "/api/ich/profil")).StatusCode);
        var selbst = await PersonIdAsync(_verbindung, "ohne.namen@example.org");
        Assert.Equal(HttpStatusCode.Created, (await AnmeldenAsync(ohneNamen, _maX, selbst)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"PersonId\" = @p", ("p", selbst)));
        // Miriam hat als Leitung von X und Z Freizeit-Rollen, aber weder Profil mit Namen noch Freigabe: Bei ihr zählen nur die Rollen.
        var miriam = await PersonIdAsync(_verbindung, Miriam);
        Assert.Equal(2L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeit_rolle WHERE \"PersonId\" = @p", ("p", miriam)));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"PersonId\" = @p", ("p", miriam)));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p AND \"Vorname\" IS NOT NULL", ("p", miriam)));

        using var mitFreigabe = await PostAsync(ohneNamen, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(token));
        using var mitRolle = await PostAsync(_miriam, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(token));

        Assert.Equal(HttpStatusCode.Conflict, mitFreigabe.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, mitRolle.StatusCode);
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", selbst)));
        Assert.Equal(2L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeit_rolle WHERE \"PersonId\" = @p", ("p", miriam)));
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang WHERE \"UebernommenAm\" IS NULL"));
    }

    // ---------- Mit Konto ----------

    [Fact]
    public async Task Mit_Konto_meldet_ein_Elternteil_ein_Kind_unter_18_und_sich_selbst_an_ohne_Mail()
    {
        var mailsVorher = _factory.Mails.An(FamilienHelfer.Katrin).Count;

        using var emma = await AnmeldenAsync(_katrin, _tnX, _emma);
        using var selbst = await AnmeldenAsync(_katrin, _tnX, null);

        Assert.Equal(HttpStatusCode.Created, emma.StatusCode);
        Assert.Equal(["anmeldungId"], Namen(await JsonAsync(emma)));
        Assert.Equal(HttpStatusCode.Created, selbst.StatusCode);
        var katrin = await PersonIdAsync(_verbindung, FamilienHelfer.Katrin);
        Assert.Equal(2L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"FreizeitId\" = @f AND \"Art\" = 1 AND \"PersonId\" IN (@e, @k)",
            ("f", _x), ("e", _emma), ("k", katrin)));
        Assert.Equal(mailsVorher, _factory.Mails.An(FamilienHelfer.Katrin).Count);
        Assert.Equal(HttpStatusCode.Conflict, (await AnmeldenAsync(_katrin, _tnX, katrin)).StatusCode);
    }

    [Fact]
    public async Task Mit_Konto_fremde_Person_volljaehriges_Kind_und_Kind_beim_Link_fuer_Mitarbeitende_ergeben_404_Haken_fehlt_400()
    {
        using var daniel = await AngemeldeterClientAsync(_factory, FamilienHelfer.Daniel);
        var mia = await FamilienHelfer.KindAsync(daniel, "Mia", new DateOnly(2016, 5, 21), "Probst");

        Assert.Equal(HttpStatusCode.NotFound, (await AnmeldenAsync(_katrin, _tnX, mia)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AnmeldenAsync(_katrin, _tnX, _paul)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AnmeldenAsync(_katrin, _maX, _emma)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AnmeldenAsync(_katrin, Zugangslink.NeuesToken(), _emma)).StatusCode);
        using var ohneHaken = await AnmeldenAsync(_katrin, _tnX, _emma, bestaetigt: false);
        Assert.Equal(HttpStatusCode.BadRequest, ohneHaken.StatusCode);
        Assert.True((await ProblemAsync(ohneHaken)).GetProperty("errors").TryGetProperty("teilnahmebedingungenBestaetigt", out _));
        Assert.Equal(0L, await AnzahlAsync("freizeiten.anmeldung"));
    }

    [Fact]
    public async Task Mit_Konto_ueber_den_Link_fuer_Mitarbeitende_meldet_sich_die_Person_selbst_an_auch_wenn_ausgebucht()
    {
        using var jonas = await AngemeldeterClientAsync(_factory, Jonas);
        var version = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch))).GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(_miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite", $$"""{"version":{{version}},"alterWert":null,"hoechstzahlTeilnehmende":1}""")).StatusCode);

        using var antwort = await AnmeldenAsync(jonas, _maX, null);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"FreizeitId\" = @f AND \"Art\" = 2 AND \"PersonId\" = @p",
            ("f", _x), ("p", await PersonIdAsync(_verbindung, Jonas))));
    }

    [Fact]
    public async Task Meine_Anmeldungen_zeigen_Person_und_Kinder_ueber_Organisationen_hinweg_nur_Uebersichtsfelder_fremden_nichts()
    {
        Assert.Equal(HttpStatusCode.Created, (await AnmeldenAsync(_katrin, _tnX, _emma)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AnmeldenAsync(_katrin, _tnZ, _emma)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AnmeldenAsync(_katrin, _tnZ, null)).StatusCode);
        using var thomas = await FamilienHelfer.ZweiterElternteilAsync(_factory, _katrin, FamilienHelfer.Thomas);
        using var daniel = await AngemeldeterClientAsync(_factory, FamilienHelfer.Daniel);

        using var antwort = await _katrin.GetAsync("/api/ich/anmeldungen", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var zeilen = (await JsonAsync(antwort)).EnumerateArray().ToList();
        Assert.Equal(3, zeilen.Count);
        Assert.All(zeilen, z => Assert.Equal(["anmeldungId", "art", "einsichtBis", "ersterTag", "fragen", "freizeitName", "kontaktMail", "kontaktTelefon", "letzterTag", "personId", "status"], Namen(z)));
        Assert.Equal(["Z", "Z", Kapf], zeilen.Select(z => z.GetProperty("freizeitName").GetString()));
        Assert.All(zeilen, z => Assert.Equal(("Teilnehmende", "Eingegangen"), (z.GetProperty("art").GetString(), z.GetProperty("status").GetString())));

        // Der zweite Elternteil sieht Emmas Anmeldungen, nicht die von Katrin selbst; Daniel sieht nichts davon.
        var vonThomas = await thomas.GetFromJsonAsync<List<AnmeldungZeile>>("/api/ich/anmeldungen", Abbruch);
        Assert.Equal(2, vonThomas!.Count);
        Assert.All(vonThomas, z => Assert.Equal(_emma, z.PersonId));
        Assert.Empty((await daniel.GetFromJsonAsync<List<AnmeldungZeile>>("/api/ich/anmeldungen", Abbruch))!);
    }

    // ---------- Teilnahmebedingungen ----------

    [Fact]
    public async Task Teilnahmebedingungen_pflegt_die_Leitung_per_Autosave_das_Team_bekommt_403_und_sie_stehen_auf_der_Freizeitseite()
    {
        using var jonas = await AngemeldeterClientAsync(_factory, Jonas);
        await AusfuehrenAsync(
            _verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"OrganisationId\", \"FreizeitId\", \"PersonId\", \"Rolle\", \"VergebenAm\") VALUES (@o, @f, @p, 2, now())",
            ("o", _a), ("f", _x), ("p", await PersonIdAsync(_verbindung, Jonas)));
        var einstellungen = await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch));
        Assert.Equal(JsonValueKind.Null, einstellungen.GetProperty("teilnahmebedingungen").ValueKind);
        var version = einstellungen.GetProperty("version").GetUInt32();
        var koerper = $$"""{"version":{{version}},"alterWert":null,"teilnahmebedingungen":"Die Hausordnung gilt."}""";

        using var team = await SendenAsync(jonas, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite", koerper);
        using var zuLang = await SendenAsync(
            _miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite",
            $$"""{"version":{{version}},"alterWert":null,"teilnahmebedingungen":{{JsonSerializer.Serialize(new string('x', 4001))}}}""");
        using var leitung = await SendenAsync(_miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite", koerper);

        Assert.Equal(HttpStatusCode.Forbidden, team.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zuLang.StatusCode);
        Assert.True((await ProblemAsync(zuLang)).GetProperty("errors").TryGetProperty("teilnahmebedingungen", out _));
        Assert.Equal(HttpStatusCode.OK, leitung.StatusCode);
        Assert.Equal("Die Hausordnung gilt.", (await JsonAsync(leitung)).GetProperty("teilnahmebedingungen").GetString());
        var seite = await JsonAsync(await _anonym.GetAsync($"/api/freizeiten/oeffentlich/{_tnX}", Abbruch));
        Assert.Equal("Die Hausordnung gilt.", seite.GetProperty("teilnahmebedingungen").GetString());
    }

    // ---------- Löschen und Auskunft ----------

    [Fact]
    public async Task Auskunft_nennt_Anmeldungen_und_Freigaben_der_Kinder_Konto_loeschen_ersetzt_die_Person_der_Anmeldung_durch_den_Platzhalter()
    {
        Assert.Equal(HttpStatusCode.Created, (await AnmeldenAsync(_katrin, _tnX, _emma)).StatusCode);
        // Katrin selbst auch: Beim Löschen der Familie entstehen zwei Platzhalter in derselben Freizeit (Teilindex ohne Platzhalter).
        Assert.Equal(HttpStatusCode.Created, (await AnmeldenAsync(_katrin, _tnX, null)).StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        var auskunft = (await _katrin.GetFromJsonAsync<Zeltlotse.Api.Contracts.Betrieb.DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var anmeldungen = Assert.Single(auskunft, a => a.Titel == "Anmeldungen");
        Assert.Contains(anmeldungen.Zeilen, z => z.Feld == Kapf && z.Wert.StartsWith("Emma Muster, Teilnehmende, eingegangen am", StringComparison.Ordinal));
        var freigaben = Assert.Single(auskunft, a => a.Titel == "Freigaben");
        Assert.Contains(freigaben.Zeilen, z => z.Feld == "Freigabe an eine Freizeit, Stufe Leitung" && z.Wert.Contains("gültig bis 12.09.2027", StringComparison.Ordinal));

        using var loeschen = await PostAsync(_katrin, "/api/ich/profil/loeschen");
        Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freigabe WHERE \"PersonId\" = @p", ("p", _emma)));
        Assert.Equal(2L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p", ("f", _x), ("p", GeloeschtePerson.Id)));
    }

    [Fact]
    public async Task Loeschlauf_loescht_abgelaufene_nie_uebernommene_Gast_Profile_mit_Zugang_und_Freigaben_Anmeldungen_tragen_den_Platzhalter()
    {
        var (token, lina) = await GastAsync();
        Assert.Equal(HttpStatusCode.Created, (await GastAnmeldenAsync(_tnX, token, lina)).StatusCode);
        var (ohneAnmeldung, ohneKind) = await GastAsync("ohne.anmeldung@example.org");
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        var eltern = await WertAsync<Guid>(_verbindung, "SELECT \"ElternPersonId\" FROM personen.gast_zugang WHERE \"Mail\" = @m", ("m", Lina));

        // Einen Tag später: Der Gast ohne Anmeldung ist abgelaufen, Lina hat eine laufende Freigabe.
        _factory.Uhr.SetUtcNow(_factory.Uhr.GetUtcNow() + TimeSpan.FromDays(2));
        await _factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", ohneKind)));
        Assert.Equal(1L, await AnzahlAsync("personen.gast_zugang"));
        Assert.Equal(HttpStatusCode.OK, (await _anonym.GetAsync(GastPfad(token), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(GastPfad(ohneAnmeldung), Abbruch)).StatusCode);

        // Nach dem Ende des Freigabefensters.
        _factory.Uhr.SetUtcNow(FreigabeEndeX);
        await _factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" IN (@e, @k)", ("e", eltern), ("k", lina)));
        Assert.Equal(0L, await AnzahlAsync("personen.gast_zugang"));
        Assert.Equal(0L, await AnzahlAsync("personen.freigabe"));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.personen_schluessel WHERE \"PersonId\" IN (@e, @k)", ("e", eltern), ("k", lina)));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(*) FROM freizeiten.anmeldung WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p", ("f", _x), ("p", GeloeschtePerson.Id)));
    }

    // ---------- Hilfen ----------

    private static string GastPfad(string token) => $"/api/freizeiten/gast/{token}";

    private static string TokenAus(string adresse) => adresse[(adresse.LastIndexOf('/') + 1)..];

    private Task<HttpResponseMessage> GastAnlegenAsync(GastAnlegen anfrage) => PostAsync(_anonym, "/api/personen/oeffentlich/gaeste", anfrage);

    /// <summary>Legt einen Gast mit dem Kind „Lina“ an und liefert Gast-Token und Kind.</summary>
    private async Task<(string Token, Guid Kind)> GastAsync(string mail = Lina)
    {
        using var antwort = await GastAnlegenAsync(new GastAnlegen("Sabine", "Beispiel", mail, "Lina", "Beispiel", new DateOnly(2016, 4, 1)));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        return (json.GetProperty("gastToken").GetString()!, json.GetProperty("kindPersonId").GetGuid());
    }

    private Task<HttpResponseMessage> GastAnmeldenAsync(string anmeldelink, string? gastToken, Guid kind, bool bestaetigt = true) =>
        PostAsync(_anonym, $"/api/freizeiten/oeffentlich/{anmeldelink}/anmeldungen", new GastAnmeldungAbsenden(gastToken, kind, bestaetigt));

    private static Task<HttpResponseMessage> AnmeldenAsync(HttpClient client, string anmeldelink, Guid? person, bool bestaetigt = true) =>
        PostAsync(client, "/api/ich/anmeldungen", new AnmeldungAbsenden(anmeldelink, person, bestaetigt));

    private Task<long> AnzahlAsync(string tabelle) => WertAsync<long>(_verbindung, $"SELECT count(*) FROM {tabelle}");

    private async Task<Dictionary<string, string>> AnmeldelinksAsync(Guid freizeit) =>
        (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{freizeit}/freizeitseite", Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
        .ToDictionary(l => l.GetProperty("art").GetString()!, l => l.GetProperty("adresse").GetString()!);
}
