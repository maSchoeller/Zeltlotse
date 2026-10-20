using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 5-3, Beitrag bezahlt abhaken (CAP-34, FR-60, NS-8, AD-7, AD-16, AD-17, AD-20, AD-28, AD-30) gegen echtes PostgreSQL: die Liste der bestätigten
/// Teilnehmenden mit „x von y bezahlt“, abhaken und wieder öffnen (wiederholt unschädlich, der erste Vermerk zählt, zwei gleichzeitige Haken ergeben einen
/// Vermerk), keine Teilnehmende → 404, eine spätere Absage, der Schreibschutz nach dem Abschluss, die Pflichtfälle der Freizeit-Route für alle drei Routen,
/// der Schalter Finanzen, der Lösch-Lauf, <c>PersonGeloescht</c> für beide Spalten und die Datenauskunft. Freizeit X (Organisation A; Org-Admin Andrea,
/// Leitung Miriam, Team Tobias) und Z (Organisation B, Leitung Miriam) haben Finanzen an; Katrins Kinder Mia und Emma melden sich in X an.
/// </summary>
public sealed class BeitraegeTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;
    private Guid _anmeldungMia;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres);
        await SchaltenAsync(_s.X, an: true);
        await SchaltenAsync(_s.Z, an: true);
        // Mia ist bestätigte Teilnehmende von X, Emma nur eingegangen, Tobias Mitarbeitender.
        _anmeldungMia = await _s.KindAnmeldenAsync(_s.Mia);
        await _s.BestaetigenAsync(_anmeldungMia);
        await _s.KindAnmeldenAsync(_s.Emma);
        await _s.ZustellenAsync();
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string BeitraegePfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/finanzen/beitraege";

    private static string BezahltPfad(Guid freizeit, Guid person) => $"{BeitraegePfad(freizeit)}/{person}/bezahlt";

    private static string OffenPfad(Guid freizeit, Guid person) => $"{BeitraegePfad(freizeit)}/{person}/offen";

    /// <summary>Schaltet Finanzen mit dem aktuellen Stand als altem Wert (Miriam ist Leitung von X und Z).</summary>
    private async Task SchaltenAsync(Guid freizeit, bool an)
    {
        using var stand = await _s.Miriam.GetAsync($"/api/freizeiten/{freizeit}/module", Abbruch);
        var json = await JsonAsync(stand);
        var koerper = new Dictionary<string, object?>
        {
            ["version"] = json.GetProperty("version").GetUInt32(),
            ["alterWert"] = json.GetProperty("finanzen").GetBoolean() ? "true" : "false",
            ["finanzen"] = an ? "true" : "false",
        };
        using var antwort = await SendenAsync(_s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/module", koerper);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private async Task<JsonElement> ListeAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(BeitraegePfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> BezahltAsync(Guid person, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, BezahltPfad(freizeit ?? _s.X, person));

    private Task<HttpResponseMessage> OffenAsync(Guid person, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, OffenPfad(freizeit ?? _s.X, person));

    private async Task<JsonElement> ZeileAsync(Task<HttpResponseMessage> aufruf)
    {
        using var antwort = await aufruf;
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<long> AnzahlAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM finanzen.beitrag_vermerk WHERE {bedingung}", parameter);

    private static (Guid PersonId, bool Bezahlt, Guid? AbgehaktVon, DateTimeOffset? AbgehaktAm) Werte(JsonElement zeile) => (
        zeile.GetProperty("personId").GetGuid(),
        zeile.GetProperty("bezahlt").GetBoolean(),
        zeile.GetProperty("abgehaktVon").ValueKind == JsonValueKind.Null ? null : zeile.GetProperty("abgehaktVon").GetGuid(),
        zeile.GetProperty("abgehaktAm").ValueKind == JsonValueKind.Null ? null : zeile.GetProperty("abgehaktAm").GetDateTimeOffset());

    // ---------- Liste ----------

    [Fact]
    public async Task Liste_nur_bestaetigte_Teilnehmende_ohne_eingegangene_und_Mitarbeitende_noch_nichts_bezahlt()
    {
        var liste = await ListeAsync(_s.Tobias);

        Assert.Equal(["bezahlt", "gesamt", "zeilen"], Namen(liste));
        Assert.Equal((0, 1), (liste.GetProperty("bezahlt").GetInt32(), liste.GetProperty("gesamt").GetInt32()));
        var zeile = Assert.Single(liste.GetProperty("zeilen").EnumerateArray());
        Assert.Equal(["abgehaktAm", "abgehaktVon", "bezahlt", "personId"], Namen(zeile));
        Assert.Equal((_s.Mia, false, (Guid?)null, (DateTimeOffset?)null), Werte(zeile));
    }

    // ---------- Abhaken und wieder öffnen ----------

    [Fact]
    public async Task Abhaken_vermerkt_wer_und_wann_ein_zweiter_Haken_aendert_nichts_der_erste_zaehlt()
    {
        var jetzt = _s.VolleSekunde();

        var gesetzt = await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));

        Assert.Equal(["abgehaktAm", "abgehaktVon", "bezahlt", "personId"], Namen(gesetzt));
        Assert.Equal((_s.Mia, true, (Guid?)_s.TobiasId, (DateTimeOffset?)jetzt), Werte(gesetzt));
        var liste = await ListeAsync();
        Assert.Equal((1, 1), (liste.GetProperty("bezahlt").GetInt32(), liste.GetProperty("gesamt").GetInt32()));
        Assert.Equal((_s.Mia, true, (Guid?)_s.TobiasId, (DateTimeOffset?)jetzt), Werte(Assert.Single(liste.GetProperty("zeilen").EnumerateArray())));
        Assert.Equal(1L, await AnzahlAsync("\"OrganisationId\" = @o AND \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"AbgehaktVon\" = @v",
            ("o", _s.A), ("f", _s.X), ("p", _s.Mia), ("v", _s.TobiasId)));

        // Miriam hakt später erneut ab: Tobias bleibt, ebenso der Zeitpunkt.
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));
        var doppelt = await ZeileAsync(BezahltAsync(_s.Mia, _s.Miriam));

        Assert.Equal((_s.Mia, true, (Guid?)_s.TobiasId, (DateTimeOffset?)jetzt), Werte(doppelt));
        Assert.Equal(1L, await AnzahlAsync());
    }

    [Fact]
    public async Task Wieder_offen_loescht_den_Vermerk_auch_zweimal_200()
    {
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));

        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            var offen = await ZeileAsync(OffenAsync(_s.Mia, client));
            Assert.Equal((_s.Mia, false, (Guid?)null, (DateTimeOffset?)null), Werte(offen));
        }

        Assert.Equal(0L, await AnzahlAsync());
        var liste = await ListeAsync();
        Assert.Equal((0, 1), (liste.GetProperty("bezahlt").GetInt32(), liste.GetProperty("gesamt").GetInt32()));

        // Nach dem Öffnen zählt ein neuer Haken wieder neu: jetzt mit Miriam.
        Assert.Equal(_s.MiriamId, Werte(await ZeileAsync(BezahltAsync(_s.Mia))).AbgehaktVon);
    }

    [Fact]
    public async Task Zwei_gleichzeitige_Haken_ergeben_genau_einen_Vermerk()
    {
        var antworten = await Task.WhenAll(BezahltAsync(_s.Mia, _s.Tobias), BezahltAsync(_s.Mia, _s.Miriam));

        var zeilen = new List<JsonElement>();
        foreach (var antwort in antworten)
        {
            using (antwort)
            {
                Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
                zeilen.Add(await JsonAsync(antwort));
            }
        }

        Assert.Equal(1L, await AnzahlAsync());
        var gespeichert = await WertAsync<Guid>(_s.Verbindung, "SELECT \"AbgehaktVon\" FROM finanzen.beitrag_vermerk");
        Assert.All(zeilen, z => Assert.Equal((true, (Guid?)gespeichert), (Werte(z).Bezahlt, Werte(z).AbgehaktVon)));
    }

    [Fact]
    public async Task Keine_bestaetigte_Teilnehmende_404_nichts_gespeichert()
    {
        // Ein Kind, das in Z bestätigt teilnimmt, ist in X keine Teilnehmende.
        var (_, kindZ, anmeldungZ) = await _s.GastAnmeldenAsync("sabine.beispiel@example.org", "Ben", _s.TeilnehmendeZ);
        await _s.BestaetigenAsync(anmeldungZ, _s.Z);

        foreach (var person in new[] { _s.Emma, _s.TobiasId, Guid.CreateVersion7(), kindZ, GeloeschtePerson.Id })
        {
            using var bezahlt = await BezahltAsync(person);
            Assert.Equal(HttpStatusCode.NotFound, bezahlt.StatusCode);
            using var offen = await OffenAsync(person);
            Assert.Equal(HttpStatusCode.NotFound, offen.StatusCode);
        }

        Assert.Equal(0L, await AnzahlAsync());
        // In Z ist das Kind Teilnehmende.
        Assert.True(Werte(await ZeileAsync(BezahltAsync(kindZ, freizeit: _s.Z))).Bezahlt);
    }

    [Fact]
    public async Task Abgesagt_danach_fehlt_in_der_Liste_der_Vermerk_bleibt_in_der_Tabelle()
    {
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));

        await _s.AbsagenAsync(_anmeldungMia);
        await _s.ZustellenAsync();

        var liste = await ListeAsync();
        Assert.Empty(liste.GetProperty("zeilen").EnumerateArray());
        Assert.Equal((0, 0), (liste.GetProperty("bezahlt").GetInt32(), liste.GetProperty("gesamt").GetInt32()));
        Assert.Equal(1L, await AnzahlAsync("\"PersonId\" = @p", ("p", _s.Mia)));
        using var bezahlt = await BezahltAsync(_s.Mia);
        Assert.Equal(HttpStatusCode.NotFound, bezahlt.StatusCode);
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufen_Lesen_200_beide_Uebergaenge_409_freizeit_abgeschlossen()
    {
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        Assert.Equal(1, (await ListeAsync(_s.Tobias)).GetProperty("bezahlt").GetInt32());
        foreach (var aufruf in new Func<Task<HttpResponseMessage>>[] { () => OffenAsync(_s.Mia, _s.Tobias), () => BezahltAsync(_s.Mia) })
        {
            using var antwort = await aufruf();
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
        }

        Assert.Equal(1L, await AnzahlAsync("\"AbgehaktVon\" = @v", ("v", _s.TobiasId)));
    }

    // ---------- Rechte und Schalter ----------

    /// <summary>Alle drei Routen für einen Client unter einer Freizeit, für Mia.</summary>
    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit) =>
    [
        () => client.GetAsync(BeitraegePfad(freizeit), Abbruch),
        () => BezahltAsync(_s.Mia, client, freizeit),
        () => OffenAsync(_s.Mia, client, freizeit),
    ];

    private async Task<List<HttpStatusCode>> StatusAllerRoutenAsync(HttpClient client, Guid freizeit)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_200_auf_allen_drei_Routen()
    {
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK], await StatusAllerRoutenAsync(client, _s.X));
        }
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_Leitung_von_Z_unbekannte_Freizeit_404_ohne_Sitzung_401_auf_allen_drei_Routen()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kasse@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var faelle = new (HttpClient Client, Guid Freizeit, HttpStatusCode Erwartet)[]
        {
            (_s.Andrea, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, fremde, HttpStatusCode.NotFound),
            (sara, fremde, HttpStatusCode.NotFound),
            (leitungZ, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
            (_s.Anonym, _s.X, HttpStatusCode.Unauthorized),
        };

        foreach (var (client, freizeit, erwartet) in faelle)
        {
            Assert.All(await StatusAllerRoutenAsync(client, freizeit), s => Assert.Equal(erwartet, s));
        }

        // Leitung von Z mit Finanzen an: Mia nimmt dort nicht teil.
        Assert.All((await StatusAllerRoutenAsync(_s.Miriam, _s.Z)).Skip(1), s => Assert.Equal(HttpStatusCode.NotFound, s));
        Assert.Equal(0L, await AnzahlAsync());
    }

    [Fact]
    public async Task Schalter_aus_jede_Route_404_fuer_Leitung_und_Team_ohne_Sitzung_401_nach_dem_Einschalten_ist_der_Vermerk_wieder_da()
    {
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));
        await SchaltenAsync(_s.X, an: false);

        foreach (var client in new[] { _s.Miriam, _s.Tobias, _s.Andrea })
        {
            Assert.All(await StatusAllerRoutenAsync(client, _s.X), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.All(await StatusAllerRoutenAsync(_s.Anonym, _s.X), s => Assert.Equal(HttpStatusCode.Unauthorized, s));

        await SchaltenAsync(_s.X, an: true);
        var liste = await ListeAsync(_s.Tobias);
        Assert.Equal((_s.Mia, true, (Guid?)_s.TobiasId), (Werte(liste.GetProperty("zeilen")[0]).PersonId, Werte(liste.GetProperty("zeilen")[0]).Bezahlt,
            Werte(liste.GetProperty("zeilen")[0]).AbgehaktVon));
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Vermerke_der_abgelaufenen_Freizeit_die_in_Z_bleiben()
    {
        var (_, kindZ, anmeldungZ) = await _s.GastAnmeldenAsync("sabine.beispiel@example.org", "Ben", _s.TeilnehmendeZ);
        await _s.BestaetigenAsync(anmeldungZ, _s.Z);
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));
        await ZeileAsync(BezahltAsync(kindZ, freizeit: _s.Z));

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "FinanzenLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    [Fact]
    public async Task Auskunft_eigener_Beitrag_und_eigene_Haken_fuer_andere_ohne_deren_Namen()
    {
        var heute = FreizeitHelfer.Heute(_s.Factory).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));

        var tobias = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var miriam = (await _s.Miriam.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        Assert.Equal(
            [new DatenauskunftZeile("Beitrag einer teilnehmenden Person als bezahlt abgehakt", $"am {heute}")],
            Assert.Single(tobias, a => a.Titel == "Beiträge").Zeilen);
        Assert.DoesNotContain(miriam, a => a.Titel == "Beiträge");

        // Mias eigene Auskunft (sie hat kein Konto; die Auskunft der Finanzen direkt).
        await using var scope = _s.Factory.Services.CreateAsyncScope();
        var auskunft = scope.ServiceProvider.GetServices<IDatenauskunft>().Single(a => a.GetType().Name == "FinanzenAuskunft");
        var abschnitt = Assert.Single(await auskunft.AuskunftAsync(_s.Mia, Abbruch), a => a.Titel == "Beiträge");
        Assert.Equal([new AuskunftZeile("Beitrag bezahlt", $"vermerkt am {heute}")], abschnitt.Zeilen);
        Assert.DoesNotContain(abschnitt.Zeilen, z => (z.Feld + z.Wert).Contains("Tobias", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Person_geloescht_Tobias_der_Vermerk_bleibt_abgehakt_von_ist_der_Platzhalter()
    {
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        using (var loeschen = await PostAsync(_s.Tobias, "/api/ich/profil/loeschen"))
        {
            Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        }

        await _s.ZustellenAsync();

        var zeile = Assert.Single((await ListeAsync()).GetProperty("zeilen").EnumerateArray());
        Assert.Equal((_s.Mia, true, (Guid?)GeloeschtePerson.Id), (Werte(zeile).PersonId, Werte(zeile).Bezahlt, Werte(zeile).AbgehaktVon));
        Assert.Empty(await FinanzenSpaltenMitAsync(_s.TobiasId));
    }

    [Fact]
    public async Task Person_geloescht_Mia_der_Vermerk_bleibt_mit_Platzhalter_steht_aber_nicht_mehr_in_der_Liste()
    {
        await ZeileAsync(BezahltAsync(_s.Mia, _s.Tobias));

        // Katrin löscht ihr Konto; als einziger Elternteil gehen Mia und Emma mit (AD-7).
        await AnmeldenAsync(_s.Factory, _s.Katrin, FamilienHelfer.Katrin);
        using (var loeschen = await PostAsync(_s.Katrin, "/api/ich/profil/loeschen"))
        {
            Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        }

        await _s.ZustellenAsync();

        Assert.Equal(1L, await AnzahlAsync("\"PersonId\" = @p AND \"AbgehaktVon\" = @v", ("p", GeloeschtePerson.Id), ("v", _s.TobiasId)));
        Assert.Empty(await FinanzenSpaltenMitAsync(_s.Mia));
        Assert.Empty((await ListeAsync()).GetProperty("zeilen").EnumerateArray());
    }

    /// <summary>Alle Spalten vom Typ uuid im Schema finanzen (außer Outbox und Inbox), die die Kennung noch enthalten.</summary>
    private async Task<List<string>> FinanzenSpaltenMitAsync(Guid kennung)
    {
        var spalten = new List<(string Tabelle, string Spalte)>();
        await using var db = new NpgsqlConnection(_s.Verbindung);
        await db.OpenAsync(Abbruch);
        await using (var befehl = new NpgsqlCommand(
                         "SELECT table_name, column_name FROM information_schema.columns WHERE data_type = 'uuid' AND table_schema = 'finanzen' " +
                         "AND table_name NOT IN ('outbox', 'outbox_zustellung', 'inbox', 'inbox_stand')",
                         db))
        await using (var leser = await befehl.ExecuteReaderAsync(Abbruch))
        {
            while (await leser.ReadAsync(Abbruch))
            {
                spalten.Add((leser.GetString(0), leser.GetString(1)));
            }
        }

        Assert.Contains(spalten, s => s == ("beitrag_vermerk", "PersonId"));
        Assert.Contains(spalten, s => s == ("beitrag_vermerk", "AbgehaktVon"));
        var treffer = new List<string>();
        foreach (var (tabelle, spalte) in spalten)
        {
            await using var zaehlen = new NpgsqlCommand($"SELECT count(*) FROM finanzen.\"{tabelle}\" WHERE \"{spalte}\" = @k", db);
            zaehlen.Parameters.AddWithValue("k", kennung);
            if ((long)(await zaehlen.ExecuteScalarAsync(Abbruch))! > 0)
            {
                treffer.Add($"{tabelle}.{spalte}");
            }
        }

        return treffer;
    }
}
