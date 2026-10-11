using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Matrix-Zeilen „Organisation anlegen“, „Falsche Rolle global“ und „Rollen und Einladen“ (FR-5, AD-30): Global Admin
/// legt an und lädt ein, Org-Admin sieht Rollen und lädt ein; erlaubte Aktionen in <c>/api/ich/konto</c>.
/// </summary>
public sealed class OrganisationenTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Global_Admin_legt_an_201_mit_einer_Einladungs_Mail_und_die_Liste_zeigt_nicht_eingerichtet()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);

        using var antwort = await PostAsync(gerd, "/api/organisationen", new OrganisationAnlegen("  Jugendwerk Musterstadt ", " Andrea.Beispiel@Example.org"));

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var id = (await antwort.Content.ReadFromJsonAsync<OrganisationAngelegt>(Abbruch))!.Id;
        Assert.Equal($"/api/organisationen/{id}", antwort.Headers.Location?.OriginalString);
        var mail = Assert.Single(factory.Mails.An(Andrea));
        Assert.Equal("Einladung zu Zeltlotse", mail.Betreff);
        Assert.Contains("„Jugendwerk Musterstadt“", mail.Text, StringComparison.Ordinal);
        Assert.Contains("„Org-Admin“", mail.Text, StringComparison.Ordinal);
        Assert.StartsWith("https://localhost/app/einladung/", mail.Link, StringComparison.Ordinal);
        Assert.Contains("7 Tage", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Zum Annehmen mit dieser Mail-Adresse anmelden.", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(?:[Dd]u|Sie|Ihr|[Dd]ein)\b", mail.Text);

        var liste = await gerd.GetFromJsonAsync<List<OrganisationZeile>>("/api/organisationen", Abbruch);
        Assert.Equal([new OrganisationZeile(id, "Jugendwerk Musterstadt", false)], liste);
    }

    [Fact]
    public async Task Liste_ist_nach_Name_sortiert_und_enthaelt_nur_die_Vertragsfelder()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        await AnlegenAsync(gerd, "Zeltlager-Verein", Andrea);
        await AnlegenAsync(gerd, "Bezirksjugendwerk", Jonas);

        using var antwort = await gerd.GetAsync("/api/organisationen", Abbruch);

        var json = await JsonAsync(antwort);
        Assert.Equal(["Bezirksjugendwerk", "Zeltlager-Verein"], json.EnumerateArray().Select(o => o.GetProperty("name").GetString()));
        Assert.All(json.EnumerateArray(), o => Assert.Equal(["eingerichtet", "id", "name"], Namen(o)));
    }

    [Theory]
    [InlineData("", Andrea, "name")]
    [InlineData("   ", Andrea, "name")]
    [InlineData(null, Andrea, "name")]
    [InlineData("101", Andrea, "name")]
    [InlineData("Jugendwerk", "keine-mail", "mail")]
    [InlineData("Jugendwerk", "", "mail")]
    public async Task Anlegen_mit_ungueltigen_Angaben_ergibt_400_je_Feld_ohne_Organisation_und_ohne_Mail(string? name, string mail, string feld)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);

        using var antwort = await PostAsync(gerd, "/api/organisationen", new OrganisationAnlegen(name == "101" ? new string('x', 101) : name, mail));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal([feld], problem.GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.organisation"));
        Assert.DoesNotContain(factory.Mails.Alle(), m => m.Betreff == "Einladung zu Zeltlotse");
    }

    [Fact]
    public async Task Ohne_Global_Admin_ergeben_GET_und_POST_403_auch_bei_ungueltigem_Koerper()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var andrea = await AngemeldeterClientAsync(factory, Andrea);

        using var liste = await andrea.GetAsync("/api/organisationen", Abbruch);
        using var anlegen = await PostAsync(andrea, "/api/organisationen", new OrganisationAnlegen("Jugendwerk", Jonas));
        using var leer = await PostAsync(andrea, "/api/organisationen", new OrganisationAnlegen("", "keine"));
        using var kaputt = await PostAsync(andrea, "/api/organisationen", "kein json");

        foreach (var antwort in new[] { liste, anlegen, leer, kaputt })
        {
            Assert.Equal(HttpStatusCode.Forbidden, antwort.StatusCode);
            Assert.Equal(403, (await ProblemAsync(antwort)).GetProperty("status").GetInt32());
        }

        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.organisation"));
    }

    [Fact]
    public async Task Ich_Konto_erlaubt_Organisationen_anlegen_nur_dem_Global_Admin()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        using var andrea = await AngemeldeterClientAsync(factory, Andrea);

        var gerdKonto = await JsonAsync(await gerd.GetAsync("/api/ich/konto", Abbruch));
        var andreaKonto = await JsonAsync(await andrea.GetAsync("/api/ich/konto", Abbruch));

        Assert.Equal(["organisationen-anlegen"], gerdKonto.GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(andreaKonto.GetProperty("erlaubt").EnumerateArray());
    }

    [Fact]
    public async Task Org_Admin_sieht_Rollen_mit_Namen_ohne_Mail_Adressen_und_nur_offene_nicht_abgelaufene_Einladungen()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (orgId, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk", Andrea);
        using (andrea)
        {
            using (var profil = await PostAsync(andrea, "/api/ich/profil"))
            {
                var version = (await JsonAsync(profil)).GetProperty("version").GetUInt32();
                using var vorname = await SendenAsync(andrea, HttpMethod.Patch, "/api/ich/profil", $$"""{"version":{{version}},"alterWert":null,"vorname":"Andrea"}""");
                Assert.Equal(HttpStatusCode.OK, vorname.StatusCode);
            }

            await EinladenAsync(andrea, orgId, Jonas, OrgRolle.OrgUeberblick);
            using var jonas = await AnnehmenAlsAsync(factory, Jonas);
            await EinladenAsync(andrea, orgId, Lea, OrgRolle.OrgAdmin);
            await EinladenAsync(andrea, orgId, "alt@example.org", OrgRolle.OrgUeberblick);
            factory.Uhr.Advance(TimeSpan.FromDays(6));
            await EinladenAsync(andrea, orgId, "VORSTAND@example.org", OrgRolle.OrgUeberblick);
            factory.Uhr.Advance(TimeSpan.FromDays(2));

            using var antwort = await andrea.GetAsync($"/api/organisationen/{orgId}/rollen", Abbruch);

            Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            var json = await JsonAsync(antwort);
            Assert.Equal(["einladungen", "personen"], Namen(json));
            var personen = json.GetProperty("personen").EnumerateArray().ToList();
            Assert.All(personen, p => Assert.Equal(["nachname", "personId", "rolle", "vorname"], Namen(p)));
            Assert.DoesNotContain("@", personen.Select(p => p.ToString()).Aggregate((a, b) => a + b), StringComparison.Ordinal);
            Assert.Equal(
                [(await PersonIdAsync(verbindung, Andrea), "Andrea", "OrgAdmin"), (await PersonIdAsync(verbindung, Jonas), (string?)null, "OrgUeberblick")],
                personen.Select(p => (p.GetProperty("personId").GetGuid(), p.GetProperty("vorname").GetString(), p.GetProperty("rolle").GetString()!))
                    .OrderBy(p => p.Item3, StringComparer.Ordinal));
            // Personen mit Namen zuerst, ohne Profilnamen („Name fehlt“) zuletzt.
            Assert.Equal(["Andrea", null], personen.Select(p => p.GetProperty("vorname").GetString()));
            var einladung = Assert.Single(json.GetProperty("einladungen").EnumerateArray());
            Assert.Equal(["gueltigBis", "id", "mail", "rolle"], Namen(einladung));
            Assert.Equal("vorstand@example.org", einladung.GetProperty("mail").GetString());
            Assert.Equal("OrgUeberblick", einladung.GetProperty("rolle").GetString());
        }
    }

    [Fact]
    public async Task Org_Admin_laedt_ein_202_mit_Mail_und_400_je_Feld()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (orgId, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk Musterstadt", Andrea);
        using (andrea)
        {
            using var antwort = await PostAsync(andrea, $"/api/organisationen/{orgId}/einladungen", new EinladungSchicken("Jonas.Huettner@example.org", OrgRolle.OrgUeberblick));

            Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
            var id = (await antwort.Content.ReadFromJsonAsync<EinladungVerschickt>(Abbruch))!.Id;
            Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_einladung WHERE \"Id\" = @id AND \"Mail\" = @mail", ("id", id), ("mail", Jonas)));
            var mail = Assert.Single(factory.Mails.An(Jonas));
            Assert.Contains("„Org-Überblick“", mail.Text, StringComparison.Ordinal);
            Assert.Contains("„Jugendwerk Musterstadt“", mail.Text, StringComparison.Ordinal);

            using var ohneRolle = await PostAsync(andrea, $"/api/organisationen/{orgId}/einladungen", """{"mail":"x@example.org"}""");
            Assert.Equal(HttpStatusCode.BadRequest, ohneRolle.StatusCode);
            Assert.Equal(["rolle"], (await ProblemAsync(ohneRolle)).GetProperty("errors").EnumerateObject().Select(e => e.Name));

            using var falscheMail = await PostAsync(andrea, $"/api/organisationen/{orgId}/einladungen", new EinladungSchicken("keine", OrgRolle.OrgAdmin));
            Assert.Equal(HttpStatusCode.BadRequest, falscheMail.StatusCode);
            Assert.Equal(["mail"], (await ProblemAsync(falscheMail)).GetProperty("errors").EnumerateObject().Select(e => e.Name));
            Assert.Single(factory.Mails.An(Jonas));
        }
    }

    [Fact]
    public async Task Einladen_mit_unbekannter_Rolle_ergibt_400_am_Feld_rolle_ohne_Einladung()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (orgId, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk", Andrea);
        using (andrea)
        {
            var vorher = await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_einladung");

            using var antwort = await PostAsync(andrea, $"/api/organisationen/{orgId}/einladungen", """{"mail":"x@example.org","rolle":99}""");

            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.Equal(["rolle"], (await ProblemAsync(antwort)).GetProperty("errors").EnumerateObject().Select(e => e.Name));
            Assert.Equal(vorher, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_einladung"));
            Assert.Empty(factory.Mails.An("x@example.org"));
        }
    }
}
