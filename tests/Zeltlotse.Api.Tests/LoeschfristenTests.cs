using System.Net;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Löschfristen der Organisation (NFR-5, AD-19, AD-30, Story 1-7): Vorgabewerte beim Anlegen, Lesen und Ändern per Autosave,
/// Prüfung der Werte (1–120), Konflikt am selben Feld, Rollen (Org-Überblick <c>403</c>, fremde Organisation <c>404</c>).
/// </summary>
public sealed class LoeschfristenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ZeltlotseApiFactory _factory = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private Guid _orgId;

    private string Pfad => $"/api/organisationen/{_orgId}/loeschfristen";

    public async ValueTask InitializeAsync()
    {
        _factory = OrgHelfer.Factory(await postgres.NeueDatenbankAsync());
        _gerd = await AngemeldeterClientAsync(_factory, OrgHelfer.Gerd);
        (_orgId, _andrea) = await OrgHelfer.OrganisationMitOrgAdminAsync(_factory, _gerd, "Jugendwerk Musterstadt", OrgHelfer.Andrea);
    }

    public async ValueTask DisposeAsync()
    {
        _gerd.Dispose();
        _andrea.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Neue_Organisation_hat_die_Vorgabewerte_und_der_Org_Admin_liest_sie()
    {
        using var antwort = await _andrea.GetAsync(Pfad, Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["freizeitDatenMonate", "nachweisBestaetigungMonate", "version"], Namen(json));
        Assert.Equal(12, json.GetProperty("freizeitDatenMonate").GetInt32());
        Assert.Equal(12, json.GetProperty("nachweisBestaetigungMonate").GetInt32());
        using var organisation = await _andrea.GetAsync($"/api/organisationen/{_orgId}", Abbruch);
        Assert.Contains(Aktionsschluessel.LoeschfristenVerwalten, (await JsonAsync(organisation)).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Aendern_liefert_die_neue_Version_und_ein_anderes_Feld_mit_alter_Version_gilt_noch()
    {
        var version = await VersionAsync();

        using var erstes = await PatchAsync($$"""{"version":{{version}},"alterWert":"12","freizeitDatenMonate":"24"}""");
        Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        var json = await JsonAsync(erstes);
        Assert.Equal(["freizeitDatenMonate", "version"], Namen(json));
        Assert.Equal("24", json.GetProperty("freizeitDatenMonate").GetString());
        Assert.NotEqual(version, json.GetProperty("version").GetUInt32());

        // AD-19: Nur ein anderes Feld hat sich seit der mitgeschickten Version geändert: wird angewendet.
        using var zweites = await PatchAsync($$"""{"version":{{version}},"alterWert":"12","nachweisBestaetigungMonate":"6"}""");
        Assert.Equal(HttpStatusCode.OK, zweites.StatusCode);
        using var lesen = await _andrea.GetAsync(Pfad, Abbruch);
        var stand = await JsonAsync(lesen);
        Assert.Equal((24, 6), (stand.GetProperty("freizeitDatenMonate").GetInt32(), stand.GetProperty("nachweisBestaetigungMonate").GetInt32()));
    }

    [Fact]
    public async Task Anderer_alter_Wert_desselben_Felds_ergibt_409_konflikt()
    {
        var version = await VersionAsync();
        (await PatchAsync($$"""{"version":{{version}},"alterWert":"12","freizeitDatenMonate":"24"}""")).Dispose();

        using var konflikt = await PatchAsync($$"""{"version":{{version}},"alterWert":"12","freizeitDatenMonate":"36"}""");

        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal("konflikt", Code(await ProblemAsync(konflikt)));
    }

    [Theory]
    [InlineData("freizeitDatenMonate", "0")]
    [InlineData("freizeitDatenMonate", "121")]
    [InlineData("nachweisBestaetigungMonate", "0")]
    [InlineData("nachweisBestaetigungMonate", "121")]
    [InlineData("nachweisBestaetigungMonate", "zwölf")]
    public async Task Werte_ausserhalb_1_bis_120_ergeben_400_am_Feld(string feld, string wert)
    {
        var version = await VersionAsync();

        using var antwort = await PatchAsync($$"""{"version":{{version}},"alterWert":"12","{{feld}}":"{{wert}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _));
    }

    [Fact]
    public async Task Version_als_Text_ergibt_400_Version_fehlt()
    {
        using var antwort = await PatchAsync("""{"version":"5","alterWert":"12","freizeitDatenMonate":"24"}""");

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal("Version fehlt.", (await ProblemAsync(antwort)).GetProperty("errors").GetProperty("version")[0].GetString());
    }

    [Fact]
    public async Task Org_Ueberblick_bekommt_403_und_Org_Admin_einer_fremden_Organisation_404()
    {
        await OrgHelfer.EinladenAsync(_andrea, _orgId, OrgHelfer.Jonas, OrgRolle.OrgUeberblick);
        using var jonas = await OrgHelfer.AnnehmenAlsAsync(_factory, OrgHelfer.Jonas);
        using var lea = (await OrgHelfer.OrganisationMitOrgAdminAsync(_factory, _gerd, "Anderes Jugendwerk", OrgHelfer.Lea)).OrgAdmin;
        var version = await VersionAsync();
        var koerper = $$"""{"version":{{version}},"alterWert":"12","freizeitDatenMonate":"24"}""";

        using var ueberblickLesen = await jonas.GetAsync(Pfad, Abbruch);
        using var ueberblickAendern = await SendenAsync(jonas, HttpMethod.Patch, Pfad, koerper);
        using var fremdLesen = await lea.GetAsync(Pfad, Abbruch);
        using var fremdAendern = await SendenAsync(lea, HttpMethod.Patch, Pfad, koerper);

        Assert.Equal(HttpStatusCode.Forbidden, ueberblickLesen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ueberblickAendern.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fremdLesen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fremdAendern.StatusCode);
        Assert.Equal(version, await VersionAsync());
    }

    private async Task<uint> VersionAsync()
    {
        using var antwort = await _andrea.GetAsync(Pfad, Abbruch);
        return (await JsonAsync(antwort)).GetProperty("version").GetUInt32();
    }

    private Task<HttpResponseMessage> PatchAsync(string json) => SendenAsync(_andrea, HttpMethod.Patch, Pfad, json);
}
