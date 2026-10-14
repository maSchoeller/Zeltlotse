using System.Net;
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
/// Gemeinsamer Aufbau der Durchlauf-Tests zu Story 2-3 über die echten Endpunkte: Organisation (Andrea) mit Freizeit „Zeltlager“,
/// Miriam als Leitung, Katrin meldet Emma als Teilnehmende an, Jonas sich selbst als Mitarbeitender. Story 2-5: Vorher wählt Miriam die
/// Fragen (Teilnehmende Allergien und Notfallkontakte, Mitarbeitende Notfallkontakte). Die Outbox ist danach einmal zugestellt
/// (Freigaben der Stufe Leitung mit diesen Fragen bestehen).
/// </summary>
internal sealed class AnmeldeAblauf : IAsyncDisposable
{
    public const string Gerd = "gerd.betreiber@example.org";
    public const string Andrea = "andrea.beispiel@example.org";
    public const string Miriam = "miriam.zeltner@example.org";
    public const string Katrin = "katrin.muster@example.org";
    public const string Jonas = "jonas.huettner@example.org";

    private readonly List<HttpClient> _clients = [];

    private AnmeldeAblauf(string verbindung)
    {
        Verbindung = verbindung;
        Factory = new ZeltlotseApiFactory(verbindung);
        Factory.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        Factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "10000";
        Factory.Einstellungen["Zeltlotse:GlobalAdmins:0"] = Gerd;
    }

    public static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public string Verbindung { get; }

    public ZeltlotseApiFactory Factory { get; }

    public Guid OrgId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public HttpClient MiriamClient { get; private set; } = null!;

    /// <summary>Story 3-3: Andrea als Org-Admin (Inventar pflegen).</summary>
    public HttpClient AndreaClient { get; private set; } = null!;

    public HttpClient JonasClient { get; private set; } = null!;

    public Guid Emma { get; private set; }

    public Guid JonasPerson { get; private set; }

    public Guid AnmeldungEmma { get; private set; }

    public Guid AnmeldungJonas { get; private set; }

    public static async Task<AnmeldeAblauf> AufbauenAsync(PostgresContainer postgres)
    {
        var ablauf = new AnmeldeAblauf(await postgres.NeueDatenbankAsync());
        await ablauf.InitAsync();
        return ablauf;
    }

