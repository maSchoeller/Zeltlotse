using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using FamilienHelfer = Zeltlotse.Api.Tests.FamilienHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-4, Freizeitpass ausfüllen mit Konto (FR-23, AD-10, AD-14, AD-19, AD-26, AD-30) gegen echtes PostgreSQL: die eigene Person
/// und Kinder der Familie (auch über den zweiten Elternteil), fremde Personen <c>404</c>, Antworten nur verschlüsselt, Autosave mit
/// Konfliktregel und Anlegen beim ersten PATCH, Prüfung der Werte, „Stimmt das noch?“ nach 12 Monaten mit künstlicher Uhr, Vorausfüllen
/// bei einer zweiten Freizeit und Löschen mit dem Konto.
/// </summary>
public sealed class FreizeitpassTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _katrin = null!;
    private HttpClient _thomas = null!;
    private HttpClient _daniel = null!;
    private Guid _emma;
    private Guid _katrinId;

    /// <summary>Katrin hat Emma im Familienkonto, Thomas ist zweiter Elternteil; Daniel hat ein eigenes Konto.</summary>
    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = OrgHelfer.Factory(_verbindung);
        _katrin = await AngemeldeterClientAsync(_factory, FamilienHelfer.Katrin);
        await NameSetzenAsync(_katrin, "Katrin", "Muster");
        _katrinId = await PersonIdAsync(_verbindung, FamilienHelfer.Katrin);
        _emma = await FamilienHelfer.KindAsync(_katrin, "Emma");
        _thomas = await FamilienHelfer.ZweiterElternteilAsync(_factory, _katrin, FamilienHelfer.Thomas);
        _daniel = await AngemeldeterClientAsync(_factory, FamilienHelfer.Daniel);
        (await PostAsync(_daniel, "/api/ich/profil")).Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _katrin, _thomas, _daniel })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    // ---------- Lesen und Rechte ----------

    [Fact]
    public async Task Der_Pass_liefert_jede_Katalogfrage_mit_Art_Optionen_und_ohne_Antwort_Version_0()
    {
        using var antwort = await _katrin.GetAsync(Pfad(_emma), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["fragen", "personId", "vorname"], Namen(json));
        Assert.Equal((_emma, "Emma"), (json.GetProperty("personId").GetGuid(), json.GetProperty("vorname").GetString()));
        var fragen = json.GetProperty("fragen").EnumerateArray().ToList();
        Assert.Equal(
            ["allergien", "medikamente", "notfallkontakte", "ernaehrung", "krankheiten", "krankenversicherung", "tetanus", "schwimmen", "zecken-entfernen", "hinweise"],
            fragen.Select(f => f.GetProperty("kennung").GetString()));
        Assert.All(fragen, f => Assert.Equal(["art", "frage", "kennung", "mitSonstiges", "optionen", "sonstiges", "veraltet", "version", "wert"], Namen(f)));
        Assert.All(fragen, f => Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, 0u, false), (
            f.GetProperty("wert").ValueKind, f.GetProperty("sonstiges").ValueKind, f.GetProperty("version").GetUInt32(), f.GetProperty("veraltet").GetBoolean())));
        Assert.Equal(
            ["Auswahl", "Freitext", "Freitext", "Auswahl", "Freitext", "Freitext", "Datum", "JaNein", "JaNein", "Freitext"],
            fragen.Select(f => f.GetProperty("art").GetString()));
        Assert.Equal(14, fragen[0].GetProperty("optionen").GetArrayLength());
        Assert.Equal(["text", "wert"], Namen(fragen[0].GetProperty("optionen")[0]));
        Assert.True(fragen[0].GetProperty("mitSonstiges").GetBoolean());
        Assert.Equal(["ja", "nein"], fragen[7].GetProperty("optionen").EnumerateArray().Select(o => o.GetProperty("wert").GetString()));
    }

    [Fact]
    public async Task Selbst_und_beide_Eltern_duerfen_fremde_Personen_und_volljaehrige_Kinder_ergeben_404()
    {
        var volljaehrig = await FamilienHelfer.VolljaehrigesKindAsync(_katrin, "Paul");

        Assert.Equal(HttpStatusCode.OK, (await _katrin.GetAsync(Pfad(_katrinId), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _thomas.GetAsync(Pfad(_emma), Abbruch)).StatusCode);
        foreach (var (client, person) in new[] { (_daniel, _emma), (_daniel, _katrinId), (_katrin, volljaehrig), (_katrin, Guid.CreateVersion7()) })
        {
            using var lesen = await client.GetAsync(Pfad(person), Abbruch);
            using var aendern = await PatchAsync(client, person, "allergien", """{"version":0,"alterWert":null,"wert":"milch"}""");
            using var bestaetigen = await PostAsync(client, $"{Pfad(person)}/antworten/allergien/bestaetigen");

            Assert.Equal(
                [HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
                [lesen.StatusCode, aendern.StatusCode, bestaetigen.StatusCode]);
        }

        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
        // Ohne Sitzung 401.
        using var anonym = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonym.GetAsync(Pfad(_emma), Abbruch)).StatusCode);
    }

    // ---------- Autosave ----------

    [Fact]
    public async Task Die_erste_Antwort_legt_die_Zeile_an_normalisiert_die_Auswahl_und_speichert_nur_verschluesselt()
    {
        var jetzt = _factory.Uhr.GetUtcNow();

        using var antwort = await PatchAsync(_katrin, _emma, "allergien", """{"version":0,"alterWert":null,"wert":"milch,eier"}""");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["version", "wert"], Namen(json));
        Assert.Equal("eier,milch", json.GetProperty("wert").GetString());
        Assert.NotEqual(0u, json.GetProperty("version").GetUInt32());
        Assert.Equal("bytea", await WertAsync<string>(_verbindung, "SELECT pg_typeof(\"Wert\")::text FROM personen.freizeitpass_antwort"));
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung,
            "SELECT count(*) FROM personen.freizeitpass_antwort WHERE \"PersonId\" = @p AND \"Kennung\" = 'allergien' AND position('milch'::bytea in \"Wert\") = 0 AND \"Sonstiges\" IS NULL",
            ("p", _emma)));
        Assert.Equal(Mikro(jetzt), new DateTimeOffset(await WertAsync<DateTime>(_verbindung, "SELECT \"BestaetigtAm\" FROM personen.freizeitpass_antwort"), TimeSpan.Zero));

        // Sonstiges der Frage mit der Version vor dem ersten PATCH: nur der alte Wert desselben Felds zählt (AD-19).
        using var sonstiges = await PatchAsync(_thomas, _emma, "allergien", """{"version":0,"alterWert":null,"sonstiges":" Kiwi "}""");
        Assert.Equal(HttpStatusCode.OK, sonstiges.StatusCode);
        Assert.Equal("Kiwi", (await JsonAsync(sonstiges)).GetProperty("sonstiges").GetString());

        var allergien = (await PassAsync(_katrin, _emma))[0];
        Assert.Equal(("eier,milch", "Kiwi", false), (allergien.GetProperty("wert").GetString(), allergien.GetProperty("sonstiges").GetString(), allergien.GetProperty("veraltet").GetBoolean()));
    }

    [Fact]
    public async Task Abweichender_alter_Wert_desselben_Felds_ergibt_409_auch_beim_ersten_Anlegen()
    {
        using (var erstes = await PatchAsync(_katrin, _emma, "medikamente", """{"version":0,"alterWert":null,"wert":"Ibuprofen bei Bedarf"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        }

        // Gleichzeitig angelegt: Der zweite kennt die Antwort nicht.
        using var zweites = await PatchAsync(_thomas, _emma, "medikamente", """{"version":0,"alterWert":null,"wert":"Keine"}""");
        using var veraltet = await PatchAsync(_thomas, _emma, "medikamente", """{"version":1,"alterWert":"Etwas anderes","wert":"Keine"}""");

        Assert.Equal(HttpStatusCode.Conflict, zweites.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, (await ProblemAsync(zweites)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, veraltet.StatusCode);
        Assert.Equal("Ibuprofen bei Bedarf", (await PassAsync(_katrin, _emma))[1].GetProperty("wert").GetString());

        using var passend = await PatchAsync(_thomas, _emma, "medikamente", """{"version":1,"alterWert":"Ibuprofen bei Bedarf","wert":""}""");
        Assert.Equal(HttpStatusCode.OK, passend.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(passend)).GetProperty("wert").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await PassAsync(_katrin, _emma))[1].GetProperty("wert").ValueKind);
    }

    [Theory]
    [InlineData("schwimmen", "wert", "vielleicht")]
    [InlineData("tetanus", "wert", "31.02.")]
    [InlineData("allergien", "wert", "nuss")]
    [InlineData("medikamente", "sonstiges", "x")]
    public async Task Ungueltige_Werte_ergeben_400_am_Feld(string kennung, string feld, string wert)
    {
        using var antwort = await PatchAsync(_katrin, _emma, kennung, $$"""{"version":0,"alterWert":null,"{{feld}}":"{{wert}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal([feld], problem.GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
    }

    [Fact]
    public async Task Freitext_ueber_2000_Zeichen_und_Koerper_mit_zwei_Feldern_ergeben_400_unbekannte_Kennung_404()
    {
        using var lang = await PatchAsync(_katrin, _emma, "hinweise", $$"""{"version":0,"alterWert":null,"wert":"{{new string('x', 2001)}}"}""");
        using var zwei = await PatchAsync(_katrin, _emma, "allergien", """{"version":0,"alterWert":null,"wert":"milch","sonstiges":"Kiwi"}""");
        using var unbekannt = await PatchAsync(_katrin, _emma, "lieblingsfarbe", """{"version":0,"alterWert":null,"wert":"blau"}""");
        using var unbekanntBestaetigen = await PostAsync(_katrin, $"{Pfad(_emma)}/antworten/lieblingsfarbe/bestaetigen");

        Assert.Equal(HttpStatusCode.BadRequest, lang.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zwei.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unbekanntBestaetigen.StatusCode);
    }

    // ---------- Veraltet ----------

    [Fact]
    public async Task Nach_12_Monaten_und_einem_Tag_ist_eine_Antwort_veraltet_und_Stimmt_bestaetigt_sie_neu()
    {
        (await PatchAsync(_katrin, _emma, "schwimmen", """{"version":0,"alterWert":null,"wert":"ja"}""")).Dispose();
        _factory.Uhr.Advance(TimeSpan.FromDays(364));
        await AnmeldenAsync(_factory, _katrin, FamilienHelfer.Katrin);
        Assert.False((await PassAsync(_katrin, _emma))[7].GetProperty("veraltet").GetBoolean());

        _factory.Uhr.Advance(TimeSpan.FromDays(2));
        var vorher = (await PassAsync(_katrin, _emma))[7];
        Assert.True(vorher.GetProperty("veraltet").GetBoolean());
        Assert.False((await PassAsync(_katrin, _emma))[0].GetProperty("veraltet").GetBoolean());

        using var stimmt = await PostAsync(_katrin, $"{Pfad(_emma)}/antworten/schwimmen/bestaetigen");
        using var ohneAntwort = await PostAsync(_katrin, $"{Pfad(_emma)}/antworten/tetanus/bestaetigen");

        Assert.Equal(HttpStatusCode.NoContent, stimmt.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ohneAntwort.StatusCode);
        var nachher = (await PassAsync(_katrin, _emma))[7];
        Assert.Equal(("ja", false), (nachher.GetProperty("wert").GetString(), nachher.GetProperty("veraltet").GetBoolean()));
        Assert.NotEqual(vorher.GetProperty("version").GetUInt32(), nachher.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task Eine_geaenderte_Antwort_ist_nicht_mehr_veraltet()
    {
        (await PatchAsync(_katrin, _emma, "notfallkontakte", """{"version":0,"alterWert":null,"wert":"Oma, 0171 1234567"}""")).Dispose();
        _factory.Uhr.Advance(TimeSpan.FromDays(400));
        await AnmeldenAsync(_factory, _katrin, FamilienHelfer.Katrin);
        var frage = (await PassAsync(_katrin, _emma))[2];
        Assert.True(frage.GetProperty("veraltet").GetBoolean());

        using var antwort = await PatchAsync(
            _katrin, _emma, "notfallkontakte", $$"""{"version":{{frage.GetProperty("version").GetUInt32()}},"alterWert":"Oma, 0171 1234567","wert":"Opa, 0171 7654321"}""");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.False((await PassAsync(_katrin, _emma))[2].GetProperty("veraltet").GetBoolean());
    }

    [Fact]
    public async Task Eine_geleerte_Antwort_ist_nie_veraltet_und_laesst_sich_nicht_bestaetigen()
    {
        using (var setzen = await PatchAsync(_katrin, _emma, "allergien", """{"version":0,"alterWert":null,"wert":"milch"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, setzen.StatusCode);
        }

        using (var leeren = await PatchAsync(_katrin, _emma, "allergien", """{"version":0,"alterWert":"milch","wert":""}"""))
        {
            Assert.Equal(HttpStatusCode.OK, leeren.StatusCode);
        }

        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort WHERE \"Wert\" IS NULL AND \"Sonstiges\" IS NULL"));
        _factory.Uhr.Advance(TimeSpan.FromDays(400));
        await AnmeldenAsync(_factory, _katrin, FamilienHelfer.Katrin);

        var allergien = (await PassAsync(_katrin, _emma))[0];
        using var stimmt = await PostAsync(_katrin, $"{Pfad(_emma)}/antworten/allergien/bestaetigen");

        Assert.Equal((JsonValueKind.Null, false), (allergien.GetProperty("wert").ValueKind, allergien.GetProperty("veraltet").GetBoolean()));
        Assert.Equal(HttpStatusCode.NotFound, stimmt.StatusCode);
    }

    // ---------- Nächste Freizeit ----------

    [Fact]
    public async Task Antworten_gehoeren_der_Person_und_stehen_bei_einer_zweiten_Freizeit_schon_da()
    {
        var gerd = await AngemeldeterClientAsync(_factory, Gerd);
        var (orgId, andrea) = await OrganisationMitOrgAdminAsync(_factory, gerd, "CVJM Musterstadt", Andrea);
        var erste = await AnlegenAsync(andrea, orgId, "Pfingstlager 2027", new DateOnly(2027, 5, 14), new DateOnly(2027, 5, 17));
        var zweite = await AnlegenAsync(andrea, orgId, "Zeltlager 2027");
        var miriam = await LeitungAsync(_factory, andrea, orgId, erste, Miriam);
        await FreizeitHelfer.EinladenAsync(andrea, orgId, zweite, Miriam);
        await AnnehmenMitAsync(_factory, miriam, Miriam);
        await FragenSetzenAsync(miriam, erste, "allergien,notfallkontakte");
        await FragenSetzenAsync(miriam, zweite, "notfallkontakte,schwimmen");
        await AnmeldenMitKontoAsync(miriam, erste);
        (await PatchAsync(_katrin, _emma, "notfallkontakte", """{"version":0,"alterWert":null,"wert":"Oma, 0171 1234567"}""")).Dispose();

        await AnmeldenMitKontoAsync(miriam, zweite);

        var zeilen = (await JsonAsync(await _katrin.GetAsync("/api/ich/anmeldungen", Abbruch))).EnumerateArray().ToList();
        Assert.Equal(
            [("Zeltlager 2027", "notfallkontakte,schwimmen"), ("Pfingstlager 2027", "allergien,notfallkontakte")],
            zeilen.Select(z => (z.GetProperty("freizeitName").GetString(), string.Join(',', z.GetProperty("fragen").EnumerateArray().Select(f => f.GetString())))));
        Assert.Equal("Oma, 0171 1234567", (await PassAsync(_thomas, _emma))[2].GetProperty("wert").GetString());
        gerd.Dispose();
        andrea.Dispose();
        miriam.Dispose();
    }

    // ---------- Löschen ----------

    [Fact]
    public async Task Konto_loeschen_entfernt_die_Antworten_der_Person_und_der_Kinder_einziger_Eltern()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = OrgHelfer.Factory(verbindung);
        using var daniel = await AngemeldeterClientAsync(factory, FamilienHelfer.Daniel);
        (await PostAsync(daniel, "/api/ich/profil")).Dispose();
        var danielId = await PersonIdAsync(verbindung, FamilienHelfer.Daniel);
        var kind = await FamilienHelfer.KindAsync(daniel, "Mia");
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(daniel, danielId, "hinweise", """{"version":0,"alterWert":null,"wert":"Brille"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(daniel, kind, "schwimmen", """{"version":0,"alterWert":null,"wert":"nein"}""")).StatusCode);
        Assert.Equal(2L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));

        using var antwort = await PostAsync(daniel, "/api/ich/profil/loeschen");

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
    }

    // ---------- Hilfen ----------

    private static string Pfad(Guid person) => $"/api/ich/freizeitpass/{person}";

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid person, string kennung, string json) =>
        SendenAsync(client, HttpMethod.Patch, $"{Pfad(person)}/antworten/{kennung}", json);

    private static async Task<List<JsonElement>> PassAsync(HttpClient client, Guid person)
    {
        using var antwort = await client.GetAsync(Pfad(person), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("fragen").EnumerateArray().ToList();
    }

    private static async Task FragenSetzenAsync(HttpClient leitung, Guid freizeit, string teilnehmende)
    {
        var version = (await JsonAsync(await leitung.GetAsync($"/api/freizeiten/{freizeit}/fragen", Abbruch))).GetProperty("version").GetUInt32();
        using var antwort = await SendenAsync(
            leitung, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/fragen", $$"""{"version":{{version}},"alterWert":null,"teilnehmende":"{{teilnehmende}}"}""");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private async Task AnmeldenMitKontoAsync(HttpClient leitung, Guid freizeit)
    {
        var adresse = (await JsonAsync(await leitung.GetAsync($"/api/freizeiten/{freizeit}/freizeitseite", Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
            .Single(l => l.GetProperty("art").GetString() == nameof(AnmeldungArt.Teilnehmende)).GetProperty("adresse").GetString()!;
        using var antwort = await PostAsync(_katrin, "/api/ich/anmeldungen", new AnmeldungAbsenden(adresse[(adresse.LastIndexOf('/') + 1)..], _emma, true));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
    }

    private static DateTimeOffset Mikro(DateTimeOffset zeit) => new(zeit.Ticks - (zeit.Ticks % 10), zeit.Offset);
}
