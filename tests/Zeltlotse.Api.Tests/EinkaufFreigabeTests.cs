using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.KuecheTestHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 4-3, Einkauf freigeben und abhaken (CAP-30, FR-50, FR-51, AD-8, AD-16, AD-17, AD-19, AD-20, AD-30) gegen echtes PostgreSQL: <c>freigegeben</c>
/// und <c>gekauft</c> an berechneten Posten und Hand-Posten (Autosave mit Konflikt und Feldfehler), die Tages-Freigabe (idempotent, zurücknehmen, ohne Tag,
/// Feldfehler), die Seite der Einkaufenden (nur Freigegebenes, ohne Abgleich und ohne Protokoll), die Tausch-Vorschau ohne gekaufte Posten und das
/// Übernehmen ohne Stand, eine veraltete Vorschau, Gleichzeitigkeit, die Pflichtfälle der Freizeit-Route, der Schalter Verpflegung, die abgeschlossene
/// Freizeit und der Lösch-Lauf. Aufbau wie <see cref="EinkaufslisteTests"/>: Freizeit X (02.–13.08.2027, Leitung Miriam, Team Tobias, 2 Personen).
/// </summary>
public sealed class EinkaufFreigabeTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly DateOnly Dienstag = new(2027, 8, 3);
    private static readonly DateOnly Mittwoch = new(2027, 8, 4);

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await FreizeitpassSzenario.AufbauenAsync(postgres);
        await VerpflegungAsync(_s, _s.X);
        await VerpflegungAsync(_s, _s.Z);
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

    private async Task<Guid> GerichtAsync(DateOnly tag, string mahlzeit, Guid rezept, Guid? freizeit = null)
    {
        using var antwort = await PostAsync(_s.Miriam, $"{KuechePfad(freizeit ?? _s.X)}/gerichte", new { programmpunktId = await MahlzeitAsync(tag, mahlzeit, freizeit), rezeptId = rezept });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return Id(await JsonAsync(antwort));
    }

    /// <summary>Pfannkuchen (Basis 4) mit Mehl 400 g, Milch 0,75 l und Salz ohne Menge.</summary>
    private async Task<(Guid Rezept, Guid Mehl, Guid Milch)> PfannkuchenAsync(Guid? freizeit = null)
    {
        var rezept = Id(await RezeptAsync(_s, "Pfannkuchen", "4", freizeit));
        var mehl = Id(await ZutatAsync(_s, rezept, "Mehl", "400", "g", "gluten", freizeit));
        var milch = Id(await ZutatAsync(_s, rezept, "Milch", "0,75", "l", "milch", freizeit));
        await ZutatAsync(_s, rezept, "Salz", freizeit: freizeit);
        return (rezept, mehl, milch);
    }

    private async Task<Guid> ChiliAsync()
    {
        var chili = Id(await RezeptAsync(_s, "Chili sin Carne", "4"));
        await ZutatAsync(_s, chili, "Kidneybohnen", "500", "g");
        await ZutatAsync(_s, chili, "Zwiebeln", "2", "Stück");
        return chili;
    }

    private Task<HttpResponseMessage> StandAsync(Guid gericht, Guid zutat, string feld, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(
            client ?? _s.Miriam, HttpMethod.Patch, $"{KuechePfad(freizeit ?? _s.X)}/einkaufsliste/gerichte/{gericht}/zutaten/{zutat}",
            new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = alterWert, [feld] = wert });

    private async Task StandOkAsync(Guid gericht, Guid zutat, string feld, string alterWert, string wert)
    {
        using var antwort = await StandAsync(gericht, zutat, feld, alterWert, wert);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    private async Task<Guid> PostenNeuAsync(string bezeichnung, DateOnly? tag = null, Guid? freizeit = null)
    {
        using var antwort = await PostAsync(_s.Miriam, $"{KuechePfad(freizeit ?? _s.X)}/einkaufsliste/posten", new { bezeichnung, tag = tag is { } t ? Iso(t) : null });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("postenId").GetGuid();
    }

    private Task<HttpResponseMessage> PostenPatchAsync(Guid posten, string feld, string? alterWert, string? wert, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(
            client ?? _s.Miriam, HttpMethod.Patch, $"{KuechePfad(freizeit ?? _s.X)}/einkaufsliste/posten/{posten}",
            new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = alterWert, [feld] = wert });

    private Task<HttpResponseMessage> FreigabeAsync(object koerper, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, $"{KuechePfad(freizeit ?? _s.X)}/einkaufsliste/freigabe", koerper);

    private async Task TagFreigebenAsync(DateOnly? tag, bool freigegeben)
    {
        using var antwort = await FreigabeAsync(new { tag = tag is { } t ? Iso(t) : null, freigegeben });
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    private async Task<JsonElement> EinkaufAsync(HttpClient? client = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync($"{KuechePfad(_s.X)}/einkauf", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    /// <summary>Alle Posten über alle Tage: „Tag Bezeichnung[ F][ G]“ (F = freigegeben, G = gekauft); ohne Tag „-“.</summary>
    private static List<string> Stand(JsonElement liste) =>
        [.. liste.GetProperty("tage").EnumerateArray().SelectMany(t => t.GetProperty("posten").EnumerateArray().Select(p =>
            $"{t.GetProperty("tag").GetString() ?? "-"} {p.GetProperty("bezeichnung").GetString()}"
            + (p.GetProperty("freigegeben").GetBoolean() ? " F" : "") + (p.GetProperty("gekauft").GetBoolean() ? " G" : "")))];

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
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM kueche.posten_stand WHERE {bedingung}", parameter);

    private Task<long> GerichtVersionAsync(Guid gericht) =>
        WertAsync<long>(_s.Verbindung, "SELECT xmin::text::bigint FROM kueche.gericht WHERE \"Id\" = @g", ("g", gericht));

    // ---------- Posten freigeben und abhaken ----------

    [Fact]
    public async Task Posten_freigeben_200_Liste_und_Einkauf_zeigen_ihn_freigegeben_anderer_Altwert_409()
    {
        var (pfannkuchen, mehl, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        Assert.Empty((await EinkaufAsync()).GetProperty("tage").EnumerateArray());

        using var antwort = await StandAsync(gericht, mehl, "freigegeben", "false", "true", _s.Tobias);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["freigegeben", "version"], Namen(json));
        Assert.Equal("true", json.GetProperty("freigegeben").GetString());
        Assert.NotEqual(0u, json.GetProperty("version").GetUInt32());
        var liste = await EinkaufslisteAsync(_s);
        Assert.Equal(["2027-08-03 Mehl F", "2027-08-03 Milch", "2027-08-03 Salz"], Stand(liste));
        Assert.Contains("gekauft", Namen(liste.GetProperty("tage")[0].GetProperty("posten")[0]));
        var einkauf = await EinkaufAsync(_s.Tobias);
        Assert.Equal(["tage"], Namen(einkauf));
        var tag = Assert.Single(einkauf.GetProperty("tage").EnumerateArray());
        Assert.Equal(["ersatzportionen", "posten", "tag"], Namen(tag));
        Assert.Equal(["2027-08-03 Mehl F"], Stand(einkauf));
        Assert.Equal(["Mehl 200 g Pfannkuchen Frühstück"], Posten(einkauf));

        using var konflikt = await StandAsync(gericht, mehl, "freigegeben", "false", "true");
        await KonfliktAsync(konflikt);
        Assert.Equal(1L, await StaendeAsync());
    }

    [Fact]
    public async Task Abhaken_gekauft_true_Einkauf_zeigt_ihn_gekauft_Wert_ja_oder_leer_400_am_Feld()
    {
        var (pfannkuchen, mehl, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await StandOkAsync(gericht, mehl, "freigegeben", "false", "true");

        using var gekauft = await StandAsync(gericht, mehl, "gekauft", "false", "true", _s.Tobias);

        Assert.Equal(HttpStatusCode.OK, gekauft.StatusCode);
        Assert.Equal("true", (await JsonAsync(gekauft)).GetProperty("gekauft").GetString());
        Assert.Equal(["2027-08-03 Mehl F G"], Stand(await EinkaufAsync()));
        foreach (var (feld, wert) in new[] { ("gekauft", "ja"), ("gekauft", ""), ("freigegeben", "1"), ("freigegeben", null) })
        {
            using var antwort = await StandAsync(gericht, mehl, feld, "false", wert);
            await FeldfehlerAsync(antwort, feld);
        }

        // Rückgängig ist ein PATCH mit dem vorigen Wert; die Felder sind unabhängig.
        await StandOkAsync(gericht, mehl, "gekauft", "true", "false");
        await StandOkAsync(gericht, mehl, "gekauft", "false", "true");
        await StandOkAsync(gericht, mehl, "freigegeben", "true", "false");
        Assert.Equal("2027-08-03 Mehl G", Stand(await EinkaufslisteAsync(_s))[0]);
        Assert.Empty((await EinkaufAsync()).GetProperty("tage").EnumerateArray());
    }

    [Fact]
    public async Task Hand_Posten_freigeben_und_abhaken_wie_berechnete_Konflikt_409_ungueltig_400_fremder_Posten_404()
    {
        var aepfel = await PostenNeuAsync("Äpfel", Dienstag);
        var inZ = await PostenNeuAsync("Brot", freizeit: _s.Z);

        using var freigegeben = await PostenPatchAsync(aepfel, "freigegeben", "false", "true", _s.Tobias);
        using var gekauft = await PostenPatchAsync(aepfel, "gekauft", "false", "true");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (freigegeben.StatusCode, gekauft.StatusCode));
        var json = await JsonAsync(gekauft);
        Assert.Equal(["gekauft", "version"], Namen(json));
        Assert.Equal("true", json.GetProperty("gekauft").GetString());
        Assert.Equal(["2027-08-03 Äpfel F G"], Stand(await EinkaufAsync()));
        Assert.Equal(["2027-08-03 Äpfel F G"], Stand(await EinkaufslisteAsync(_s)));
        using var konflikt = await PostenPatchAsync(aepfel, "gekauft", "false", "true");
        await KonfliktAsync(konflikt);
        using var ungueltig = await PostenPatchAsync(aepfel, "freigegeben", "true", "ja");
        await FeldfehlerAsync(ungueltig, "freigegeben");
        using var fremd = await PostenPatchAsync(inZ, "gekauft", "false", "true");
        Assert.Equal(HttpStatusCode.NotFound, fremd.StatusCode);
    }

    [Fact]
    public async Task Berechneter_Posten_eines_anderen_Rezepts_oder_einer_anderen_Freizeit_404()
    {
        var (pfannkuchen, _, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var chili = await ChiliAsync();
        var bohnen = await WertAsync<Guid>(_s.Verbindung, "SELECT \"Id\" FROM kueche.zutat WHERE \"RezeptId\" = @r ORDER BY \"Id\" LIMIT 1", ("r", chili));
        var (inZ, mehlInZ, _) = await PfannkuchenAsync(_s.Z);
        var gerichtInZ = await GerichtAsync(new DateOnly(2027, 9, 1), "Frühstück", inZ, _s.Z);

        using var fremdeZutat = await StandAsync(gericht, bohnen, "gekauft", "false", "true");
        using var fremdesGericht = await StandAsync(gerichtInZ, mehlInZ, "gekauft", "false", "true");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (fremdeZutat.StatusCode, fremdesGericht.StatusCode));
        Assert.Equal(0L, await StaendeAsync());
    }

    // ---------- Tages-Freigabe ----------

    [Fact]
    public async Task Tag_freigeben_setzt_alle_Posten_des_Tags_zweiter_Aufruf_aendert_nichts_gekauft_bleibt()
    {
        var (pfannkuchen, _, milch) = await PfannkuchenAsync();
        var chili = await ChiliAsync();
        var fruehstueck = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var mittag = await GerichtAsync(Dienstag, "Mittagessen", chili);
        await GerichtAsync(Mittwoch, "Frühstück", pfannkuchen);
        await PostenNeuAsync("Äpfel", Dienstag);
        await PostenNeuAsync("Kaffee");
        await StandOkAsync(fruehstueck, milch, "gekauft", "false", "true");

        await TagFreigebenAsync(Dienstag, true);

        Assert.Equal(
            [
                "2027-08-03 Mehl F", "2027-08-03 Milch F G", "2027-08-03 Salz F", "2027-08-03 Kidneybohnen F", "2027-08-03 Zwiebeln F", "2027-08-03 Äpfel F",
                "2027-08-04 Mehl", "2027-08-04 Milch", "2027-08-04 Salz", "- Kaffee",
            ],
            Stand(await EinkaufslisteAsync(_s)));
        Assert.Equal(5L, await StaendeAsync());
        var (versionFrueh, versionMittag) = (await GerichtVersionAsync(fruehstueck), await GerichtVersionAsync(mittag));

        // Idempotent: nichts ändert sich, auch nicht die Version der Gerichte.
        await TagFreigebenAsync(Dienstag, true);
        Assert.Equal(5L, await StaendeAsync());
        Assert.Equal((versionFrueh, versionMittag), (await GerichtVersionAsync(fruehstueck), await GerichtVersionAsync(mittag)));
        Assert.Equal(6, Stand(await EinkaufAsync()).Count);
    }

    [Fact]
    public async Task Tag_ausserhalb_der_Freizeit_oder_kein_Datum_400_am_Feld_tag_ohne_freigegeben_400()
    {
        foreach (var (koerper, feld) in new (object, string)[]
                 {
                     (new { tag = "2027-09-01", freigegeben = true }, "tag"),
                     (new { tag = "2027-08-01", freigegeben = true }, "tag"),
                     (new { tag = "morgen", freigegeben = true }, "tag"),
                     (new { tag = "2027-08-03" }, "freigegeben"),
                     (new { tag = (string?)null, freigegeben = (bool?)null }, "freigegeben"),
                 })
        {
            using var antwort = await FreigabeAsync(koerper);
            await FeldfehlerAsync(antwort, feld);
        }
    }

    [Fact]
    public async Task Freigabe_zuruecknehmen_Einkauf_ohne_diesen_Tag_gekaufte_bleiben_gekauft()
    {
        var (pfannkuchen, mehl, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await PostenNeuAsync("Äpfel", Dienstag);
        await TagFreigebenAsync(Dienstag, true);
        await StandOkAsync(gericht, mehl, "gekauft", "false", "true");

        await TagFreigebenAsync(Dienstag, false);

        Assert.Empty((await EinkaufAsync()).GetProperty("tage").EnumerateArray());
        Assert.Equal(["2027-08-03 Mehl G", "2027-08-03 Milch", "2027-08-03 Salz", "2027-08-03 Äpfel"], Stand(await EinkaufslisteAsync(_s)));
    }

    [Fact]
    public async Task Ohne_Tag_gibt_nur_die_Hand_Posten_ohne_Tag_frei()
    {
        var (pfannkuchen, _, _) = await PfannkuchenAsync();
        await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await PostenNeuAsync("Äpfel", Dienstag);
        await PostenNeuAsync("Kaffee");

        await TagFreigebenAsync(null, true);

        Assert.Equal(["- Kaffee F"], Stand(await EinkaufAsync()));
        Assert.Equal(0L, await StaendeAsync());
        var einkauf = await EinkaufAsync();
        Assert.Equal(JsonValueKind.Null, einkauf.GetProperty("tage")[0].GetProperty("tag").ValueKind);
    }

    // ---------- Seite der Einkaufenden ----------

    [Fact]
    public async Task Einkauf_ohne_Freigabe_hat_keine_Tage_und_liest_keine_Allergien_kein_Eintrag_im_Zugriffsprotokoll()
    {
        await _s.AntwortAsync(_s.Mia, "allergien", "gluten");
        await _s.BestaetigenAsync(await _s.KindAnmeldenAsync(_s.Mia));
        await _s.ZustellenAsync();
        var (pfannkuchen, _, _) = await PfannkuchenAsync();
        await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        Task<long> ProtokollAsync() => WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll");
        var vorher = await ProtokollAsync();

        Assert.Empty((await EinkaufAsync()).GetProperty("tage").EnumerateArray());
        await TagFreigebenAsync(Dienstag, true);
        var einkauf = await EinkaufAsync(_s.Tobias);

        Assert.Equal(3, Stand(einkauf).Count);
        Assert.Empty(einkauf.GetProperty("tage")[0].GetProperty("ersatzportionen").EnumerateArray());
        Assert.Equal(vorher, await ProtokollAsync());
        // Gegenprobe: Die Einkaufsliste der Küche gleicht ab und protokolliert.
        Assert.Single((await EinkaufslisteAsync(_s)).GetProperty("tage")[0].GetProperty("ersatzportionen").EnumerateArray());
        Assert.Equal(vorher + 1, await ProtokollAsync());
    }

    // ---------- Tausch ----------

    [Fact]
    public async Task Tausch_Vorschau_ohne_gekaufte_Posten_schonGekauft_1_Uebernehmen_entfernt_den_Stand_des_Gerichts()
    {
        var (pfannkuchen, mehl, _) = await PfannkuchenAsync();
        var chili = await ChiliAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await TagFreigebenAsync(Dienstag, true);
        await StandOkAsync(gericht, mehl, "gekauft", "false", "true");

        using var antwort = await _s.Tobias.GetAsync($"{KuechePfad(_s.X)}/gerichte/{gericht}/tausch?rezeptId={chili}", Abbruch);

        var vorschau = await JsonAsync(antwort);
        Assert.Equal(["gerichtId", "nachher", "neuesRezept", "rezept", "schonGekauft", "version", "vorher"], Namen(vorschau));
        Assert.Equal(["Milch", "Salz"], vorschau.GetProperty("vorher").EnumerateArray().Select(z => z.GetProperty("bezeichnung").GetString()));
        Assert.Equal(["Kidneybohnen", "Zwiebeln"], vorschau.GetProperty("nachher").EnumerateArray().Select(z => z.GetProperty("bezeichnung").GetString()));
        Assert.Equal(1, vorschau.GetProperty("schonGekauft").GetInt32());

        using var gleich = await _s.Miriam.GetAsync($"{KuechePfad(_s.X)}/gerichte/{gericht}/tausch?rezeptId={pfannkuchen}", Abbruch);
        var ohneTausch = await JsonAsync(gleich);
        Assert.Equal(["Milch", "Salz"], ohneTausch.GetProperty("nachher").EnumerateArray().Select(z => z.GetProperty("bezeichnung").GetString()));
        Assert.Equal(1, ohneTausch.GetProperty("schonGekauft").GetInt32());

        // Gleiches Rezept: alles bleibt, wie es ist.
        using (var gleichTauschen = await PostAsync(_s.Miriam, $"{KuechePfad(_s.X)}/gerichte/{gericht}/tauschen", new { version = ohneTausch.GetProperty("version").GetUInt32(), rezeptId = pfannkuchen }))
        {
            Assert.Equal(HttpStatusCode.OK, gleichTauschen.StatusCode);
        }

        Assert.Equal(3L, await StaendeAsync());

        using var tauschen = await PostAsync(_s.Miriam, $"{KuechePfad(_s.X)}/gerichte/{gericht}/tauschen", new { version = vorschau.GetProperty("version").GetUInt32(), rezeptId = chili });

        Assert.Equal(HttpStatusCode.OK, tauschen.StatusCode);
        Assert.Equal(["2027-08-03 Kidneybohnen", "2027-08-03 Zwiebeln"], Stand(await EinkaufslisteAsync(_s)));
        Assert.Equal(0L, await StaendeAsync("\"GerichtId\" = @g", ("g", gericht)));
        Assert.Empty((await EinkaufAsync()).GetProperty("tage").EnumerateArray());
    }

    [Fact]
    public async Task Vorschau_geladen_danach_abgehakt_oder_Tag_freigegeben_Tauschen_409_konflikt()
    {
        var (pfannkuchen, _, milch) = await PfannkuchenAsync();
        var chili = await ChiliAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);

        async Task<uint> VorschauVersionAsync()
        {
            using var antwort = await _s.Miriam.GetAsync($"{KuechePfad(_s.X)}/gerichte/{gericht}/tausch?rezeptId={chili}", Abbruch);
            return (await JsonAsync(antwort)).GetProperty("version").GetUInt32();
        }

        Task<HttpResponseMessage> TauschenAsync(uint version) =>
            PostAsync(_s.Miriam, $"{KuechePfad(_s.X)}/gerichte/{gericht}/tauschen", new { version, rezeptId = chili });

        var version = await VorschauVersionAsync();
        await StandOkAsync(gericht, milch, "gekauft", "false", "true");
        using (var veraltet = await TauschenAsync(version))
        {
            await KonfliktAsync(veraltet);
        }

        version = await VorschauVersionAsync();
        await TagFreigebenAsync(Dienstag, true);
        using (var veraltet = await TauschenAsync(version))
        {
            await KonfliktAsync(veraltet);
        }

        Assert.Equal(pfannkuchen, await WertAsync<Guid>(_s.Verbindung, "SELECT \"RezeptId\" FROM kueche.gericht WHERE \"Id\" = @g", ("g", gericht)));
        using var aktuell = await TauschenAsync(await VorschauVersionAsync());
        Assert.Equal(HttpStatusCode.OK, aktuell.StatusCode);
    }

    // ---------- Cascade ----------

    [Fact]
    public async Task Zutat_oder_Rezept_mit_Stand_loeschen_204_der_Stand_verschwindet_mit()
    {
        var (pfannkuchen, mehl, milch) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await StandOkAsync(gericht, mehl, "gekauft", "false", "true");
        await StandOkAsync(gericht, milch, "gekauft", "false", "true");

        using var zutatWeg = await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{KuechePfad(_s.X)}/rezepte/{pfannkuchen}/zutaten/{mehl}");

        Assert.Equal(HttpStatusCode.NoContent, zutatWeg.StatusCode);
        Assert.Equal(0L, await StaendeAsync("\"ZutatId\" = @z", ("z", mehl)));
        Assert.Equal(1L, await StaendeAsync());

        using var rezeptWeg = await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{KuechePfad(_s.X)}/rezepte/{pfannkuchen}");

        Assert.Equal(HttpStatusCode.NoContent, rezeptWeg.StatusCode);
        Assert.Equal(0L, await StaendeAsync());
    }

    // ---------- Gleichzeitig ----------

    [Fact]
    public async Task Gleichzeitig_zwei_PATCH_gekauft_auf_neuen_Posten_und_Tages_Freigabe_neben_PATCH_je_genau_eine_Zeile_nie_500()
    {
        var (pfannkuchen, mehl, milch) = await PfannkuchenAsync();
        for (var runde = 0; runde < 4; runde++)
        {
            var gericht = await GerichtAsync(Dienstag, runde % 2 == 0 ? "Frühstück" : "Abendessen", pfannkuchen);

            var antworten = await Task.WhenAll(
                StandAsync(gericht, mehl, "gekauft", "false", "true", _s.Tobias),
                StandAsync(gericht, mehl, "gekauft", "false", "true"),
                FreigabeAsync(new { tag = Iso(Dienstag), freigegeben = runde % 2 == 0 }),
                StandAsync(gericht, milch, "freigegeben", "false", "true"));

            var status = antworten.Select(a => a.StatusCode).ToList();
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], status.Take(2).Order());
            Assert.Equal(HttpStatusCode.NoContent, status[2]);
            Assert.Contains(status[3], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }

            Assert.Equal(1L, await StaendeAsync("\"GerichtId\" = @g AND \"ZutatId\" = @z", ("g", gericht), ("z", mehl)));
            Assert.Equal(1L, await StaendeAsync("\"GerichtId\" = @g AND \"ZutatId\" = @z AND \"Gekauft\"", ("g", gericht), ("z", mehl)));
            Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM (SELECT 1 FROM kueche.posten_stand GROUP BY \"GerichtId\", \"ZutatId\" HAVING count(*) > 1) d"));
        }
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Gelaufene_Freizeit_Freigeben_und_Abhaken_409_freizeit_abgeschlossen_Einkauf_200()
    {
        var (pfannkuchen, mehl, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var aepfel = await PostenNeuAsync("Äpfel", Dienstag);
        await TagFreigebenAsync(Dienstag, true);
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        foreach (var antwort in new[]
                 {
                     await StandAsync(gericht, mehl, "gekauft", "false", "true"),
                     await StandAsync(gericht, mehl, "freigegeben", "true", "false", _s.Tobias),
                     await PostenPatchAsync(aepfel, "gekauft", "false", "true"),
                     await FreigabeAsync(new { tag = Iso(Dienstag), freigegeben = false }),
                 })
        {
            await KonfliktAsync(antwort, Problemcodes.FreizeitAbgeschlossen);
            antwort.Dispose();
        }

        Assert.Equal(4, Stand(await EinkaufAsync(_s.Tobias)).Count);
    }

    // ---------- Rechte und Schalter ----------

    private IEnumerable<Func<Task<HttpResponseMessage>>> AlleRouten(HttpClient client, Guid freizeit, Guid gericht, Guid zutat, Guid posten, bool an) =>
    [
        () => client.GetAsync($"{KuechePfad(freizeit)}/einkauf", Abbruch),
        () => FreigabeAsync(new { tag = Iso(Dienstag), freigegeben = an }, client, freizeit),
        () => StandAsync(gericht, zutat, "gekauft", an ? "false" : "true", an ? "true" : "false", client, freizeit),
        () => PostenPatchAsync(posten, "gekauft", an ? "false" : "true", an ? "true" : "false", client, freizeit),
    ];

    private async Task<List<HttpStatusCode>> StatusAsync(HttpClient client, Guid freizeit, Guid gericht, Guid zutat, Guid posten, bool an = true)
    {
        var status = new List<HttpStatusCode>();
        foreach (var aufruf in AlleRouten(client, freizeit, gericht, zutat, posten, an))
        {
            using var antwort = await aufruf();
            status.Add(antwort.StatusCode);
        }

        return status;
    }

    [Fact]
    public async Task Rechte_Leitung_und_Team_2xx_auf_Einkauf_Tages_Freigabe_und_den_neuen_PATCH_Feldern()
    {
        var (pfannkuchen, mehl, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var posten = await PostenNeuAsync("Äpfel", Dienstag);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.OK, HttpStatusCode.OK], await StatusAsync(_s.Miriam, _s.X, gericht, mehl, posten, an: true));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.OK, HttpStatusCode.OK], await StatusAsync(_s.Tobias, _s.X, gericht, mehl, posten, an: false));
    }

    [Fact]
    public async Task Rechte_Org_Admin_ohne_Rolle_fremde_Organisation_fremde_und_unbekannte_Freizeit_404_ohne_Sitzung_401_Schalter_aus_404()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        using var leitungZ = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        var (pfannkuchen, mehl, _) = await PfannkuchenAsync();
        var gericht = await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        var posten = await PostenNeuAsync("Äpfel", Dienstag);
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
            Assert.All(await StatusAsync(client, freizeit, gericht, mehl, posten), s => Assert.Equal(erwartet, s));
        }

        await VerpflegungAsync(_s, _s.X, an: false);
        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            Assert.All(await StatusAsync(client, _s.X, gericht, mehl, posten), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.Equal(0L, await StaendeAsync());
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM kueche.einkaufsposten WHERE \"Freigegeben\" OR \"Gekauft\""));
    }

    // ---------- Lösch-Lauf ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_den_Stand_der_Posten_der_abgelaufenen_Freizeit_die_andere_bleibt()
    {
        var (pfannkuchen, _, _) = await PfannkuchenAsync();
        await GerichtAsync(Dienstag, "Frühstück", pfannkuchen);
        await TagFreigebenAsync(Dienstag, true);
        var (inZ, mehlInZ, _) = await PfannkuchenAsync(_s.Z);
        var gerichtInZ = await GerichtAsync(new DateOnly(2027, 9, 1), "Frühstück", inZ, _s.Z);
        using (var standInZ = await StandAsync(gerichtInZ, mehlInZ, "gekauft", "false", "true", freizeit: _s.Z))
        {
            Assert.Equal(HttpStatusCode.OK, standInZ.StatusCode);
        }

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "KuecheLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await StaendeAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await StaendeAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }
}
