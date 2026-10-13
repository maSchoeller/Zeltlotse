using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.DokumentHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-9, Schutzkonzept (CAP-16, FR-30, AD-16, AD-19, AD-30) gegen echtes PostgreSQL und S3: <c>GET</c> mit dem Team und wer gelesen
/// hat, <c>PATCH</c> des Dokuments nur für die Leitung (eigenes oder eingebundenes Dokument der Freizeit), Bestätigen per <c>POST</c>,
/// die Regel „wirksames Schutzkonzept“ (gelöschtes Dokument gilt als keins), Austausch, abgeschlossene Freizeit, die Pflichtfälle der
/// Freizeit-Route, Löschen mit der Person und nach der Frist sowie die Datenauskunft.
/// </summary>
public sealed class SchutzkonzeptTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres, s3);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/sicherheit/schutzkonzept";

    private async Task<JsonElement> LesenAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(Pfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private static Dictionary<string, object?> Koerper(uint version, object? alterWert, object? dokumentId) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, ["dokumentId"] = dokumentId };

    private Task<HttpResponseMessage> PatchAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, Pfad(freizeit ?? _s.X), koerper);

    /// <summary>Setzt das Dokument mit dem aktuellen wirksamen Wert als altem Wert; erwartet <c>200</c>.</summary>
    private async Task<JsonElement> SetzenAsync(Guid? dokument, Guid? freizeit = null)
    {
        var konzept = await LesenAsync(freizeit: freizeit);
        var alt = konzept.GetProperty("dokumentId").ValueKind == JsonValueKind.Null ? null : konzept.GetProperty("dokumentId").GetString();
        using var antwort = await PatchAsync(Koerper(konzept.GetProperty("version").GetUInt32(), alt, dokument), freizeit: freizeit);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> GelesenAsync(HttpClient client, Guid dokument, Guid? freizeit = null) =>
        PostAsync(client, $"{Pfad(freizeit ?? _s.X)}/gelesen", new SchutzkonzeptGelesen(dokument));

    private Task<Guid> DokumentAsync(string dateiname, Guid? freizeit = null) =>
        HochgeladenAsync(_s.Miriam, FreizeitPfad(freizeit ?? _s.X), Pdf, dateiname);

    private static JsonElement Leser(JsonElement konzept, Guid person) =>
        konzept.GetProperty("team").EnumerateArray().Single(l => l.GetProperty("personId").GetGuid() == person);

    private Task<long> BestaetigungenAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM sicherheit.schutzkonzept_bestaetigung WHERE {bedingung}", parameter);

    // ---------- Lesen, Anhängen ----------

    [Fact]
    public async Task Leer_ohne_Schutzkonzept_mit_dem_Team_nach_Nachname_niemand_hat_gelesen()
    {
        var konzept = await LesenAsync();

        Assert.Equal(["dateiart", "dokumentId", "selbstGelesenAm", "team", "titel", "version"], Namen(konzept));
        foreach (var feld in new[] { "dokumentId", "titel", "dateiart", "selbstGelesenAm" })
        {
            Assert.Equal(JsonValueKind.Null, konzept.GetProperty(feld).ValueKind);
        }

        Assert.Equal(0u, konzept.GetProperty("version").GetUInt32());
        var team = konzept.GetProperty("team").EnumerateArray().ToList();
        Assert.Equal(
            [(_s.TobiasId, "Tobias", "Wanderer", false), (_s.MiriamId, "Miriam", "Zeltner", true)],
            team.Select(l => (l.GetProperty("personId").GetGuid(), l.GetProperty("vorname").GetString(), l.GetProperty("nachname").GetString(), l.GetProperty("leitung").GetBoolean())));
        Assert.All(team, l => Assert.Equal(JsonValueKind.Null, l.GetProperty("gelesenAm").ValueKind));
        Assert.Equal(["gelesenAm", "leitung", "nachname", "personId", "vorname"], Namen(team[0]));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.schutzkonzept"));
    }

    [Fact]
    public async Task Anhaengen_eines_eigenen_Dokuments_dann_liefert_GET_Titel_und_Dateiart()
    {
        var d1 = await DokumentAsync("Schutzkonzept.pdf");

        var antwort = await SetzenAsync(d1);

        Assert.Equal(["dokumentId", "version"], Namen(antwort));
        Assert.Equal(d1, antwort.GetProperty("dokumentId").GetGuid());
        var konzept = await LesenAsync();
        Assert.Equal(
            (d1, "Schutzkonzept", "PDF", antwort.GetProperty("version").GetUInt32()),
            (konzept.GetProperty("dokumentId").GetGuid(), konzept.GetProperty("titel").GetString(), konzept.GetProperty("dateiart").GetString(),
                konzept.GetProperty("version").GetUInt32()));
        Assert.NotEqual(0u, konzept.GetProperty("version").GetUInt32());
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.schutzkonzept WHERE \"OrganisationId\" = @o", ("o", _s.A)));
        // Das Team liest dasselbe.
        Assert.Equal(d1, (await LesenAsync(_s.Tobias)).GetProperty("dokumentId").GetGuid());
    }

    [Fact]
    public async Task Eingebunden_ein_Org_Dokument_als_Schutzkonzept_Titel_aus_Organisationen()
    {
        var orgDokument = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Schutzkonzept Jugendwerk.pdf");
        using var eingebunden = await PostAsync(_s.Miriam, $"{FreizeitPfad(_s.X)}/einbindungen", new DokumentEinbinden(orgDokument));
        Assert.Equal(HttpStatusCode.Created, eingebunden.StatusCode);
        var einbindung = (await JsonAsync(eingebunden)).GetProperty("id").GetGuid();

        await SetzenAsync(einbindung);

        var konzept = await LesenAsync();
        Assert.Equal(
            (einbindung, "Schutzkonzept Jugendwerk", "PDF"),
            (konzept.GetProperty("dokumentId").GetGuid(), konzept.GetProperty("titel").GetString(), konzept.GetProperty("dateiart").GetString()));
    }

    [Fact]
    public async Task Feldfehler_Dokument_aus_Z_unbekannt_kein_Guid_zwei_Felder_kein_Feld_sind_400()
    {
        var ausZ = await DokumentAsync("Z.pdf", _s.Z);
        var faelle = new (object Koerper, string Feld)[]
        {
            (Koerper(0, null, ausZ), "dokumentId"),
            (Koerper(0, null, Guid.CreateVersion7()), "dokumentId"),
            (Koerper(0, null, "abc"), "dokumentId"),
            (Koerper(0, null, 5), "dokumentId"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["dokumentId"] = null, ["titel"] = "x" }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["geheim"] = "x" }, "feld"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await PatchAsync(koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            var fehler = (await ProblemAsync(antwort)).GetProperty("errors");
            Assert.True(fehler.TryGetProperty(feld, out var texte), feld);
            if (koerper is Dictionary<string, object?> k && k.GetValueOrDefault("dokumentId") is Guid)
            {
                Assert.Equal("Dieses Dokument gibt es in der Freizeit nicht.", texte[0].GetString());
            }
        }

        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.schutzkonzept WHERE \"DokumentId\" IS NOT NULL"));
    }

    [Fact]
    public async Task Konflikt_bei_veraltetem_alten_Wert()
    {
        var (d1, d2) = (await DokumentAsync("Eins.pdf"), await DokumentAsync("Zwei.pdf"));
        var erste = await SetzenAsync(d1);
        await SetzenAsync(d2);

        using var konflikt = await PatchAsync(Koerper(erste.GetProperty("version").GetUInt32(), d1, null));

        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));
        Assert.Equal(d2, (await LesenAsync()).GetProperty("dokumentId").GetGuid());
        using var ohneZeile = await PatchAsync(Koerper(0, d1, null), freizeit: _s.Z);
        Assert.Equal(HttpStatusCode.Conflict, ohneZeile.StatusCode);
    }

    // ---------- Bestätigen ----------

    [Fact]
    public async Task Gelesen_Tobias_bestaetigt_GET_zeigt_es_bei_ihm_und_fuer_ihn_selbst_nicht_fuer_Miriam()
    {
        var d1 = await DokumentAsync("Schutzkonzept.pdf");
        await SetzenAsync(d1);
        var jetzt = _s.VolleSekunde();

        using var antwort = await GelesenAsync(_s.Tobias, d1);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        var tobias = await LesenAsync(_s.Tobias);
        Assert.Equal(jetzt, tobias.GetProperty("selbstGelesenAm").GetDateTimeOffset());
        Assert.Equal(jetzt, Leser(tobias, _s.TobiasId).GetProperty("gelesenAm").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, Leser(tobias, _s.MiriamId).GetProperty("gelesenAm").ValueKind);
        var miriam = await LesenAsync();
        Assert.Equal(JsonValueKind.Null, miriam.GetProperty("selbstGelesenAm").ValueKind);
        Assert.Equal(jetzt, Leser(miriam, _s.TobiasId).GetProperty("gelesenAm").GetDateTimeOffset());
        Assert.Equal(1L, await BestaetigungenAsync(
            "\"PersonId\" = @p AND \"DokumentId\" = @d AND \"FreizeitId\" = @f AND \"OrganisationId\" = @o", ("p", _s.TobiasId), ("d", d1), ("f", _s.X), ("o", _s.A)));
    }

    [Fact]
    public async Task Doppelt_auch_gleichzeitig_ergibt_beide_204_und_eine_Zeile()
    {
        var d1 = await DokumentAsync("Schutzkonzept.pdf");
        await SetzenAsync(d1);

        var gleichzeitig = await Task.WhenAll(GelesenAsync(_s.Tobias, d1), GelesenAsync(_s.Tobias, d1));
        using var spaeter = await GelesenAsync(_s.Tobias, d1);

        Assert.All(gleichzeitig.Append(spaeter), a => Assert.Equal(HttpStatusCode.NoContent, a.StatusCode));
        foreach (var antwort in gleichzeitig)
        {
            antwort.Dispose();
        }

        Assert.Equal(1L, await BestaetigungenAsync());
    }

    [Fact]
    public async Task Falsches_Dokument_oder_ohne_Schutzkonzept_ist_409_konflikt_ohne_Dokument_400()
    {
        var (d1, d2) = (await DokumentAsync("Eins.pdf"), await DokumentAsync("Zwei.pdf"));

        using var ohne = await GelesenAsync(_s.Tobias, d1);
        Assert.Equal(HttpStatusCode.Conflict, ohne.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(ohne)));

        await SetzenAsync(d1);
        using var falsch = await GelesenAsync(_s.Tobias, d2);
        Assert.Equal(HttpStatusCode.Conflict, falsch.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(falsch)));

        using var leer = await PostAsync(_s.Tobias, $"{Pfad(_s.X)}/gelesen", new Dictionary<string, object?>());
        Assert.Equal(HttpStatusCode.BadRequest, leer.StatusCode);
        Assert.True((await ProblemAsync(leer)).GetProperty("errors").TryGetProperty("dokumentId", out _));
        Assert.Equal(0L, await BestaetigungenAsync());
    }

    [Fact]
    public async Task Austausch_verlangt_eine_neue_Bestaetigung()
    {
        var (d1, d2) = (await DokumentAsync("Eins.pdf"), await DokumentAsync("Zwei.pdf"));
        await SetzenAsync(d1);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Tobias, d1)).StatusCode);

        await SetzenAsync(d2);

        var konzept = await LesenAsync(_s.Tobias);
        Assert.Equal(JsonValueKind.Null, konzept.GetProperty("selbstGelesenAm").ValueKind);
        Assert.Equal(JsonValueKind.Null, Leser(konzept, _s.TobiasId).GetProperty("gelesenAm").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await GelesenAsync(_s.Tobias, d1)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Tobias, d2)).StatusCode);
        Assert.NotEqual(JsonValueKind.Null, Leser(await LesenAsync(), _s.TobiasId).GetProperty("gelesenAm").ValueKind);
        Assert.Equal(2L, await BestaetigungenAsync());
    }

    [Fact]
    public async Task Verschwunden_ein_geloeschtes_Dokument_gilt_als_kein_Schutzkonzept()
    {
        var d1 = await DokumentAsync("Schutzkonzept.pdf");
        await SetzenAsync(d1);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Tobias, d1)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{FreizeitPfad(_s.X)}/{d1}")).StatusCode);

        var konzept = await LesenAsync(_s.Tobias);
        foreach (var feld in new[] { "dokumentId", "titel", "dateiart", "selbstGelesenAm" })
        {
            Assert.Equal(JsonValueKind.Null, konzept.GetProperty(feld).ValueKind);
        }

        Assert.Equal(JsonValueKind.Null, Leser(konzept, _s.TobiasId).GetProperty("gelesenAm").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await GelesenAsync(_s.Tobias, d1)).StatusCode);
        using var antwort = await PatchAsync(Koerper(konzept.GetProperty("version").GetUInt32(), null, null));
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(antwort)).GetProperty("dokumentId").ValueKind);
    }

    [Fact]
    public async Task Verschwunden_ein_geloeschtes_Org_Dokument_gilt_als_kein_Schutzkonzept()
    {
        var orgDokument = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Schutzkonzept Jugendwerk.pdf");
        using var eingebunden = await PostAsync(_s.Miriam, $"{FreizeitPfad(_s.X)}/einbindungen", new DokumentEinbinden(orgDokument));
        Assert.Equal(HttpStatusCode.Created, eingebunden.StatusCode);
        var einbindung = (await JsonAsync(eingebunden)).GetProperty("id").GetGuid();
        await SetzenAsync(einbindung);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Tobias, einbindung)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{orgDokument}")).StatusCode);

        var konzept = await LesenAsync(_s.Tobias);
        foreach (var feld in new[] { "dokumentId", "titel", "dateiart", "selbstGelesenAm" })
        {
            Assert.Equal(JsonValueKind.Null, konzept.GetProperty(feld).ValueKind);
        }

        Assert.All(konzept.GetProperty("team").EnumerateArray(), l => Assert.Equal(JsonValueKind.Null, l.GetProperty("gelesenAm").ValueKind));
        using var post = await GelesenAsync(_s.Tobias, einbindung);
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(post)));
        using var antwort = await PatchAsync(Koerper(konzept.GetProperty("version").GetUInt32(), null, null));
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    [Fact]
    public async Task Abgeschlossen_PATCH_und_POST_409_freizeit_abgeschlossen_GET_200()
    {
        var (d1, d2) = (await DokumentAsync("Eins.pdf"), await DokumentAsync("Zwei.pdf"));
        var gesetzt = await SetzenAsync(d1);
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        using var patch = await PatchAsync(Koerper(gesetzt.GetProperty("version").GetUInt32(), d1, d2));
        using var post = await GelesenAsync(_s.Tobias, d1);

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (patch.StatusCode, post.StatusCode));
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(patch)));
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(post)));
        Assert.Equal(d1, (await LesenAsync(_s.Tobias)).GetProperty("dokumentId").GetGuid());
        Assert.Equal(0L, await BestaetigungenAsync());
    }

    // ---------- Rechte (AD-30) ----------

    [Fact]
    public async Task Rechte_Miriam_Tobias_Andrea_fremde_Organisation_ohne_Sitzung()
    {
        var d1 = await DokumentAsync("Schutzkonzept.pdf");
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        var lesen = new List<HttpStatusCode>();
        var aendern = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in faelle)
        {
            using var get = await client.GetAsync(Pfad(freizeit), Abbruch);
            lesen.Add(get.StatusCode);
            using var patch = await PatchAsync(Koerper(0, null, d1), client, freizeit);
            aendern.Add(patch.StatusCode);
        }

        var bestaetigen = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in faelle)
        {
            using var post = await GelesenAsync(client, d1, freizeit);
            bestaetigen.Add(post.StatusCode);
        }

        HttpStatusCode[] erwartet(HttpStatusCode erster, HttpStatusCode zweiter) =>
            [erster, zweiter, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized];
        Assert.Equal(erwartet(HttpStatusCode.OK, HttpStatusCode.OK), lesen);
        // Miriam setzt beim ersten Durchlauf D1; Tobias (Team) darf nicht.
        Assert.Equal(erwartet(HttpStatusCode.OK, HttpStatusCode.Forbidden), aendern);
        Assert.Equal(erwartet(HttpStatusCode.NoContent, HttpStatusCode.NoContent), bestaetigen);
        Assert.Equal(2L, await BestaetigungenAsync());
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Person_geloescht_ihre_Bestaetigungen_sind_weg()
    {
        var d1 = await DokumentAsync("Schutzkonzept.pdf");
        await SetzenAsync(d1);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Tobias, d1)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Miriam, d1)).StatusCode);

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_s.Tobias, "/api/ich/profil/loeschen")).StatusCode);
        await _s.ZustellenAsync();

        Assert.Equal(0L, await BestaetigungenAsync("\"PersonId\" = @p", ("p", _s.TobiasId)));
        Assert.Equal(1L, await BestaetigungenAsync("\"PersonId\" = @p", ("p", _s.MiriamId)));
    }

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Bestaetigungen_der_abgelaufenen_Freizeit_die_anderen_bleiben()
    {
        var (inX, inZ) = (await DokumentAsync("X.pdf"), await DokumentAsync("Z.pdf", _s.Z));
        await SetzenAsync(inX);
        await SetzenAsync(inZ, _s.Z);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Tobias, inX)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Miriam, inX)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Miriam, inZ, _s.Z)).StatusCode);

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "SicherheitLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await BestaetigungenAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await BestaetigungenAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    [Fact]
    public async Task Auskunft_Tobias_sieht_den_Abschnitt_Schutzkonzept_mit_seiner_Bestaetigung()
    {
        var d1 = await DokumentAsync("Schutzkonzept.pdf");
        await SetzenAsync(d1);
        var keine = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(keine, a => a.Titel == "Schutzkonzept");
        var jetzt = _s.VolleSekunde();
        Assert.Equal(HttpStatusCode.NoContent, (await GelesenAsync(_s.Tobias, d1)).StatusCode);

        var auskunft = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        var zeile = Assert.Single(Assert.Single(auskunft, a => a.Titel == "Schutzkonzept").Zeilen);
        Assert.Equal(("Als gelesen bestätigt", AuskunftFormat.Zeitpunkt(jetzt)), (zeile.Feld, zeile.Wert));
        var miriam = (await _s.Miriam.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(miriam, a => a.Titel == "Schutzkonzept");
    }
}
