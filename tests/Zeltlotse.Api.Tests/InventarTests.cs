using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;
using OrgRolle = Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 3-3, Inventar und Reservierung (CAP-21, FR-37, AD-6, AD-19, AD-28, AD-30) gegen echtes PostgreSQL: das Inventar der Organisation
/// (anlegen, Autosave mit Konfliktregel auch gleichzeitig, löschen, nicht mit Reservierungen), das Inventar der Freizeit (nur eigene
/// Reservierungen), reservieren mit und ohne Zeitraum, Konflikthinweise zwischen zwei Freizeiten derselben Organisation mit Grenztag, aufheben,
/// abgeschlossene Freizeit, das Material am Programmpunkt über das Ereignis und die Pflichtfälle beider Routenarten. Freizeit X (Leitung Miriam,
/// Team Tobias) geht vom 02. bis 13.08.2027; Freizeit Y „Kinderbibeltage 2027“ derselben Organisation vom 10. bis 12.08.2027.
/// </summary>
public sealed class InventarTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Kinderbibeltage = "Kinderbibeltage 2027";
    private const string Tim = "tim.kroeger@example.org";

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string OrgPfad(Guid org) => $"/api/organisationen/{org}/inventar";

    private static string FreizeitPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/inventar";

    private static string ReservierenPfad(Guid freizeit, Guid artikel) => $"{FreizeitPfad(freizeit)}/{artikel}/reservierungen";

    private static string AufhebenPfad(Guid freizeit, Guid reservierung) => $"{FreizeitPfad(freizeit)}/reservierungen/{reservierung}";

    private Task<HttpResponseMessage> AnlegenAsync(object koerper, HttpClient? client = null, Guid? org = null) =>
        PostAsync(client ?? _s.Andrea, OrgPfad(org ?? _s.A), koerper);

    /// <summary>Andrea legt einen Artikel an; liefert die Antwort.</summary>
    private async Task<JsonElement> NeuAsync(string bezeichnung, string? menge = null)
    {
        using var antwort = await AnlegenAsync(new { bezeichnung, menge });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private async Task<List<JsonElement>> OrgInventarAsync(HttpClient? client = null, Guid? org = null)
    {
        using var antwort = await (client ?? _s.Andrea).GetAsync(OrgPfad(org ?? _s.A), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["artikel"], Namen(json));
        return [.. json.GetProperty("artikel").EnumerateArray()];
    }

    private async Task<JsonElement> FreizeitInventarAsync(HttpClient? client = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(FreizeitPfad(freizeit ?? _s.X), Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private static List<JsonElement> Artikel(JsonElement inventar) => [.. inventar.GetProperty("artikel").EnumerateArray()];

    private static List<JsonElement> Reservierungen(JsonElement artikel) => [.. artikel.GetProperty("reservierungen").EnumerateArray()];

    private Task<HttpResponseMessage> ReservierenAsync(Guid artikel, object? koerper = null, HttpClient? client = null, Guid? freizeit = null) =>
        PostAsync(client ?? _s.Miriam, ReservierenPfad(freizeit ?? _s.X, artikel), koerper ?? new { });

    /// <summary>Reserviert und liefert die Antwort.</summary>
    private async Task<JsonElement> ReserviertAsync(Guid artikel, string? von = null, string? bis = null, HttpClient? client = null, Guid? freizeit = null, Guid? material = null)
    {
        using var antwort = await ReservierenAsync(artikel, new { von, bis, materialId = material }, client, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    private Task<HttpResponseMessage> AufhebenAsync(Guid reservierung, HttpClient? client = null, Guid? freizeit = null) =>
        SendenAsync(client ?? _s.Miriam, HttpMethod.Delete, AufhebenPfad(freizeit ?? _s.X, reservierung));

    private Task<HttpResponseMessage> PatchAsync(Guid artikel, object koerper, HttpClient? client = null, Guid? org = null) =>
        SendenAsync(client ?? _s.Andrea, HttpMethod.Patch, $"{OrgPfad(org ?? _s.A)}/{artikel}", koerper);

    private Task<HttpResponseMessage> LoeschenAsync(Guid artikel, HttpClient? client = null, Guid? org = null) =>
        SendenAsync(client ?? _s.Andrea, HttpMethod.Delete, $"{OrgPfad(org ?? _s.A)}/{artikel}");

    private static Dictionary<string, object?> Koerper(uint version, string? alterWert, string feld, string? wert) =>
        new() { ["version"] = version, ["alterWert"] = alterWert, [feld] = wert };

    private Task<long> AnzahlAsync(string tabelle) => WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM inventar.{tabelle}");

    /// <summary>Freizeit Y „Kinderbibeltage 2027“ in Organisation A mit Tim als Leitung.</summary>
    private async Task<(Guid Y, HttpClient Tim)> KinderbibeltageAsync()
    {
        var y = await FreizeitHelfer.AnlegenAsync(_s.Andrea, _s.A, Kinderbibeltage, new DateOnly(2027, 8, 10), new DateOnly(2027, 8, 12));
        return (y, await FreizeitHelfer.LeitungAsync(_s.Factory, _s.Andrea, _s.A, y, Tim));
    }

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static string? Text(JsonElement json, string feld) => json.GetProperty(feld).GetString();

    private static List<string?> Konflikte(JsonElement reservierung) => [.. reservierung.GetProperty("konflikte").EnumerateArray().Select(k => k.GetString())];

    // ---------- Org-Inventar ----------

    [Fact]
    public async Task Org_Inventar_leer_dann_Artikel_201_getrimmt_mit_Version_nach_Bezeichnung_und_ohne_weitere_Eingabe_in_der_Freizeit()
    {
        Assert.Empty(await OrgInventarAsync());

        using var antwort = await AnlegenAsync(new { bezeichnung = "  Fahnen ", menge = " 8 Stück " });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(OrgPfad(_s.A), antwort.Headers.Location?.OriginalString);
        var fahnen = await JsonAsync(antwort);
        Assert.Equal(["bezeichnung", "id", "menge", "reservierungen", "version"], Namen(fahnen));
        Assert.Equal(("Fahnen", "8 Stück"), (Text(fahnen, "bezeichnung"), Text(fahnen, "menge")));
        Assert.NotEqual(0u, fahnen.GetProperty("version").GetUInt32());
        Assert.Empty(Reservierungen(fahnen));
        var grosszelt = await NeuAsync("Großzelt", "  ");
        Assert.Equal(JsonValueKind.Null, grosszelt.GetProperty("menge").ValueKind);

        Assert.Equal(["Fahnen", "Großzelt"], (await OrgInventarAsync()).Select(a => Text(a, "bezeichnung")));
        var inX = await FreizeitInventarAsync(_s.Tobias);
        Assert.Equal(["artikel", "ersterTag", "letzterTag"], Namen(inX));
        Assert.Equal(("2027-08-02", "2027-08-13"), (Text(inX, "ersterTag"), Text(inX, "letzterTag")));
        Assert.Equal([Id(fahnen), Id(grosszelt)], Artikel(inX).Select(Id));
    }

    [Fact]
    public async Task Anlegen_Feldfehler_Bezeichnung_leer_oder_zu_lang_Menge_zu_lang_400()
    {
        var faelle = new (object Koerper, string Feld)[]
        {
            (new { bezeichnung = "  " }, "bezeichnung"),
            (new { menge = "2" }, "bezeichnung"),
            (new { bezeichnung = new string('a', 121) }, "bezeichnung"),
            (new { bezeichnung = "Fahnen", menge = new string('m', 41) }, "menge"),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await AnlegenAsync(koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        Assert.Equal(0L, await AnzahlAsync("inventar_artikel"));
    }

    [Fact]
    public async Task Menge_aendern_200_mit_Version_anderer_Altwert_409_konflikt_anderes_Feld_angewendet_Feldfehler_400()
    {
        var artikel = await NeuAsync("Kanus", "6 Stück");
        var alteVersion = artikel.GetProperty("version").GetUInt32();

        using var erste = await PatchAsync(Id(artikel), Koerper(alteVersion, "6 Stück", "menge", " 5 Stück "));
        Assert.Equal(HttpStatusCode.OK, erste.StatusCode);
        var json = await JsonAsync(erste);
        Assert.Equal(["menge", "version"], Namen(json));
        Assert.Equal("5 Stück", Text(json, "menge"));
        Assert.NotEqual(alteVersion, json.GetProperty("version").GetUInt32());

        using var konflikt = await PatchAsync(Id(artikel), Koerper(alteVersion, "6 Stück", "menge", "4 Stück"));
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        using var anderesFeld = await PatchAsync(Id(artikel), Koerper(alteVersion, "Kanus", "bezeichnung", " Kanadier "));
        Assert.Equal(HttpStatusCode.OK, anderesFeld.StatusCode);

        foreach (var (koerper, feld) in new (object, string)[]
                 {
                     (Koerper(0, "Kanadier", "bezeichnung", " "), "bezeichnung"),
                     (Koerper(0, null, "menge", new string('m', 41)), "menge"),
                     (Koerper(0, null, "farbe", "rot"), "feld"),
                     (new Dictionary<string, object?> { ["alterWert"] = "Kanadier", ["bezeichnung"] = "Boote" }, "version"),
                 })
        {
            using var antwort = await PatchAsync(Id(artikel), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _), feld);
        }

        using var unbekannt = await PatchAsync(Guid.CreateVersion7(), Koerper(0, null, "menge", "1"));
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        var gespeichert = Assert.Single(await OrgInventarAsync());
        Assert.Equal(("Kanadier", "5 Stück"), (Text(gespeichert, "bezeichnung"), Text(gespeichert, "menge")));
    }

    [Fact]
    public async Task Gleichzeitige_Aenderungen_an_Bezeichnung_und_Menge_gelingen_beide()
    {
        var artikel = await NeuAsync("Kanus", "6 Stück");
        var version = artikel.GetProperty("version").GetUInt32();
        var (bezeichnung, menge) = ("Kanus", "6 Stück");

        for (var runde = 0; runde < 5; runde++)
        {
            var neueBezeichnung = $"Kanus {runde}";
            var neueMenge = $"{runde} Stück";
            var antworten = await Task.WhenAll(
                Task.Run(() => PatchAsync(Id(artikel), Koerper(version, bezeichnung, "bezeichnung", neueBezeichnung))),
                Task.Run(() => PatchAsync(Id(artikel), Koerper(version, menge, "menge", neueMenge))));
            foreach (var antwort in antworten)
            {
                using (antwort)
                {
                    Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
                }
            }

            (bezeichnung, menge) = (neueBezeichnung, neueMenge);
            var gespeichert = Assert.Single(await OrgInventarAsync());
            Assert.Equal((bezeichnung, menge), (Text(gespeichert, "bezeichnung"), Text(gespeichert, "menge")));
            version = gespeichert.GetProperty("version").GetUInt32();
        }
    }

    [Fact]
    public async Task Loeschen_204_mit_Reservierungen_409_konflikt_unbekannt_404()
    {
        var gaskocher = await NeuAsync("Gaskocher", "2 Stück");
        var grosszelt = await NeuAsync("Großzelt");
        await ReserviertAsync(Id(grosszelt));

        using var geloescht = await LoeschenAsync(Id(gaskocher));
        using var nochmal = await LoeschenAsync(Id(gaskocher));
        using var reserviert = await LoeschenAsync(Id(grosszelt));

        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nochmal.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reserviert.StatusCode);
        var problem = await ProblemAsync(reserviert);
        Assert.Equal((Problemcodes.Konflikt, "Noch reserviert."), (Code(problem), problem.GetProperty("title").GetString()));
        Assert.Equal(["Großzelt"], (await OrgInventarAsync()).Select(a => Text(a, "bezeichnung")));
    }

    // ---------- Reservieren ----------

    [Fact]
    public async Task Reservieren_ohne_Datum_gilt_der_Freizeitzeitraum_mit_Datum_der_Zeitraum_mehrere_je_Freizeit_erlaubt()
    {
        var fahnen = await NeuAsync("Fahnen", "8 Stück");

        using var antwort = await ReservierenAsync(Id(fahnen), new { }, _s.Tobias);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.Equal(FreizeitPfad(_s.X), antwort.Headers.Location?.OriginalString);
        var ganz = await JsonAsync(antwort);
        Assert.Equal(["bis", "freizeit", "freizeitId", "id", "konflikte", "materialId", "von"], Namen(ganz));
        Assert.Equal(
            ("2027-08-02", "2027-08-13", _s.X, FreizeitpassSzenario.Kapf, JsonValueKind.Null),
            (Text(ganz, "von"), Text(ganz, "bis"), ganz.GetProperty("freizeitId").GetGuid(), Text(ganz, "freizeit"), ganz.GetProperty("materialId").ValueKind));
        Assert.Empty(Konflikte(ganz));
        var teil = await ReserviertAsync(Id(fahnen), "2027-08-03", "2027-08-03");

        var inX = Assert.Single(Artikel(await FreizeitInventarAsync()));
        Assert.Equal([Id(ganz), Id(teil)], Reservierungen(inX).Select(Id));
        Assert.Equal(2L, await AnzahlAsync("reservierung"));
    }

    [Fact]
    public async Task Reservieren_nur_ein_Datum_oder_Von_nach_Bis_400_am_Feld_bis_Artikel_unbekannt_oder_fremd_404()
    {
        var fahnen = await NeuAsync("Fahnen");
        foreach (var koerper in new object[]
                 {
                     new { von = "2027-08-02" }, new { bis = "2027-08-13" }, new { von = "2027-08-13", bis = "2027-08-02" },
                 })
        {
            using var antwort = await ReservierenAsync(Id(fahnen), koerper);
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty("bis", out _), JsonSerializer.Serialize(koerper));
        }

        using var unbekannt = await ReservierenAsync(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        // Ein Artikel von Organisation B ist für Freizeit X (Organisation A) unbekannt, auch für Miriam als Leitung in beiden.
        using var inB = await AnlegenAsync(new { bezeichnung = "Kanus" }, _s.Lea, _s.B);
        var kanus = Id(await JsonAsync(inB));
        using var fremd = await ReservierenAsync(kanus);
        Assert.Equal(HttpStatusCode.NotFound, fremd.StatusCode);
        Assert.Equal(0L, await AnzahlAsync("reservierung"));
    }

    [Fact]
    public async Task Konflikt_zwischen_zwei_Freizeiten_nennt_den_Namen_der_anderen_in_Antwort_und_beiden_Inventaren_Grenztag_zaehlt()
    {
        var (y, tim) = await KinderbibeltageAsync();
        using var _ = tim;
        var grosszelt = await NeuAsync("Großzelt", "1 Stück");
        var zeltlager = await ReserviertAsync(Id(grosszelt), "2027-08-02", "2027-08-13");
        Assert.Empty(Konflikte(zeltlager));

        var kbt = await ReserviertAsync(Id(grosszelt), "2027-08-10", "2027-08-12", tim, y);
        var danach = await ReserviertAsync(Id(grosszelt), "2027-08-14", "2027-08-15", tim, y);
        var grenztag = await ReserviertAsync(Id(grosszelt), "2027-08-13", "2027-08-13", tim, y);

        Assert.Equal([FreizeitpassSzenario.Kapf], Konflikte(kbt));
        Assert.Empty(Konflikte(danach));
        Assert.Equal([FreizeitpassSzenario.Kapf], Konflikte(grenztag));
        var inX = Reservierungen(Assert.Single(Artikel(await FreizeitInventarAsync())));
        Assert.Equal([Kinderbibeltage], Konflikte(Assert.Single(inX)));
        var inY = Reservierungen(Assert.Single(Artikel(await FreizeitInventarAsync(tim, y))));
        Assert.Equal([Id(kbt), Id(grenztag), Id(danach)], inY.Select(Id));
        var org = Reservierungen(Assert.Single(await OrgInventarAsync()));
        Assert.Equal(
            [$"{FreizeitpassSzenario.Kapf} 2027-08-02 {Kinderbibeltage}", $"{Kinderbibeltage} 2027-08-10 {FreizeitpassSzenario.Kapf}",
             $"{Kinderbibeltage} 2027-08-13 {FreizeitpassSzenario.Kapf}", $"{Kinderbibeltage} 2027-08-14 -"],
            org.Select(r => $"{Text(r, "freizeit")} {Text(r, "von")} {(Konflikte(r) is [] ? "-" : string.Join(", ", Konflikte(r)))}"));
        Assert.Equal(4L, await AnzahlAsync("reservierung"));
    }

    // ---------- Aufheben ----------

    [Fact]
    public async Task Aufheben_204_unbekannt_oder_Reservierung_einer_anderen_Freizeit_404()
    {
        var (y, tim) = await KinderbibeltageAsync();
        using var _ = tim;
        var grosszelt = await NeuAsync("Großzelt");
        var inX = await ReserviertAsync(Id(grosszelt));
        var inY = await ReserviertAsync(Id(grosszelt), client: tim, freizeit: y);

        using var fremd = await AufhebenAsync(Id(inY));
        using var aufgehoben = await AufhebenAsync(Id(inX), _s.Tobias);
        using var nochmal = await AufhebenAsync(Id(inX));

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NoContent, HttpStatusCode.NotFound], [fremd.StatusCode, aufgehoben.StatusCode, nochmal.StatusCode]);
        Assert.Empty(Reservierungen(Assert.Single(Artikel(await FreizeitInventarAsync()))));
        Assert.Equal([Id(inY)], Reservierungen(Assert.Single(await OrgInventarAsync())).Select(Id));
    }

    [Fact]
    public async Task Gleichzeitiges_Aufheben_derselben_Reservierung_genau_einmal_204_und_einmal_404()
    {
        var grosszelt = await NeuAsync("Großzelt");

        for (var runde = 0; runde < 5; runde++)
        {
            var reservierung = await ReserviertAsync(Id(grosszelt));
            var antworten = await Task.WhenAll(
                Task.Run(() => AufhebenAsync(Id(reservierung))),
                Task.Run(() => AufhebenAsync(Id(reservierung), _s.Tobias)));
            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], antworten.Select(a => a.StatusCode).Order());
            foreach (var antwort in antworten)
            {
                antwort.Dispose();
            }
        }

        Assert.Equal(0L, await AnzahlAsync("reservierung"));
    }

    [Fact]
    public async Task Gleichzeitig_Artikel_loeschen_und_reservieren_ergibt_204_und_404_oder_409_und_201()
    {
        for (var runde = 0; runde < 5; runde++)
        {
            var artikel = await NeuAsync($"Kanus {runde}");
            var loeschen = Task.Run(() => LoeschenAsync(Id(artikel)));
            var reservieren = Task.Run(() => ReservierenAsync(Id(artikel)));
            await Task.WhenAll(loeschen, reservieren);
            using var geloescht = loeschen.Result;
            using var reserviert = reservieren.Result;

            Assert.Contains(
                (geloescht.StatusCode, reserviert.StatusCode),
                new[] { (HttpStatusCode.NoContent, HttpStatusCode.NotFound), (HttpStatusCode.Conflict, HttpStatusCode.Created) });
            var stehtNoch = (await OrgInventarAsync()).Any(a => Id(a) == Id(artikel));
            Assert.Equal(geloescht.StatusCode == HttpStatusCode.Conflict, stehtNoch);
        }
    }

    // ---------- Lösch-Lauf ----------

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Reservierungen_der_abgelaufenen_Freizeit_danach_laesst_sich_der_Artikel_loeschen()
    {
        var (y, tim) = await KinderbibeltageAsync();
        using var _ = tim;
        var grosszelt = await NeuAsync("Großzelt");
        await ReserviertAsync(Id(grosszelt));
        var bleibt = await ReserviertAsync(Id(grosszelt), client: tim, freizeit: y);

        await LoeschlaufAsync(_s.X);

        Assert.Equal([Id(bleibt)], Reservierungen(Assert.Single(await OrgInventarAsync())).Select(Id));
        await LoeschlaufAsync(y);
        using var geloescht = await LoeschenAsync(Id(grosszelt));
        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.Equal(0L, await AnzahlAsync("inventar_artikel"));
    }

    private async Task LoeschlaufAsync(Guid abgelaufen)
    {
        await using var scope = _s.Factory.Services.CreateAsyncScope();
        var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "InventarLoeschlauf");
        await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [abgelaufen], []), Abbruch);
    }

    // ---------- Material am Programmpunkt ----------

    [Fact]
    public async Task Reservieren_fuer_ein_Material_setzt_dort_vorhanden_und_die_Kennung_Aufheben_wieder_gebraucht()
    {
        using var punkt = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/programm/punkte", new { titel = "Geländespiel", tag = "2027-08-03" });
        var punktId = Id(await JsonAsync(punkt));
        using var anlegen = await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/programm/punkte/{punktId}/material", new { bezeichnung = "Seile" });
        var material = Id(await JsonAsync(anlegen));
        var seile = await NeuAsync("Seile", "4 × 10 m");

        var reservierung = await ReserviertAsync(Id(seile), material: material);
        Assert.Equal(material, reservierung.GetProperty("materialId").GetGuid());
        await _s.ZustellenAsync();

        var nachher = Assert.Single((await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/programm/material", Abbruch))).GetProperty("material").EnumerateArray());
        Assert.Equal(("Vorhanden", Id(reservierung)), (Text(nachher, "status"), nachher.GetProperty("reservierungId").GetGuid()));

        using var aufheben = await AufhebenAsync(Id(reservierung));
        Assert.Equal(HttpStatusCode.NoContent, aufheben.StatusCode);
        await _s.ZustellenAsync();
        var zurueck = Assert.Single((await JsonAsync(await _s.Miriam.GetAsync($"/api/freizeiten/{_s.X}/programm/material", Abbruch))).GetProperty("material").EnumerateArray());
        Assert.Equal(("Gebraucht", JsonValueKind.Null), (Text(zurueck, "status"), zurueck.GetProperty("reservierungId").ValueKind));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Abgeschlossene_Freizeit_Reservieren_und_Aufheben_409_freizeit_abgeschlossen_Inventar_200()
    {
        var fahnen = await NeuAsync("Fahnen");
        var reservierung = await ReserviertAsync(Id(fahnen));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        using var reservieren = await ReservierenAsync(Id(fahnen));
        using var aufheben = await AufhebenAsync(Id(reservierung));

        foreach (var antwort in new[] { reservieren, aufheben })
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
        }

        Assert.Single(Reservierungen(Assert.Single(Artikel(await FreizeitInventarAsync(_s.Tobias)))));
        // Org-Commands hängen an keiner Freizeit: Andrea pflegt weiter.
        using var andrea = await AnlegenAsync(new { bezeichnung = "Kanus" });
        Assert.Equal(HttpStatusCode.Created, andrea.StatusCode);
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Rechte_Org_Admin_alles_Org_Ueberblick_liest_aber_schreibt_403_Org_Admin_von_B_ohne_Org_Rolle_404_ohne_Sitzung_401()
    {
        const string ueberblickMail = "vorstand@example.org";
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, ueberblickMail, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, ueberblickMail);
        var artikel = await NeuAsync("Kanus", "6 Stück");

        async Task<HttpStatusCode[]> AlleAsync(HttpClient client)
        {
            using var get = await client.GetAsync(OrgPfad(_s.A), Abbruch);
            using var post = await AnlegenAsync(new { bezeichnung = "Paddel" }, client);
            using var patch = await PatchAsync(Id(artikel), Koerper(0, null, "bezeichnung", "Boote"), client);
            using var delete = await LoeschenAsync(Guid.CreateVersion7(), client);
            return [get.StatusCode, post.StatusCode, patch.StatusCode, delete.StatusCode];
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden], await AlleAsync(ueberblick));
        foreach (var client in new[] { _s.Lea, _s.Miriam, _s.Katrin })
        {
            Assert.All(await AlleAsync(client), s => Assert.Equal(HttpStatusCode.NotFound, s));
        }

        Assert.All(await AlleAsync(_s.Anonym), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(["Kanus"], (await OrgInventarAsync(ueberblick)).Select(a => Text(a, "bezeichnung")));

        using var get = await _s.Andrea.GetAsync(OrgPfad(_s.A), Abbruch);
        using var post = await AnlegenAsync(new { bezeichnung = "Paddel" });
        using var patch = await PatchAsync(Id(artikel), Koerper(0, "6 Stück", "menge", "7 Stück"));
        using var delete = await LoeschenAsync(Id(await NeuAsync("Gaskocher")));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent], [get.StatusCode, post.StatusCode, patch.StatusCode, delete.StatusCode]);
    }

    [Fact]
    public async Task Rechte_Freizeit_Leitung_und_Team_2xx_Org_Admin_ohne_Rolle_und_fremde_Organisation_404_ohne_Sitzung_401()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var fahnen = await NeuAsync("Fahnen");
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        var status = new List<string>();
        foreach (var (client, freizeit) in faelle)
        {
            using var get = await client.GetAsync(FreizeitPfad(freizeit), Abbruch);
            using var post = await ReservierenAsync(Id(fahnen), new { }, client, freizeit);
            var reservierung = post.StatusCode == HttpStatusCode.Created ? Id(await JsonAsync(post)) : Guid.CreateVersion7();
            using var delete = await AufhebenAsync(reservierung, client, freizeit);
            status.Add($"{(int)get.StatusCode} {(int)post.StatusCode} {(int)delete.StatusCode}");
        }

        Assert.Equal(["200 201 204", "200 201 204", "404 404 404", "404 404 404", "401 401 401"], status);
        Assert.Equal(0L, await AnzahlAsync("reservierung"));
    }

    [Fact]
    public async Task Org_Admin_bekommt_inventar_sehen_und_verwalten_Org_Ueberblick_nur_sehen()
    {
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, OrgHelfer.Jonas, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, OrgHelfer.Jonas);

        static async Task<List<string?>> ErlaubtAsync(HttpClient client, Guid org) =>
            [.. (await JsonAsync(await client.GetAsync($"/api/organisationen/{org}", Abbruch))).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString())];

        var andrea = await ErlaubtAsync(_s.Andrea, _s.A);
        Assert.Contains(Aktionsschluessel.InventarSehen, andrea);
        Assert.Contains(Aktionsschluessel.InventarVerwalten, andrea);
        Assert.Equal([Aktionsschluessel.InventarSehen], await ErlaubtAsync(ueberblick, _s.A));
    }
}
