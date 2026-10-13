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
/// Durchlauf-Test (AD-28, AD-30) für die Zeile <c>PersonGeloescht</c> (Personen → jedes Modul mit Personenbezug) der Tabelle
/// Ereignis-Fluss: Jonas (Org-Überblick und Leitung) löscht sein Konto; die Outbox wird sofort abgearbeitet. Danach haben Konten,
/// Organisationen, Freizeiten und Sicherheit (Story 2-9: Schutzkonzept-Bestätigungen) das Ereignis je genau einmal verarbeitet (Inbox),
/// und keines hat noch eine Zeile der Person. Ein
/// zweites Zustellen desselben Ereignisses verwirft die Inbox.
/// </summary>
public sealed class PersonGeloeschtTests(PostgresContainer postgres)
{
    private const string Gerd = "gerd.betreiber@example.org";
    private const string Andrea = "andrea.beispiel@example.org";
    private const string Jonas = "jonas.huettner@example.org";

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Konto_loeschen_Outbox_zustellen_loescht_in_Konten_Organisationen_und_Freizeiten_genau_einmal()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        factory.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "10000";
        factory.Einstellungen["Zeltlotse:GlobalAdmins:0"] = Gerd;

        using var gerd = await AngemeldetAsync(factory, Gerd);
        var orgId = await IdAsync<OrganisationAngelegt>(await SendenAsync(gerd, "/api/organisationen", new OrganisationAnlegen("Jugendwerk", Andrea)), HttpStatusCode.Created, a => a.Id);
        using var andrea = await AngemeldetAsync(factory, Andrea);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(andrea, "/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(TokenAus(factory, Andrea, "Einladung zu Zeltlotse")))).StatusCode);
        var freizeitId = await IdAsync<FreizeitAngelegt>(
            await SendenAsync(andrea, $"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen("Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13))),
            HttpStatusCode.Created,
            f => f.Id);
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(andrea, $"/api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen", new LeitungEinladen(Jonas))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(andrea, $"/api/organisationen/{orgId}/einladungen", new EinladungSchicken(Jonas, OrgRolle.OrgUeberblick))).StatusCode);
        using var jonas = await AngemeldetAsync(factory, Jonas);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(jonas, "/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(TokenAus(factory, Jonas, "Einladung als Leitung in Zeltlotse")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(jonas, "/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(TokenAus(factory, Jonas, "Einladung zu Zeltlotse")))).StatusCode);
        await factory.OutboxJetztZustellenAsync(Abbruch);
        var jonasId = await WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Jonas));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_rolle WHERE \"PersonId\" = @p", ("p", jonasId)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"PersonId\" = @p", ("p", jonasId)));

        // Story 2-9: eine Schutzkonzept-Bestätigung von Jonas (direkt eingefügt; das Bestätigen selbst prüfen die API-Tests).
        await AusfuehrenAsync(
            verbindung,
            "INSERT INTO sicherheit.schutzkonzept_bestaetigung (\"Id\", \"OrganisationId\", \"FreizeitId\", \"DokumentId\", \"PersonId\", \"GelesenAm\") VALUES (@id, @o, @f, @d, @p, now())",
            ("id", Guid.CreateVersion7()), ("o", orgId), ("f", freizeitId), ("d", Guid.CreateVersion7()), ("p", jonasId));

        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(jonas, "/api/ich/profil/loeschen", null)).StatusCode);
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto WHERE \"PersonId\" = @p", ("p", jonasId)));

        await factory.OutboxJetztZustellenAsync(Abbruch);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto WHERE \"PersonId\" = @p", ("p", jonasId)));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.org_rolle WHERE \"PersonId\" = @p", ("p", jonasId)));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"PersonId\" = @p", ("p", jonasId)));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM sicherheit.schutzkonzept_bestaetigung WHERE \"PersonId\" = @p", ("p", jonasId)));
        Assert.Equal(4L, await WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM personen.outbox_zustellung z JOIN personen.outbox o ON o.\"Id\" = z.\"EreignisId\" WHERE o.\"Typ\" LIKE '%.PersonGeloescht' AND z.\"ErledigtAm\" IS NOT NULL"));
        foreach (var schema in new[] { "konten", "organisationen", "freizeiten", "sicherheit" })
        {
            Assert.Equal(1L, await WertAsync<long>(verbindung, $"SELECT count(*) FROM {schema}.inbox WHERE \"Empfaenger\" LIKE '%PersonLoeschen' OR \"Empfaenger\" LIKE '%KontoLoeschen'"));
        }

        // Ein zweites Zustellen desselben Ereignisses verwirft die Inbox (Zustellstand zurücksetzen und erneut zustellen).
        await AusfuehrenAsync(verbindung, "UPDATE personen.outbox_zustellung SET \"ErledigtAm\" = NULL");
        await factory.OutboxJetztZustellenAsync(Abbruch);
        foreach (var schema in new[] { "konten", "organisationen", "freizeiten", "sicherheit" })
        {
            Assert.Equal(1L, await WertAsync<long>(verbindung, $"SELECT count(*) FROM {schema}.inbox WHERE \"Empfaenger\" LIKE '%PersonLoeschen' OR \"Empfaenger\" LIKE '%KontoLoeschen'"));
        }
    }

    private static async Task<HttpClient> AngemeldetAsync(ZeltlotseApiFactory factory, string mail)
    {
        var client = factory.CreateClient();
        var vorher = factory.Mails.An(mail).Count;
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(client, "/api/konten/login-link", new LoginLinkAnfordern(mail))).StatusCode);
        var token = (await factory.Mails.WartenAufAsync(mail, vorher + 1)).Token;
        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token))).StatusCode);
        // Wie die Web-App nach jeder Anmeldung: Profil und Personen-Schlüssel sicherstellen.
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(client, "/api/ich/profil", null)).StatusCode);
        return client;
    }

    private static string TokenAus(ZeltlotseApiFactory factory, string mail, string betreff) =>
        factory.Mails.An(mail).Last(m => m.Betreff == betreff).Token;

    private static async Task<HttpResponseMessage> SendenAsync(HttpClient client, string pfad, object? koerper)
    {
        var token = (await client.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", Abbruch))!.Token;
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, pfad);
        if (koerper is not null)
        {
            anfrage.Content = JsonContent.Create(koerper, koerper.GetType());
        }

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

    private static async Task AusfuehrenAsync(string verbindung, string sql, params (string Name, object Wert)[] parameter)
    {
        await using var db = new NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        await befehl.ExecuteNonQueryAsync(Abbruch);
    }
}
