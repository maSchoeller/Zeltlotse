using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Durchlauf-Test (AD-28, AD-30) für die Zeile <c>AnmeldungEingegangen</c> (Freizeiten → Personen) der Tabelle Ereignis-Fluss:
/// Katrin meldet Emma über den Anmeldelink für Teilnehmende mit Konto an, ein Gast meldet Lina an; die Outbox wird sofort
/// abgearbeitet (zweimal). Danach steht je Anmeldung genau eine Freigabe der Stufe Leitung in <c>personen.freigabe</c>, mit der
/// Organisation aus dem Umschlag und „gültig bis“ = Ende des Freigabefensters der Freizeit; die Inbox verwirft die zweite Zustellung.
/// </summary>
public sealed class AnmeldungEingegangenTests(PostgresContainer postgres)
{
    private const string Gerd = "gerd.betreiber@example.org";
    private const string Andrea = "andrea.beispiel@example.org";
    private const string Miriam = "miriam.zeltner@example.org";
    private const string Katrin = "katrin.muster@example.org";

    /// <summary>Letzter Tag 13.08.2027 plus 30 Tage Freigabe-Intervall: 13.09.2027 00:00 Europe/Berlin.</summary>
    private static readonly DateTimeOffset FreigabeEnde = new(2027, 9, 12, 22, 0, 0, TimeSpan.Zero);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anmelden_Outbox_zustellen_ergibt_je_Anmeldung_genau_eine_Freigabe_der_Stufe_Leitung()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = new ZeltlotseApiFactory(verbindung);
        factory.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "10000";
        factory.Einstellungen["Zeltlotse:GlobalAdmins:0"] = Gerd;

        // Organisation mit Andrea, Freizeit mit Miriam als Leitung; Miriam liest den Anmeldelink für Teilnehmende.
        using var gerd = await AngemeldetAsync(factory, Gerd);
        var orgId = (await AntwortAsync<OrganisationAngelegt>(await SendenAsync(gerd, "/api/organisationen", new OrganisationAnlegen("Jugendwerk", Andrea)), HttpStatusCode.Created)).Id;
        using var andrea = await AngemeldetAsync(factory, Andrea);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(andrea, "/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(TokenAus(factory, Andrea, "Einladung zu Zeltlotse")))).StatusCode);
        var freizeitId = (await AntwortAsync<FreizeitAngelegt>(
            await SendenAsync(andrea, $"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen("Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13))),
            HttpStatusCode.Created)).Id;
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(andrea, $"/api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen", new LeitungEinladen(Miriam))).StatusCode);
        using var miriam = await AngemeldetAsync(factory, Miriam);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(miriam, "/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(TokenAus(factory, Miriam, "Einladung als Leitung in Zeltlotse")))).StatusCode);
        var einstellungen = await miriam.GetFromJsonAsync<FreizeitseiteEinstellungen>($"/api/freizeiten/{freizeitId}/freizeitseite", Abbruch);
        var adresse = einstellungen!.Anmeldelinks.Single(l => l.Art == AnmeldelinkArt.Teilnehmende).Adresse;
        var link = adresse[(adresse.LastIndexOf('/') + 1)..];

        // Katrin meldet Emma mit Konto an.
        using var katrin = await AngemeldetAsync(factory, Katrin);
        var emma = (await AntwortAsync<KindHinzugefuegt>(await SendenAsync(katrin, "/api/ich/familie/kinder", new KindHinzufuegen("Emma", "Muster", new DateOnly(2017, 9, 2))), HttpStatusCode.Created)).PersonId;
        var anmeldungEmma = (await AntwortAsync<AnmeldungAngelegt>(await SendenAsync(katrin, "/api/ich/anmeldungen", new AnmeldungAbsenden(link, emma, true)), HttpStatusCode.Created)).AnmeldungId;

        // Ein Gast meldet Lina an.
        using var anonym = factory.CreateClient();
        var gast = await AntwortAsync<GastAngelegt>(
            await SendenAsync(anonym, "/api/personen/oeffentlich/gaeste", new GastAnlegen("Sabine", "Beispiel", "lina.eltern@example.org", "Lina", "Beispiel", new DateOnly(2016, 4, 1))),
            HttpStatusCode.Created);
        var anmeldungLina = (await AntwortAsync<AnmeldungAngelegt>(
            await SendenAsync(anonym, $"/api/freizeiten/oeffentlich/{link}/anmeldungen", new GastAnmeldungAbsenden(gast.GastToken, gast.KindPersonId, true)),
            HttpStatusCode.Created)).AnmeldungId;
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freigabe"));

        await factory.OutboxJetztZustellenAsync(Abbruch);
        await factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(2L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freigabe"));
        foreach (var (anmeldung, person) in new[] { (anmeldungEmma, emma), (anmeldungLina, gast.KindPersonId) })
        {
            Assert.Equal(1L, await WertAsync<long>(
                verbindung,
                "SELECT count(*) FROM personen.freigabe WHERE \"AnmeldungId\" = @a AND \"PersonId\" = @p AND \"FreizeitId\" = @f AND \"OrganisationId\" = @o " +
                "AND \"Stufe\" = 1 AND \"GueltigBis\" = @bis",
                ("a", anmeldung), ("p", person), ("f", freizeitId), ("o", orgId), ("bis", FreigabeEnde)));
        }

        var personenEmpfaenger = "Zeltlotse.Personen.Freigaben.FreigabeAnlegen";
        Assert.Equal(2L, await WertAsync<long>(
            verbindung, "SELECT count(*) FROM freizeiten.outbox_zustellung WHERE \"Empfaenger\" = @e AND \"ErledigtAm\" IS NOT NULL", ("e", personenEmpfaenger)));
        Assert.Equal(2L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.inbox WHERE \"Empfaenger\" = @e", ("e", personenEmpfaenger)));

        // Der Gast-Link gilt jetzt bis zum Ende des Freigabefensters.
        var ansicht = await anonym.GetFromJsonAsync<JsonElement>($"/api/freizeiten/gast/{gast.GastToken}", Abbruch);
        Assert.Equal(FreigabeEnde, ansicht.GetProperty("gueltigBis").GetDateTimeOffset());
    }

    private static async Task<HttpClient> AngemeldetAsync(ZeltlotseApiFactory factory, string mail)
    {
        var client = factory.CreateClient();
        var vorher = factory.Mails.An(mail).Count;
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(client, "/api/konten/login-link", new LoginLinkAnfordern(mail))).StatusCode);
        var token = (await factory.Mails.WartenAufAsync(mail, vorher + 1)).Token;
        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(client, "/api/ich/profil", null)).StatusCode);
        return client;
    }

    private static string TokenAus(ZeltlotseApiFactory factory, string mail, string betreff) =>
        factory.Mails.An(mail).Last(m => m.Betreff == betreff).Token;

    private static async Task<HttpResponseMessage> SendenAsync(HttpClient client, string pfad, object? koerper)
    {
        var token = (await client.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", Abbruch))!.Token;
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, pfad) { Content = koerper is null ? null : JsonContent.Create(koerper, koerper.GetType()) };
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
        return await client.SendAsync(anfrage, Abbruch);
    }

    private static async Task<T> AntwortAsync<T>(HttpResponseMessage antwort, HttpStatusCode erwartet)
    {
        using (antwort)
        {
            Assert.Equal(erwartet, antwort.StatusCode);
            return (await antwort.Content.ReadFromJsonAsync<T>(Abbruch))!;
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
