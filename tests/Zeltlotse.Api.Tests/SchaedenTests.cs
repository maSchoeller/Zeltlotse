using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using OrgRolle = Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-4, Schäden, Verluste und Anschaffungswünsche (CAP-22, FR-32, FR-38, AD-17, AD-19, AD-30) gegen echtes PostgreSQL: die Schadens- und
/// Verlustliste der Freizeit (eintragen, Formfehler, Autosave mit Konfliktregel, entfernen, auch nach dem Abschluss), die Anschaffungswünsche der
/// Organisation (nur Freizeiten in Phase gelaufen, „erledigt“) und die Pflichtfälle der sechs Routen. Freizeit X (Leitung Miriam, Team Tobias) in
/// Organisation A, Freizeit Z (Leitung Miriam) in Organisation B; Freizeit Y „Kinderbibeltage 2027“ (Leitung Tim) in Organisation A.
/// </summary>
public sealed class SchaedenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Kinderbibeltage = "Kinderbibeltage 2027";
    private const string Tim = "tim.kroeger@example.org";

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/inventar/schaeden";

    private static string WuenschePfad(Guid org) => $"/api/organisationen/{org}/inventar/anschaffungswuensche";

    private Task<HttpResponseMessage> EintragenAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, Pfad(freizeit ?? _s.X), koerper);

    /// <summary>Trägt ein (die Uhr läuft danach eine Minute weiter, damit die Reihenfolge eindeutig ist) und liefert die Antwort.</summary>
    private async Task<JsonElement> EingetragenAsync(string art, string bezeichnung, HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await EintragenAsync(new { art, bezeichnung }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        return await JsonAsync(antwort);
    }

    private async Task<List<JsonElement>> ListeAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(Pfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    private async Task<List<JsonElement>> WuenscheAsync(HttpClient? client = null)
    {
        using var antwort = await (client ?? _s.Andrea).GetAsync(WuenschePfad(_s.A), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    private Task<HttpResponseMessage> PatchAsync(Guid eintrag, object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Patch, $"{Pfad(freizeit ?? _s.X)}/{eintrag}", koerper);

    private Task<HttpResponseMessage> EntfernenAsync(Guid eintrag, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, $"{Pfad(freizeit ?? _s.X)}/{eintrag}");

    private Task<HttpResponseMessage> ErledigtAsync(Guid eintrag, HttpClient? client = null, Guid? org = null) =>
        SendenAsync(client ?? _s.Andrea, HttpMethod.Delete, $"{WuenschePfad(org ?? _s.A)}/{eintrag}");

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string? wert, string feld = "bezeichnung") =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private Task AbschliessenAsync(Guid freizeit) =>
        AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", freizeit));

    private Task<long> AnzahlAsync() => WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM inventar.schaden_verlust");

    private async Task<(Guid Y, HttpClient Tim)> KinderbibeltageAsync()
    {
        var y = await FreizeitHelfer.AnlegenAsync(_s.Andrea, _s.A, Kinderbibeltage, new DateOnly(2027, 8, 10), new DateOnly(2027, 8, 12));
        return (y, await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Andrea, _s.A, y, Tim));
    }

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static string? Text(JsonElement json, string feld) => json.GetProperty(feld).GetString();

    // ---------- Schadens- und Verlustliste ----------

    [Fact]
    public async Task Eintragen_201_getrimmt_mit_Version_und_Zeitpunkt_die_Liste_zeigt_den_neuesten_oben()
    {
        Assert.Empty(await ListeAsync());
        var zelt = await EingetragenAsync("Schaden", "Großzelt: Stange verbogen");
        var jetzt = _s.Factory.Uhr.GetUtcNow();

        using var antwort = await EintragenAsync(new { art = "Verlust", bezeichnung = " Taschenlampe " }, _s.Tobias);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(Pfad(_s.X), antwort.Headers.Location?.OriginalString);
        var lampe = await JsonAsync(antwort);
        Assert.Equal(["art", "bezeichnung", "erfasstAm", "id", "version", "vorfallEintragId"], Namen(lampe));
        Assert.Equal(("Verlust", "Taschenlampe", jetzt, JsonValueKind.Null), (Text(lampe, "art"), Text(lampe, "bezeichnung"), lampe.GetProperty("erfasstAm").GetDateTimeOffset(), lampe.GetProperty("vorfallEintragId").ValueKind));
        Assert.NotEqual(0u, lampe.GetProperty("version").GetUInt32());
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var pflaster = await EingetragenAsync("Entnahme", "Pflaster");

        Assert.Equal([Id(pflaster), Id(lampe), Id(zelt)], (await ListeAsync(_s.Tobias)).Select(Id));
        Assert.Equal(["Entnahme Pflaster", "Verlust Taschenlampe", "Schaden Großzelt: Stange verbogen"], (await ListeAsync()).Select(e => $"{Text(e, "art")} {Text(e, "bezeichnung")}"));
    }

    [Fact]
    public async Task Eintragen_Formfehler_je_400_am_Feld()
    {
        var faelle = new (object Koerper, string Feld)[]
        {
            (new { art = "Verlust", bezeichnung = "" }, "bezeichnung"),
            (new { art = "Verlust", bezeichnung = "   " }, "bezeichnung"),
            (new { art = "Verlust" }, "bezeichnung"),
            (new { art = "Verlust", bezeichnung = new string('a', 121) }, "bezeichnung"),
            (new { bezeichnung = "Taschenlampe" }, "art"),
            (new { art = 9, bezeichnung = "Taschenlampe" }, "art"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await EintragenAsync(koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), JsonSerializer.Serialize(koerper));
        }

        Assert.Equal(0L, await AnzahlAsync());
        using var genau = await EintragenAsync(new { art = 2, bezeichnung = new string('a', 120) });
        Assert.Equal(HttpStatusCode.Created, genau.StatusCode);
    }

    [Fact]
    public async Task Aendern_200_mit_Version_anderer_Altwert_409_konflikt_unbekannt_404_Feldfehler_400()
    {
        var eintrag = await EingetragenAsync("Verlust", "Taschenlampe");
        var alteVersion = eintrag.GetProperty("version").GetUInt32();

        using var erste = await PatchAsync(Id(eintrag), Koerper(alteVersion, "Taschenlampe", " Stirnlampe "), _s.Tobias);
        Assert.Equal(HttpStatusCode.OK, erste.StatusCode);
        var json = await JsonAsync(erste);
        Assert.Equal(["bezeichnung", "version"], Namen(json));
        Assert.Equal("Stirnlampe", Text(json, "bezeichnung"));
        Assert.NotEqual(alteVersion, json.GetProperty("version").GetUInt32());

        using var konflikt = await PatchAsync(Id(eintrag), Koerper(alteVersion, "Taschenlampe", "Laterne"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        foreach (var (koerper, feld) in new (object, string)[]
                 {
                     (Koerper(0, "Stirnlampe", " "), "bezeichnung"),
                     (Koerper(0, "Stirnlampe", new string('a', 121)), "bezeichnung"),
                     (Koerper(0, null, "Schaden", "art"), "feld"),
                 })
        {
            using var antwort = await PatchAsync(Id(eintrag), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        using var unbekannt = await PatchAsync(Guid.CreateVersion7(), Koerper(0, null, "x"));
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        Assert.Equal("Stirnlampe", Text(Assert.Single(await ListeAsync()), "bezeichnung"));
    }

    [Fact]
    public async Task Entfernen_204_nochmal_404_Eintrag_einer_anderen_Freizeit_404()
    {
        var eintrag = await EingetragenAsync("Schaden", "Kochtopf verbeult");
        var inZ = await EingetragenAsync("Verlust", "Kompass", freizeit: _s.Z);

        using var fremd = await EntfernenAsync(Id(inZ));
        using var fremdPatch = await PatchAsync(Id(inZ), Koerper(inZ.GetProperty("version").GetUInt32(), "Kompass", "Karte"));
        using var entfernt = await EntfernenAsync(Id(eintrag), _s.Tobias);
        using var nochmal = await EntfernenAsync(Id(eintrag));

        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NoContent, HttpStatusCode.NotFound],
            [fremd.StatusCode, fremdPatch.StatusCode, entfernt.StatusCode, nochmal.StatusCode]);
        Assert.Empty(await ListeAsync());
        Assert.Equal("Kompass", Text(Assert.Single(await ListeAsync(freizeit: _s.Z)), "bezeichnung"));
    }

    [Fact]
    public async Task Abgeschlossene_Freizeit_Eintragen_Aendern_und_Entfernen_gehen_weiter()
    {
        var eintrag = await EingetragenAsync("Verlust", "Taschenlampe");
        var weg = await EingetragenAsync("Verlust", "Kompass");
        await AbschliessenAsync(_s.X);

        using var liste = await _s.Tobias.GetAsync(Pfad(_s.X), Abbruch);
        using var neu = await EintragenAsync(new { art = "Schaden", bezeichnung = "Plane gerissen" }, _s.Tobias);
        using var patch = await PatchAsync(Id(eintrag), Koerper(eintrag.GetProperty("version").GetUInt32(), "Taschenlampe", "Stirnlampe"));
        using var entfernt = await EntfernenAsync(Id(weg));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent],
            [liste.StatusCode, neu.StatusCode, patch.StatusCode, entfernt.StatusCode]);
        Assert.Equal(["Plane gerissen", "Stirnlampe"], (await ListeAsync()).Select(e => Text(e, "bezeichnung")));
    }

    // ---------- Anschaffungswünsche ----------

    [Fact]
    public async Task Anschaffungswuensche_nur_Freizeiten_in_Phase_gelaufen_mit_Namen_neueste_Gruppe_zuerst()
    {
        var (y, tim) = await KinderbibeltageAsync();
        using var _ = tim;
        var zelt = await EingetragenAsync("Schaden", "Großzelt: Stange verbogen");
        var lampe = await EingetragenAsync("Verlust", "Taschenlampe", _s.Tobias);
        await EingetragenAsync("Verlust", "Bibel", tim, y);
        await EingetragenAsync("Verlust", "Kompass", freizeit: _s.Z);

        Assert.Empty(await WuenscheAsync());

        await AbschliessenAsync(_s.X);
        var gruppe = Assert.Single(await WuenscheAsync());
        Assert.Equal(["eintraege", "freizeitId", "name"], Namen(gruppe));
        Assert.Equal((_s.X, FreizeitpassSzenario.Kapf), (gruppe.GetProperty("freizeitId").GetGuid(), Text(gruppe, "name")));
        var eintraege = gruppe.GetProperty("eintraege").EnumerateArray().ToList();
        Assert.Equal([Id(lampe), Id(zelt)], eintraege.Select(Id));
        Assert.Equal(["art", "bezeichnung", "erfasstAm", "id", "version", "vorfallEintragId"], Namen(eintraege[0]));

        // Die Kinderbibeltage haben den neuesten Eintrag: Ihre Gruppe steht oben, sobald sie abgeschlossen sind.
        await AbschliessenAsync(y);
        Assert.Equal([Kinderbibeltage, FreizeitpassSzenario.Kapf], (await WuenscheAsync()).Select(g => Text(g, "name")));
    }

    [Fact]
    public async Task Erledigt_204_und_weg_auch_in_der_Liste_der_Freizeit_Eintrag_einer_laufenden_Freizeit_404()
    {
        var (y, tim) = await KinderbibeltageAsync();
        using var _ = tim;
        var zelt = await EingetragenAsync("Schaden", "Großzelt: Stange verbogen");
        var lampe = await EingetragenAsync("Verlust", "Taschenlampe");
        var inY = await EingetragenAsync("Verlust", "Bibel", tim, y);
        await AbschliessenAsync(_s.X);

        using var erledigt = await ErledigtAsync(Id(zelt));
        using var nochmal = await ErledigtAsync(Id(zelt));
        using var laufend = await ErledigtAsync(Id(inY));
        using var unbekannt = await ErledigtAsync(Guid.CreateVersion7());

        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
            [erledigt.StatusCode, nochmal.StatusCode, laufend.StatusCode, unbekannt.StatusCode]);
        Assert.Equal([Id(lampe)], (await ListeAsync()).Select(Id));
        Assert.Equal([Id(lampe)], Assert.Single(await WuenscheAsync()).GetProperty("eintraege").EnumerateArray().Select(Id));
        Assert.Single(await ListeAsync(tim, y));
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Rechte_Freizeit_Leitung_und_Team_2xx_Org_Admin_ohne_Rolle_und_fremde_Organisation_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        var status = new List<string>();
        foreach (var (client, freizeit) in faelle)
        {
            using var get = await client.GetAsync(Pfad(freizeit), Abbruch);
            using var post = await EintragenAsync(new { art = "Verlust", bezeichnung = "Taschenlampe" }, client, freizeit);
            var (eintrag, version) = post.StatusCode == HttpStatusCode.Created
                ? (Id(await JsonAsync(post)), 0u)
                : (Guid.CreateVersion7(), 0u);
            if (post.StatusCode == HttpStatusCode.Created)
            {
                version = Assert.Single(await ListeAsync(client, freizeit), e => Id(e) == eintrag).GetProperty("version").GetUInt32();
            }

            using var patch = await PatchAsync(eintrag, Koerper(version, "Taschenlampe", "Stirnlampe"), client, freizeit);
            using var delete = await EntfernenAsync(eintrag, client, freizeit);
            status.Add($"{(int)get.StatusCode} {(int)post.StatusCode} {(int)patch.StatusCode} {(int)delete.StatusCode}");
        }

        Assert.Equal(["200 201 200 204", "200 201 200 204", "404 404 404 404", "404 404 404 404", "401 401 401 401"], status);
        Assert.Equal(0L, await AnzahlAsync());
    }

    [Fact]
    public async Task Rechte_Org_Admin_alles_Org_Ueberblick_liest_aber_erledigt_403_Org_Admin_von_B_ohne_Org_Rolle_404_ohne_Sitzung_401()
    {
        const string ueberblickMail = "vorstand@example.org";
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, ueberblickMail, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, ueberblickMail);
        var lampe = await EingetragenAsync("Verlust", "Taschenlampe");
        await AbschliessenAsync(_s.X);

        async Task<HttpStatusCode[]> BeideAsync(HttpClient client)
        {
            using var get = await client.GetAsync(WuenschePfad(_s.A), Abbruch);
            using var delete = await ErledigtAsync(Id(lampe), client);
            return [get.StatusCode, delete.StatusCode];
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Forbidden], await BeideAsync(ueberblick));
        Assert.Equal("Taschenlampe", Text(Assert.Single(Assert.Single(await WuenscheAsync(ueberblick)).GetProperty("eintraege").EnumerateArray()), "bezeichnung"));
        foreach (var client in new[] { _s.Lea, _s.Miriam, _s.Tobias, _s.Katrin })
        {
            Assert.All(await BeideAsync(client), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.All(await BeideAsync(_s.Anonym), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NoContent], await BeideAsync(_s.Andrea));
        Assert.Equal(0L, await AnzahlAsync());
    }
}