    private async Task InitAsync()
    {
        var gerd = await AngemeldetAsync(Gerd);
        OrgId = (await AntwortAsync<OrganisationAngelegt>(await SendenAsync(gerd, "/api/organisationen", new OrganisationAnlegen("Jugendwerk", Andrea)), HttpStatusCode.Created)).Id;
        var andrea = await AngemeldetAsync(Andrea);
        AndreaClient = andrea;
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(andrea, "/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(TokenAus(Andrea, "Einladung zu Zeltlotse")))).StatusCode);
        FreizeitId = (await AntwortAsync<FreizeitAngelegt>(
            await SendenAsync(andrea, $"/api/organisationen/{OrgId}/freizeiten", new FreizeitAnlegen("Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13))),
            HttpStatusCode.Created)).Id;
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(andrea, $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}/leitung/einladungen", new LeitungEinladen(Miriam))).StatusCode);
        MiriamClient = await AngemeldetAsync(Miriam);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(MiriamClient, "/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(TokenAus(Miriam, "Einladung als Leitung in Zeltlotse")))).StatusCode);
        var einstellungen = await MiriamClient.GetFromJsonAsync<FreizeitseiteEinstellungen>($"/api/freizeiten/{FreizeitId}/freizeitseite", Abbruch);
        string Link(AnmeldelinkArt art)
        {
            var adresse = einstellungen!.Anmeldelinks.Single(l => l.Art == art).Adresse;
            return adresse[(adresse.LastIndexOf('/') + 1)..];
        }

        await FragenWaehlenAsync(FragenFeld.Teilnehmende, "allergien,notfallkontakte");
        await FragenWaehlenAsync(FragenFeld.Mitarbeitende, "notfallkontakte");

        var katrin = await AngemeldetAsync(Katrin);
        Emma = (await AntwortAsync<KindHinzugefuegt>(await SendenAsync(katrin, "/api/ich/familie/kinder", new KindHinzufuegen("Emma", "Muster", new DateOnly(2017, 9, 2))), HttpStatusCode.Created)).PersonId;
        AnmeldungEmma = (await AntwortAsync<AnmeldungAngelegt>(
            await SendenAsync(katrin, "/api/ich/anmeldungen", new AnmeldungAbsenden(Link(AnmeldelinkArt.Teilnehmende), Emma, true)), HttpStatusCode.Created)).AnmeldungId;
        JonasClient = await AngemeldetAsync(Jonas);
        JonasPerson = await WertAsync<Guid>("SELECT \"PersonId\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Jonas));
        AnmeldungJonas = (await AntwortAsync<AnmeldungAngelegt>(
            await SendenAsync(JonasClient, "/api/ich/anmeldungen", new AnmeldungAbsenden(Link(AnmeldelinkArt.Mitarbeitende), null, true)), HttpStatusCode.Created)).AnmeldungId;
        await Factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(2L, await WertAsync<long>("SELECT count(*) FROM personen.freigabe WHERE \"Stufe\" = 1"));
        // Story 2-5: Die Freigaben tragen die Fragen der jeweiligen Zielgruppe zum Zeitpunkt des Eingangs.
        Assert.Equal(["allergien", "notfallkontakte"], await FreigabeFragenAsync(AnmeldungEmma));
        Assert.Equal(["notfallkontakte"], await FreigabeFragenAsync(AnmeldungJonas));
    }

    /// <summary>Story 2-5: Miriam setzt die Fragen-Auswahl einer Zielgruppe (Autosave-PATCH mit dem aktuellen Wert als altem Wert).</summary>
    public async Task FragenWaehlenAsync(FragenFeld feld, string kennungen)
    {
        var auswahl = await MiriamClient.GetFromJsonAsync<FragenAuswahl>($"/api/freizeiten/{FreizeitId}/fragen", Abbruch);
        var alt = feld == FragenFeld.Teilnehmende ? auswahl!.Teilnehmende : auswahl!.Mitarbeitende;
        var koerper = new Dictionary<string, object?> { ["version"] = auswahl.Version, ["alterWert"] = alt, [FragenFelder.JsonName(feld)] = kennungen };
        using var antwort = await SendenAsync(MiriamClient, $"/api/freizeiten/{FreizeitId}/fragen", koerper, HttpMethod.Patch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    /// <summary>Story 2-5: die Fragen der Freigabe einer Anmeldung (<c>personen.freigabe."Fragen"</c>).</summary>
    public Task<string[]> FreigabeFragenAsync(Guid anmeldung) =>
        WertAsync<string[]>("SELECT \"Fragen\" FROM personen.freigabe WHERE \"AnmeldungId\" = @a", ("a", anmeldung));

    /// <summary>Miriam bestätigt oder sagt ab (<c>204</c>).</summary>
    public async Task EntscheidenAsync(Guid anmeldung, string uebergang)
    {
        using var antwort = await SendenAsync(MiriamClient, $"/api/freizeiten/{FreizeitId}/anmeldungen/{anmeldung}/{uebergang}", null);
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    /// <summary>
    /// Stellt die Outbox zweimal zu und setzt danach alle erledigten Zustellungen an Ereignis-Empfänger zurück, damit sie ein drittes
    /// Mal ankommen: Die Inbox muss sie verwerfen (AD-4). Mail-Zustellungen bleiben erledigt.
    /// </summary>
    public async Task ZustellenMitWiederholungAsync()
    {
        await Factory.OutboxJetztZustellenAsync(Abbruch);
        await Factory.OutboxJetztZustellenAsync(Abbruch);
        foreach (var schema in new[] { "freizeiten", "personen", "organisationen", "konten" })
        {
            await AusfuehrenAsync(
                $"UPDATE {schema}.outbox_zustellung SET \"ErledigtAm\" = NULL, \"NaechsterVersuch\" = '2000-01-01' WHERE \"Empfaenger\" <> 'Zeltlotse.Mail'");
        }

        await Factory.OutboxJetztZustellenAsync(Abbruch);
    }

    /// <summary>Stellt die Uhr auf die nächste volle Sekunde und liefert diesen Zeitpunkt (die Datenbank speichert Mikrosekunden).</summary>
    public DateTimeOffset VolleSekunde()
    {
        var jetzt = Factory.Uhr.GetUtcNow();
        var ziel = new DateTimeOffset(jetzt.Ticks - (jetzt.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero).AddSeconds(1);
        Factory.Uhr.SetUtcNow(ziel);
        return ziel;
    }

    public async Task<T> WertAsync<T>(string sql, params (string Name, object Wert)[] parameter)
    {
        await using var db = new NpgsqlConnection(Verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        return (T)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }

    public async Task AusfuehrenAsync(string sql)
    {
        await using var db = new NpgsqlConnection(Verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        await befehl.ExecuteNonQueryAsync(Abbruch);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await Factory.DisposeAsync();
    }

    private async Task<HttpClient> AngemeldetAsync(string mail)
    {
        var client = Factory.CreateClient();
        _clients.Add(client);
        var vorher = Factory.Mails.An(mail).Count;
        Assert.Equal(HttpStatusCode.Accepted, (await SendenAsync(client, "/api/konten/login-link", new LoginLinkAnfordern(mail))).StatusCode);
        var token = (await Factory.Mails.WartenAufAsync(mail, vorher + 1)).Token;
        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendenAsync(client, "/api/ich/profil", null)).StatusCode);
        return client;
    }

    private string TokenAus(string mail, string betreff) => Factory.Mails.An(mail).Last(m => m.Betreff == betreff).Token;

    /// <summary>Sendet mit Antiforgery-Header (Vorgabe <c>POST</c>).</summary>
    public static async Task<HttpResponseMessage> SendenAsync(HttpClient client, string pfad, object? koerper, HttpMethod? methode = null)
    {
        var token = (await client.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", Abbruch))!.Token;
        using var anfrage = new HttpRequestMessage(methode ?? HttpMethod.Post, pfad) { Content = koerper is null ? null : JsonContent.Create(koerper, koerper.GetType()) };
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
}
