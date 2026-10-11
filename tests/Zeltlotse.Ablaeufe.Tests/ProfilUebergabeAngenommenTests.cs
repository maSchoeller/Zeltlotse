using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;

namespace Zeltlotse.Ablaeufe.Tests;

/// <summary>
/// Durchlauf-Test (AD-28, AD-30) für die Zeile <c>ProfilUebergabeAngenommen</c> (Personen → Konten) der Tabelle Ereignis-Fluss:
/// Katrin übergibt Pauls Profil, Paul nimmt mit einer neuen Mail-Adresse an, die Outbox wird sofort abgearbeitet (zweimal).
/// Danach gehört Pauls Konto zu Pauls Profil, der Sitzungsstempel ist neu, die alte Sitzung endet mit <c>401</c>, und nach der
/// neuen Anmeldung liefert <c>GET /api/ich/profil</c> Pauls Namen.
/// </summary>
public sealed class ProfilUebergabeAngenommenTests(PostgresContainer postgres)
{
    private const string Katrin = "katrin.muster@example.org";
    private const string Paul = "paul.muster@example.org";

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Annehmen_Outbox_zustellen_verbindet_das_Konto_mit_Pauls_Profil()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        factory.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "10000";

        // Katrin legt Paul an, setzt das Geburtsdatum auf volljährig und übergibt an Pauls neue Mail-Adresse.
        using var katrin = await AngemeldetAsync(factory, Katrin);
        Guid kind;
        using (var angelegt = await SendenAsync(katrin, HttpMethod.Post, "/api/ich/familie/kinder", new KindHinzufuegen("Paul", "Muster", new DateOnly(2015, 3, 14))))
        {
            Assert.Equal(HttpStatusCode.Created, angelegt.StatusCode);
            kind = (await angelegt.Content.ReadFromJsonAsync<KindHinzugefuegt>(Abbruch))!.PersonId;
        }

        var version = (await katrin.GetFromJsonAsync<FamilieAntwort>("/api/ich/familie", Abbruch))!.Kinder.Single().Version;
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(katrin, HttpMethod.Patch, $"/api/ich/familie/kinder/{kind}", $$"""{"version":{{version}},"alterWert":"2015-03-14","geburtsdatum":"2008-03-14"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(katrin, HttpMethod.Post, $"/api/ich/familie/kinder/{kind}/uebergabe", new ProfilUebergeben(Paul))).StatusCode);

        // Paul meldet sich mit der neuen Mail-Adresse an (leeres Profil) und nimmt an.
        using var paul = await AngemeldetAsync(factory, Paul);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(paul, HttpMethod.Post, "/api/ich/profil", null)).StatusCode);
        var stempel = await WertAsync<Guid>(verbindung, "SELECT \"SitzungsStempel\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Paul));
        var token = factory.Mails.An(Paul).Last(m => m.Betreff == "Profil in Zeltlotse übernehmen").Token;
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(paul, HttpMethod.Post, "/api/personen/uebergabe/annehmen", new ProfilUebernehmen(token))).StatusCode);

        await factory.OutboxJetztZustellenAsync(Abbruch);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(kind, await WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Paul)));
        Assert.NotEqual(stempel, await WertAsync<Guid>(verbindung, "SELECT \"SitzungsStempel\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Paul)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.outbox_zustellung WHERE \"ErledigtAm\" IS NOT NULL"));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.inbox"));

        // Die alte Sitzung gilt nicht mehr; nach der neuen Anmeldung gehört das Profil Paul.
        Assert.Equal(HttpStatusCode.Unauthorized, (await paul.GetAsync("/api/ich/profil", Abbruch)).StatusCode);
        using var neu = await AngemeldetAsync(factory, Paul);
        var profil = await neu.GetFromJsonAsync<ProfilAntwort>("/api/ich/profil", Abbruch);
        Assert.Equal(("Paul", "Muster"), (profil!.Vorname, profil.Nachname));
        Assert.Empty((await katrin.GetFromJsonAsync<FamilieAntwort>("/api/ich/familie", Abbruch))!.Kinder);
    }

    private static async Task<HttpClient> AngemeldetAsync(ZeltlotseApiFactory factory, string mail)
    {
        var client = factory.CreateClient();
        var vorher = factory.Mails.An(mail).Count;
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(client, HttpMethod.Post, "/api/konten/login-link", new LoginLinkAnfordern(mail))).StatusCode);
        var token = (await factory.Mails.WartenAufAsync(mail, vorher + 1)).Token;
        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(client, HttpMethod.Post, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token))).StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> SendenAsync(HttpClient client, HttpMethod methode, string pfad, object? koerper)
    {
        var token = (await client.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", Abbruch))!.Token;
        using var anfrage = new HttpRequestMessage(methode, pfad);
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
        anfrage.Content = koerper switch
        {
            null => null,
            string json => new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            _ => JsonContent.Create(koerper, koerper.GetType()),
        };
        return await client.SendAsync(anfrage, Abbruch);
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
