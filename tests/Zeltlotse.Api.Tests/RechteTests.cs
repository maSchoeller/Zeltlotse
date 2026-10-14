using System.Net;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Zentrale Rechteprüfung des Hosts (AD-6, AD-12, AD-30), Matrix-Zeilen „Org lesen“ und „Rollen und Einladen“: berechtigt;
/// falsche Rolle im eigenen Mandanten → <c>403</c>; fremde Organisation, unbekannte Organisation und Global Admin ohne
/// eigene Rolle → <c>404</c>. Alle Fehler als ProblemDetails, vor der Modellbindung.
/// </summary>
public sealed class RechteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ZeltlotseApiFactory _factory = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _jonas = null!;
    private HttpClient _lea = null!;
    private HttpClient _ohneRolle = null!;
    private Guid _a;
    private Guid _b;

    /// <summary>
    /// Organisation A: Andrea Org-Admin, Jonas Org-Überblick. Organisation B: Lea Org-Admin. Tobias ohne Rolle, Gerd Global
    /// Admin ohne eigene Rolle.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _factory = Factory(await postgres.NeueDatenbankAsync());
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        (_a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "A", Andrea);
        (_b, _lea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "B", Lea);
        await EinladenAsync(_andrea, _a, Jonas, OrgRolle.OrgUeberblick);
        _jonas = await AnnehmenAlsAsync(_factory, Jonas);
        _ohneRolle = await AngemeldeterClientAsync(_factory, "tobias.wanderer@example.org");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _jonas, _lea, _ohneRolle })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Org_Admin_liest_die_Organisation_mit_Rolle_und_erlaubter_Aktion()
    {
        using var antwort = await _andrea.GetAsync($"/api/organisationen/{_a}", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["erlaubt", "id", "name", "rollen"], Namen(json));
        Assert.Equal(_a, json.GetProperty("id").GetGuid());
        Assert.Equal("A", json.GetProperty("name").GetString());
        Assert.Equal(["OrgAdmin"], json.GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
        // Story 1-5: dazu Nachweise prüfen und die Nachweis-Richtlinie verwalten, aus denselben Rollenlisten wie die Endpunkte.
        // Story 1-7: dazu die Löschfristen verwalten; Story 2-7: die Org-Dokumente; Story 2-10: das Archiv der Vorfall-Notizen; Story 3-3: das Inventar;
        // Story 3-5: die Aufgaben-Vorlagen.
        Assert.Equal(
            [
                Aktionsschluessel.OrgRollenVergeben, Aktionsschluessel.NachweisePruefen, Aktionsschluessel.NachweisRichtlinieVerwalten, Aktionsschluessel.LoeschfristenVerwalten,
                Aktionsschluessel.OrgDokumenteVerwalten, Aktionsschluessel.VorfallArchivSehen, Aktionsschluessel.InventarSehen, Aktionsschluessel.InventarVerwalten,
                Aktionsschluessel.AufgabenVorlagenVerwalten,
            ],
            json.GetProperty("erlaubt").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Org_Ueberblick_liest_die_Organisation_nur_mit_der_erlaubten_Aktion_inventar_sehen()
    {
        using var antwort = await _jonas.GetAsync($"/api/organisationen/{_a}", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["OrgUeberblick"], json.GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
        // Story 3-3: Org-Überblick sieht das Inventar (NS-13), pflegt es aber nicht.
        Assert.Equal([Aktionsschluessel.InventarSehen], json.GetProperty("erlaubt").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Ohne_Rolle_fremde_Organisation_Global_Admin_ohne_Rolle_und_unbekannte_Organisation_ergeben_404()
    {
        var unbekannt = Guid.CreateVersion7();
        var faelle = new (HttpClient Client, Guid OrgId)[] { (_ohneRolle, _a), (_lea, _a), (_gerd, _a), (_andrea, unbekannt), (_gerd, unbekannt) };

        foreach (var (client, orgId) in faelle)
        {
            foreach (var pfad in new[] { $"/api/organisationen/{orgId}", $"/api/organisationen/{orgId}/rollen" })
            {
                using var antwort = await client.GetAsync(pfad, Abbruch);
                Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
                Assert.Equal(404, (await ProblemAsync(antwort)).GetProperty("status").GetInt32());
            }

            using var einladen = await PostAsync(client, $"/api/organisationen/{orgId}/einladungen", new EinladungSchicken("x@example.org", OrgRolle.OrgAdmin));
            Assert.Equal(HttpStatusCode.NotFound, einladen.StatusCode);
        }

        Assert.Empty(_factory.Mails.An("x@example.org"));
    }

    [Fact]
    public async Task Org_Ueberblick_bekommt_fuer_Rollen_und_Einladen_403_auch_bei_ungueltigem_Koerper()
    {
        using var rollen = await _jonas.GetAsync($"/api/organisationen/{_a}/rollen", Abbruch);
        using var einladen = await PostAsync(_jonas, $"/api/organisationen/{_a}/einladungen", new EinladungSchicken("x@example.org", OrgRolle.OrgAdmin));
        using var kaputt = await PostAsync(_jonas, $"/api/organisationen/{_a}/einladungen", "kein json");

        foreach (var antwort in new[] { rollen, einladen, kaputt })
        {
            Assert.Equal(HttpStatusCode.Forbidden, antwort.StatusCode);
            Assert.Equal(403, (await ProblemAsync(antwort)).GetProperty("status").GetInt32());
        }

        Assert.Empty(_factory.Mails.An("x@example.org"));
    }

    [Fact]
    public async Task Org_Admin_von_B_liest_und_laedt_in_B_ein_aber_nie_in_A()
    {
        using var eigene = await _lea.GetAsync($"/api/organisationen/{_b}/rollen", Abbruch);
        using var fremd = await _lea.GetAsync($"/api/organisationen/{_a}/rollen", Abbruch);

        Assert.Equal(HttpStatusCode.OK, eigene.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fremd.StatusCode);
        var personen = (await JsonAsync(eigene)).GetProperty("personen").EnumerateArray().ToList();
        Assert.Single(personen);
    }

    [Fact]
    public async Task Org_Endpunkte_ohne_Guid_in_der_Route_gibt_es_nicht()
    {
        using var antwort = await _andrea.GetAsync("/api/organisationen/keine-guid", Abbruch);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Global_Admin_sieht_in_der_Liste_eingerichtet_aber_keine_Rollen()
    {
        var liste = await JsonAsync(await _gerd.GetAsync("/api/organisationen", Abbruch));

        Assert.All(liste.EnumerateArray(), o => Assert.True(o.GetProperty("eingerichtet").GetBoolean()));
        Assert.Equal(HttpStatusCode.NotFound, (await _gerd.GetAsync($"/api/organisationen/{_b}/rollen", Abbruch)).StatusCode);
    }
}
