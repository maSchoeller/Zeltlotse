using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-8, Sicherheitskette (FR-29, AD-19, AD-30) gegen echtes PostgreSQL: <c>GET</c> für Leitung und Team (ohne Zeile alles leer),
/// <c>PATCH</c> mit genau einem Feld nur für die Leitung, Leeren, Feldfehler, Konfliktregel, gleichzeitige erste Anlage, abgeschlossene
/// Freizeit und die Pflichtfälle der Freizeit-Route.
/// </summary>
public sealed class SicherheitsketteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/sicherheit/sicherheitskette";

    private async Task<JsonElement> LesenAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(Pfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> PatchAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, Pfad(freizeit ?? _s.X), koerper);

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    /// <summary>Setzt ein Feld mit dem aktuellen Wert als altem Wert und liefert die Antwort als JSON.</summary>
    private async Task<JsonElement> SetzenAsync(string feld, string? wert)
    {
        var kette = await LesenAsync();
        using var antwort = await PatchAsync(Koerper(kette.GetProperty("version").GetUInt32(), kette.GetProperty(feld).GetString(), feld, wert));
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    [Fact]
    public async Task Leer_lesen_ohne_Zeile_alle_Felder_null_nicht_eingerichtet_Version_0()
    {
        var kette = await LesenAsync();

        Assert.Equal(["eingerichtet", "heimatName", "heimatTelefon", "leitungTelefon", "version", "wasTun"], Namen(kette));
        foreach (var feld in new[] { "wasTun", "leitungTelefon", "heimatName", "heimatTelefon" })
        {
            Assert.Equal(JsonValueKind.Null, kette.GetProperty(feld).ValueKind);
        }

        Assert.False(kette.GetProperty("eingerichtet").GetBoolean());
        Assert.Equal(0u, kette.GetProperty("version").GetUInt32());
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.sicherheitskette"));
    }

    [Fact]
    public async Task Einrichten_drei_Felder_nacheinander_dann_eingerichtet_und_das_Team_liest_dieselben_Werte()
    {
        var versionen = new List<uint>();
        foreach (var (feld, wert) in new[] { ("wasTun", " Ruhe bewahren.\nErste Hilfe leisten. "), ("leitungTelefon", "0123 456782"), ("heimatTelefon", "0123 456783") })
        {
            var antwort = await SetzenAsync(feld, wert);
            Assert.Equal(new[] { feld, "version" }.Order(StringComparer.Ordinal), Namen(antwort));
            Assert.Equal(wert.Trim(), antwort.GetProperty(feld).GetString());
            versionen.Add(antwort.GetProperty("version").GetUInt32());
        }

        Assert.Equal(3, versionen.Distinct().Count());
        var kette = await LesenAsync();
        Assert.True(kette.GetProperty("eingerichtet").GetBoolean());
        Assert.Equal(versionen[^1], kette.GetProperty("version").GetUInt32());
        Assert.Equal(JsonValueKind.Null, kette.GetProperty("heimatName").ValueKind);

        var team = await LesenAsync(_s.Tobias);
        Assert.Equal(kette.GetRawText(), team.GetRawText());
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.sicherheitskette"));
    }

    [Fact]
    public async Task Leeren_mit_Leerzeichen_setzt_das_Feld_auf_null_und_die_Kette_ist_nicht_mehr_eingerichtet()
    {
        await SetzenAsync("wasTun", "Ruhe bewahren.");
        await SetzenAsync("leitungTelefon", "0123 456782");
        await SetzenAsync("heimatTelefon", "0123 456783");

        var antwort = await SetzenAsync("heimatTelefon", "  ");

        Assert.Equal(JsonValueKind.Null, antwort.GetProperty("heimatTelefon").ValueKind);
        var kette = await LesenAsync();
        Assert.Equal(JsonValueKind.Null, kette.GetProperty("heimatTelefon").ValueKind);
        Assert.False(kette.GetProperty("eingerichtet").GetBoolean());
    }

    [Fact]
    public async Task Feldfehler_Telefon_ohne_Ziffer_zu_lang_zwei_Felder_kein_Feld_und_unbekanntes_Feld_sind_400()
    {
        var faelle = new (object Koerper, string Feld)[]
        {
            (Koerper(0, null, "leitungTelefon", "abc"), "leitungTelefon"),
            (Koerper(0, null, "wasTun", new string('a', 2001)), "wasTun"),
            (Koerper(0, null, "leitungTelefon", new string('1', 41)), "leitungTelefon"),
            (Koerper(0, null, "heimatTelefon", "0123 456 Durchwahl 5"), "heimatTelefon"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["wasTun"] = "a", ["heimatName"] = "b" }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null }, "feld"),
            (new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["geheim"] = "x" }, "feld"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await PatchAsync(koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.sicherheitskette"));
    }

    [Fact]
    public async Task Konflikt_nur_bei_veraltetem_alten_Wert_desselben_Felds_ein_anderes_Feld_wird_angewendet()
    {
        var erste = await SetzenAsync("heimatName", "Petra Heimann");
        var alteVersion = erste.GetProperty("version").GetUInt32();
        await SetzenAsync("heimatName", "Petra Heimann (Gemeindebüro)");

        using var konflikt = await PatchAsync(Koerper(alteVersion, "Petra Heimann", "heimatName", "Jemand"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        using var anderesFeld = await PatchAsync(Koerper(alteVersion, null, "heimatTelefon", "0123 456783"));
        Assert.Equal(HttpStatusCode.OK, anderesFeld.StatusCode);

        var kette = await LesenAsync();
        Assert.Equal(("Petra Heimann (Gemeindebüro)", "0123 456783"), (kette.GetProperty("heimatName").GetString(), kette.GetProperty("heimatTelefon").GetString()));
    }

    [Fact]
    public async Task Erste_Aenderung_mit_altem_Wert_ohne_Zeile_ist_ein_Konflikt()
    {
        using var antwort = await PatchAsync(Koerper(0, "x", "heimatName", "Petra"));

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        Assert.Equal(JsonValueKind.Null, (await LesenAsync()).GetProperty("heimatName").ValueKind);
    }

    [Fact]
    public async Task Zwei_erste_Aenderungen_gleichzeitig_gelingen_beide_und_es_gibt_genau_eine_Zeile()
    {
        var antworten = await Task.WhenAll(
            PatchAsync(Koerper(0, null, "wasTun", "Ruhe bewahren.")),
            PatchAsync(Koerper(0, null, "leitungTelefon", "0123 456782")));

        Assert.All(antworten, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
        foreach (var antwort in antworten)
        {
            antwort.Dispose();
        }

        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM sicherheit.sicherheitskette"));
        var kette = await LesenAsync();
        Assert.Equal(("Ruhe bewahren.", "0123 456782"), (kette.GetProperty("wasTun").GetString(), kette.GetProperty("leitungTelefon").GetString()));
    }

    [Fact]
    public async Task Abgeschlossene_Freizeit_PATCH_409_freizeit_abgeschlossen_GET_200()
    {
        await SetzenAsync("wasTun", "Ruhe bewahren.");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));
        var kette = await LesenAsync();

        using var antwort = await PatchAsync(Koerper(kette.GetProperty("version").GetUInt32(), null, "heimatName", "Petra"));

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
        Assert.Equal(JsonValueKind.Null, (await LesenAsync()).GetProperty("heimatName").ValueKind);
    }

    [Fact]
    public async Task Rechte_GET_Leitung_und_Team_200_Org_Admin_fremde_Organisation_und_Leitung_nur_von_Z_404()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);

        var status = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (leitungZ, _s.X) })
        {
            using var antwort = await client.GetAsync(Pfad(freizeit), Abbruch);
            status.Add(antwort.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], status);
        Assert.Equal(HttpStatusCode.OK, (await leitungZ.GetAsync(Pfad(_s.Z), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Rechte_PATCH_Leitung_200_Team_403_Org_Admin_404_fremde_Organisation_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");

        var status = new List<HttpStatusCode>();
        foreach (var (client, freizeit, feld) in new[]
                 {
                     (_s.Tobias, _s.X, "wasTun"), (_s.Andrea, _s.X, "wasTun"), (_s.Miriam, fremde, "wasTun"), (_s.Anonym, _s.X, "wasTun"),
                     (_s.Miriam, _s.X, "heimatName"),
                 })
        {
            using var antwort = await PatchAsync(Koerper(0, null, feld, "x 1"), client, freizeit);
            status.Add(antwort.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.OK], status);
        var kette = await LesenAsync();
        Assert.Equal(JsonValueKind.Null, kette.GetProperty("wasTun").ValueKind);
        Assert.Equal("x 1", kette.GetProperty("heimatName").GetString());
    }
}
