using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Einladung annehmen über HTTP (AD-11, AD-30, Token-Pflichtfälle): gültig, abgelaufen, benutzt, verworfen, anderer
/// Zweck, unbekannt, Formfehler; Bindung an Mail-Adresse und Organisation; doppelte Rolle; Org-Überblick; persönlicher
/// Lesepfad <c>/api/ich/organisationen</c> (AD-26).
/// </summary>
public sealed class EinladungTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Gueltige_Einladung_vergibt_die_Rolle_protokolliert_die_Rollenvergabe_und_richtet_die_Organisation_ein()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var orgId = await AnlegenAsync(gerd, "Jugendwerk", Andrea);
        var token = Token(factory, Andrea);
        using var andrea = await AngemeldeterClientAsync(factory, Andrea);

        using var antwort = await AnnehmenAsync(andrea, token);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["organisationId"], Namen(json));
        Assert.Equal(orgId, json.GetProperty("organisationId").GetGuid());
        var personId = await PersonIdAsync(verbindung, Andrea);
        Assert.Equal(1L, await WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art AND \"PersonId\" = @person AND \"OrganisationId\" = @org AND \"BezugId\" IS NOT NULL",
            ("art", (short)SicherheitsEreignisArt.Rollenvergabe), ("person", personId), ("org", orgId)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_rolle WHERE \"PersonId\" = @p AND \"Rolle\" = 1", ("p", personId)));
        Assert.Equal(2, await WertAsync<short>(verbindung, "SELECT \"Status\" FROM organisationen.org_einladung"));
        Assert.Equal([new OrganisationZeile(orgId, "Jugendwerk", true)], await gerd.GetFromJsonAsync<List<OrganisationZeile>>("/api/organisationen", Abbruch));
        Assert.Equal(HttpStatusCode.OK, (await andrea.GetAsync($"/api/organisationen/{orgId}", Abbruch)).StatusCode);
    }

    public static TheoryData<string> TokenFaelle => ["abgelaufen", "zweites Annehmen", "durch neue Einladung verworfen", "Login-Token", "unbekannt", "Formfehler", "kein Token"];

    [Theory]
    [MemberData(nameof(TokenFaelle))]
    public async Task Ungueltiges_Token_ergibt_404_ohne_Rolle_und_einen_Eintrag_im_Sicherheitsprotokoll(string fall)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (orgId, lea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk", Lea);
        using var _ = lea;
        await EinladenAsync(lea, orgId, Andrea, OrgRolle.OrgAdmin);
        var token = Token(factory, Andrea);
        using var andrea = await AngemeldeterClientAsync(factory, Andrea);
        var rollen = 1L;
        switch (fall)
        {
            case "abgelaufen":
                factory.Uhr.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
                break;
            case "zweites Annehmen":
                using (var erstes = await AnnehmenAsync(andrea, token))
                {
                    Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
                }

                rollen = 2;
                break;
            case "durch neue Einladung verworfen":
                await EinladenAsync(lea, orgId, Andrea, OrgRolle.OrgAdmin);
                break;
            case "Login-Token":
                await LinkAnfordernAsync(factory.CreateClient(), Andrea);
                token = factory.Mails.An(Andrea).Last(m => m.Betreff == "Anmelden bei Zeltlotse").Token;
                break;
            case "unbekannt":
                token = Zeltlotse.SharedKernel.Zugangslinks.Zugangslink.NeuesToken();
                break;
            case "Formfehler":
                token = token[..20] + "!";
                break;
            case "kein Token":
                token = null!;
                break;
        }

        var vorher = await ProtokollAsync(verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);
        using var antwort = await AnnehmenAsync(andrea, token);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(vorher + 1, await ProtokollAsync(verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(rollen, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_rolle"));
        Assert.Equal(fall == "zweites Annehmen" ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await andrea.GetAsync($"/api/organisationen/{orgId}", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Andere_Mail_ergibt_404_und_danach_nimmt_die_eingeladene_Person_dieselbe_Einladung_an()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var orgId = await AnlegenAsync(gerd, "Jugendwerk", Andrea);
        var token = Token(factory, Andrea);
        using var jonas = await AngemeldeterClientAsync(factory, Jonas);

        using var falsch = await AnnehmenAsync(jonas, token);
        using var vomGlobalAdmin = await AnnehmenAsync(gerd, token);

        Assert.Equal(HttpStatusCode.NotFound, falsch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, vomGlobalAdmin.StatusCode);
        Assert.Equal(1, await WertAsync<short>(verbindung, "SELECT \"Status\" FROM organisationen.org_einladung"));
        using var andrea = await AngemeldeterClientAsync(factory, Andrea);
        using var richtig = await AnnehmenAsync(andrea, token);
        Assert.Equal(HttpStatusCode.OK, richtig.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await jonas.GetAsync($"/api/organisationen/{orgId}", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Einladung_fuer_X_gibt_eine_Rolle_nur_in_X()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var y = await AnlegenAsync(gerd, "Y", Jonas);
        var (x, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "X", Andrea);
        using (andrea)
        {
            Assert.Equal(HttpStatusCode.OK, (await andrea.GetAsync($"/api/organisationen/{x}", Abbruch)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await andrea.GetAsync($"/api/organisationen/{y}", Abbruch)).StatusCode);
        }
    }

    [Fact]
    public async Task Zweite_Einladung_gleicher_Rolle_ergibt_eine_Rollen_Zeile()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (orgId, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk", Andrea);
        using (andrea)
        {
            await EinladenAsync(andrea, orgId, Andrea, OrgRolle.OrgAdmin);
            using var zweite = await AnnehmenAsync(andrea, Token(factory, Andrea));

            Assert.Equal(HttpStatusCode.OK, zweite.StatusCode);
            Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_rolle"));
        }
    }

    [Fact]
    public async Task Org_Ueberblick_annehmen_vergibt_Org_Ueberblick_und_aendert_eingerichtet_nicht()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var orgId = await AnlegenAsync(gerd, "Jugendwerk", Andrea);
        await AusfuehrenAsync(verbindung, "UPDATE organisationen.org_einladung SET \"Rolle\" = 2");

        using var andrea = await AnnehmenAlsAsync(factory, Andrea);

        var json = await JsonAsync(await andrea.GetAsync($"/api/organisationen/{orgId}", Abbruch));
        Assert.Equal(["OrgUeberblick"], json.GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
        // Story 3-3: Org-Überblick sieht nur das Inventar.
        Assert.Equal(["inventar-sehen"], json.GetProperty("erlaubt").EnumerateArray().Select(r => r.GetString()));
        Assert.False((await JsonAsync(await gerd.GetAsync("/api/organisationen", Abbruch)))[0].GetProperty("eingerichtet").GetBoolean());
    }

    [Fact]
    public async Task Ich_Organisationen_liefert_je_Person_nur_die_eigenen_Rollen_und_nur_die_Vertragsfelder()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (orgId, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk", Andrea);
        var zweite = await AnlegenAsync(gerd, "Bezirk", Andrea);
        using (andrea)
        {
            await EinladenAsync(andrea, orgId, Jonas, OrgRolle.OrgUeberblick);
            using var jonas = await AnnehmenAlsAsync(factory, Jonas);
            using var lea = await AngemeldeterClientAsync(factory, Lea);

            var vonAndrea = await JsonAsync(await andrea.GetAsync("/api/ich/organisationen", Abbruch));
            var vonJonas = await JsonAsync(await jonas.GetAsync("/api/ich/organisationen", Abbruch));
            var vonLea = await JsonAsync(await lea.GetAsync("/api/ich/organisationen", Abbruch));
            var vonGerd = await JsonAsync(await gerd.GetAsync("/api/ich/organisationen", Abbruch));

            var eintrag = Assert.Single(vonAndrea.EnumerateArray());
            Assert.Equal(["name", "organisationId", "rollen"], Namen(eintrag));
            Assert.Equal(orgId, eintrag.GetProperty("organisationId").GetGuid());
            Assert.Equal(["OrgAdmin"], eintrag.GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
            Assert.Equal(["OrgUeberblick"], Assert.Single(vonJonas.EnumerateArray()).GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
            Assert.Empty(vonLea.EnumerateArray());
            Assert.Empty(vonGerd.EnumerateArray());
            Assert.NotEqual(orgId, zweite);
        }
    }

    [Fact]
    public async Task Ich_Organisationen_liefert_bei_mehreren_Organisationen_je_Eintrag_nur_dessen_Rollen_nach_Name()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (zeltlager, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Zeltlager", Andrea);
        using (andrea)
        {
            var bezirk = await AnlegenAsync(gerd, "Bezirk", Andrea);
            using (var antwort = await AnnehmenAsync(andrea, Token(factory, Andrea)))
            {
                Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            }

            await EinladenAsync(andrea, bezirk, Andrea, OrgRolle.OrgUeberblick);
            using (var antwort = await AnnehmenAsync(andrea, Token(factory, Andrea)))
            {
                Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            }

            var eintraege = (await JsonAsync(await andrea.GetAsync("/api/ich/organisationen", Abbruch))).EnumerateArray().ToList();

            Assert.Equal([bezirk, zeltlager], eintraege.Select(e => e.GetProperty("organisationId").GetGuid()));
            Assert.Equal(["OrgAdmin", "OrgUeberblick"], eintraege[0].GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
            Assert.Equal(["OrgAdmin"], eintraege[1].GetProperty("rollen").EnumerateArray().Select(r => r.GetString()));
        }
    }

    [Fact]
    public async Task Annehmen_ist_zugriffsbegrenzt()
    {
        await using var factory = AnmeldeHelfer.Factory(await postgres.NeueDatenbankAsync(), mitZugriffsbegrenzung: true);
        factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "7";
        using var andrea = factory.CreateClient();
        await AnmeldenAsync(factory, andrea, Andrea);

        // Ein Token für alle Versuche: Auch das Holen des Tokens zählt zur Grenze.
        var token = await AntiforgeryTokenAsync(andrea);
        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/organisationen/einladungen/annehmen")
            {
                Content = JsonContent.Create(new EinladungAnnehmen("x")),
            };
            anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
            using var antwort = await andrea.SendAsync(anfrage, Abbruch);
            status.Add(antwort.StatusCode);
        }

        Assert.Contains(HttpStatusCode.NotFound, status);
        Assert.Equal(HttpStatusCode.TooManyRequests, status[^1]);
    }
}
