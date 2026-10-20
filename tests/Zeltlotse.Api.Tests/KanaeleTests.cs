using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Story 6-1: Sendet nichts, hält die Pushes fest. Endpunkte mit „abgelaufen“ meldet der Push-Dienst als unbekannt (410), mit „kaputt“ wirft er.</summary>
internal sealed class PushAufzeichnung : IPushVersand
{
    public ConcurrentQueue<(PushZiel Ziel, PushInhalt Inhalt)> Gesendet { get; } = new();

    public Task<PushErgebnis> SendenAsync(PushZiel ziel, PushInhalt inhalt, CancellationToken abbruch = default)
    {
        if (ziel.Endpunkt.Contains("kaputt", StringComparison.Ordinal))
        {
            throw new HttpRequestException("Push-Dienst nicht erreichbar.");
        }

        if (ziel.Endpunkt.Contains("abgelaufen", StringComparison.Ordinal))
        {
            return Task.FromResult(PushErgebnis.ZielUngueltig);
        }

        Gesendet.Enqueue((ziel, inhalt));
        return Task.FromResult(PushErgebnis.Zugestellt);
    }
}

/// <summary>
/// Story 6-1, Kanäle „Team“ und „Alle“ mit Push (CAP-35, FR-61, NFR-10, AD-13, AD-16, AD-17, AD-21, AD-30) gegen echtes PostgreSQL: Nachrichten lesen und
/// senden (der Kanal „Alle“ nur mit Bestätigung), Text-Grenzen, unbekannter Kanal, die neuesten 200 Nachrichten, Push über die Outbox an die Abos von Leitung
/// und Team ohne die Person, die geschrieben hat, Push-Abo speichern, überschreiben, abmelden und abräumen, nur bekannte Push-Dienste, der Schlüssel, die
/// Pflichtfälle der Freizeit-Route auf allen fünf Routen, der Schreibschutz nach dem Abschluss, <c>PersonGeloescht</c>, die Datenauskunft und der Lösch-Lauf.
/// Freizeit X (Organisation A; Org-Admin Andrea, Leitung Miriam, Team Tobias) und Z (Organisation B, Leitung Miriam).
/// </summary>
public sealed class KanaeleTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly string P256dh = Base64Url([0x04, .. Enumerable.Range(1, 64).Select(i => (byte)i)]);
    private static readonly string Auth = Base64Url([.. Enumerable.Range(1, 16).Select(i => (byte)i)]);

    private readonly PushAufzeichnung _push = new();
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() =>
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres, zusatzDienste: s => s.Replace(ServiceDescriptor.Singleton<IPushVersand>(_push)));

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string NachrichtenPfad(Guid freizeit, string kanal = "team") => $"/api/freizeiten/{freizeit}/kanaele/{kanal}/nachrichten";

    private static string AboPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/kanaele/push/abo";

    private static string AbmeldenPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/kanaele/push/abo/abmelden";

    private static string SchluesselPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/kanaele/push/schluessel";

    private static string Endpunkt(string name) => $"https://fcm.googleapis.com/fcm/send/{name}";

    private Task<HttpResponseMessage> SendenAsync(HttpClient client, string text, string kanal = "team", bool? bestaetigt = null, Guid? freizeit = null) =>
        PostAsync(client, NachrichtenPfad(freizeit ?? _s.X, kanal), new { text, bestaetigt });

    private async Task<JsonElement> GesendetAsync(HttpClient client, string text, string kanal = "team", bool? bestaetigt = null, Guid? freizeit = null)
    {
        using var antwort = await SendenAsync(client, text, kanal, bestaetigt, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<JsonElement> ListeAsync(HttpClient? client = null, string kanal = "team", Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(NachrichtenPfad(freizeit ?? _s.X, kanal), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> AboAsync(HttpClient client, string endpunkt, string? p256dh = null, string? auth = null, Guid? freizeit = null) =>
        PostAsync(client, AboPfad(freizeit ?? _s.X), new { endpunkt, p256dh = p256dh ?? P256dh, auth = auth ?? Auth });

    private async Task AbonnierenAsync(HttpClient client, string endpunkt, Guid? freizeit = null)
    {
        using var antwort = await AboAsync(client, endpunkt, freizeit: freizeit);
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    private Task<long> AnzahlAsync(string tabelle, string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM kanaele.{tabelle} WHERE {bedingung}", parameter);

    private static async Task FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
    }

    private static IReadOnlyList<string?> Texte(JsonElement liste) =>
        [.. liste.GetProperty("nachrichten").EnumerateArray().Select(n => n.GetProperty("text").GetString())];

    // ---------- Senden und Push ----------

    [Fact]
    public async Task Team_Nachricht_201_und_nach_der_Zustellung_bekommt_genau_Miriams_Abo_den_Push_mit_Freizeit_Kanal_Text_und_Adresse()
    {
        await AbonnierenAsync(_s.Miriam, Endpunkt("miriam"));
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));
        var jetzt = _s.VolleSekunde();

        var nachricht = await GesendetAsync(_s.Tobias, "  Feuerholz liegt am Küchenzelt.  ");

        Assert.Equal(["autorPersonId", "gesendetAm", "id", "text"], Namen(nachricht));
        Assert.Equal(
            (_s.TobiasId, "Feuerholz liegt am Küchenzelt.", jetzt),
            (nachricht.GetProperty("autorPersonId").GetGuid(), nachricht.GetProperty("text").GetString(), nachricht.GetProperty("gesendetAm").GetDateTimeOffset()));
        Assert.Empty(_push.Gesendet);

        await _s.ZustellenAsync();

        var (ziel, inhalt) = Assert.Single(_push.Gesendet);
        Assert.Equal((Endpunkt("miriam"), P256dh, Auth), (ziel.Endpunkt, ziel.P256dh, ziel.Auth));
        Assert.Equal(new PushInhalt($"{FreizeitpassSzenario.Kapf} · Team", "Feuerholz liegt am Küchenzelt.", $"/app/freizeiten/{_s.X}/kanaele"), inhalt);
        Assert.DoesNotContain("Tobias", inhalt.Titel + inhalt.Text, StringComparison.Ordinal);

        // In der Outbox steht nur die Kennung der Nachricht.
        var outbox = await WertAsync<string>(_s.Verbindung, "SELECT \"Inhalt\" FROM kanaele.outbox");
        Assert.DoesNotContain("Feuerholz", outbox, StringComparison.Ordinal);

        // Erneut zustellen schickt nichts doppelt.
        await _s.ZustellenAsync();
        Assert.Single(_push.Gesendet);
    }

    [Fact]
    public async Task Alle_ohne_Bestaetigung_400_am_Feld_bestaetigt_nichts_gespeichert_kein_Push()
    {
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));

        foreach (var bestaetigt in new bool?[] { false, null })
        {
            using var antwort = await SendenAsync(_s.Miriam, "Essen 13:30 statt 13:00", "alle", bestaetigt);
            await FeldfehlerAsync(antwort, "bestaetigt");
        }

        await _s.ZustellenAsync();
        Assert.Equal(0L, await AnzahlAsync("nachricht"));
        Assert.Empty(_push.Gesendet);
    }

    [Fact]
    public async Task Alle_bestaetigt_201_Push_an_das_Team_mit_der_Adresse_von_Alle_der_andere_Kanal_bleibt_getrennt()
    {
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));

        await GesendetAsync(_s.Miriam, "Essen 13:30 statt 13:00", "alle", bestaetigt: true);
        await GesendetAsync(_s.Miriam, "Nur fürs Team", "team");
        await _s.ZustellenAsync();

        Assert.Equal(
            [
                new PushInhalt($"{FreizeitpassSzenario.Kapf} · Alle", "Essen 13:30 statt 13:00", $"/app/freizeiten/{_s.X}/kanaele?kanal=alle"),
                new PushInhalt($"{FreizeitpassSzenario.Kapf} · Team", "Nur fürs Team", $"/app/freizeiten/{_s.X}/kanaele"),
            ],
            _push.Gesendet.Select(p => p.Inhalt));
        Assert.Equal(["Essen 13:30 statt 13:00"], Texte(await ListeAsync(_s.Tobias, "alle")));
        Assert.Equal(["Nur fürs Team"], Texte(await ListeAsync(_s.Tobias, "team")));
        // Im Team-Kanal ist die Bestätigung egal.
        Assert.Equal("Ohne Bestätigung", (await GesendetAsync(_s.Tobias, "Ohne Bestätigung", "team", bestaetigt: false)).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Langer_Text_wird_im_Push_auf_150_Zeichen_gekuerzt_die_Nachricht_bleibt_ganz()
    {
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));
        var lang = string.Concat(Enumerable.Repeat("Zeltplatz ", 100));

        await GesendetAsync(_s.Miriam, lang.Trim());
        await _s.ZustellenAsync();

        var text = Assert.Single(_push.Gesendet).Inhalt.Text;
        Assert.Equal(150, text.Length);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
        Assert.Equal(lang.Trim(), Texte(await ListeAsync()).Single());
    }

    [Fact]
    public async Task Text_leer_nur_Leerzeichen_oder_ueber_1000_Zeichen_400_am_Feld_text_1000_Zeichen_gehen()
    {
        foreach (var text in new[] { "", "   ", new string('a', 1001), null })
        {
            using var antwort = await PostAsync(_s.Miriam, NachrichtenPfad(_s.X), new { text });
            await FeldfehlerAsync(antwort, "text");
        }

        Assert.Equal(0L, await AnzahlAsync("nachricht"));
        await GesendetAsync(_s.Miriam, new string('a', 1000));
    }

    [Fact]
    public async Task Unbekannter_Kanal_404_beim_Lesen_und_Senden()
    {
        foreach (var kanal in new[] { "foo", "push", "1" })
        {
            using var lesen = await _s.Miriam.GetAsync(NachrichtenPfad(_s.X, kanal), Abbruch);
            Assert.Equal(HttpStatusCode.NotFound, lesen.StatusCode);
            using var senden = await SendenAsync(_s.Miriam, "Hallo", kanal, bestaetigt: true);
            Assert.Equal(HttpStatusCode.NotFound, senden.StatusCode);
        }

        Assert.Equal(0L, await AnzahlAsync("nachricht"));
        // Groß geschrieben gilt der Kanal.
        Assert.Equal(HttpStatusCode.OK, (await _s.Miriam.GetAsync(NachrichtenPfad(_s.X, "Team"), Abbruch)).StatusCode);
    }

    // ---------- Liste ----------

    [Fact]
    public async Task Liste_hoechstens_die_neuesten_200_aelteste_zuerst_mit_der_Zahl_der_Personen_fuer_Alle()
    {
        var leer = await ListeAsync(_s.Tobias, "alle");
        Assert.Equal(["kanal", "nachrichten", "personenAlle"], Namen(leer));
        // Leitung Miriam und Team Tobias; mit Mia als bestätigter Teilnehmender drei.
        Assert.Equal(("Alle", 0, 2), (leer.GetProperty("kanal").GetString(), leer.GetProperty("nachrichten").GetArrayLength(), leer.GetProperty("personenAlle").GetInt32()));
        var mia = await _s.KindAnmeldenAsync(_s.Mia);
        await _s.BestaetigenAsync(mia);
        await _s.ZustellenAsync();
        Assert.Equal(3, (await ListeAsync(_s.Tobias, "alle")).GetProperty("personenAlle").GetInt32());

        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO kanaele.nachricht (\"Id\", \"OrganisationId\", \"FreizeitId\", \"Kanal\", \"AutorPersonId\", \"Text\", \"GesendetAm\", \"Folgenummer\") " +
            "SELECT gen_random_uuid(), @o, @f, 1, @p, 'Nachricht ' || i, @t + i * interval '1 minute', 1 FROM generate_series(1, 205) AS i",
            ("o", _s.A), ("f", _s.X), ("p", _s.MiriamId), ("t", new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc)));

        var liste = await ListeAsync(_s.Tobias);
        Assert.Equal([.. Enumerable.Range(6, 200).Select(i => (string?)$"Nachricht {i}")], Texte(liste));
        Assert.Equal(0, (await ListeAsync(_s.Tobias, "alle")).GetProperty("nachrichten").GetArrayLength());
    }

    // ---------- Push-Abo ----------

    [Fact]
    public async Task Abo_204_ein_zweites_Senden_desselben_Endpunkts_auch_von_einer_anderen_Person_ueberschreibt_es()
    {
        await AbonnierenAsync(_s.Miriam, Endpunkt("geraet"));
        Assert.Equal(1L, await AnzahlAsync("push_abo", "\"PersonId\" = @p AND \"OrganisationId\" = @o AND \"FreizeitId\" = @f", ("p", _s.MiriamId), ("o", _s.A), ("f", _s.X)));

        // Tobias meldet sich am selben Gerät an und schaltet Benachrichtigungen ein.
        var neuerSchluessel = Base64Url([0x04, .. Enumerable.Range(100, 64).Select(i => (byte)i)]);
        using (var antwort = await AboAsync(_s.Tobias, Endpunkt("geraet"), neuerSchluessel))
        {
            Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        }

        Assert.Equal(1L, await AnzahlAsync("push_abo"));
        Assert.Equal(1L, await AnzahlAsync("push_abo", "\"PersonId\" = @p AND \"P256dh\" = @k", ("p", _s.TobiasId), ("k", neuerSchluessel)));

        // Dasselbe Gerät in einer zweiten Freizeit ist ein eigenes Abo.
        await AbonnierenAsync(_s.Miriam, Endpunkt("geraet"), _s.Z);
        Assert.Equal(2L, await AnzahlAsync("push_abo"));

        // Ein Push von Miriam erreicht jetzt Tobias am Gerät.
        await GesendetAsync(_s.Miriam, "Hallo");
        await _s.ZustellenAsync();
        Assert.Equal(Endpunkt("geraet"), Assert.Single(_push.Gesendet).Ziel.Endpunkt);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("https://localhost/push")]
    [InlineData("https://example.org/push")]
    [InlineData("kein endpunkt")]
    [InlineData("")]
    public async Task Abo_mit_fremdem_Endpunkt_400_am_Feld_endpunkt_nichts_gespeichert(string endpunkt)
    {
        using var antwort = await AboAsync(_s.Miriam, endpunkt);

        await FeldfehlerAsync(antwort, "endpunkt");
        Assert.Equal(0L, await AnzahlAsync("push_abo"));
    }

    [Fact]
    public async Task Abo_mit_ungueltigen_Schluesseln_400_an_den_Feldern()
    {
        using (var antwort = await AboAsync(_s.Miriam, Endpunkt("x"), p256dh: "kurz!", auth: Base64Url(new byte[15])))
        {
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            var fehler = (await ProblemAsync(antwort)).GetProperty("errors");
            Assert.True(fehler.TryGetProperty("p256dh", out _));
            Assert.True(fehler.TryGetProperty("auth", out _));
        }

        Assert.Equal(0L, await AnzahlAsync("push_abo"));
    }

    /// <summary>Freizeit Y in Organisation A mit Tobias als Leitung (gehört damit zu Leitung und Team von Y).</summary>
    private async Task<Guid> ZweiteFreizeitMitTobiasAsync()
    {
        var y = await FreizeitHelfer.AnlegenAsync(_s.Andrea, _s.A, "Herbstlager 2027");
        await FreizeitHelfer.EinladenAsync(_s.Andrea, _s.A, y, FreizeitHelfer.Tobias);
        await FreizeitHelfer.AnnehmenMitAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        await _s.ZustellenAsync();
        return y;
    }

    [Fact]
    public async Task Abmelden_loescht_die_eigenen_Abos_des_Endpunkts_auch_in_anderen_Freizeiten_der_Organisation()
    {
        var y = await ZweiteFreizeitMitTobiasAsync();
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"), y);
        await AbonnierenAsync(_s.Tobias, Endpunkt("anderes-geraet"), y);

        using (var antwort = await PostAsync(_s.Tobias, AbmeldenPfad(_s.X), new { endpunkt = Endpunkt("tobias") }))
        {
            Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        }

        Assert.Equal(0L, await AnzahlAsync("push_abo", "\"Endpunkt\" = @e", ("e", Endpunkt("tobias"))));
        Assert.Equal(1L, await AnzahlAsync("push_abo", "\"Endpunkt\" = @e AND \"FreizeitId\" = @f", ("e", Endpunkt("anderes-geraet")), ("f", y)));
    }

    [Fact]
    public async Task Abos_in_zwei_Freizeiten_derselben_Organisation_eine_Nachricht_in_X_geht_nur_an_das_Abo_von_X()
    {
        var y = await ZweiteFreizeitMitTobiasAsync();
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias-x"));
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias-y"), y);

        await GesendetAsync(_s.Miriam, "Nur in X");
        await _s.ZustellenAsync();

        Assert.Equal(Endpunkt("tobias-x"), Assert.Single(_push.Gesendet).Ziel.Endpunkt);
    }

    [Fact]
    public async Task Emoji_an_der_Kuerzungsstelle_wird_nicht_zerteilt()
    {
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));
        // 148 Zeichen, dann ein Emoji auf den Stellen 149 und 150: Gekürzt wird vor dem Emoji.
        var text = new string('a', 148) + "🔥" + " Lagerfeuer";

        await GesendetAsync(_s.Miriam, text);
        await _s.ZustellenAsync();

        var gekuerzt = Assert.Single(_push.Gesendet).Inhalt.Text;
        Assert.Equal(new string('a', 148) + "…", gekuerzt);
        Assert.DoesNotContain(gekuerzt, char.IsSurrogate);
    }

    [Fact]
    public void Der_Platzhalter_der_API_ist_der_des_Servers() =>
        Assert.Equal(GeloeschtePerson.Id, Zeltlotse.Api.Contracts.Kanaele.KanalFelder.GeloeschtePerson);

    [Fact]
    public async Task Abmelden_204_loescht_nur_das_eigene_Abo_unbekannt_ebenfalls_204()
    {
        await AbonnierenAsync(_s.Miriam, Endpunkt("miriam"));
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));

        foreach (var (client, endpunkt) in new[] { (_s.Tobias, Endpunkt("miriam")), (_s.Miriam, Endpunkt("unbekannt")), (_s.Miriam, Endpunkt("miriam")) })
        {
            using var antwort = await PostAsync(client, AbmeldenPfad(_s.X), new { endpunkt });
            Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        }

        Assert.Equal(1L, await AnzahlAsync("push_abo"));
        Assert.Equal(1L, await AnzahlAsync("push_abo", "\"PersonId\" = @p", ("p", _s.TobiasId)));
    }

    [Fact]
    public async Task Schluessel_200_der_oeffentliche_VAPID_Schluessel_als_unkomprimierter_Punkt()
    {
        using var antwort = await _s.Tobias.GetAsync(SchluesselPfad(_s.X), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["oeffentlicherSchluessel"], Namen(json));
        await using var scope = _s.Factory.Services.CreateAsyncScope();
        var erwartet = scope.ServiceProvider.GetRequiredService<VapidSchluessel>().Oeffentlich;
        Assert.Equal(erwartet, json.GetProperty("oeffentlicherSchluessel").GetString());
        var bytes = Convert.FromBase64String(erwartet.Replace('-', '+').Replace('_', '/') + "=");
        Assert.Equal((65, (byte)0x04), (bytes.Length, bytes[0]));
    }

    [Fact]
    public async Task Abgelaufenes_Abo_wird_abgeraeumt_die_uebrigen_Empfaenger_bekommen_den_Push_ein_kaputtes_haelt_nichts_auf()
    {
        await AbonnierenAsync(_s.Miriam, Endpunkt("abgelaufen"));
        await AbonnierenAsync(_s.Miriam, Endpunkt("kaputt"));
        await AbonnierenAsync(_s.Miriam, Endpunkt("miriam"));

        await GesendetAsync(_s.Tobias, "Abendessen fertig");
        await _s.ZustellenAsync();

        Assert.Equal(Endpunkt("miriam"), Assert.Single(_push.Gesendet).Ziel.Endpunkt);
        Assert.Equal(0L, await AnzahlAsync("push_abo", "\"Endpunkt\" = @e", ("e", Endpunkt("abgelaufen"))));
        Assert.Equal(2L, await AnzahlAsync("push_abo"));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM kanaele.outbox_zustellung WHERE \"ErledigtAm\" IS NULL"));
    }

    [Fact]
    public async Task Person_aus_dem_Team_entfernt_bekommt_keinen_Push_mehr()
    {
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM freizeiten.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p", ("f", _s.X), ("p", _s.TobiasId));

        await GesendetAsync(_s.Miriam, "Nur noch für die Leitung");
        await _s.ZustellenAsync();

        Assert.Empty(_push.Gesendet);
        Assert.Equal(1L, await AnzahlAsync("push_abo"));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufen_Lesen_200_Senden_Abo_und_Abmelden_409_freizeit_abgeschlossen()
    {
        await GesendetAsync(_s.Miriam, "Letzte Nachricht");
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        Assert.Equal(["Letzte Nachricht"], Texte(await ListeAsync(_s.Tobias)));
        Assert.Equal(HttpStatusCode.OK, (await _s.Tobias.GetAsync(SchluesselPfad(_s.X), Abbruch)).StatusCode);
        foreach (var aufruf in new Func<Task<HttpResponseMessage>>[]
                 {
                     () => SendenAsync(_s.Miriam, "Zu spät"),
                     () => SendenAsync(_s.Miriam, "Zu spät", "alle", bestaetigt: true),
                     () => AboAsync(_s.Miriam, Endpunkt("miriam")),
                     () => PostAsync(_s.Tobias, AbmeldenPfad(_s.X), new { endpunkt = Endpunkt("tobias") }),
                 })
        {
            using var antwort = await aufruf();
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
        }

        Assert.Equal((1L, 1L), (await AnzahlAsync("nachricht"), await AnzahlAsync("push_abo")));
    }

    // ---------- Rechte ----------

    /// <summary>Alle fünf Routen für einen Client unter einer Freizeit.</summary>
    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit) =>
    [
        () => client.GetAsync(NachrichtenPfad(freizeit), Abbruch),
        () => SendenAsync(client, "Hallo", freizeit: freizeit),
        () => client.GetAsync(SchluesselPfad(freizeit), Abbruch),
        () => AboAsync(client, Endpunkt("rechte"), freizeit: freizeit),
        () => PostAsync(client, AbmeldenPfad(freizeit), new { endpunkt = Endpunkt("rechte") }),
    ];

    private async Task<List<HttpStatusCode>> StatusAllerRoutenAsync(HttpClient client, Guid freizeit)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_auf_allen_fuenf_Routen()
    {
        HttpStatusCode[] erwartet = [HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NoContent];
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.Equal(erwartet, await StatusAllerRoutenAsync(client, _s.X));
        }

        // Miriam ist auch Leitung von Z.
        Assert.Equal(erwartet, await StatusAllerRoutenAsync(_s.Miriam, _s.Z));
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_Leitung_von_Z_unbekannte_Freizeit_404_ohne_Sitzung_401_auf_allen_fuenf_Routen()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kasse@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var faelle = new (HttpClient Client, Guid Freizeit, HttpStatusCode Erwartet)[]
        {
            (_s.Andrea, _s.X, HttpStatusCode.NotFound),
            (_s.Katrin, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, fremde, HttpStatusCode.NotFound),
            (sara, fremde, HttpStatusCode.NotFound),
            (leitungZ, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
            (_s.Anonym, _s.X, HttpStatusCode.Unauthorized),
        };

        foreach (var (client, freizeit, erwartet) in faelle)
        {
            Assert.All(await StatusAllerRoutenAsync(client, freizeit), s => Assert.Equal(erwartet, s));
        }

        Assert.Equal((0L, 0L), (await AnzahlAsync("nachricht"), await AnzahlAsync("push_abo")));
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Person_geloescht_Tobias_Nachricht_bleibt_mit_Platzhalter_sein_Abo_ist_sofort_weg()
    {
        await GesendetAsync(_s.Tobias, "Ich bringe die Gitarre mit.");
        await AbonnierenAsync(_s.Tobias, Endpunkt("tobias"));
        await AbonnierenAsync(_s.Miriam, Endpunkt("miriam"));

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        using (var loeschen = await PostAsync(_s.Tobias, "/api/ich/profil/loeschen"))
        {
            Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        }

        await _s.ZustellenAsync();

        var nachricht = Assert.Single((await ListeAsync()).GetProperty("nachrichten").EnumerateArray());
        Assert.Equal((GeloeschtePerson.Id, "Ich bringe die Gitarre mit."), (nachricht.GetProperty("autorPersonId").GetGuid(), nachricht.GetProperty("text").GetString()));
        Assert.Equal(1L, await AnzahlAsync("push_abo"));
        Assert.Empty(await KanaeleSpaltenMitAsync(_s.TobiasId));
    }

    [Fact]
    public async Task Auskunft_nennt_die_eigenen_Nachrichten_und_die_Zahl_der_eigenen_Push_Abos()
    {
        var heute = FreizeitHelfer.Heute(_s.Factory).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        await GesendetAsync(_s.Tobias, "Ich bringe die Gitarre mit.");
        await GesendetAsync(_s.Tobias, "Essen fertig", "alle", bestaetigt: true);
        await GesendetAsync(_s.Miriam, "Danke!");
        await AbonnierenAsync(_s.Tobias, Endpunkt("handy"));
        await AbonnierenAsync(_s.Tobias, Endpunkt("laptop"));

        var tobias = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var katrin = (await _s.Katrin.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        Assert.Equal(
            [
                new DatenauskunftZeile($"Nachricht im Kanal „Team“ vom {heute}", "Ich bringe die Gitarre mit."),
                new DatenauskunftZeile($"Nachricht im Kanal „Alle“ vom {heute}", "Essen fertig"),
                new DatenauskunftZeile("Push-Abos (Geräte mit Benachrichtigungen)", "2"),
            ],
            Assert.Single(tobias, a => a.Titel == "Kanäle").Zeilen);
        Assert.DoesNotContain(katrin, a => a.Titel == "Kanäle");
    }

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_Nachrichten_und_Abos_der_abgelaufenen_Freizeit_die_in_Z_bleiben()
    {
        await GesendetAsync(_s.Miriam, "In X");
        await AbonnierenAsync(_s.Miriam, Endpunkt("miriam"));
        await GesendetAsync(_s.Miriam, "In Z", freizeit: _s.Z);
        await AbonnierenAsync(_s.Miriam, Endpunkt("miriam"), _s.Z);
        // Story 6-3: Notizen sind Arbeitsdaten der Freizeit und gehen mit ihr.
        foreach (var freizeit in new[] { _s.X, _s.Z })
        {
            using var notiz = await PostAsync(_s.Miriam, $"/api/freizeiten/{freizeit}/kanaele/notizen/ansagen", new { text = "Taschenlampen" });
            Assert.Equal(HttpStatusCode.Created, notiz.StatusCode);
        }

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<Zeltlotse.SharedKernel.Datenschutz.ILoeschlauf>().Single(l => l.GetType().Name == "KanaeleLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        foreach (var tabelle in new[] { "nachricht", "push_abo", "notiz" })
        {
            Assert.Equal(0L, await AnzahlAsync(tabelle, "\"FreizeitId\" = @f", ("f", _s.X)));
            Assert.Equal(1L, await AnzahlAsync(tabelle, "\"FreizeitId\" = @f", ("f", _s.Z)));
        }
    }

    /// <summary>Alle Spalten vom Typ uuid im Schema kanaele (außer Outbox und Inbox), die die Kennung noch enthalten.</summary>
    private async Task<List<string>> KanaeleSpaltenMitAsync(Guid kennung)
    {
        var spalten = new List<(string Tabelle, string Spalte)>();
        await using var db = new NpgsqlConnection(_s.Verbindung);
        await db.OpenAsync(Abbruch);
        await using (var befehl = new NpgsqlCommand(
                         "SELECT table_name, column_name FROM information_schema.columns WHERE data_type = 'uuid' AND table_schema = 'kanaele' " +
                         "AND table_name NOT IN ('outbox', 'outbox_zustellung', 'inbox', 'inbox_stand')",
                         db))
        await using (var leser = await befehl.ExecuteReaderAsync(Abbruch))
        {
            while (await leser.ReadAsync(Abbruch))
            {
                spalten.Add((leser.GetString(0), leser.GetString(1)));
            }
        }

        Assert.Contains(spalten, s => s == ("nachricht", "AutorPersonId"));
        Assert.Contains(spalten, s => s == ("push_abo", "PersonId"));
        var treffer = new List<string>();
        foreach (var (tabelle, spalte) in spalten)
        {
            await using var zaehlen = new NpgsqlCommand($"SELECT count(*) FROM kanaele.\"{tabelle}\" WHERE \"{spalte}\" = @k", db);
            zaehlen.Parameters.AddWithValue("k", kennung);
            if ((long)(await zaehlen.ExecuteScalarAsync(Abbruch))! > 0)
            {
                treffer.Add($"{tabelle}.{spalte}");
            }
        }

        return treffer;
    }
}
