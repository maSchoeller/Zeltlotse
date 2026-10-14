using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-7, Module pro Freizeit (CAP-26, FR-44, AD-19, AD-20, AD-30) gegen echtes PostgreSQL: neue Freizeit ohne Module, Schalten per
/// Autosave mit Konfliktregel je Modul, ungültige Körper, Rechte-Matrix, Schreibschutz nach dem Abschluss, der Kontext für Leitung und Team,
/// die Wirkung auf einen Endpunkt mit <c>.GehoertZuSchalter(Verpflegung)</c> (Test-Endpunkt), unveränderte Daten und der Mandant. Freizeit X
/// (Organisation A, Org-Admin Andrea, Leitung Miriam, Team Tobias), Freizeit Z gehört Organisation B (Org-Admin Lea, Leitung Miriam).
/// </summary>
public sealed class ModuleTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres, mitTestEndpunkten: true);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/module";

    private static string SchalterPfad(Guid freizeit) => $"/api/test/schalter/{freizeit}";

    private async Task<JsonElement> ModuleAsync(Guid? freizeit = null)
    {
        using var antwort = await _s.Miriam.GetAsync(Pfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> PatchAsync(string koerper, Guid? freizeit = null, HttpClient? client = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, Pfad(freizeit ?? _s.X), koerper);

    /// <summary>Schaltet ein Modul mit dem aktuellen Stand als altem Wert.</summary>
    private async Task SchaltenAsync(string feld, bool an, Guid? freizeit = null)
    {
        var stand = await ModuleAsync(freizeit);
        var alt = stand.GetProperty(feld).GetBoolean() ? "true" : "false";
        using var antwort = await PatchAsync(
            $$"""{"version":{{stand.GetProperty("version").GetUInt32()}},"alterWert":"{{alt}}","{{feld}}":"{{(an ? "true" : "false")}}"}""", freizeit);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private static async Task<List<string?>> SchalterAsync(HttpClient client, Guid freizeit)
    {
        using var antwort = await client.GetAsync($"/api/freizeiten/{freizeit}/kontext", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).GetProperty("schalter").EnumerateArray().Select(s => s.GetString())];
    }

    [Fact]
    public async Task Eine_neue_Freizeit_nutzt_kein_Modul_und_der_Kontext_hat_keine_Schalter()
    {
        var json = await ModuleAsync();

        Assert.Equal(["finanzen", "teilnehmerZugang", "verpflegung", "version"], Namen(json));
        Assert.Equal((false, false, false), (json.GetProperty("verpflegung").GetBoolean(), json.GetProperty("finanzen").GetBoolean(), json.GetProperty("teilnehmerZugang").GetBoolean()));
        Assert.Empty(await SchalterAsync(_s.Miriam, _s.X));
        Assert.Empty(await SchalterAsync(_s.Tobias, _s.X));
    }

    [Fact]
    public async Task Miriam_schaltet_Verpflegung_ein_und_aus_und_Tobias_sieht_es_im_Kontext()
    {
        var version = (await ModuleAsync()).GetProperty("version").GetUInt32();

        using var an = await PatchAsync($$"""{"version":{{version}},"alterWert":"false","verpflegung":"true"}""");

        Assert.Equal(HttpStatusCode.OK, an.StatusCode);
        var json = await JsonAsync(an);
        Assert.Equal(["verpflegung", "version"], Namen(json));
        Assert.Equal("true", json.GetProperty("verpflegung").GetString());
        Assert.NotEqual(version, json.GetProperty("version").GetUInt32());
        Assert.Equal(["Verpflegung"], await SchalterAsync(_s.Miriam, _s.X));
        Assert.Equal(["Verpflegung"], await SchalterAsync(_s.Tobias, _s.X));
        Assert.True((await ModuleAsync()).GetProperty("verpflegung").GetBoolean());

        await SchaltenAsync("verpflegung", an: false);

        Assert.Empty(await SchalterAsync(_s.Tobias, _s.X));
    }

    [Fact]
    public async Task Der_Kontext_nennt_die_Schalter_in_Enum_Reihenfolge()
    {
        await SchaltenAsync("teilnehmerZugang", an: true);
        await SchaltenAsync("verpflegung", an: true);
        await SchaltenAsync("finanzen", an: true);

        Assert.Equal(["Verpflegung", "Finanzen", "TeilnehmerZugang"], await SchalterAsync(_s.Tobias, _s.X));
    }

    [Fact]
    public async Task Ein_veralteter_alter_Wert_desselben_Moduls_ergibt_409_ein_anderes_Modul_mit_alter_Version_wird_angewendet()
    {
        var version = (await ModuleAsync()).GetProperty("version").GetUInt32();
        using var an = await PatchAsync($$"""{"version":{{version}},"alterWert":"false","verpflegung":"true"}""");
        Assert.Equal(HttpStatusCode.OK, an.StatusCode);

        using var konflikt = await PatchAsync($$"""{"version":{{version}},"alterWert":"false","verpflegung":"false"}""");
        using var anderes = await PatchAsync($$"""{"version":{{version}},"alterWert":"false","finanzen":"true"}""");

        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, (await ProblemAsync(konflikt)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, anderes.StatusCode);
        Assert.Equal("true", (await JsonAsync(anderes)).GetProperty("finanzen").GetString());
        Assert.Equal(["Verpflegung", "Finanzen"], await SchalterAsync(_s.Miriam, _s.X));
    }

    [Theory]
    [InlineData("""{"version":1,"alterWert":"false","verpflegung":"ja"}""", "verpflegung")]
    [InlineData("""{"version":1,"alterWert":"false","verpflegung":null}""", "verpflegung")]
    [InlineData("""{"version":1,"alterWert":"false","verpflegung":true}""", "verpflegung")]
    [InlineData("""{"version":1,"alterWert":"false","verpflegung":"true","finanzen":"true"}""", "feld")]
    [InlineData("""{"version":1,"alterWert":"false","kueche":"true"}""", "feld")]
    [InlineData("""{}""", "version")]
    public async Task Ungueltige_Koerper_ergeben_400_am_Feld_und_aendern_nichts(string koerper, string feld)
    {
        using var antwort = await PatchAsync(koerper);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal([feld], (await ProblemAsync(antwort)).GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Empty(await SchalterAsync(_s.Miriam, _s.X));
    }

    [Fact]
    public async Task Team_bekommt_403_Org_Admin_ohne_Rolle_fremde_Organisation_und_unbekannte_Freizeit_404_ohne_Sitzung_401()
    {
        var version = (await ModuleAsync()).GetProperty("version").GetUInt32();
        var patch = $$"""{"version":{{version}},"alterWert":"false","verpflegung":"true"}""";

        foreach (var (client, freizeit, status) in new (HttpClient, Guid, HttpStatusCode)[]
                 {
                     (_s.Tobias, _s.X, HttpStatusCode.Forbidden),
                     (_s.Andrea, _s.X, HttpStatusCode.NotFound),
                     (_s.Lea, _s.X, HttpStatusCode.NotFound),
                     (_s.Gerd, _s.X, HttpStatusCode.NotFound),
                     (_s.Miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
                 })
        {
            using var laden = await client.GetAsync(Pfad(freizeit), Abbruch);
            using var aendern = await PatchAsync(patch, freizeit, client);

            Assert.Equal([status, status], [laden.StatusCode, aendern.StatusCode]);
        }

        using var anonymLaden = await _s.Anonym.GetAsync(Pfad(_s.X), Abbruch);
        using var anonymAendern = await PatchAsync(patch, client: _s.Anonym);
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized], [anonymLaden.StatusCode, anonymAendern.StatusCode]);
        Assert.Empty(await SchalterAsync(_s.Miriam, _s.X));
    }

    [Fact]
    public async Task Eine_abgeschlossene_Freizeit_ist_nur_lesbar()
    {
        var version = (await ModuleAsync()).GetProperty("version").GetUInt32();
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        using var antwort = await PatchAsync($$"""{"version":{{version}},"alterWert":"false","verpflegung":"true"}""");

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, (await ProblemAsync(antwort)).GetProperty("code").GetString());
        Assert.False((await ModuleAsync()).GetProperty("verpflegung").GetBoolean());
    }

    [Fact]
    public async Task Ein_Endpunkt_des_Schalters_antwortet_aus_404_an_200_fuer_Leitung_und_Team_und_wieder_aus_404()
    {
        async Task<(HttpStatusCode Leitung, HttpStatusCode Team)> AbrufenAsync()
        {
            using var leitung = await _s.Miriam.GetAsync(SchalterPfad(_s.X), Abbruch);
            using var team = await _s.Tobias.GetAsync(SchalterPfad(_s.X), Abbruch);
            return (leitung.StatusCode, team.StatusCode);
        }

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), await AbrufenAsync());
        using (var aus = await _s.Miriam.GetAsync(SchalterPfad(_s.X), Abbruch))
        {
            Assert.Equal(404, (await ProblemAsync(aus)).GetProperty("status").GetInt32());
        }

        await SchaltenAsync("verpflegung", an: true);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), await AbrufenAsync());

        // Ohne Rolle (Org-Admin, Global Admin), fremde Organisation und unbekannte Freizeit bleiben 404, ohne Sitzung 401.
        foreach (var (client, freizeit) in new[] { (_s.Andrea, _s.X), (_s.Gerd, _s.X), (_s.Lea, _s.X), (_s.Miriam, Guid.CreateVersion7()) })
        {
            using var antwort = await client.GetAsync(SchalterPfad(freizeit), Abbruch);
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }

        using (var anonym = await _s.Anonym.GetAsync(SchalterPfad(_s.X), Abbruch))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonym.StatusCode);
        }

        await SchaltenAsync("verpflegung", an: false);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), await AbrufenAsync());
    }

    [Fact]
    public async Task Ohne_Rolle_bleibt_es_404_auch_wenn_der_Schalter_aus_ist_ohne_Sitzung_401()
    {
        foreach (var client in new[] { _s.Andrea, _s.Lea })
        {
            using var antwort = await client.GetAsync(SchalterPfad(_s.X), Abbruch);
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }

        using var anonym = await _s.Anonym.GetAsync(SchalterPfad(_s.X), Abbruch);
        Assert.Equal(HttpStatusCode.Unauthorized, anonym.StatusCode);
    }

    [Fact]
    public async Task GehoertZuSchalter_ohne_NurFreizeitRolle_ist_ein_Konfigurationsfehler()
    {
        using var antwort = await _s.Miriam.GetAsync($"/api/test/schalter-ohne-rolle/{_s.X}", Abbruch);

        Assert.Equal(HttpStatusCode.InternalServerError, antwort.StatusCode);
        Assert.Equal(nameof(InvalidOperationException), (await ProblemAsync(antwort)).GetProperty("ausnahmeTyp").GetString());
    }

    [Fact]
    public async Task An_und_Abschalten_laesst_Freizeitseite_und_Fragen_unveraendert_nur_die_Version_zaehlt()
    {
        var seite = await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/freizeitseite", Abbruch));
        using (var ort = await SendenAsync(
                   _s.Miriam, HttpMethod.Patch, $"/api/freizeiten/{_s.X}/freizeitseite",
                   $$"""{"version":{{seite.GetProperty("version").GetUInt32()}},"alterWert":null,"ort":"Kapf"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, ort.StatusCode);
        }

        var seiteVorher = await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/freizeitseite", Abbruch));
        var fragenVorher = await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/fragen", Abbruch));

        await SchaltenAsync("verpflegung", an: true);
        await SchaltenAsync("verpflegung", an: false);

        var seiteNachher = await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/freizeitseite", Abbruch));
        var fragenNachher = await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/fragen", Abbruch));
        Assert.Equal(OhneVersion(seiteVorher), OhneVersion(seiteNachher));
        Assert.Equal(OhneVersion(fragenVorher), OhneVersion(fragenNachher));
        Assert.Equal("Kapf", seiteNachher.GetProperty("ort").GetString());
        Assert.Equal("allergien,notfallkontakte,schwimmen", fragenNachher.GetProperty("teilnehmende").GetString());
        Assert.NotEqual(seiteVorher.GetProperty("version").GetUInt32(), seiteNachher.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task Schalter_gelten_je_Freizeit_im_eigenen_Mandanten()
    {
        await SchaltenAsync("verpflegung", an: true);

        Assert.Equal(["Verpflegung"], await SchalterAsync(_s.Miriam, _s.X));
        Assert.Empty(await SchalterAsync(_s.Miriam, _s.Z));
        Assert.False((await ModuleAsync(_s.Z)).GetProperty("verpflegung").GetBoolean());
        using var aufZ = await _s.Miriam.GetAsync(SchalterPfad(_s.Z), Abbruch);
        Assert.Equal(HttpStatusCode.NotFound, aufZ.StatusCode);
    }

    [Fact]
    public async Task Rolle_vor_Schalter_Team_bekommt_403_auch_bei_ausgeschaltetem_Schalter_die_Leitung_404_dann_200()
    {
        var pfad = $"/api/test/schalter-leitung/{_s.X}";

        using (var team = await _s.Tobias.GetAsync(pfad, Abbruch))
        using (var leitung = await _s.Miriam.GetAsync(pfad, Abbruch))
        {
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.NotFound), (team.StatusCode, leitung.StatusCode));
        }

        await SchaltenAsync("verpflegung", an: true);

        using var teamAn = await _s.Tobias.GetAsync(pfad, Abbruch);
        using var leitungAn = await _s.Miriam.GetAsync(pfad, Abbruch);
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.OK), (teamAn.StatusCode, leitungAn.StatusCode));
    }

    [Fact]
    public async Task SchalterAusAusnahme_der_Pipeline_ergibt_404_ohne_Details_wie_ein_abgeschalteter_Endpunkt()
    {
        using var ausnahme = await _s.Miriam.GetAsync($"/api/test/schalter-aus-ausnahme/{_s.X}", Abbruch);
        using var endpunkt = await _s.Miriam.GetAsync(SchalterPfad(_s.X), Abbruch);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (ausnahme.StatusCode, endpunkt.StatusCode));
        Assert.Equal("application/problem+json", ausnahme.Content.Headers.ContentType?.MediaType);
        var problem = await ProblemAsync(ausnahme);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("ausnahmeTyp", Namen(problem));
        Assert.DoesNotContain("errors", Namen(problem));
        Assert.Equal(Namen(await ProblemAsync(endpunkt)), Namen(problem));
    }

    /// <summary>
    /// Story 3-8: Reihenfolge nach AD-27 – Validierung, Schreibschutz, Funktions-Schalter, Transaktion und Outbox (die Inbox gilt nur für
    /// Ereignis-Empfänger).
    /// </summary>
    [Fact]
    public void Die_Schalter_Stufe_folgt_auf_Validierung_und_Schreibschutz_fuer_Benutzer_Commands_des_Hosts()
    {
        // Command und Ergebnis sind intern im Modul; die Stufen löst der generierte Mediator ebenso über GetServices auf.
        var modul = typeof(Zeltlotse.Freizeiten.FreizeitenModul).Assembly;
        var command = modul.GetType("Zeltlotse.Freizeiten.Module.ModulAendernCommand", throwOnError: true)!;
        var ergebnis = modul.GetType("Zeltlotse.Freizeiten.Module.ModulAenderung", throwOnError: true)!;
        using var scope = _s.Factory.Services.CreateScope();

        var stufen = scope.ServiceProvider.GetServices(typeof(global::Mediator.IPipelineBehavior<,>).MakeGenericType(command, ergebnis)).ToList();

        Assert.Equal(
            [
                typeof(Zeltlotse.SharedKernel.Pipeline.ValidierungsStufe<,>),
                typeof(Zeltlotse.SharedKernel.Pipeline.SchreibschutzStufe<,>),
                typeof(Zeltlotse.SharedKernel.Pipeline.SchalterStufe<,>),
                typeof(Zeltlotse.SharedKernel.Pipeline.TransaktionsStufe<,>),
                typeof(Zeltlotse.SharedKernel.Pipeline.OutboxStufe<,>),
            ],
            stufen.Select(s => s!.GetType().GetGenericTypeDefinition()));
    }

    private static string OhneVersion(JsonElement json) =>
        JsonSerializer.Serialize(json.EnumerateObject().Where(e => e.Name != "version").ToDictionary(e => e.Name, e => e.Value));
}
