using System.Net;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 1-1, Matrix-Zeilen „Profil“, „Autosave“ und „Persönlich“ (AD-19, AD-26, AD-30 „persönlich“): das eigene Profil
/// unter <c>/api/ich/profil</c>, gegen echtes PostgreSQL.
/// </summary>
public sealed class ProfilTests(PostgresContainer postgres)
{
    private const string Mail = "lea.feuerstein@example.org";

    [Fact]
    public async Task Profil_zweimal_sicherstellen_legt_eine_Person_und_einen_Schluessel_an_mit_gleicher_Antwort()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = await AngemeldeterClientAsync(factory, Mail);
        var personId = await PersonIdAsync(verbindung, Mail);

        using var vorher = await client.GetAsync("/api/ich/profil", Abbruch);
        Assert.Equal(HttpStatusCode.NotFound, vorher.StatusCode);

        using var erstes = await PostAsync(client, "/api/ich/profil");
        using var zweites = await PostAsync(client, "/api/ich/profil");

        Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, zweites.StatusCode);
        var json = await JsonAsync(erstes);
        Assert.Equal(["nachname", "version", "vorname"], Namen(json));
        Assert.Equal(json.GetRawText(), (await JsonAsync(zweites)).GetRawText());
        Assert.True(json.GetProperty("version").GetUInt32() > 0);
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @id", ("id", personId)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.personen_schluessel WHERE \"PersonId\" = @id", ("id", personId)));
        using var laden = await client.GetAsync("/api/ich/profil", Abbruch);
        Assert.Equal(json.GetRawText(), (await JsonAsync(laden)).GetRawText());
    }

    [Fact]
    public async Task Feld_mit_passendem_alten_Wert_speichert_und_liefert_neue_Version_und_nur_das_Feld()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Mail);
        var version = await SicherstellenAsync(client);

        using var antwort = await PatchAsync(client, $$"""{"version":{{version}},"alterWert":null,"vorname":"  Lea "}""");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["version", "vorname"], Namen(json));
        Assert.Equal("Lea", json.GetProperty("vorname").GetString());
        Assert.NotEqual(version, json.GetProperty("version").GetUInt32());
        using var laden = await client.GetAsync("/api/ich/profil", Abbruch);
        var profil = await JsonAsync(laden);
        Assert.Equal("Lea", profil.GetProperty("vorname").GetString());
        Assert.Equal(json.GetProperty("version").GetUInt32(), profil.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task Abweichender_alter_Wert_desselben_Felds_ergibt_409_konflikt_und_aendert_nichts()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Mail);
        var version = await SicherstellenAsync(client);
        using (var erstes = await PatchAsync(client, $$"""{"version":{{version}},"alterWert":null,"vorname":"Lea"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        }

        // Ein zweites Gerät kennt noch den alten Stand (leer) und schreibt denselben Wert anders.
        using var antwort = await PatchAsync(client, $$"""{"version":{{version}},"alterWert":null,"vorname":"Leonie"}""");

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal("konflikt", Code(await ProblemAsync(antwort)));
        using var laden = await client.GetAsync("/api/ich/profil", Abbruch);
        Assert.Equal("Lea", (await JsonAsync(laden)).GetProperty("vorname").GetString());
    }

    [Fact]
    public async Task Inzwischen_geaendertes_anderes_Feld_stoert_nicht()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Mail);
        var version = await SicherstellenAsync(client);
        using (var vorname = await PatchAsync(client, $$"""{"version":{{version}},"alterWert":null,"vorname":"Lea"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, vorname.StatusCode);
        }

        using var antwort = await PatchAsync(client, $$"""{"version":{{version}},"alterWert":null,"nachname":"Feuerstein"}""");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(["nachname", "version"], Namen(await JsonAsync(antwort)));
        using var laden = await client.GetAsync("/api/ich/profil", Abbruch);
        var profil = await JsonAsync(laden);
        Assert.Equal("Lea", profil.GetProperty("vorname").GetString());
        Assert.Equal("Feuerstein", profil.GetProperty("nachname").GetString());
    }

    [Fact]
    public async Task Gleichzeitige_Aenderungen_an_verschiedenen_Feldern_gelingen_beide()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Mail);
        var version = await SicherstellenAsync(client);
        string? vorname = null;
        string? nachname = null;

        for (var runde = 0; runde < 5; runde++)
        {
            // Beide Geräte kennen dieselbe Version; jedes ändert ein anderes Feld.
            var antworten = await Task.WhenAll(
                Task.Run(() => PatchAsync(client, $$"""{"version":{{version}},"alterWert":{{Json(vorname)}},"vorname":"Lea{{runde}}"}""")),
                Task.Run(() => PatchAsync(client, $$"""{"version":{{version}},"alterWert":{{Json(nachname)}},"nachname":"Feuerstein{{runde}}"}""")));
            foreach (var antwort in antworten)
            {
                using (antwort)
                {
                    Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
                }
            }

            vorname = $"Lea{runde}";
            nachname = $"Feuerstein{runde}";
            using var laden = await client.GetAsync("/api/ich/profil", Abbruch);
            var profil = await JsonAsync(laden);
            Assert.Equal(vorname, profil.GetProperty("vorname").GetString());
            Assert.Equal(nachname, profil.GetProperty("nachname").GetString());
            version = profil.GetProperty("version").GetUInt32();
        }

        static string Json(string? wert) => wert is null ? "null" : $"\"{wert}\"";
    }

    [Theory]
    [InlineData(100, HttpStatusCode.OK)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    public async Task Je_Feld_hoechstens_100_Zeichen(int laenge, HttpStatusCode erwartet)
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Mail);
        var version = await SicherstellenAsync(client);

        using var antwort = await PatchAsync(client, $$"""{"version":{{version}},"alterWert":null,"nachname":"{{new string('x', laenge)}}"}""");

        Assert.Equal(erwartet, antwort.StatusCode);
        if (erwartet == HttpStatusCode.BadRequest)
        {
            Assert.Equal(["nachname"], Namen((await ProblemAsync(antwort)).GetProperty("errors")));
        }
    }

    [Theory]
    [InlineData("""{"version":1,"alterWert":null}""")]
    [InlineData("""{"version":1,"alterWert":null,"vorname":"A","nachname":"B"}""")]
    [InlineData("""{"alterWert":null,"vorname":"A"}""")]
    [InlineData("""{"version":1,"alterWert":null,"vorname":5}""")]
    [InlineData("""[1]""")]
    public async Task Koerper_ohne_genau_ein_Feld_oder_ohne_Version_ergibt_400(string koerper)
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Mail);
        await SicherstellenAsync(client);

        using var antwort = await PatchAsync(client, koerper);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal("validierung", Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task Aendern_ohne_angelegte_Person_ergibt_404()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Mail);

        using var antwort = await PatchAsync(client, """{"version":1,"alterWert":null,"vorname":"Lea"}""");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Die_Sitzung_von_Person_A_liefert_nur_die_Daten_von_A_in_der_Vertragsform()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var a = await AngemeldeterClientAsync(factory, "a@example.org");
        using var b = await AngemeldeterClientAsync(factory, "b@example.org");
        foreach (var (client, name) in new[] { (a, "Anna"), (b, "Bernd") })
        {
            var version = await SicherstellenAsync(client);
            using var antwort = await PatchAsync(client, $$"""{"version":{{version}},"alterWert":null,"vorname":"{{name}}"}""");
            Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        }

        using var profil = await a.GetAsync("/api/ich/profil", Abbruch);
        using var konto = await a.GetAsync("/api/ich/konto", Abbruch);

        var p = await JsonAsync(profil);
        Assert.Equal(["nachname", "version", "vorname"], Namen(p));
        Assert.Equal("Anna", p.GetProperty("vorname").GetString());
        var k = await JsonAsync(konto);
        Assert.Equal(["hatPasswort", "mail"], Namen(k));
        Assert.Equal("a@example.org", k.GetProperty("mail").GetString());
        Assert.DoesNotContain("Bernd", p.GetRawText(), StringComparison.Ordinal);
    }

    private static async Task<uint> SicherstellenAsync(HttpClient client)
    {
        using var antwort = await PostAsync(client, "/api/ich/profil");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("version").GetUInt32();
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string json) =>
        SendenAsync(client, HttpMethod.Patch, "/api/ich/profil", json);
}
