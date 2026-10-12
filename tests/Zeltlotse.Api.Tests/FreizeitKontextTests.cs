using System.Net;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Freizeit-Routen und persönlicher Lesepfad (AD-6, AD-12, AD-26, AD-29, AD-30), Matrix-Zeilen „Freizeit-Routen“,
/// „Freigabefenster“ und „Persönlich“: Leitung → <c>200</c>; Org-Admin und Global Admin ohne Freizeit-Rolle, Leitung einer
/// anderen Freizeit und unbekannte Freizeit → <c>404</c>; falsche Freizeit-Rolle → <c>403</c> (Test-Endpunkt nur für Team).
/// </summary>
public sealed class FreizeitKontextTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ZeltlotseApiFactory _factory = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _lea = null!;
    private HttpClient _miriam = null!;
    private HttpClient _jonas = null!;
    private HttpClient _ohneRolle = null!;
    private Guid _a;
    private Guid _x;
    private Guid _y;
    private Guid _z;

    /// <summary>
    /// Organisation A (Andrea Org-Admin) mit den Freizeiten X (Leitung Miriam) und Y (Leitung Jonas); Organisation B (Lea
    /// Org-Admin) mit Freizeit Z (Leitung Miriam). Tobias ohne Rolle, Gerd Global Admin.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _factory = OrgHelfer.Factory(await postgres.NeueDatenbankAsync());
        _factory.ZusatzDienste.Add(TestEndpunkte.Registrieren);
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        (_a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "A", Andrea);
        Guid b;
        (b, _lea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "B", Lea);
        _x = await AnlegenAsync(_andrea, _a, "X");
        _y = await AnlegenAsync(_andrea, _a, "Y", new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 5));
        _z = await AnlegenAsync(_lea, b, "Z", new DateOnly(2027, 9, 1), new DateOnly(2027, 9, 3));
        _miriam = await LeitungAsync(_factory, _andrea, _a, _x, Miriam);
        await FreizeitHelfer.EinladenAsync(_lea, b, _z, Miriam);
        await AnnehmenMitAsync(_factory, _miriam, Miriam);
        await NameSetzenAsync(_miriam, "Miriam", "Zeltner");
        _jonas = await LeitungAsync(_factory, _andrea, _a, _y, Jonas);
        _ohneRolle = await AngemeldeterClientAsync(_factory, Tobias);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _lea, _miriam, _jonas, _ohneRolle })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Leitung_liest_die_Freizeit_mit_Phase_Freigabefenster_und_Namen_der_Leitung()
    {
        using var antwort = await _miriam.GetAsync($"/api/freizeiten/{_x}", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["ersterTag", "freigabeBis", "id", "leitung", "letzterTag", "name", "phase"], Namen(json));
        Assert.Equal((_x, "X", "2027-08-02", "2027-08-13", "InVorbereitung", "2027-09-12"), (
            json.GetProperty("id").GetGuid(),
            json.GetProperty("name").GetString(),
            json.GetProperty("ersterTag").GetString(),
            json.GetProperty("letzterTag").GetString(),
            json.GetProperty("phase").GetString(),
            json.GetProperty("freigabeBis").GetString()));
        var leitung = Assert.Single(json.GetProperty("leitung").EnumerateArray());
        Assert.Equal(["nachname", "vorname"], Namen(leitung));
        Assert.Equal(("Miriam", "Zeltner"), (leitung.GetProperty("vorname").GetString(), leitung.GetProperty("nachname").GetString()));
    }

    [Fact]
    public async Task Kontext_liefert_Rollen_Phase_leere_Schalter_und_fuer_die_Leitung_Nachweise_pruefen()
    {
        using var antwort = await _miriam.GetAsync($"/api/freizeiten/{_x}/kontext", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["erlaubt", "freizeitId", "name", "phase", "rollen", "schalter"], Namen(json));
        Assert.Equal(_x, json.GetProperty("freizeitId").GetGuid());
        Assert.Equal("X", json.GetProperty("name").GetString());
        Assert.Equal("InVorbereitung", json.GetProperty("phase").GetString());
        Assert.Equal(["Leitung"], json.GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
        Assert.Empty(json.GetProperty("schalter").EnumerateArray());
        // Story 1-5: die Leitung prüft Nachweise (dieselbe Rollenliste wie die Prüf-Endpunkte); Story 2-1: und pflegt die Einstellungen.
        Assert.Equal(
            [Zeltlotse.Api.Contracts.Gemeinsam.Aktionsschluessel.NachweisePruefen, Zeltlotse.Api.Contracts.Gemeinsam.Aktionsschluessel.FreizeitEinstellungen, Zeltlotse.Api.Contracts.Gemeinsam.Aktionsschluessel.AnmeldungenBearbeiten],
            json.GetProperty("erlaubt").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Leitung_in_einer_anderen_Organisation_liest_dort_im_Mandanten_der_Freizeit()
    {
        using var antwort = await _miriam.GetAsync($"/api/freizeiten/{_z}/kontext", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("Z", (await JsonAsync(antwort)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Org_Admin_Global_Admin_ohne_Rolle_Leitung_von_Y_und_unbekannte_Freizeit_ergeben_404()
    {
        var faelle = new (HttpClient Client, Guid FreizeitId)[]
        {
            (_andrea, _x), (_lea, _z), (_gerd, _x), (_ohneRolle, _x), (_jonas, _x), (_miriam, _y), (_miriam, Guid.CreateVersion7()),
        };

        foreach (var (client, freizeitId) in faelle)
        {
            foreach (var pfad in new[] { $"/api/freizeiten/{freizeitId}", $"/api/freizeiten/{freizeitId}/kontext" })
            {
                using var antwort = await client.GetAsync(pfad, Abbruch);
                Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
                Assert.Equal(404, (await ProblemAsync(antwort)).GetProperty("status").GetInt32());
            }
        }
    }

    [Fact]
    public async Task Falsche_Freizeit_Rolle_ergibt_403_ohne_Rolle_404()
    {
        using var leitung = await _miriam.GetAsync($"/api/test/nur-team/{_x}", Abbruch);
        using var orgAdmin = await _andrea.GetAsync($"/api/test/nur-team/{_x}", Abbruch);

        Assert.Equal(HttpStatusCode.Forbidden, leitung.StatusCode);
        Assert.Equal(403, (await ProblemAsync(leitung)).GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, orgAdmin.StatusCode);
    }

    [Fact]
    public async Task Ich_Freizeiten_liefert_nur_eigene_ueber_Organisationen_hinweg_nach_erstem_Tag_absteigend_mit_den_Vertragsfeldern()
    {
        var miriam = (await JsonAsync(await _miriam.GetAsync("/api/ich/freizeiten", Abbruch))).EnumerateArray().ToList();
        var jonas = (await JsonAsync(await _jonas.GetAsync("/api/ich/freizeiten", Abbruch))).EnumerateArray().ToList();
        var andrea = (await JsonAsync(await _andrea.GetAsync("/api/ich/freizeiten", Abbruch))).EnumerateArray().ToList();
        var ohneRolle = (await JsonAsync(await _ohneRolle.GetAsync("/api/ich/freizeiten", Abbruch))).EnumerateArray().ToList();

        Assert.Equal([_z, _x], miriam.Select(f => f.GetProperty("freizeitId").GetGuid()));
        Assert.All(miriam, f => Assert.Equal(["ersterTag", "freizeitId", "letzterTag", "name", "phase", "rollen"], Namen(f)));
        Assert.Equal(("X", "2027-08-02", "2027-08-13", "InVorbereitung"), (
            miriam[1].GetProperty("name").GetString(),
            miriam[1].GetProperty("ersterTag").GetString(),
            miriam[1].GetProperty("letzterTag").GetString(),
            miriam[1].GetProperty("phase").GetString()));
        Assert.All(miriam, f => Assert.Equal(["Leitung"], f.GetProperty("rollen").EnumerateArray().Select(r => r.GetString())));
        Assert.Equal([_y], jonas.Select(f => f.GetProperty("freizeitId").GetGuid()));
        Assert.Empty(andrea);
        Assert.Empty(ohneRolle);
    }
}
