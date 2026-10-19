using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Api.Datenschutz;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-4, Freizeitpass über den Gast-Link (FR-17, FR-23, AD-11, AD-14, AD-16, AD-30) gegen echtes PostgreSQL: Lesen und Speichern
/// als Formular für ein Kind der Gast-Familie, nur geänderte Werte, „Stimmt noch“ als Haken, Prüfung der Werte, die Token-Fälle mit
/// Sicherheitsprotokoll, die Übernahme in ein Konto und das Löschen im Gast-Löschlauf.
/// </summary>
public sealed class GastFreizeitpassTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Sabine = "sabine.beispiel@example.org";
    private static readonly DateTimeOffset NachFreigabeEnde = new(2027, 9, 13, 0, 0, 0, TimeSpan.Zero);

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _miriam = null!;
    private HttpClient _anonym = null!;
    private string _tnX = null!;
    private string _token = null!;
    private Guid _lina;

    /// <summary>Organisation A mit Freizeit X (Leitung Miriam); Sabine meldet Lina als Gast an, die Freigabe läuft.</summary>
    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueMigrierteDatenbankAsync();
        _factory = OrgHelfer.Factory(_verbindung);
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        Guid a;
        (a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "CVJM Musterstadt", Andrea);
        var x = await AnlegenAsync(_andrea, a, "Zeltlager Kapf 2027");
        _miriam = await LeitungAsync(_factory, _andrea, a, x, Miriam);
        var adresse = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{x}/freizeitseite", Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
            .Single(l => l.GetProperty("art").GetString() == "Teilnehmende").GetProperty("adresse").GetString()!;
        _tnX = adresse[(adresse.LastIndexOf('/') + 1)..];
        _anonym = _factory.CreateClient();
        (_token, _lina) = await GastAsync(Sabine, "Lina");
        await _factory.OutboxJetztZustellenAsync(Abbruch);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _miriam, _anonym })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Der_Gast_liest_den_Pass_des_Kindes_und_speichert_das_Formular_verschluesselt()
    {
        using var lesen = await _anonym.GetAsync(Pfad(_token, _lina), Abbruch);
        Assert.Equal(HttpStatusCode.OK, lesen.StatusCode);
        var json = await JsonAsync(lesen);
        Assert.Equal(("Lina", 10), (json.GetProperty("vorname").GetString(), json.GetProperty("fragen").GetArrayLength()));

        using var speichern = await SpeichernAsync(_token, _lina, new FreizeitpassFormular(
            [new("allergien", "milch,eier", "Kiwi"), new("schwimmen", "ja"), new("notfallkontakte", " Oma, 0171 1234567 "), new("medikamente", "")]));

        Assert.Equal(HttpStatusCode.NoContent, speichern.StatusCode);
        Assert.Equal(3L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort WHERE \"PersonId\" = @p", ("p", _lina)));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort WHERE position('Oma'::bytea in \"Wert\") > 0"));
        var fragen = await FragenAsync(_token, _lina);
        Assert.Equal(("eier,milch", "Kiwi"), (fragen["allergien"].GetProperty("wert").GetString(), fragen["allergien"].GetProperty("sonstiges").GetString()));
        Assert.Equal("Oma, 0171 1234567", fragen["notfallkontakte"].GetProperty("wert").GetString());
        Assert.Equal(JsonValueKind.Null, fragen["medikamente"].GetProperty("wert").ValueKind);
    }

    [Fact]
    public async Task Nur_geaenderte_Werte_werden_neu_bestaetigt_unveraenderte_nur_mit_Haken()
    {
        (await SpeichernAsync(_token, _lina, new FreizeitpassFormular([new("allergien", "milch"), new("schwimmen", "ja"), new("tetanus", "2024-05-17")]))).Dispose();
        await AusfuehrenAsync(_verbindung, "UPDATE personen.freizeitpass_antwort SET \"BestaetigtAm\" = @t", ("t", _factory.Uhr.GetUtcNow().AddMonths(-13)));
        Assert.All((await FragenAsync(_token, _lina)).Where(f => f.Key is "allergien" or "schwimmen" or "tetanus"), f => Assert.True(f.Value.GetProperty("veraltet").GetBoolean()));

        using var antwort = await SpeichernAsync(_token, _lina, new FreizeitpassFormular(
            [new("allergien", "milch"), new("schwimmen", "nein"), new("tetanus", "2024-05-17")], ["allergien", "notfallkontakte"]));

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        var fragen = await FragenAsync(_token, _lina);
        Assert.Equal(
            [("allergien", false), ("schwimmen", false), ("tetanus", true), ("notfallkontakte", false)],
            new[] { "allergien", "schwimmen", "tetanus", "notfallkontakte" }.Select(k => (k, fragen[k].GetProperty("veraltet").GetBoolean())));
        Assert.Equal("nein", fragen["schwimmen"].GetProperty("wert").GetString());
        Assert.Equal(3L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
    }

    [Fact]
    public async Task Ungueltige_Werte_ergeben_400_je_Feld_und_nichts_wird_gespeichert()
    {
        using var antwort = await SpeichernAsync(_token, _lina, new FreizeitpassFormular(
            [new("allergien", "milch"), new("schwimmen", "vielleicht"), new("tetanus", "31.02."), new("medikamente", null, "x")]));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(
            ["medikamente.sonstiges", "schwimmen.wert", "tetanus.wert"],
            (await ProblemAsync(antwort)).GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
    }

    [Fact]
    public async Task Fremdes_Kind_Anmeldelink_statt_Gast_Token_und_unbekanntes_Token_ergeben_404_mit_Sicherheitsprotokoll()
    {
        var (andererToken, ben) = await GastAsync("anna.anders@example.org", "Ben");
        var formular = new FreizeitpassFormular([new("schwimmen", "ja")]);
        var vorher = await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);

        foreach (var (token, person) in new[] { (_token, ben), (andererToken, _lina), (_tnX, _lina), (Zugangslink.NeuesToken(), _lina) })
        {
            using var lesen = await _anonym.GetAsync(Pfad(token, person), Abbruch);
            using var speichern = await SpeichernAsync(token, person, formular);

            Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound], [lesen.StatusCode, speichern.StatusCode]);
        }

        Assert.Equal(vorher + 4, await ProtokollAsync(_verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
        Assert.Equal(HttpStatusCode.NoContent, (await SpeichernAsync(andererToken, ben, formular)).StatusCode);
    }

    [Fact]
    public async Task Nach_dem_Freigabefenster_gilt_der_Link_nicht_mehr_und_der_Loeschlauf_loescht_die_Antworten()
    {
        (await SpeichernAsync(_token, _lina, new FreizeitpassFormular([new("schwimmen", "ja")]))).Dispose();
        _factory.Uhr.SetUtcNow(NachFreigabeEnde);

        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(Pfad(_token, _lina), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SpeichernAsync(_token, _lina, new FreizeitpassFormular([new("schwimmen", "nein")]))).StatusCode);

        await _factory.Services.GetRequiredService<Loeschlauf>().LaufAsync(Abbruch);
        await _factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @p", ("p", _lina)));
    }

    [Fact]
    public async Task Nach_der_Uebernahme_in_ein_Konto_stehen_die_Antworten_im_Konto_bereit()
    {
        (await SpeichernAsync(_token, _lina, new FreizeitpassFormular([new("notfallkontakte", "Oma, 0171 1234567")]))).Dispose();
        using var konto = await AngemeldeterClientAsync(_factory, Sabine);
        (await PostAsync(konto, "/api/ich/profil")).Dispose();
        using (var uebernehmen = await PostAsync(konto, "/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(_token)))
        {
            Assert.Equal(HttpStatusCode.OK, uebernehmen.StatusCode);
        }

        await _factory.OutboxJetztZustellenAsync(Abbruch);
        using var neu = await AngemeldeterClientAsync(_factory, Sabine);

        using var antwort = await neu.GetAsync($"/api/ich/freizeitpass/{_lina}", Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var notfall = (await JsonAsync(antwort)).GetProperty("fragen").EnumerateArray().Single(f => f.GetProperty("kennung").GetString() == "notfallkontakte");
        Assert.Equal("Oma, 0171 1234567", notfall.GetProperty("wert").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _anonym.GetAsync(Pfad(_token, _lina), Abbruch)).StatusCode);
    }

    // ---------- Hilfen ----------

    private static string Pfad(string token, Guid person) => $"/api/personen/oeffentlich/gaeste/{token}/freizeitpass/{person}";

    private Task<HttpResponseMessage> SpeichernAsync(string token, Guid person, FreizeitpassFormular formular) =>
        PostAsync(_anonym, Pfad(token, person), formular);

    private async Task<Dictionary<string, JsonElement>> FragenAsync(string token, Guid person)
    {
        using var antwort = await _anonym.GetAsync(Pfad(token, person), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("fragen").EnumerateArray().ToDictionary(f => f.GetProperty("kennung").GetString()!, f => f);
    }

    /// <summary>Eine Gast-Familie mit Kind, angemeldet zu X; liefert Gast-Token und Kind.</summary>
    private async Task<(string Token, Guid Kind)> GastAsync(string mail, string kind)
    {
        using var gast = await PostAsync(_anonym, "/api/personen/oeffentlich/gaeste", new GastAnlegen("Eltern", "Beispiel", mail, kind, "Beispiel", new DateOnly(2016, 4, 1)));
        Assert.Equal(HttpStatusCode.Created, gast.StatusCode);
        var json = await JsonAsync(gast);
        var (token, kindId) = (json.GetProperty("gastToken").GetString()!, json.GetProperty("kindPersonId").GetGuid());
        using var anmeldung = await PostAsync(_anonym, $"/api/freizeiten/oeffentlich/{_tnX}/anmeldungen", new GastAnmeldungAbsenden(token, kindId, true));
        Assert.Equal(HttpStatusCode.Created, anmeldung.StatusCode);
        return (token, kindId);
    }
}
