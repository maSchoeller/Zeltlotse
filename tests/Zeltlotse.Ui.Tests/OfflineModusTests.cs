using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Layout;
using Zeltlotse.App.Offline;
using Zeltlotse.App.Sitzung;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui.Transport;
using KetteSeite = Zeltlotse.App.Features.Sicherheit.Sicherheitskette;
using KarteSeite = Zeltlotse.App.Features.Sicherheit.Notfallkarte;
using ProtokollSeite = Zeltlotse.App.Features.Sicherheit.VorfallProtokoll;
using TagesansichtSeite = Zeltlotse.App.Features.Kueche.Tagesansicht;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 6-5, Offline-Modus (FR-65, AD-18) in der Web-App mit nachgebildetem Server und Browser: der Abgleich (nur laufend, Schalter Verpflegung, Aktion
/// „freizeitpaesse-sehen“, 403/404 löscht, nicht mehr gelistet löscht, Netzfehler behält), die Warteschlange des Vorfall-Protokolls (Erfolg, Netzfehler,
/// 409, 400, 403), der Ablauf zum Freizeitende, Abmelden und abgelaufene Sitzung löschen, das Layout offline (nutzbare Seiten nicht ausgegraut,
/// Notfall-Box zur gespeicherten Kette, Offline-Leiste mit wartenden Einträgen) und die Seiten Sicherheitskette, Notfallkarte, Tagesansicht und
/// Vorfall-Protokoll aus dem Speicher.
/// </summary>
public sealed class OfflineModusTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199fb00-0000-7000-8000-0000000000d1");
    private static readonly Guid Andere = Guid.Parse("0199fb00-0000-7000-8000-0000000000d2");
    private static readonly Guid Emma = Guid.Parse("0199fb00-0000-7000-8000-0000000000e1");
    private static readonly Guid Jonas = Guid.Parse("0199fb00-0000-7000-8000-0000000000e2");
    private static readonly Guid Fruehstueck = Guid.Parse("0199fb00-0000-7000-8000-0000000000c1");
    private static readonly DateOnly Erster = new(2027, 8, 2);
    private static readonly DateOnly Letzter = new(2027, 8, 4);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>3.8.2027 14:00 Uhr in Berlin, mitten in der Freizeit.</summary>
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2027, 8, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly NachgebildeterHandler _server = new();
    private readonly NachgebildeterBrowser _browser;
    private int _tokens;

    public OfflineModusTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _browser = new NachgebildeterBrowser(_uhr);
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Post, "/api/betrieb/fehler", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, "/api/konten/abmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Services.AddSingleton<TimeProvider>(_uhr);
        Services.AddSingleton<IBrowser>(_browser);
        Services.AddSingleton<NeueVersion>();
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new VerbindungsHandler(sp.GetRequiredService<IVerbindung>())
            {
                InnerHandler = new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
        Services.AddOfflineDienste();
    }

    private OfflineAbgleich Abgleich => Services.GetRequiredService<OfflineAbgleich>();

    private OfflineSpeicher Speicher => Services.GetRequiredService<OfflineSpeicher>();

    private AppVerbindung Verbindung => Services.GetRequiredService<AppVerbindung>();

    private static string Pfad(Guid freizeit, string rest = "") => $"/api/freizeiten/{freizeit}{rest}";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, Web));

    private static Task<HttpResponseMessage> Netzfehler(HttpRequestMessage _) => throw new HttpRequestException("Kein Netz.");

    private static FreizeitKontext Kontext(Guid id, Phase phase = Phase.Laeuft, bool verpflegung = true, bool paesse = true) =>
        new(id, id == Freizeit ? "Zeltlager Sommer 2027" : "Andere Freizeit", phase, [FreizeitRolle.Team], verpflegung ? [FunktionsSchalter.Verpflegung] : [],
            paesse ? [Aktionsschluessel.FreizeitpaesseSehen] : []);

    private static FreizeitAntwort Kopf(Guid id, Phase phase = Phase.Laeuft) =>
        new(id, id == Freizeit ? "Zeltlager Sommer 2027" : "Andere Freizeit", Erster, Letzter, phase, new DateOnly(2027, 9, 3),
            [new LeitungsName("Jonas", "Hüttner", Jonas)]);

    private static Sicherheitskette Kette() => new("Ruhe bewahren.\nLeitung anrufen.", "0123 456782", null, null, true, 3);

    private static FreizeitpassFrage Frage(string kennung, FrageArt art, string? wert, params FreizeitpassOption[] optionen) =>
        new(kennung, kennung, art, optionen, false, wert, null, 0, false);

    private static List<NotfallkartePerson> Karte() =>
    [
        new(Emma, "Emma", "Muster",
        [
            Frage("allergien", FrageArt.Auswahl, "milch", new FreizeitpassOption("milch", "Milch")),
            Frage("medikamente", FrageArt.Freitext, "Salbutamol-Spray bei Bedarf"),
            Frage("notfallkontakte", FrageArt.Freitext, null),
        ]),
    ];

    private static Tagesansicht Ansicht(DateOnly tag) => new(
        tag, [Erster, Erster.AddDays(1), Letzter], 8, true, [new AllergenAnzahl("milch", 1)], false, [],
        [new TagesansichtMahlzeit(Fruehstueck, "08:00", $"Frühstück {tag:dd.MM.}", MahlzeitArt.Fruehstueck, false, 2, [])]);

    /// <summary>Die Antworten des Servers für eine laufende Freizeit; ohne Angabe Team mit Verpflegung und „freizeitpaesse-sehen“.</summary>
    private void Laufend(Guid id, FreizeitKontext? kontext = null, Func<HttpResponseMessage>? kette = null)
    {
        _server.Bei(HttpMethod.Get, Pfad(id, "/kontext"), () => Json(kontext ?? Kontext(id)));
        _server.Bei(HttpMethod.Get, Pfad(id), () => Json(Kopf(id)));
        _server.Bei(HttpMethod.Get, Pfad(id, "/sicherheit/sicherheitskette"), kette ?? (() => Json(Kette())));
        _server.Bei(HttpMethod.Get, Pfad(id, "/personen/notfallkarte"), () => Json(Karte()));
        _server.Bei(
            r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == Pfad(id, "/kueche/tagesansicht"),
            r => Task.FromResult(Json(Ansicht(DateOnly.Parse(r.RequestUri!.Query.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture)))));
        _server.Bei(HttpMethod.Get, Pfad(id, "/personenliste"), () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, Pfad(id, "/sicherheit/vorfaelle"), () => Json(Array.Empty<object>()));
    }

    private void Meine(params MeineFreizeit[] freizeiten) => _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(freizeiten));

    private static MeineFreizeit Meine(Guid id, Phase phase = Phase.Laeuft, params FreizeitRolle[] rollen) =>
        new(id, id == Freizeit ? "Zeltlager Sommer 2027" : "Andere Freizeit", Erster, Letzter, phase, rollen.Length == 0 ? [FreizeitRolle.Team] : rollen);

    private IEnumerable<string> Schluessel(Guid freizeit) => _browser.Offline.Keys.Where(k => k.Contains(freizeit.ToString("D"), StringComparison.Ordinal));

    /// <summary>Legt eine abgeglichene laufende Freizeit im Speicher ab (wie nach einem Abgleich).</summary>
    private async Task GespeichertAsync(Guid id = default)
    {
        id = id == default ? Freizeit : id;
        var kopf = Kopf(id);
        await Speicher.FreizeitSchreibenAsync(new OfflineFreizeit(kopf, Kontext(id)));
        await Speicher.SicherheitsketteSchreibenAsync(kopf, Kette());
        await Speicher.NotfallkarteSchreibenAsync(kopf, Karte());
        for (var tag = Erster; tag <= Letzter; tag = tag.AddDays(1))
        {
            await Speicher.TagesansichtSchreibenAsync(kopf, Ansicht(tag));
        }
    }

    private async Task OfflineAsync()
    {
        _browser.Online = false;
        await Verbindung.StartenAsync();
    }

    // ---------- Abgleich ----------

    [Fact]
    public async Task Abgleich_speichert_Kopf_Kette_Notfallkarte_und_jeden_Tag_einer_laufenden_Freizeit_verschluesselt_mit_Ablauf_Freizeitende()
    {
        Meine(Meine(Freizeit), Meine(Andere, Phase.InVorbereitung));
        Laufend(Freizeit);
        Laufend(Andere, Kontext(Andere, Phase.InVorbereitung));

        await Abgleich.AbgleichenAsync();

        var f = Freizeit.ToString("D");
        Assert.Equal(
            [$"{f}/freizeit", $"{f}/notfallkarte", $"{f}/sicherheitskette", $"{f}/tagesansicht/2027-08-02", $"{f}/tagesansicht/2027-08-03", $"{f}/tagesansicht/2027-08-04"],
            _browser.Offline.Keys);
        // Tag nach dem letzten Freizeittag, 00:00 Uhr in Berlin (Sommerzeit).
        var ende = new DateTimeOffset(2027, 8, 4, 22, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.All(_browser.Offline.Values, d => Assert.Equal((Freizeit, ende), (d.FreizeitId, d.Ablauf)));
        Assert.Equal(ende, OfflineSpeicher.Ablauf(Letzter));
        Assert.Equal("Salbutamol-Spray bei Bedarf", (await Speicher.NotfallkarteLesenAsync(Freizeit))![0].Fragen[1].Wert);
        Assert.Equal(1, _browser.SpeicherGehalten);
        Assert.Equal(["Zeltlager Sommer 2027"], Abgleich.Gespeichert.Select(g => g.Freizeit.Name));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.StartsWith(Pfad(Andere), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ohne_Verpflegung_keine_Tagesansichten_ohne_freizeitpaesse_sehen_keine_Notfallkarte_und_alte_werden_entfernt()
    {
        await GespeichertAsync();
        Meine(Meine(Freizeit));
        Laufend(Freizeit, Kontext(Freizeit, verpflegung: false, paesse: false));

        await Abgleich.AbgleichenAsync();

        var f = Freizeit.ToString("D");
        Assert.Equal([$"{f}/freizeit", $"{f}/sicherheitskette"], _browser.Offline.Keys);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("/notfallkarte", StringComparison.Ordinal) || a.Pfad.Contains("/tagesansicht", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nur_mit_Rolle_Leitung_oder_Team_und_nur_in_Phase_laeuft()
    {
        Meine(Meine(Freizeit, Phase.Nachbereitung), new MeineFreizeit(Andere, "Andere Freizeit", Erster, Letzter, Phase.Laeuft, []));
        Laufend(Freizeit);
        Laufend(Andere);

        await Abgleich.AbgleichenAsync();

        Assert.Empty(_browser.Offline);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("/kontext", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Antwort_403_oder_404_beim_Abgleich_loescht_die_Daten_der_Freizeit_die_andere_bleibt(HttpStatusCode status)
    {
        await GespeichertAsync();
        await GespeichertAsync(Andere);
        Meine(Meine(Freizeit), Meine(Andere));
        Laufend(Freizeit, kette: () => new HttpResponseMessage(status));
        Laufend(Andere);

        await Abgleich.AbgleichenAsync();

        Assert.Empty(Schluessel(Freizeit));
        Assert.NotEmpty(Schluessel(Andere));
        Assert.Equal(["Andere Freizeit"], Abgleich.Gespeichert.Select(g => g.Freizeit.Name));
    }

    [Fact]
    public async Task Nicht_mehr_laufend_gelistete_Freizeiten_werden_geloescht()
    {
        await GespeichertAsync();
        await GespeichertAsync(Andere);
        Meine(Meine(Andere));
        Laufend(Andere);

        await Abgleich.AbgleichenAsync();

        Assert.Empty(Schluessel(Freizeit));
        Assert.Contains($"{Andere:D}/freizeit", _browser.Offline.Keys);
    }

    [Fact]
    public async Task Netzfehler_mitten_im_Abgleich_bisher_Gespeichertes_bleibt()
    {
        Meine(Meine(Freizeit));
        Laufend(Freizeit);
        _server.Bei(r => r.RequestUri!.AbsolutePath == Pfad(Freizeit, "/personen/notfallkarte"), Netzfehler);

        await Abgleich.AbgleichenAsync();

        var f = Freizeit.ToString("D");
        Assert.Equal([$"{f}/freizeit", $"{f}/sicherheitskette"], _browser.Offline.Keys);
        Assert.True(Verbindung.Offline);
        Assert.Equal(0, _browser.SpeicherGehalten);
    }

    [Fact]
    public async Task Offline_gleicht_er_nicht_ab_kommt_die_Verbindung_zurueck_schon()
    {
        Meine(Meine(Freizeit));
        Laufend(Freizeit);
        await OfflineAsync();
        await GespeichertAsync();
        Services.GetRequiredService<AppSitzung>().Anmelden("Lea");

        await Abgleich.StartenAsync();
        await Abgleich.AbgleichenAsync();
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == "/api/ich/freizeiten");

        await _browser.MeldenAsync(true);

        await WartenAufAsync(() => _server.Anfragen.Any(a => a.Pfad == "/api/ich/freizeiten"));
        await Abgleich.AbgleichenAsync();
        Assert.Equal(1, _browser.SpeicherGehalten);
    }

    [Fact]
    public async Task Nach_dem_Login_gleicht_er_ab()
    {
        Meine(Meine(Freizeit));
        Laufend(Freizeit);
        await Verbindung.StartenAsync();
        await Abgleich.StartenAsync();
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == "/api/ich/freizeiten");

        Services.GetRequiredService<AppSitzung>().Anmelden("Lea");

        await WartenAufAsync(() => _browser.SpeicherGehalten == 1);
        Assert.Contains($"{Freizeit:D}/sicherheitskette", _browser.Offline.Keys);
    }

    // ---------- Warteschlange ----------

    private async Task<VorfallEintragNeu> WartendAsync(Guid? freizeit = null, string text = "Knie aufgeschürft")
    {
        var eintrag = new VorfallEintragNeu(Guid.CreateVersion7(), Emma, VorfallArt.Geschehen, text, _uhr.GetUtcNow().AddMinutes(-30));
        Assert.True(await Abgleich.VorfallEinreihenAsync(freizeit ?? Freizeit, eintrag));
        return eintrag;
    }

    [Fact]
    public async Task Einreihen_geht_nur_fuer_eine_gespeicherte_Freizeit_und_zaehlt_die_wartenden()
    {
        Assert.False(await Abgleich.VorfallEinreihenAsync(Freizeit, new VorfallEintragNeu(Guid.CreateVersion7(), Emma, VorfallArt.Geschehen, "Knie")));
        Assert.Empty(_browser.Offline);

        await GespeichertAsync();
        await WartendAsync();
        await WartendAsync();

        Assert.Equal(2, Abgleich.Wartend);
    }

    [Fact]
    public async Task Wieder_online_sendet_jeden_Eintrag_mit_Kennung_und_erfasstAm_und_entfernt_ihn_bei_201()
    {
        await GespeichertAsync();
        var eintrag = await WartendAsync();
        _server.Bei(HttpMethod.Post, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => new HttpResponseMessage(HttpStatusCode.Created));
        Meine(Meine(Freizeit));
        Laufend(Freizeit);

        await Abgleich.AbgleichenAsync();

        var post = Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad.EndsWith("/vorfaelle", StringComparison.Ordinal));
        var koerper = JsonDocument.Parse(post.Koerper!).RootElement;
        Assert.Equal((eintrag.Id, "Knie aufgeschürft", eintrag.ErfasstAm), (koerper.GetProperty("id").GetGuid(), koerper.GetProperty("text").GetString(), koerper.GetProperty("erfasstAm").GetDateTimeOffset()));
        Assert.Equal(0, Abgleich.Wartend);
        Assert.DoesNotContain(_browser.Offline.Keys, k => k.StartsWith("vorfall/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Netzfehler_beim_Senden_der_Eintrag_bleibt()
    {
        await GespeichertAsync();
        await WartendAsync();
        _server.Bei(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == Pfad(Freizeit, "/sicherheit/vorfaelle"), Netzfehler);
        Meine(Meine(Freizeit));

        await Abgleich.AbgleichenAsync();

        Assert.Equal(1, Abgleich.Wartend);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == "/api/ich/freizeiten");
    }

    [Fact]
    public async Task Serverfehler_beim_Senden_der_Eintrag_bleibt_und_nichts_wird_geloescht()
    {
        await GespeichertAsync();
        await WartendAsync();
        _server.Bei(HttpMethod.Post, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Meine();

        await Abgleich.AbgleichenAsync();

        Assert.Equal(1, Abgleich.Wartend);
        Assert.Contains($"{Freizeit:D}/freizeit", _browser.Offline.Keys);
    }

    public static TheoryData<string> Endgueltig => ["abgeschlossen", "feldfehler", "konflikt"];

    [Theory]
    [MemberData(nameof(Endgueltig))]
    public async Task Antwort_409_oder_400_entfernt_den_Eintrag(string fall)
    {
        await GespeichertAsync();
        await WartendAsync();
        _server.Bei(HttpMethod.Post, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => fall switch
        {
            "abgeschlossen" => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen),
            "konflikt" => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt),
            _ => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"text":["Text fehlt."]}}""", "application/problem+json"),
        });
        Meine(Meine(Freizeit));
        Laufend(Freizeit);

        await Abgleich.AbgleichenAsync();

        Assert.Equal(0, Abgleich.Wartend);
        Assert.Contains($"{Freizeit:D}/freizeit", _browser.Offline.Keys);
    }

    [Fact]
    public async Task Antwort_403_beim_Senden_loescht_die_Daten_der_Freizeit_die_andere_bleibt()
    {
        await GespeichertAsync();
        await GespeichertAsync(Andere);
        await WartendAsync();
        await WartendAsync(text: "Zweiter Eintrag");
        var andere = await WartendAsync(Andere);
        _server.Bei(HttpMethod.Post, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.Bei(HttpMethod.Post, Pfad(Andere, "/sicherheit/vorfaelle"), () => new HttpResponseMessage(HttpStatusCode.Created));
        Meine(Meine(Andere));
        Laufend(Andere);

        await Abgleich.AbgleichenAsync();

        Assert.Empty(Schluessel(Freizeit));
        Assert.Equal(1, _server.Anfragen.Count(a => a.Methode == HttpMethod.Post && a.Pfad == Pfad(Freizeit, "/sicherheit/vorfaelle")));
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Koerper!.Contains(andere.Id.ToString(), StringComparison.Ordinal));
        Assert.Equal(0, Abgleich.Wartend);
    }

    // ---------- Ablauf und Abmelden ----------

    [Fact]
    public async Task Nach_dem_Freizeitende_sind_die_Daten_beim_Start_auch_offline_weg()
    {
        await GespeichertAsync();
        await WartendAsync();
        await OfflineAsync();
        _uhr.SetUtcNow(new DateTimeOffset(2027, 8, 4, 22, 0, 0, TimeSpan.Zero));

        await Abgleich.StartenAsync();

        Assert.Empty(_browser.Offline);
        Assert.Equal((0, 0), (Abgleich.Wartend, Abgleich.Gespeichert.Count));
        Assert.Empty(_server.Anfragen);
    }

    [Fact]
    public async Task Eine_Minute_vor_dem_Freizeitende_ist_noch_alles_da_und_vor_jedem_Lesen_wird_geprueft()
    {
        await GespeichertAsync();
        _uhr.SetUtcNow(new DateTimeOffset(2027, 8, 4, 21, 59, 0, TimeSpan.Zero));
        Assert.NotNull(await Speicher.SicherheitsketteLesenAsync(Freizeit));

        _uhr.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(await Speicher.SicherheitsketteLesenAsync(Freizeit));
    }

    [Fact]
    public async Task Abmelden_loescht_die_Offline_Datenbank()
    {
        await GespeichertAsync();

        await Services.GetRequiredService<AnmeldungAbschluss>().AbmeldenAsync(abbruch: Xunit.TestContext.Current.CancellationToken);

        Assert.Empty(_browser.Offline);
        Assert.Equal(1, _browser.OfflineAllesGeloescht);
    }

    [Fact]
    public async Task Abgelaufene_Sitzung_401_loescht_die_Offline_Datenbank()
    {
        await GespeichertAsync();
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Abgleich.AbgleichenAsync();

        Assert.Empty(_browser.Offline);
        Assert.Equal(1, _browser.OfflineAllesGeloescht);
        Assert.Empty(Abgleich.Gespeichert);
    }

    // ---------- Layout ----------

    private IRenderedComponent<MainLayout> Layout(bool nutzbar)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"freizeiten/{Freizeit}/kueche/einkaufsliste");
        return Render<MainLayout>(ps => ps
            .AddCascadingValue(OfflineNutzbarAttribute.Kaskade, nutzbar)
            .Add(p => p.Body, b => b.AddMarkupContent(0, "<h1>Seite</h1><button>Bearbeiten</button>")));
    }

    [Fact]
    public async Task Offline_bleibt_eine_offline_nutzbare_Seite_bedienbar_die_anderen_sind_ausgegraut()
    {
        await OfflineAsync();

        var nutzbar = Layout(nutzbar: true);
        Assert.False(nutzbar.Find(".zl-offline-bereich").HasAttribute("inert"));
        Assert.Empty(nutzbar.FindAll(".zl-offline-satz"));

        var andere = Layout(nutzbar: false);
        Assert.True(andere.Find(".zl-offline-bereich").HasAttribute("inert"));
        Assert.Equal("Offline nicht verfügbar.", andere.Find(".zl-offline-satz").TextContent);
    }

    [Fact]
    public void Die_Seiten_der_Story_sind_offline_nutzbar_andere_nicht()
    {
        Assert.All(
            new[] { typeof(KetteSeite), typeof(KarteSeite), typeof(ProtokollSeite), typeof(TagesansichtSeite) },
            t => Assert.True(OfflineNutzbarAttribute.Gilt(t), t.Name));
        Assert.False(OfflineNutzbarAttribute.Gilt(typeof(Zeltlotse.App.Features.Kueche.Einkaufsliste)));
        Assert.False(OfflineNutzbarAttribute.Gilt(typeof(Zeltlotse.App.Features.Freizeiten.FreizeitStart)));
        Assert.False(OfflineNutzbarAttribute.Gilt(null));
    }

    [Fact]
    public async Task Notfall_Box_offline_fuehrt_zur_gespeicherten_Kette_der_Seite_sonst_zur_ersten_nach_Name_ohne_Speicher_der_alte_Satz()
    {
        await OfflineAsync();
        var ohne = Layout(nutzbar: false);
        Assert.Contains(MainLayout.NotfallSatzOffline, ohne.Find(".zl-notfall-box").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Notfallhilfe öffnen", ohne.Find(".zl-notfall-box").TextContent, StringComparison.Ordinal);

        await GespeichertAsync(Andere);
        await GespeichertAsync();
        await Abgleich.AktualisierenAsync();

        // Die Seite gehört zu „Zeltlager Sommer 2027“; diese Kette, nicht die erste nach Name („Andere Freizeit“).
        var mit = Layout(nutzbar: false);
        var box = mit.Find(".zl-notfall-box");
        Assert.Contains(MainLayout.NotfallSatzGespeichert, box.TextContent, StringComparison.Ordinal);
        Assert.Equal($"freizeiten/{Freizeit}/notfall", box.QuerySelector("a.elkw-btn")!.GetAttribute("href"));

        Services.GetRequiredService<NavigationManager>().NavigateTo("konto");
        mit.WaitForAssertion(() => Assert.Equal($"freizeiten/{Andere}/notfall", mit.Find(".zl-notfall-box a.elkw-btn").GetAttribute("href")));
    }

    [Fact]
    public async Task Offline_Leiste_zeigt_wartende_Eintraege_auch_online_bis_sie_gesendet_sind()
    {
        await GespeichertAsync();
        await WartendAsync();
        await OfflineAsync();
        var cut = Layout(nutzbar: true);
        cut.WaitForAssertion(() =>
        {
            var zeilen = cut.FindAll(".zl-offline-leiste p").Select(p => p.TextContent).ToList();
            Assert.StartsWith("Offline", zeilen[0], StringComparison.Ordinal);
            Assert.Equal("1 Eintrag wartet auf Verbindung", zeilen[1]);
        });

        _server.Bei(HttpMethod.Post, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await cut.InvokeAsync(() => _browser.MeldenAsync(true));
        await cut.InvokeAsync(Abgleich.AbgleichenAsync);
        cut.WaitForAssertion(() => Assert.Equal(["1 Eintrag wartet auf Verbindung"], cut.FindAll(".zl-offline-leiste p").Select(p => p.TextContent)));

        _server.Bei(HttpMethod.Post, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => new HttpResponseMessage(HttpStatusCode.Created));
        Meine(Meine(Freizeit));
        Laufend(Freizeit);
        await cut.InvokeAsync(Abgleich.AbgleichenAsync);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-offline-leiste")));
    }

    // ---------- Seiten aus dem Speicher ----------

    [Fact]
    public async Task Sicherheitskette_offline_aus_dem_Speicher_Suche_fuehrt_zur_Notfallkarte_Notfallordner_ausgegraut()
    {
        await GespeichertAsync();
        await OfflineAsync();

        var cut = Render<KetteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Contains("Ruhe bewahren.", cut.Markup, StringComparison.Ordinal));

        Assert.Contains("Leitung: Jonas Hüttner", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Leitung anrufen: 0123 456782", cut.Markup, StringComparison.Ordinal);
        var drucken = cut.FindAll("a.elkw-btn").Single(a => a.TextContent == "Notfallordner drucken");
        Assert.Equal("true", drucken.GetAttribute("aria-disabled"));
        Assert.Null(drucken.GetAttribute("href"));
        cut.Find("#betroffene-person").Input("Em");
        var treffer = cut.FindAll("a").Single(a => a.TextContent == "Emma Muster");
        Assert.Equal($"freizeiten/{Freizeit}/notfallkarte/{Emma}", treffer.GetAttribute("href"));
        Assert.Empty(_server.Anfragen);
    }

    [Fact]
    public async Task Sicherheitskette_bei_Netzfehler_aus_dem_Speicher_ohne_Speicher_der_Satz_nicht_gespeichert()
    {
        _server.Bei(r => r.RequestUri!.AbsolutePath.StartsWith(Pfad(Freizeit), StringComparison.Ordinal), Netzfehler);

        var ohne = Render<KetteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        ohne.WaitForAssertion(() => Assert.Contains(OfflineSpeicher.NichtGespeichertText, ohne.Markup, StringComparison.Ordinal));
        Assert.Equal("Notruf 112", ohne.Find("a[href='tel:112']").TextContent);

        await GespeichertAsync();
        var mit = Render<KetteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        mit.WaitForAssertion(() => Assert.Contains("Ruhe bewahren.", mit.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Notfallkarte_zeigt_Name_und_vier_Angaben_mit_keine_Angabe_und_die_Links()
    {
        await GespeichertAsync();

        var cut = Render<KarteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Emma));
        cut.WaitForAssertion(() => Assert.Contains("Emma Muster", cut.Markup, StringComparison.Ordinal));

        Assert.Equal("Notfallkarte", cut.Find("h1").TextContent);
        Assert.Equal(
            ["Allergien", "Medikamente", "Krankheiten", "Notfallkontakte"],
            cut.FindAll("dt").Select(d => d.TextContent));
        Assert.Equal(
            ["Milch", "Salbutamol-Spray bei Bedarf", Zeltlotse.App.Features.Sicherheit.Notfallkarte.KeineAngabe, "keine Angabe"],
            cut.FindAll("dd").Select(d => d.TextContent));
        var links = cut.FindAll("a").Select(a => (a.TextContent, a.GetAttribute("href"))).ToList();
        Assert.Contains(("Vorfall-Protokoll", $"freizeiten/{Freizeit}/vorfall/{Emma}"), links);
        Assert.Contains(("Zurück zur Sicherheitskette", $"freizeiten/{Freizeit}/notfall"), links);
        Assert.Empty(_server.Anfragen);
    }

    [Fact]
    public void Notfallkarte_ohne_Speicher_nicht_gespeichert()
    {
        var cut = Render<KarteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Emma));

        cut.WaitForAssertion(() => Assert.Contains(OfflineSpeicher.NichtGespeichertText, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Notruf 112", cut.Find("a[href='tel:112']").TextContent);
    }

    [Fact]
    public async Task Tagesansicht_offline_zeigt_heute_aus_dem_Speicher_mit_ausgegrautem_Restetag_und_Tag_Reitern()
    {
        await GespeichertAsync();
        await OfflineAsync();

        var cut = Render<TagesansichtSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Contains("Frühstück 03.08.", cut.Markup, StringComparison.Ordinal));

        var restetag = cut.Find($"#restetag-{Fruehstueck}");
        Assert.True(restetag.HasAttribute("disabled"));
        Assert.Equal(3, cut.FindAll("a").Count(a => a.GetAttribute("href")?.Contains("tagesansicht?tag=", StringComparison.Ordinal) == true));
        Assert.Empty(_server.Anfragen);
    }

    [Fact]
    public async Task Tagesansicht_offline_ohne_den_Tag_nicht_gespeichert()
    {
        await OfflineAsync();

        var cut = Render<TagesansichtSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Contains(OfflineSpeicher.NichtGespeichertText, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-loading"));
    }

    [Fact]
    public async Task Vorfall_offline_Feld_verlassen_reiht_ein_leert_das_Feld_und_zeigt_wartet_auf_Verbindung()
    {
        await GespeichertAsync();
        await OfflineAsync();
        var cut = Render<ProtokollSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Emma));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#vorfall-geschehen")));
        Assert.Contains("Emma Muster", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(ProtokollSeite.OfflineText, cut.Markup, StringComparison.Ordinal);

        cut.Find("#vorfall-geschehen").Input("Am Knie geschnitten");
        cut.Find("#vorfall-geschehen").FocusOut();

        cut.WaitForAssertion(() => Assert.Contains(ProtokollSeite.WartetText, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("", cut.Find("#vorfall-geschehen").GetAttribute("value") ?? "");
        Assert.Contains("Am Knie geschnitten", cut.Markup, StringComparison.Ordinal);
        var wartend = Assert.Single(await Speicher.WartendeAsync(Freizeit));
        Assert.Equal((Emma, VorfallArt.Geschehen, "Am Knie geschnitten", _uhr.GetUtcNow()), (wartend.Eintrag.PersonId, wartend.Eintrag.Art, wartend.Eintrag.Text, wartend.Eintrag.ErfasstAm));
        Assert.Equal(1, Abgleich.Wartend);
        Assert.Empty(_server.Anfragen);
    }

    [Fact]
    public async Task Vorfall_online_mit_Netzfehler_beim_Speichern_wartet_statt_Fehlermeldung_wieder_online_steht_er_vom_Server_da()
    {
        await GespeichertAsync();
        Laufend(Freizeit);
        var cut = Render<ProtokollSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Emma));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#vorfall-medikament")));
        _server.Bei(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/vorfaelle", StringComparison.Ordinal), Netzfehler);

        cut.Find("#vorfall-medikament").Input("Kühlpack");
        cut.Find("#vorfall-medikament").FocusOut();

        cut.WaitForAssertion(() => Assert.Contains(ProtokollSeite.WartetText, cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal);
        var id = Assert.Single(await Speicher.WartendeAsync(Freizeit)).Eintrag.Id;

        // Wieder Verbindung: Der Abgleich sendet, die Seite lädt die Liste vom Server mit dem Eintrag.
        _server.Bei(HttpMethod.Post, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => new HttpResponseMessage(HttpStatusCode.Created));
        _server.Bei(HttpMethod.Get, Pfad(Freizeit, "/sicherheit/vorfaelle"), () => Json(new[]
        {
            new VorfallEintrag(id, Emma, "Emma", "Muster", VorfallArt.Medikament, "Kühlpack", _uhr.GetUtcNow(), "Lea", "Feuerstein"),
        }));
        Meine(Meine(Freizeit));
        await cut.InvokeAsync(Abgleich.AbgleichenAsync);

        cut.WaitForAssertion(() => Assert.Contains("von Lea Feuerstein", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(ProtokollSeite.WartetText, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vorfall_offline_ohne_gespeicherte_Freizeit_nicht_gespeichert_ohne_Felder()
    {
        await OfflineAsync();

        var cut = Render<ProtokollSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Emma));

        cut.WaitForAssertion(() => Assert.Contains(OfflineSpeicher.NichtGespeichertText, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("textarea"));
    }

    private static async Task WartenAufAsync(Func<bool> bedingung)
    {
        for (var i = 0; i < 200 && !bedingung(); i++)
        {
            await Task.Delay(10, Xunit.TestContext.Current.CancellationToken);
        }

        Assert.True(bedingung());
    }
}
