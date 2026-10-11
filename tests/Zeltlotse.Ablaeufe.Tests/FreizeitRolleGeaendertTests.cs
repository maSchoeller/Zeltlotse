using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Testing;

namespace Zeltlotse.Ablaeufe.Tests;

/// <summary>
/// Durchlauf-Test (AD-28, AD-30) für die Zeile <c>FreizeitRolleGeaendert</c> (Freizeiten → Personen) der Tabelle
/// Ereignis-Fluss: Leitung einladen und annehmen über die echten Endpunkte, Outbox sofort abarbeiten (zweimal), danach steht
/// genau eine Zeile in <c>personen.freizeit_rolle</c> mit der Rolle Leitung und der Organisation aus dem Umschlag.
/// </summary>
public sealed class FreizeitRolleGeaendertTests(PostgresContainer postgres)
{
    private const string Gerd = "gerd.betreiber@example.org";
    private const string Andrea = "andrea.beispiel@example.org";
    private const string Miriam = "miriam.zeltner@example.org";

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Einladen_Annehmen_Outbox_zustellen_ergibt_genau_eine_Rollen_Zeile_in_Personen()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        factory.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "10000";
        factory.Einstellungen["Zeltlotse:GlobalAdmins:0"] = Gerd;

        // Organisation mit Andrea als Org-Admin.
        using var gerd = await AngemeldetAsync(factory, Gerd);
        var orgId = await IdAsync<OrganisationAngelegt>(await PostAsync(gerd, "/api/organisationen", new OrganisationAnlegen("Jugendwerk", Andrea)), HttpStatusCode.Created, a => a.Id);
        using var andrea = await AngemeldetAsync(factory, Andrea);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(andrea, "/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(TokenAus(factory, Andrea, "Einladung zu Zeltlotse")))).StatusCode);

        // Freizeit anlegen, Miriam als Leitung einladen, Miriam nimmt an.
        var freizeitId = await IdAsync<FreizeitAngelegt>(
            await PostAsync(andrea, $"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen("Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13))),
            HttpStatusCode.Created,
            f => f.Id);
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(andrea, $"/api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen", new LeitungEinladen(Miriam))).StatusCode);
        using var miriam = await AngemeldetAsync(factory, Miriam);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(miriam, "/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(TokenAus(factory, Miriam, "Einladung als Leitung in Zeltlotse")))).StatusCode);
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeit_rolle"));

        await factory.OutboxJetztZustellenAsync(Abbruch);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        var personId = await WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Miriam));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeit_rolle"));
        Assert.Equal(1L, await WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM personen.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"Rolle\" = 1 AND \"OrganisationId\" = @o",
            ("f", freizeitId), ("p", personId), ("o", orgId)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.outbox_zustellung WHERE \"ErledigtAm\" IS NOT NULL"));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.inbox"));
    }

    private static async Task<HttpClient> AngemeldetAsync(ZeltlotseApiFactory factory, string mail)
    {
        var client = factory.CreateClient();
        var vorher = factory.Mails.An(mail).Count;
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(client, "/api/konten/login-link", new LoginLinkAnfordern(mail))).StatusCode);
        var token = (await factory.Mails.WartenAufAsync(mail, vorher + 1)).Token;
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token))).StatusCode);
        return client;
    }

    private static string TokenAus(ZeltlotseApiFactory factory, string mail, string betreff) =>
        factory.Mails.An(mail).Last(m => m.Betreff == betreff).Token;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string pfad, object koerper)
    {
        var token = (await client.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", Abbruch))!.Token;
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, pfad) { Content = JsonContent.Create(koerper, koerper.GetType()) };
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
        return await client.SendAsync(anfrage, Abbruch);
    }

    private static async Task<Guid> IdAsync<T>(HttpResponseMessage antwort, HttpStatusCode erwartet, Func<T, Guid> id)
    {
        using (antwort)
        {
            Assert.Equal(erwartet, antwort.StatusCode);
            return id((await antwort.Content.ReadFromJsonAsync<T>(Abbruch))!);
        }
    }

    private static async Task<T> WertAsync<T>(string verbindung, string sql, params (string Name, object Wert)[] parameter)
    {
        await using var db = new NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        return (T)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }
}
