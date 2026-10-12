using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;

namespace Zeltlotse.Ablaeufe.Tests;

/// <summary>
/// Durchlauf-Test (AD-28, AD-30) für die Zeile <c>NachweisEingesehen</c> (Personen → Organisationen) der Tabelle Ereignis-Fluss:
/// Jonas lädt seine Erste-Hilfe-Bescheinigung hoch und gibt frei, Miriam übernimmt die Prüfung und bestätigt; die Outbox wird
/// sofort abgearbeitet (zweimal). Danach steht genau eine Zeile in <c>organisationen.nachweis_bestaetigung</c> mit „gültig bis“
/// aus der Richtlinie (24 Monate), und die Inbox hat das Ereignis genau einmal verarbeitet.
/// </summary>
public sealed class NachweisEingesehenTests(PostgresContainer postgres, S3Container s3)
{
    private const string Gerd = "gerd.betreiber@example.org";
    private const string Andrea = "andrea.beispiel@example.org";
    private const string Jonas = "jonas.huettner@example.org";
    private const string Miriam = "miriam.zeltner@example.org";

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bestaetigen_Outbox_zustellen_ergibt_genau_eine_Bestaetigung_in_Organisationen()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        factory.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "10000";
        factory.Einstellungen["Zeltlotse:GlobalAdmins:0"] = Gerd;
        foreach (var (schluessel, wert) in s3.Einstellungen(S3Container.NeuerBucket()))
        {
            factory.Einstellungen[schluessel] = wert;
        }

        // Organisation, Freizeit, Jonas und Miriam als Leitung; die Rollen erreichen Personen über die Outbox.
        using var gerd = await AngemeldetAsync(factory, Gerd);
        var orgId = await IdAsync<OrganisationAngelegt>(await SendenAsync(gerd, "/api/organisationen", new OrganisationAnlegen("Jugendwerk", Andrea)), HttpStatusCode.Created, a => a.Id);
        using var andrea = await AngemeldetAsync(factory, Andrea);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(andrea, "/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(TokenAus(factory, Andrea, "Einladung zu Zeltlotse")))).StatusCode);
        var freizeitId = await IdAsync<FreizeitAngelegt>(
            await SendenAsync(andrea, $"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen("Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13))),
            HttpStatusCode.Created,
            f => f.Id);
        using var jonas = await LeitungAsync(factory, andrea, orgId, freizeitId, Jonas);
        using var miriam = await LeitungAsync(factory, andrea, orgId, freizeitId, Miriam);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        // Jonas: Erste Hilfe (2025-04-15) mit Datei, freigeben; Miriam übernimmt und bestätigt.
        var nachweisId = await IdAsync<NachweisHinzugefuegt>(
            await SendenAsync(jonas, "/api/ich/nachweise", new NachweisHinzufuegen(Zeltlotse.Api.Contracts.Personen.NachweisArt.ErsteHilfe, new DateOnly(2025, 4, 15))),
            HttpStatusCode.Created,
            n => n.NachweisId);
        Assert.Equal(HttpStatusCode.OK, (await HochladenAsync(jonas, nachweisId)).StatusCode);
        var einsichtId = (await jonas.GetFromJsonAsync<NachweiseAntwort>("/api/ich/nachweise", Abbruch))!.Anfragen.Single().Id;
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(jonas, $"/api/ich/nachweise/einsichten/{einsichtId}/freigeben", null)).StatusCode);
        var basis = $"/api/freizeiten/{freizeitId}/personen/nachweise/einsichten/{einsichtId}";
        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(miriam, $"{basis}/pruefen", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(miriam, $"{basis}/nachweise/{nachweisId}/bestaetigen", null)).StatusCode);
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung"));

        await factory.OutboxJetztZustellenAsync(Abbruch);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        var jonasId = await WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Jonas));
        var miriamId = await WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Miriam));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung"));
        Assert.Equal(1L, await WertAsync<long>(
            verbindung,
            "SELECT count(*) FROM organisationen.nachweis_bestaetigung WHERE \"OrganisationId\" = @o AND \"PersonId\" = @p AND \"Art\" = 3 AND \"GueltigBis\" = '2027-04-15' AND \"GeprueftVon\" = @m",
            ("o", orgId), ("p", jonasId), ("m", miriamId)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.outbox_zustellung z JOIN personen.outbox o ON o.\"Id\" = z.\"EreignisId\" WHERE o.\"Typ\" LIKE '%.NachweisEingesehen' AND z.\"ErledigtAm\" IS NOT NULL"));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.inbox"));

        // Ein zweites Zustellen desselben Ereignisses verwirft die Inbox (Zustellstand zurücksetzen und erneut zustellen).
        await AusfuehrenAsync(verbindung, "UPDATE personen.outbox_zustellung SET \"ErledigtAm\" = NULL");
        await factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung"));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM organisationen.inbox"));
    }

    private static async Task<HttpClient> LeitungAsync(ZeltlotseApiFactory factory, HttpClient andrea, Guid orgId, Guid freizeitId, string mail)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(andrea, $"/api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen", new LeitungEinladen(mail))).StatusCode);
        var client = await AngemeldetAsync(factory, mail);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(client, "/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(TokenAus(factory, mail, "Einladung als Leitung in Zeltlotse")))).StatusCode);
        return client;
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

    private static async Task<string> TokenAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", Abbruch))!.Token;

    private static async Task<HttpResponseMessage> SendenAsync(HttpClient client, string pfad, object? koerper)
    {
        var token = await TokenAsync(client);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, pfad);
        if (koerper is not null)
        {
            anfrage.Content = JsonContent.Create(koerper, koerper.GetType());
        }

        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
        return await client.SendAsync(anfrage, Abbruch);
    }

    private static async Task<HttpResponseMessage> HochladenAsync(HttpClient client, Guid nachweisId)
    {
        var token = await TokenAsync(client);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, $"/api/ich/nachweise/{nachweisId}/datei");
        var datei = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
        datei.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        anfrage.Content = new MultipartFormDataContent { { datei, "datei", "erste-hilfe.png" } };
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

    private static async Task AusfuehrenAsync(string verbindung, string sql)
    {
        await using var db = new NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        await befehl.ExecuteNonQueryAsync(Abbruch);
    }
}
