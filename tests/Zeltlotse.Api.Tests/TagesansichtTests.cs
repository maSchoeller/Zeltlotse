using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.KuecheTestHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 4-4, Küchen-Tagesansicht und Restetag (CAP-31, FR-52, FR-53, AD-8, AD-16, AD-17, AD-19, AD-20, AD-30) gegen echtes PostgreSQL: die Tagesansicht
/// eines Tags mit Gerichten, Ersatzportionen, Allergen- und Sonderkost-Übersicht (nur bestätigte, nur Anzahlen, Protokoll ohne lesende Person), die
/// Tag-Wahl, die Feldfehler am Tag, der Restetag (Autosave mit Konflikt und Feldfehler, ändert nichts sonst, nur Mahlzeiten dieser Freizeit, gleichzeitig
/// genau eine Zeile), die Pflichtfälle der Freizeit-Route, der Schalter Verpflegung, die abgeschlossene Freizeit, der Lösch-Lauf und Zugriffsprotokoll
/// und Datenauskunft mit dem neuen Zweck. Aufbau wie <see cref="EinkaufFreigabeTests"/>: Freizeit X (02.–13.08.2027, Leitung Miriam, Team Tobias); X
/// fragt zusätzlich „Ernährung“ ab.
/// </summary>
public sealed class TagesansichtTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly DateOnly Dienstag = new(2027, 8, 3);

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres);
        await VerpflegungAsync(_s, _s.X);
        await VerpflegungAsync(_s, _s.Z);
        await _s.FragenAsync(_s.X, "allergien,ernaehrung,notfallkontakte,schwimmen");
    }

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static string Iso(DateOnly tag) => tag.ToString("yyyy-MM-dd");

    private async Task<Guid> MahlzeitAsync(DateOnly tag, string titel, Guid? freizeit = null)
    {
        using var antwort = await _s.Miriam.GetAsync($"{KuechePfad(freizeit ?? _s.X)}/essensplan", Abbruch);
        return (await JsonAsync(antwort)).GetProperty("tage").EnumerateArray()
            .Single(t => t.GetProperty("tag").GetString() == Iso(tag))
            .GetProperty("mahlzeiten").EnumerateArray()
            .Single(m => m.GetProperty("titel").GetString() == titel)
            .GetProperty("programmpunktId").GetGuid();
    }

    private async Task<Guid> GerichtAsync(DateOnly tag, string mahlzeit, Guid rezept)
    {
        using var antwort = await PostAsync(_s.Miriam, $"{KuechePfad(_s.X)}/gerichte", new { programmpunktId = await MahlzeitAsync(tag, mahlzeit), rezeptId = rezept });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return Id(await JsonAsync(antwort));
    }

    /// <summary>Pfannkuchen (Basis 4) mit Mehl 400 g (Gluten), Milch 0,75 l (Milch) und Salz ohne Menge.</summary>
    private async Task<Guid> PfannkuchenAsync()
    {
        var rezept = Id(await RezeptAsync(_s, "Pfannkuchen", "4"));
        await ZutatAsync(_s, rezept, "Mehl", "400", "g", "gluten");
        await ZutatAsync(_s, rezept, "Milch", "0,75", "l", "milch");
        await ZutatAsync(_s, rezept, "Salz");
        return rezept;
    }

    /// <summary>Mia mit Gluten und vegetarisch im Freizeitpass, angemeldet und bestätigt; Emma (Milch, vegan) nur angemeldet.</summary>
    private async Task MiaBestaetigtEmmaEingegangenAsync()
    {
        await _s.AntwortAsync(_s.Mia, "allergien", "gluten");
        await _s.AntwortAsync(_s.Mia, "ernaehrung", "vegetarisch");
        await _s.AntwortAsync(_s.Emma, "ernaehrung", "vegan");
        await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(_s.Mia));
        await _s.KindAnmeldenAsync(_s.Emma);
        await _s.ZustellenAsync();
    }

    private Task<HttpResponseMessage> TagesansichtAntwortAsync(string? tag = null, HttpClient? client = null, Guid? freizeit = null) =>
        (client ?? _s.Miriam).GetAsync($"{KuechePfad(freizeit ?? _s.X)}/tagesansicht{(tag is null ? "" : $"?tag={tag}")}", Abbruch);

    private async Task<JsonElement> TagesansichtAsync(string? tag = null, HttpClient? client = null)
    {
        using var antwort = await TagesansichtAntwortAsync(tag, client);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> RestetagAsync(Guid programmpunkt, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(
            client ?? _s.Miriam, HttpMethod.Patch, $"{KuechePfad(freizeit ?? _s.X)}/mahlzeiten/{programmpunkt}",
            new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = alterWert, ["restetag"] = wert });

    private static JsonElement Mahlzeit(JsonElement ansicht, string titel) =>
        ansicht.GetProperty("mahlzeiten").EnumerateArray().Single(m => m.GetProperty("titel").GetString() == titel);

    private static List<string> Anzahlen(JsonElement ansicht, string liste, string feld) =>
        [.. ansicht.GetProperty(liste).EnumerateArray().Select(a => $"{a.GetProperty(feld).GetString()} {a.GetProperty("anzahl").GetInt32()}")];

    private static async Task FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
    }

    private static async Task KonfliktAsync(HttpResponseMessage antwort, string code = Problemcodes.Konflikt)
    {
        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(code, Code(await ProblemAsync(antwort)));
    }

    private Task<long> StaendeAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM kueche.mahlzeit_stand WHERE {bedingung}", parameter);

    private Task<long> ProtokollAsync(Guid person, int zweck) =>
        WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @p AND \"Zweck\" = @z AND \"LesendePersonId\" IS NULL",
            ("p", person), ("z", (short)zweck));

    // ---------- Tagesansicht ----------

    [Fact]
    public async Task Tagesansicht_Dienstag_Pfannkuchen_mit_Ersatzportion_Allergene_und_Sonderkost_nur_bestaetigt_Protokoll_einmal_am_Tag()
    {
        await MiaBestaetigtEmmaEingegangenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", await PfannkuchenAsync());

        var ansicht = await TagesansichtAsync(Iso(Dienstag), _s.Tobias);
        await TagesansichtAsync(Iso(Dienstag));

        Assert.Equal(
            ["allergene", "allergienAbgefragt", "mahlzeiten", "personenzahl", "sonderkost", "sonderkostAbgefragt", "tag", "tage"],
            Namen(ansicht));
        Assert.Equal("2027-08-03", ansicht.GetProperty("tag").GetString());
        Assert.Equal(12, ansicht.GetProperty("tage").GetArrayLength());
        Assert.Equal(("2027-08-02", "2027-08-13"), (ansicht.GetProperty("tage")[0].GetString(), ansicht.GetProperty("tage")[11].GetString()));
        Assert.Equal(3, ansicht.GetProperty("personenzahl").GetInt32());
        Assert.True(ansicht.GetProperty("allergienAbgefragt").GetBoolean());
        Assert.True(ansicht.GetProperty("sonderkostAbgefragt").GetBoolean());
        Assert.Equal(["gluten 1"], Anzahlen(ansicht, "allergene", "allergen"));
        Assert.Equal(["vegetarisch 1"], Anzahlen(ansicht, "sonderkost", "sonderkost"));

        Assert.Equal(["Frühstück", "Mittagessen", "Abendessen"], ansicht.GetProperty("mahlzeiten").EnumerateArray().Select(m => m.GetProperty("titel").GetString()));
        var fruehstueck = Mahlzeit(ansicht, "Frühstück");
        Assert.Equal(["art", "beginn", "gerichte", "programmpunktId", "restetag", "titel", "version"], Namen(fruehstueck));
        Assert.Equal(("Fruehstueck", false, 0u), (fruehstueck.GetProperty("art").GetString(), fruehstueck.GetProperty("restetag").GetBoolean(), fruehstueck.GetProperty("version").GetUInt32()));
        var pfannkuchen = Assert.Single(fruehstueck.GetProperty("gerichte").EnumerateArray());
        Assert.Equal((gericht, "Pfannkuchen"), (pfannkuchen.GetProperty("id").GetGuid(), pfannkuchen.GetProperty("rezeptName").GetString()));
        Assert.Equal(1, pfannkuchen.GetProperty("ersatzportionen").GetProperty("anzahl").GetInt32());
        Assert.Equal(["gluten"], pfannkuchen.GetProperty("ersatzportionen").GetProperty("allergene").EnumerateArray().Select(a => a.GetString()));
        Assert.Empty(Mahlzeit(ansicht, "Abendessen").GetProperty("gerichte").EnumerateArray());

        // Keine Namen, keine Kennungen von Personen, kein Freitext.
        var text = ansicht.GetRawText();
        foreach (var verboten in new[] { "Mia", "Emma", _s.Mia.ToString(), _s.Emma.ToString(), "vegan" })
        {
            Assert.DoesNotContain(verboten, text, StringComparison.Ordinal);
        }

        // Protokoll: Mia je Zweck genau ein Eintrag ohne lesende Person, Emma keiner; Katrin sieht beide im Zugriffsprotokoll.
        Assert.Equal((1L, 1L), (await ProtokollAsync(_s.Mia, 3), await ProtokollAsync(_s.Mia, 4)));
        Assert.Equal((0L, 0L), (await ProtokollAsync(_s.Emma, 3), await ProtokollAsync(_s.Emma, 4)));
        var eintraege = (await _s.Katrin.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>("/api/ich/zugriffsprotokoll", Abbruch))!;
        var sonderkost = Assert.Single(eintraege, e => e.Zweck == ZugriffZweck.SonderkostFuerKueche);
        Assert.Equal((_s.Mia, (Guid?)null, (string?)null, _s.X, _s.A), (sonderkost.BetroffenePersonId, sonderkost.LesendePersonId, sonderkost.LesenderName, sonderkost.FreizeitId, sonderkost.OrganisationId));
        using var roh = await _s.Katrin.GetAsync("/api/ich/zugriffsprotokoll", Abbruch);
        Assert.Contains("\"sonderkostFuerKueche\"", await roh.Content.ReadAsStringAsync(Abbruch), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Tag_und_heute_ausserhalb_der_Freizeit_der_erste_Tag()
    {
        // Die Uhr der Tests steht vor der Freizeit; „heute in der Freizeit“ prüfen die Fachtests mit künstlicher Uhr.
        var ansicht = await TagesansichtAsync();

        Assert.Equal("2027-08-02", ansicht.GetProperty("tag").GetString());
        Assert.Equal(3, ansicht.GetProperty("mahlzeiten").GetArrayLength());
        Assert.Equal("2027-08-02", (await TagesansichtAsync("")).GetProperty("tag").GetString());
    }

    [Fact]
    public async Task Tag_ausserhalb_der_Freizeit_oder_kein_Datum_400_am_Feld_tag()
    {
        foreach (var tag in new[] { "2027-09-01", "2027-08-01", "morgen", "03.08.2027" })
        {
            using var antwort = await TagesansichtAntwortAsync(tag);
            await FeldfehlerAsync(antwort, "tag");
        }
    }

    [Fact]
    public async Task Freizeit_fragt_keine_Ernaehrung_ab_Sonderkost_leer_kein_Sonderkost_Protokoll()
    {
        await MiaBestaetigtEmmaEingegangenAsync();
        await _s.FragenAsync(_s.X, "allergien,notfallkontakte");

        var ansicht = await TagesansichtAsync(Iso(Dienstag));

        Assert.False(ansicht.GetProperty("sonderkostAbgefragt").GetBoolean());
        Assert.Empty(ansicht.GetProperty("sonderkost").EnumerateArray());
        Assert.Equal(["gluten 1"], Anzahlen(ansicht, "allergene", "allergen"));
        Assert.Equal(0L, await ProtokollAsync(_s.Mia, 4));
        Assert.Equal(1L, await ProtokollAsync(_s.Mia, 3));

        // Ganz ohne Fragen: auch keine Allergien.
        await _s.FragenAsync(_s.X, "notfallkontakte");
        var ohne = await TagesansichtAsync(Iso(Dienstag));
        Assert.False(ohne.GetProperty("allergienAbgefragt").GetBoolean());
        Assert.Empty(ohne.GetProperty("allergene").EnumerateArray());
    }

    // ---------- Restetag ----------

    [Fact]
    public async Task Restetag_setzen_200_Tagesansicht_und_Essensplan_zeigen_ihn_Einkaufsliste_unveraendert_anderer_Altwert_409()
    {
        await GerichtAsync(Dienstag, "Abendessen", await PfannkuchenAsync());
        var abendessen = await MahlzeitAsync(Dienstag, "Abendessen");
        var liste = (await EinkaufslisteAsync(_s)).GetRawText();
        using (var essensplan = await _s.Miriam.GetAsync($"{KuechePfad(_s.X)}/essensplan", Abbruch))
        {
            Assert.False((await JsonAsync(essensplan)).GetProperty("tage")[1].GetProperty("mahlzeiten")[2].GetProperty("restetag").GetBoolean());
        }

        using var antwort = await RestetagAsync(abendessen, "false", "true", _s.Tobias);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["restetag", "version"], Namen(json));
        Assert.Equal("true", json.GetProperty("restetag").GetString());
        var version = json.GetProperty("version").GetUInt32();
        Assert.NotEqual(0u, version);
        var mahlzeit = Mahlzeit(await TagesansichtAsync(Iso(Dienstag)), "Abendessen");
        Assert.Equal((true, version), (mahlzeit.GetProperty("restetag").GetBoolean(), mahlzeit.GetProperty("version").GetUInt32()));
        Assert.Single(mahlzeit.GetProperty("gerichte").EnumerateArray());
        Assert.False(Mahlzeit(await TagesansichtAsync(Iso(Dienstag)), "Frühstück").GetProperty("restetag").GetBoolean());
        using (var essensplan = await _s.Miriam.GetAsync($"{KuechePfad(_s.X)}/essensplan", Abbruch))
        {
            var dienstag = (await JsonAsync(essensplan)).GetProperty("tage")[1].GetProperty("mahlzeiten");
            Assert.Equal([false, false, true], dienstag.EnumerateArray().Select(m => m.GetProperty("restetag").GetBoolean()));
        }

        Assert.Equal(liste, (await EinkaufslisteAsync(_s)).GetRawText());
        using var konflikt = await RestetagAsync(abendessen, "false", "true");
        await KonfliktAsync(konflikt);
        Assert.Equal(1L, await StaendeAsync());

        // Rückgängig ist ein PATCH mit dem vorigen Wert.
        using var zurueck = await RestetagAsync(abendessen, "true", "false");
        Assert.Equal(HttpStatusCode.OK, zurueck.StatusCode);
        Assert.Equal("false", (await JsonAsync(zurueck)).GetProperty("restetag").GetString());
        Assert.False(Mahlzeit(await TagesansichtAsync(Iso(Dienstag)), "Abendessen").GetProperty("restetag").GetBoolean());
        Assert.Equal(liste, (await EinkaufslisteAsync(_s)).GetRawText());
    }

    [Fact]
    public async Task Restetag_Wert_ja_leer_oder_null_400_am_Feld_restetag_kein_oder_fremdes_Feld_400()
    {
        var abendessen = await MahlzeitAsync(Dienstag, "Abendessen");

        foreach (var wert in new[] { "ja", "", null, "1" })
        {
            using var antwort = await RestetagAsync(abendessen, "false", wert);
            await FeldfehlerAsync(antwort, "restetag");
        }

        using var fremdesFeld = await SendenAsync(
            _s.Miriam, HttpMethod.Patch, $"{KuechePfad(_s.X)}/mahlzeiten/{abendessen}", new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = "false", ["titel"] = "x" });
        await FeldfehlerAsync(fremdesFeld, "feld");
        Assert.Equal(0L, await StaendeAsync());
    }

    [Fact]
    public async Task Restetag_an_normalem_Programmpunkt_an_Mahlzeit_einer_anderen_Freizeit_oder_unbekannt_404()
    {
        using (var normal = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/programm/punkte", new { titel = "Geländespiel", tag = "2027-08-03", beginn = "14:00" }))
        {
            Assert.Equal(HttpStatusCode.Created, normal.StatusCode);
            using var gelaendespiel = await RestetagAsync(Id(await JsonAsync(normal)), "false", "true");
            Assert.Equal(HttpStatusCode.NotFound, gelaendespiel.StatusCode);
        }

        var inZ = await MahlzeitAsync(new DateOnly(2027, 9, 1), "Frühstück", _s.Z);
        using var fremd = await RestetagAsync(inZ, "false", "true");
        using var unbekannt = await RestetagAsync(Guid.CreateVersion7(), "false", "true");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (fremd.StatusCode, unbekannt.StatusCode));
        Assert.Equal(0L, await StaendeAsync());
    }

    [Fact]
    public async Task Gleichzeitig_zwei_erste_PATCH_restetag_genau_eine_Zeile_nie_500()
    {
        foreach (var titel in new[] { "Frühstück", "Mittagessen", "Abendessen" })
        {
            foreach (var tag in new[] { Dienstag, Dienstag.AddDays(1) })
            {
                var mahlzeit = await MahlzeitAsync(tag, titel);

                var antworten = await Task.WhenAll(RestetagAsync(mahlzeit, "false", "true", _s.Tobias), RestetagAsync(mahlzeit, "false", "true"));

                var status = antworten.Select(a => a.StatusCode).ToList();
                Assert.Contains(HttpStatusCode.OK, status);
                Assert.All(status, s => Assert.Contains(s, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
                foreach (var antwort in antworten)
                {
                    antwort.Dispose();
                }

                Assert.Equal(1L, await StaendeAsync("\"ProgrammpunktId\" = @p AND \"Restetag\"", ("p", mahlzeit)));
            }
        }

        Assert.Equal(6L, await StaendeAsync());
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufene_Freizeit_Restetag_409_freizeit_abgeschlossen_Tagesansicht_200()
    {
        var abendessen = await MahlzeitAsync(Dienstag, "Abendessen");
        using (var gesetzt = await RestetagAsync(abendessen, "false", "true"))
        {
            Assert.Equal(HttpStatusCode.OK, gesetzt.StatusCode);
        }

        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        using var antwort = await RestetagAsync(abendessen, "true", "false", _s.Tobias);

        await KonfliktAsync(antwort, Problemcodes.FreizeitAbgeschlossen);
        Assert.True(Mahlzeit(await TagesansichtAsync(Iso(Dienstag), _s.Tobias), "Abendessen").GetProperty("restetag").GetBoolean());
    }

    // ---------- Rechte und Schalter ----------

    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid mahlzeit, bool an) =>
    [
        () => TagesansichtAntwortAsync(Iso(Dienstag), client, freizeit),
        () => RestetagAsync(mahlzeit, an ? "false" : "true", an ? "true" : "false", client, freizeit),
    ];

    private async Task<List<HttpStatusCode>> StatusAsync(HttpClient client, Guid freizeit, Guid mahlzeit, bool an = true)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, mahlzeit, an))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_2xx_auf_Tagesansicht_und_Restetag()
    {
        var abendessen = await MahlzeitAsync(Dienstag, "Abendessen");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await StatusAsync(_s.Miriam, _s.X, abendessen, an: true));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await StatusAsync(_s.Tobias, _s.X, abendessen, an: false));
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_fremde_und_unbekannte_Freizeit_404_ohne_Sitzung_401_Schalter_aus_404()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var abendessen = await MahlzeitAsync(Dienstag, "Abendessen");
        var faelle = new (HttpClient Client, Guid Freizeit, HttpStatusCode Erwartet)[]
        {
            (_s.Andrea, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, fremde, HttpStatusCode.NotFound),
            (sara, fremde, HttpStatusCode.NotFound),
            (leitungZ, _s.X, HttpStatusCode.NotFound),
            (_s.Miriam, Guid.CreateVersion7(), HttpStatusCode.NotFound),
            (_s.Anonym, _s.X, HttpStatusCode.Unauthorized),
        };

        foreach (var (client, freizeit, erwartet) in faelle)
        {
            Assert.All(await StatusAsync(client, freizeit, abendessen), s => Assert.Equal(erwartet, s));
        }

        await VerpflegungAsync(_s, _s.X, an: false);
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.All(await StatusAsync(client, _s.X, abendessen), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.Equal(0L, await StaendeAsync());
    }

    // ---------- Lösch-Lauf ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Stand_Zeilen_der_abgelaufenen_Freizeit_die_andere_bleibt()
    {
        using (var inX = await RestetagAsync(await MahlzeitAsync(Dienstag, "Abendessen"), "false", "true"))
        {
            Assert.Equal(HttpStatusCode.OK, inX.StatusCode);
        }

        using (var inZ = await RestetagAsync(await MahlzeitAsync(new DateOnly(2027, 9, 1), "Abendessen", _s.Z), "false", "true", freizeit: _s.Z))
        {
            Assert.Equal(HttpStatusCode.OK, inZ.StatusCode);
        }

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "KuecheLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await StaendeAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await StaendeAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    // ---------- Datenauskunft ----------

    [Fact]
    public async Task Datenauskunft_nennt_den_Sonderkost_Abgleich_der_Kueche_ohne_lesende_Person()
    {
        // Tobias (volljährig, eigenes Konto, als Mitarbeitender bestätigt) ist vegetarisch; seine Freigabe enthält die Frage „Ernährung“.
        await AusfuehrenAsync(_s.Verbindung, "UPDATE personen.freigabe SET \"Fragen\" = '{ernaehrung}' WHERE \"PersonId\" = @p", ("p", _s.TobiasId));
        using (var antwort = await SendenAsync(
                   _s.Tobias, HttpMethod.Patch, $"/api/ich/freizeitpass/{_s.TobiasId}/antworten/ernaehrung",
                   new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["wert"] = "vegetarisch" }))
        {
            Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        }

        Assert.Equal(["vegetarisch 1"], Anzahlen(await TagesansichtAsync(), "sonderkost", "sonderkost"));
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);

        var auskunft = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!;

        Assert.Contains(
            Assert.Single(auskunft.Abschnitte, a => a.Titel == "Zugriffsprotokoll").Zeilen,
            z => z.Wert.EndsWith("Niemand persönlich (Abgleich): Sonderkost-Abgleich der Küche", StringComparison.Ordinal));
    }
}
