using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using FamilienHelfer = Zeltlotse.Api.Tests.FamilienHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-4, Fragen-Auswahl der Leitung (FR-22, AD-10, AD-19, AD-30) gegen echtes PostgreSQL: neue Freizeit ohne Fragen, Auswahl je
/// Zielgruppe normalisiert gegen den Katalog von Personen, Konfliktregel, Rechte-Matrix, Schreibschutz nach dem Abschluss und die
/// Weitergabe an die Lesepfade (eigene Anmeldungen, Gast-Link, Vertrauensseite).
/// </summary>
public sealed class FragenAuswahlTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _lea = null!;
    private HttpClient _miriam = null!;
    private HttpClient _tobias = null!;
    private HttpClient _katrin = null!;
    private HttpClient _jonas = null!;
    private HttpClient _anonym = null!;
    private Guid _a;
    private Guid _x;
    private Guid _emma;

    /// <summary>Organisation A (Andrea) mit Freizeit X, Leitung Miriam, Tobias Team (per SQL); Organisation B (Lea). Katrin mit Emma, Jonas.</summary>
    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = OrgHelfer.Factory(_verbindung);
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        (_a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "CVJM Musterstadt", Andrea);
        (_, _lea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "EJW Beispielhausen", Lea);
        _x = await AnlegenAsync(_andrea, _a, "Zeltlager Kapf 2027");
        _miriam = await LeitungAsync(_factory, _andrea, _a, _x, Miriam);
        _tobias = await AngemeldeterClientAsync(_factory, Tobias);
        (await PostAsync(_tobias, "/api/ich/profil")).Dispose();
        await AusfuehrenAsync(
            _verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"OrganisationId\", \"FreizeitId\", \"PersonId\", \"Rolle\", \"VergebenAm\") VALUES (@o, @f, @p, 2, now())",
            ("o", _a), ("f", _x), ("p", await PersonIdAsync(_verbindung, Tobias)));
        _katrin = await AngemeldeterClientAsync(_factory, FamilienHelfer.Katrin);
        await NameSetzenAsync(_katrin, "Katrin", "Muster");
        _emma = await FamilienHelfer.KindAsync(_katrin, "Emma");
        _jonas = await AngemeldeterClientAsync(_factory, Jonas);
        await NameSetzenAsync(_jonas, "Jonas", "Hüttner");
        _anonym = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _lea, _miriam, _tobias, _katrin, _jonas, _anonym })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Eine_neue_Freizeit_fragt_nichts_ab_und_liefert_den_Katalog()
    {
        using var antwort = await _miriam.GetAsync(Pfad(_x), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["katalog", "mitarbeitende", "teilnehmende", "version"], Namen(json));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (json.GetProperty("teilnehmende").ValueKind, json.GetProperty("mitarbeitende").ValueKind));
        var katalog = json.GetProperty("katalog").EnumerateArray().ToList();
        Assert.Equal(10, katalog.Count);
        Assert.Equal(["frage", "kennung"], Namen(katalog[0]));
        Assert.Equal(("allergien", "Allergien und Unverträglichkeiten"), (katalog[0].GetProperty("kennung").GetString(), katalog[0].GetProperty("frage").GetString()));
    }

    [Fact]
    public async Task Die_Leitung_waehlt_Fragen_normalisiert_je_Zielgruppe_und_ein_veralteter_alter_Wert_ergibt_409()
    {
        var version = await VersionAsync();

        using var teilnehmende = await PatchAsync($$"""{"version":{{version}},"alterWert":"","teilnehmende":"medikamente,allergien,allergien"}""");

        Assert.Equal(HttpStatusCode.OK, teilnehmende.StatusCode);
        var json = await JsonAsync(teilnehmende);
        Assert.Equal(["teilnehmende", "version"], Namen(json));
        Assert.Equal("allergien,medikamente", json.GetProperty("teilnehmende").GetString());

        // Ein anderes Feld mit der alten Version: wird angewendet (AD-19).
        using var mitarbeitende = await PatchAsync($$"""{"version":{{version}},"alterWert":null,"mitarbeitende":"notfallkontakte"}""");
        Assert.Equal(HttpStatusCode.OK, mitarbeitende.StatusCode);

        // Derselbe Wert gleichzeitig: der zweite kennt die Änderung nicht.
        using var konflikt = await PatchAsync($$"""{"version":{{version}},"alterWert":"","teilnehmende":"schwimmen"}""");
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, (await ProblemAsync(konflikt)).GetProperty("code").GetString());

        var stand = await JsonAsync(await _miriam.GetAsync(Pfad(_x), Abbruch));
        Assert.Equal(("allergien,medikamente", "notfallkontakte"), (stand.GetProperty("teilnehmende").GetString(), stand.GetProperty("mitarbeitende").GetString()));

        // Leer heißt: keine Fragen.
        using var leer = await PatchAsync($$"""{"version":{{stand.GetProperty("version").GetUInt32()}},"alterWert":"medikamente,allergien","teilnehmende":""}""");
        Assert.Equal(HttpStatusCode.OK, leer.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(leer)).GetProperty("teilnehmende").ValueKind);
    }

    [Fact]
    public async Task Eine_unbekannte_Kennung_ergibt_400_am_Feld()
    {
        using var antwort = await PatchAsync($$"""{"version":{{await VersionAsync()}},"alterWert":null,"teilnehmende":"allergien,lieblingsfarbe"}""");

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(["teilnehmende"], (await ProblemAsync(antwort)).GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await _miriam.GetAsync(Pfad(_x), Abbruch))).GetProperty("teilnehmende").ValueKind);
    }

    [Fact]
    public async Task Team_bekommt_403_Org_Admin_ohne_Rolle_fremde_Organisation_und_unbekannte_Freizeit_404()
    {
        var version = await VersionAsync();
        var patch = $$"""{"version":{{version}},"alterWert":null,"teilnehmende":"allergien"}""";

        foreach (var (client, freizeit, status) in new (HttpClient, Guid, HttpStatusCode)[]
                 {
                     (_tobias, _x, HttpStatusCode.Forbidden),
                     (_andrea, _x, HttpStatusCode.NotFound),
                     (_lea, _x, HttpStatusCode.NotFound),
                     (_gerd, _x, HttpStatusCode.NotFound),
                     (_miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
                 })
        {
            using var laden = await client.GetAsync(Pfad(freizeit), Abbruch);
            using var aendern = await SendenAsync(client, HttpMethod.Patch, Pfad(freizeit), patch);

            Assert.Equal([status, status], [laden.StatusCode, aendern.StatusCode]);
        }

        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await _miriam.GetAsync(Pfad(_x), Abbruch))).GetProperty("teilnehmende").ValueKind);
    }

    [Fact]
    public async Task Eine_abgeschlossene_Freizeit_ist_nur_lesbar()
    {
        var version = await VersionAsync();
        await AusfuehrenAsync(_verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _x));

        using var antwort = await PatchAsync($$"""{"version":{{version}},"alterWert":null,"teilnehmende":"allergien"}""");

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, (await ProblemAsync(antwort)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _miriam.GetAsync(Pfad(_x), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Anmeldungen_tragen_genau_die_Fragen_ihrer_Zielgruppe_auch_auf_dem_Gast_Link()
    {
        await SetzenAsync("teilnehmende", "allergien,schwimmen,notfallkontakte");
        await SetzenAsync("mitarbeitende", "medikamente");
        var links = await AnmeldelinksAsync();

        var emma = await AnmeldenAsync(_katrin, links["Teilnehmende"], _emma);
        var jonas = await AnmeldenAsync(_jonas, links["Mitarbeitende"], null);
        using var gast = await PostAsync(_anonym, "/api/personen/oeffentlich/gaeste", new GastAnlegen("Sabine", "Beispiel", "sabine@example.org", "Lina", "Beispiel", new DateOnly(2016, 4, 1)));
        var gastJson = await JsonAsync(gast);
        var (gastToken, lina) = (gastJson.GetProperty("gastToken").GetString()!, gastJson.GetProperty("kindPersonId").GetGuid());
        using var gastAnmeldung = await PostAsync(_anonym, $"/api/freizeiten/oeffentlich/{links["Teilnehmende"]}/anmeldungen", new GastAnmeldungAbsenden(gastToken, lina, true));
        var linaAnmeldung = (await JsonAsync(gastAnmeldung)).GetProperty("anmeldungId").GetGuid();

        var katrin = Assert.Single((await JsonAsync(await _katrin.GetAsync("/api/ich/anmeldungen", Abbruch))).EnumerateArray());
        Assert.Equal(
            ["anmeldungId", "art", "ersterTag", "fragen", "freizeitName", "kontaktMail", "kontaktTelefon", "letzterTag", "personId", "status"],
            Namen(katrin));
        Assert.Equal((emma, "allergien,notfallkontakte,schwimmen"), (katrin.GetProperty("anmeldungId").GetGuid(), Fragen(katrin)));
        var eigene = Assert.Single((await JsonAsync(await _jonas.GetAsync("/api/ich/anmeldungen", Abbruch))).EnumerateArray());
        Assert.Equal((jonas, "medikamente"), (eigene.GetProperty("anmeldungId").GetGuid(), Fragen(eigene)));

        var gastLink = Assert.Single((await JsonAsync(await _anonym.GetAsync($"/api/freizeiten/gast/{gastToken}", Abbruch))).GetProperty("anmeldungen").EnumerateArray());
        Assert.Equal(
            ["anmeldungId", "ersterTag", "fragen", "freizeitName", "kontaktMail", "kontaktTelefon", "letzterTag", "personId", "status", "vorname"],
            Namen(gastLink));
        Assert.Equal((linaAnmeldung, lina, "allergien,notfallkontakte,schwimmen"), (
            gastLink.GetProperty("anmeldungId").GetGuid(), gastLink.GetProperty("personId").GetGuid(), Fragen(gastLink)));
    }

    [Fact]
    public async Task Die_Vertrauensseite_nennt_die_gewaehlten_Fragen_je_Zielgruppe()
    {
        var token = (await AnmeldelinksAsync())["Teilnehmende"];
        var vorher = await JsonAsync(await _anonym.GetAsync($"/api/freizeiten/oeffentlich/{token}/vertrauensseite", Abbruch));
        Assert.Empty(vorher.GetProperty("fragenTeilnehmende").EnumerateArray());
        Assert.Empty(vorher.GetProperty("fragenMitarbeitende").EnumerateArray());

        await SetzenAsync("teilnehmende", "schwimmen,allergien");
        await SetzenAsync("mitarbeitende", "notfallkontakte");

        var nachher = await JsonAsync(await _anonym.GetAsync($"/api/freizeiten/oeffentlich/{token}/vertrauensseite", Abbruch));
        Assert.Equal(["Allergien und Unverträglichkeiten", "Kann schwimmen"], nachher.GetProperty("fragenTeilnehmende").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["Notfallkontakte (Name, Telefon)"], nachher.GetProperty("fragenMitarbeitende").EnumerateArray().Select(f => f.GetString()));
    }

    // ---------- Hilfen ----------

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/fragen";

    private Task<HttpResponseMessage> PatchAsync(string json) => SendenAsync(_miriam, HttpMethod.Patch, Pfad(_x), json);

    private async Task<uint> VersionAsync() =>
        (await JsonAsync(await _miriam.GetAsync(Pfad(_x), Abbruch))).GetProperty("version").GetUInt32();

    private async Task SetzenAsync(string feld, string wert)
    {
        using var antwort = await PatchAsync($$"""{"version":{{await VersionAsync()}},"alterWert":null,"{{feld}}":"{{wert}}"}""");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private static string Fragen(JsonElement zeile) => string.Join(',', zeile.GetProperty("fragen").EnumerateArray().Select(f => f.GetString()));

    private async Task<Dictionary<string, string>> AnmeldelinksAsync() =>
        (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
        .ToDictionary(l => l.GetProperty("art").GetString()!, l => l.GetProperty("adresse").GetString()![(l.GetProperty("adresse").GetString()!.LastIndexOf('/') + 1)..]);

    private static async Task<Guid> AnmeldenAsync(HttpClient client, string anmeldelink, Guid? person)
    {
        using var antwort = await PostAsync(client, "/api/ich/anmeldungen", new AnmeldungAbsenden(anmeldelink, person, true));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("anmeldungId").GetGuid();
    }
}
