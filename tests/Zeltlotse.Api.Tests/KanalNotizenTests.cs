using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 6-3, Ansagen-Zettel und Besprechungspunkte (CAP-37, FR-63, AD-16, AD-17, AD-30) gegen echtes PostgreSQL: Punkt anlegen, abhaken und wieder öffnen
/// (idempotent), die Listen „offen“ (älteste zuerst) und „abgehakt“ (höchstens 50, zuletzt abgehakte zuerst), Text-Grenzen, unbekannte Art, fremde Notizen,
/// die Pflichtfälle der Freizeit-Route auf allen vier Routen, der Schreibschutz nach dem Abschluss und der Lösch-Lauf.
/// Freizeit X (Organisation A; Org-Admin Andrea, Leitung Miriam, Team Tobias) und Z (Organisation B, Leitung Miriam).
/// </summary>
public sealed class KanalNotizenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string ListenPfad(Guid freizeit, string art = "ansagen") => $"/api/freizeiten/{freizeit}/kanaele/notizen/{art}";

    private static string UebergangPfad(Guid freizeit, Guid notiz, string uebergang) => $"/api/freizeiten/{freizeit}/kanaele/notizen/{notiz}/{uebergang}";

    private Task<HttpResponseMessage> AnlegenAsync(HttpClient client, string? text, string art = "ansagen", Guid? freizeit = null) =>
        PostAsync(client, ListenPfad(freizeit ?? _s.X, art), new { text });

    private async Task<JsonElement> AngelegtAsync(HttpClient client, string text, string art = "ansagen", Guid? freizeit = null)
    {
        using var antwort = await AnlegenAsync(client, text, art, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<JsonElement> UebergangAsync(HttpClient client, Guid notiz, string uebergang, Guid? freizeit = null)
    {
        using var antwort = await PostAsync(client, UebergangPfad(freizeit ?? _s.X, notiz, uebergang));
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<JsonElement> ListeAsync(HttpClient? client = null, string art = "ansagen", Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(ListenPfad(freizeit ?? _s.X, art), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private static IReadOnlyList<string?> Texte(JsonElement liste, string teil) =>
        [.. liste.GetProperty(teil).EnumerateArray().Select(n => n.GetProperty("text").GetString())];

    private static Guid Id(JsonElement notiz) => notiz.GetProperty("id").GetGuid();

    private Task<long> AnzahlAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM kanaele.notiz WHERE {bedingung}", parameter);

    // ---------- Anlegen und Listen ----------

    [Fact]
    public async Task Anlegen_201_mit_offenem_Punkt_er_steht_auf_dem_Ansagen_Zettel_nicht_in_der_Besprechung()
    {
        var jetzt = _s.VolleSekunde();

        var notiz = await AngelegtAsync(_s.Tobias, "  Taschenlampen mitbringen  ");

        Assert.Equal("Taschenlampen mitbringen", notiz.GetProperty("text").GetString());
        Assert.Equal("Ansage", notiz.GetProperty("art").GetString());
        Assert.Equal(jetzt, notiz.GetProperty("angelegtAm").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, notiz.GetProperty("erledigtAm").ValueKind);

        var ansagen = await ListeAsync(_s.Miriam);
        Assert.Equal("Ansage", ansagen.GetProperty("art").GetString());
        Assert.Equal(Id(notiz), Id(Assert.Single(ansagen.GetProperty("offen").EnumerateArray())));
        Assert.Empty(ansagen.GetProperty("abgehakt").EnumerateArray());

        var besprechung = await ListeAsync(_s.Miriam, "besprechung");
        Assert.Equal("Besprechung", besprechung.GetProperty("art").GetString());
        Assert.Empty(besprechung.GetProperty("offen").EnumerateArray());
        Assert.Empty(besprechung.GetProperty("abgehakt").EnumerateArray());
    }

    [Fact]
    public async Task Offen_aelteste_zuerst_auch_bei_gleichem_Zeitpunkt_in_der_Reihenfolge_des_Speicherns()
    {
        await AngelegtAsync(_s.Miriam, "Erster", "besprechung");
        await AngelegtAsync(_s.Tobias, "Zweiter", "besprechung");
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        await AngelegtAsync(_s.Miriam, "Dritter", "besprechung");

        Assert.Equal(["Erster", "Zweiter", "Dritter"], Texte(await ListeAsync(_s.Tobias, "besprechung"), "offen"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Text_leer_oder_nur_Leerzeichen_400_am_Feld_text_nichts_gespeichert(string? text)
    {
        using var antwort = await AnlegenAsync(_s.Miriam, text);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty("text", out _));
        Assert.Equal(0L, await AnzahlAsync());
    }

    [Fact]
    public async Task Text_ueber_500_Zeichen_400_genau_500_geht()
    {
        using (var antwort = await AnlegenAsync(_s.Miriam, new string('x', 501)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty("text", out _));
        }

        Assert.Equal(0L, await AnzahlAsync());
        await AngelegtAsync(_s.Miriam, new string('x', 500));
        Assert.Equal(1L, await AnzahlAsync());
    }

    [Fact]
    public async Task Unbekannte_Art_404_bei_Lesen_und_Anlegen()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(ListenPfad(_s.X, "foo"), Abbruch)).StatusCode);
        using var antwort = await AnlegenAsync(_s.Miriam, "Etwas", "foo");
        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(0L, await AnzahlAsync());
    }

    // ---------- Abhaken und wieder öffnen ----------

    [Fact]
    public async Task Abhaken_200_mit_erledigtAm_steht_unter_abgehakt_wiederholt_bleibt_der_erste_Zeitpunkt()
    {
        var notiz = Id(await AngelegtAsync(_s.Miriam, "Spülplan besprechen", "besprechung"));
        await AngelegtAsync(_s.Miriam, "Nachtwache einteilen", "besprechung");
        var jetzt = _s.VolleSekunde();

        var erledigt = await UebergangAsync(_s.Tobias, notiz, "erledigt");
        Assert.Equal(jetzt, erledigt.GetProperty("erledigtAm").GetDateTimeOffset());

        var liste = await ListeAsync(_s.Miriam, "besprechung");
        Assert.Equal(["Nachtwache einteilen"], Texte(liste, "offen"));
        Assert.Equal(["Spülplan besprechen"], Texte(liste, "abgehakt"));

        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));
        var nochmal = await UebergangAsync(_s.Miriam, notiz, "erledigt");
        Assert.Equal(jetzt, nochmal.GetProperty("erledigtAm").GetDateTimeOffset());
    }

    [Fact]
    public async Task Wieder_oeffnen_200_ohne_erledigtAm_wieder_unter_offen_auch_wiederholt()
    {
        var notiz = Id(await AngelegtAsync(_s.Miriam, "Spülplan besprechen", "besprechung"));
        await UebergangAsync(_s.Miriam, notiz, "erledigt");

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var offen = await UebergangAsync(_s.Tobias, notiz, "offen");
            Assert.Equal(JsonValueKind.Null, offen.GetProperty("erledigtAm").ValueKind);
        }

        var liste = await ListeAsync(_s.Miriam, "besprechung");
        Assert.Equal(["Spülplan besprechen"], Texte(liste, "offen"));
        Assert.Empty(liste.GetProperty("abgehakt").EnumerateArray());
    }

    [Fact]
    public async Task Abgehakt_enthaelt_hoechstens_die_50_zuletzt_abgehakten_zuletzt_abgehakte_zuerst()
    {
        var ids = new List<Guid>();
        for (var i = 1; i <= 51; i++)
        {
            ids.Add(Id(await AngelegtAsync(_s.Miriam, $"Punkt {i}")));
        }

        // In umgekehrter Reihenfolge abhaken: Punkt 51 zuerst, Punkt 1 zuletzt.
        foreach (var id in Enumerable.Reverse(ids))
        {
            _s.Factory.Uhr.Advance(TimeSpan.FromSeconds(1));
            await UebergangAsync(_s.Miriam, id, "erledigt");
        }

        var liste = await ListeAsync();
        Assert.Empty(liste.GetProperty("offen").EnumerateArray());
        var abgehakt = Texte(liste, "abgehakt");
        Assert.Equal(50, abgehakt.Count);
        Assert.Equal([.. Enumerable.Range(1, 50).Select(i => $"Punkt {i}")], abgehakt);
        Assert.Equal(51L, await AnzahlAsync("\"ErledigtAm\" IS NOT NULL"));
    }

    [Fact]
    public async Task Fremde_oder_unbekannte_Notiz_404_auch_aus_einer_anderen_Freizeit_derselben_Organisation()
    {
        // Freizeit Y in Organisation A mit Tobias als Leitung.
        var y = await FreizeitHelfer.AnlegenAsync(_s.Andrea, _s.A, "Herbstlager 2027");
        await FreizeitHelfer.EinladenAsync(_s.Andrea, _s.A, y, FreizeitHelfer.Tobias);
        await FreizeitHelfer.AnnehmenMitAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        await _s.ZustellenAsync();

        var inY = Id(await AngelegtAsync(_s.Tobias, "In Y", freizeit: y));
        var inZ = Id(await AngelegtAsync(_s.Miriam, "In Z", freizeit: _s.Z));

        foreach (var (client, notiz) in new[] { (_s.Tobias, inY), (_s.Miriam, inZ), (_s.Miriam, Guid.CreateVersion7()) })
        {
            foreach (var uebergang in new[] { "erledigt", "offen" })
            {
                using var antwort = await PostAsync(client, UebergangPfad(_s.X, notiz, uebergang));
                Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
            }
        }

        Assert.Equal(0L, await AnzahlAsync("\"ErledigtAm\" IS NOT NULL"));
        Assert.Empty((await ListeAsync(_s.Miriam)).GetProperty("offen").EnumerateArray());
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufen_Lesen_200_Anlegen_Abhaken_und_Oeffnen_409_freizeit_abgeschlossen_nichts_geaendert()
    {
        var offen = Id(await AngelegtAsync(_s.Miriam, "Offen"));
        var erledigt = Id(await AngelegtAsync(_s.Miriam, "Erledigt"));
        await UebergangAsync(_s.Miriam, erledigt, "erledigt");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        foreach (var aufruf in new Func<Task<HttpResponseMessage>>[]
                 {
                     () => AnlegenAsync(_s.Miriam, "Zu spät"),
                     () => PostAsync(_s.Tobias, UebergangPfad(_s.X, offen, "erledigt")),
                     () => PostAsync(_s.Tobias, UebergangPfad(_s.X, erledigt, "offen")),
                 })
        {
            using var antwort = await aufruf();
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
        }

        var liste = await ListeAsync(_s.Tobias);
        Assert.Equal(["Offen"], Texte(liste, "offen"));
        Assert.Equal(["Erledigt"], Texte(liste, "abgehakt"));
        Assert.Equal(2L, await AnzahlAsync());
    }

    // ---------- Rechte ----------

    /// <summary>Alle vier Routen für einen Client unter einer Freizeit.</summary>
    private static IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid notiz) =>
    [
        () => client.GetAsync(ListenPfad(freizeit, "besprechung"), Abbruch),
        () => PostAsync(client, ListenPfad(freizeit, "besprechung"), new { text = "Hallo" }),
        () => PostAsync(client, UebergangPfad(freizeit, notiz, "erledigt")),
        () => PostAsync(client, UebergangPfad(freizeit, notiz, "offen")),
    ];

    private static async Task<List<HttpStatusCode>> StatusAllerRoutenAsync(HttpClient client, Guid freizeit, Guid notiz)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, notiz))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_auf_allen_vier_Routen()
    {
        HttpStatusCode[] erwartet = [HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK];
        var inX = Id(await AngelegtAsync(_s.Miriam, "In X", "besprechung"));
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.Equal(erwartet, await StatusAllerRoutenAsync(client, _s.X, inX));
        }

        // Miriam ist auch Leitung von Z.
        var inZ = Id(await AngelegtAsync(_s.Miriam, "In Z", "besprechung", _s.Z));
        Assert.Equal(erwartet, await StatusAllerRoutenAsync(_s.Miriam, _s.Z, inZ));
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_Leitung_von_Z_unbekannte_Freizeit_404_ohne_Sitzung_401_auf_allen_vier_Routen()
    {
        var inX = Id(await AngelegtAsync(_s.Miriam, "In X", "besprechung"));
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
            Assert.All(await StatusAllerRoutenAsync(client, freizeit, inX), s => Assert.Equal(erwartet, s));
        }

        Assert.Equal(1L, await AnzahlAsync());
        Assert.Equal(0L, await AnzahlAsync("\"ErledigtAm\" IS NOT NULL"));
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Notizen_der_abgelaufenen_Freizeit_die_in_Z_bleiben()
    {
        await AngelegtAsync(_s.Miriam, "In X");
        await AngelegtAsync(_s.Miriam, "Auch in X", "besprechung");
        await AngelegtAsync(_s.Miriam, "In Z", freizeit: _s.Z);

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "KanaeleLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await AnzahlAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }
}
