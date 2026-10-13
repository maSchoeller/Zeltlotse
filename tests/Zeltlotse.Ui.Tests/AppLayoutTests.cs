using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Layout;
using Zeltlotse.App.Sitzung;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Layout der Web-App (AD-18, AD-5, AD-22): online, offline (Matrix-Zeile „Offline-Start“ ohne Browser), Fehlergrenze
/// und neue Version, mit nachgebildetem <see cref="IBrowser"/> und künstlicher Uhr.
/// </summary>
public sealed class AppLayoutTests : BunitContext
{
    private readonly NachgebildeterBrowser _browser = new();
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2027, 8, 3, 12, 32, 0, TimeSpan.Zero));
    private readonly NachgebildeterHandler _server = new();

    public AppLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Post, "/api/betrieb/fehler", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Services.AddSingleton<TimeProvider>(_uhr);
        Services.AddSingleton<IBrowser>(_browser);
        Services.AddSingleton<AppVerbindung>();
        Services.AddSingleton<IVerbindung>(sp => sp.GetRequiredService<AppVerbindung>());
        Services.AddSingleton<NeueVersion>();
        Services.AddSingleton<AppSitzung>();
        Services.AddZeltlotseUi(_ => _server.Client());
    }

    private IRenderedComponent<MainLayout> Layout(RenderFragment? inhalt = null) =>
        Render<MainLayout>(ps => ps.Add(p => p.Body, inhalt ?? (b => b.AddMarkupContent(0, "<h1>Zeltlotse</h1><button>Bearbeiten</button>"))));

    [Fact]
    public void Online_zeigt_App_Leiste_und_Inhalt_ohne_Offline_Leiste_und_Notfall_Box()
    {
        var cut = Layout();

        Assert.Equal("Zeltlotse", cut.Find(".elkw-appbar a.zl-wortmarke").TextContent);
        Assert.Empty(cut.FindAll(".zl-offline-leiste"));
        Assert.Empty(cut.FindAll(".zl-notfall-box"));
        Assert.False(cut.Find(".zl-offline-bereich").HasAttribute("inert"));
        Assert.Equal("Zeltlotse", cut.Find("main h1").TextContent);
        Assert.Equal("Zum Inhalt springen", cut.Find("a.elkw-skiplink").TextContent);
        Assert.EndsWith("#inhalt", cut.Find("a.elkw-skiplink").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task App_Leiste_zeigt_den_Konto_Link_erst_angemeldet_mit_Vornamen_sonst_Konto()
    {
        var cut = Layout();
        Assert.Empty(cut.FindAll("a.zl-konto-link"));
        var sitzung = Services.GetRequiredService<AppSitzung>();

        await cut.InvokeAsync(() => sitzung.Anmelden("Jonas"));
        cut.WaitForAssertion(() => Assert.Equal("Jonas", cut.Find("header a.zl-konto-link").TextContent));
        Assert.Equal("konto", cut.Find("header a.zl-konto-link").GetAttribute("href"));

        await cut.InvokeAsync(() => sitzung.Anmelden(null));
        cut.WaitForAssertion(() => Assert.Equal("Konto", cut.Find("header a.zl-konto-link").TextContent));

        await cut.InvokeAsync(sitzung.Abmelden);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("a.zl-konto-link")));
    }

    [Fact]
    public void Offline_mit_Zeitpunkt_zeigt_Offline_Leiste_in_Berliner_Zeit_Notfall_Box_mit_112_und_graut_den_Rest_aus()
    {
        _browser.Online = false;
        _browser.Zuletzt = new DateTimeOffset(2027, 8, 3, 12, 32, 0, TimeSpan.Zero);

        var cut = Layout();

        Assert.Equal("Offline – zuletzt aktualisiert 14:32 Uhr", cut.Find(".zl-offline-leiste p").TextContent);
        var box = cut.Find(".zl-notfall-box");
        Assert.Equal("Im Notfall:", box.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Contains("Offline gilt der ausgedruckte Notfallordner.", box.TextContent, StringComparison.Ordinal);
        Assert.Equal("Notruf 112", box.QuerySelector("a[href='tel:112']")!.TextContent);
        Assert.DoesNotContain("Notfallhilfe öffnen", box.TextContent, StringComparison.Ordinal);
        var bereich = cut.Find(".zl-offline-bereich");
        Assert.True(bereich.HasAttribute("inert"));
        Assert.Contains("zl-offline-bereich--aus", bereich.ClassList);
        Assert.Equal("Offline nicht verfügbar.", cut.Find(".zl-offline-satz").TextContent);
        Assert.Null(box.Closest(".zl-offline-bereich"));
    }

    [Fact]
    public void Offline_ohne_Zeitpunkt_zeigt_nur_Offline()
    {
        _browser.Online = false;

        var cut = Layout();

        Assert.Equal("Offline", cut.Find(".zl-offline-leiste p").TextContent);
    }

    [Fact]
    public async Task Netzfehler_macht_offline_bis_zur_naechsten_Antwort_die_den_Zeitpunkt_speichert()
    {
        var cut = Layout();
        var verbindung = Services.GetRequiredService<AppVerbindung>();

        await cut.InvokeAsync(async () => await verbindung.NetzfehlerMeldenAsync());
        cut.WaitForAssertion(() => Assert.Equal("Offline", cut.Find(".zl-offline-leiste p").TextContent));
        Assert.NotEmpty(cut.FindAll(".zl-notfall-box"));

        _uhr.Advance(TimeSpan.FromMinutes(5));
        await cut.InvokeAsync(async () => await verbindung.ErfolgMeldenAsync());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-offline-leiste")));
        Assert.Equal(_uhr.GetUtcNow(), _browser.Zuletzt);
        await cut.InvokeAsync(async () => await verbindung.NetzfehlerMeldenAsync());
        cut.WaitForAssertion(() => Assert.Equal("Offline – zuletzt aktualisiert 14:37 Uhr", cut.Find(".zl-offline-leiste p").TextContent));
    }

    [Fact]
    public async Task Online_Ereignisse_des_Browsers_schalten_die_Offline_Ansicht()
    {
        var cut = Layout();

        await cut.InvokeAsync(() => _browser.MeldenAsync(false));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".zl-offline-leiste")));

        await cut.InvokeAsync(() => _browser.MeldenAsync(true));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-offline-leiste")));
    }

    [Fact]
    public async Task Ein_Erfolg_waehrend_des_Starts_wird_nicht_vom_gespeicherten_Zeitpunkt_ueberschrieben()
    {
        _browser.Zuletzt = _uhr.GetUtcNow().AddHours(-3);
        _browser.LesenAnhalten = new TaskCompletionSource();
        await using var verbindung = new AppVerbindung(_browser, _uhr);

        var start = verbindung.StartenAsync();
        await verbindung.ErfolgMeldenAsync();
        _browser.LesenAnhalten.SetResult();
        await start;

        Assert.Equal(_uhr.GetUtcNow(), verbindung.ZuletztAktualisiert);
    }

    [Fact]
    public async Task Der_Start_beobachtet_den_Online_Status_bevor_er_ihn_liest()
    {
        await using var verbindung = new AppVerbindung(_browser, _uhr);

        await verbindung.StartenAsync();

        Assert.Equal([nameof(IBrowser.OnlineBeobachtenAsync), nameof(IBrowser.IstOnlineAsync)], _browser.Aufrufe);
    }

    [Fact]
    public void Offline_Text_rechnet_in_Europe_Berlin_auch_im_Winter()
    {
        Assert.Equal("Offline – zuletzt aktualisiert 09:05 Uhr", AppVerbindung.OfflineTextFuer(new DateTimeOffset(2027, 1, 10, 8, 5, 0, TimeSpan.Zero)));
        Assert.Equal("Offline", AppVerbindung.OfflineTextFuer(null));
    }

    [Fact]
    public async Task Neue_Version_zeigt_den_Hinweis_mit_Neu_laden()
    {
        var cut = Layout();
        Assert.DoesNotContain("neue Version", cut.Markup, StringComparison.Ordinal);

        await cut.InvokeAsync(() => Services.GetRequiredService<NeueVersion>().Melden());

        cut.WaitForAssertion(() => Assert.Contains("Eine neue Version von Zeltlotse ist da.", cut.Markup, StringComparison.Ordinal));
        var meldung = cut.Find("section.elkw-alert--hinweis");
        Assert.Equal("Hinweis:", meldung.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Equal("status", meldung.GetAttribute("role"));
        var knopf = meldung.QuerySelector("button")!;
        Assert.Equal("Neu laden", knopf.TextContent);
        knopf.Click();
        Assert.Equal(1, _browser.NeuGeladen);
    }

    [Fact]
    public async Task Neue_Version_aus_einer_API_Antwort_ueber_den_Versions_Handler()
    {
        var neueVersion = Services.GetRequiredService<NeueVersion>();
        var server = new NachgebildeterHandler().Bei(r => true, _ =>
        {
            var antwort = new HttpResponseMessage(HttpStatusCode.OK);
            antwort.Headers.Add(global::Zeltlotse.Api.Contracts.Gemeinsam.Kopfzeilen.Version, "999.0.0+anders");
            return Task.FromResult(antwort);
        });
        var cut = Layout();
        using var client = new HttpClient(new VersionsHandler(neueVersion) { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/") };

        await cut.InvokeAsync(async () => (await client.GetAsync("/api/x")).Dispose());

        cut.WaitForAssertion(() => Assert.Contains("Eine neue Version von Zeltlotse ist da.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Fehlergrenze_meldet_Typ_Pfad_ohne_Abfrage_und_neue_Trace_ID_und_zeigt_die_Fehlermeldung()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/app/tief/unten?code=geheim#abschnitt");

        var cut = Layout(b =>
        {
            b.OpenComponent<Werfer>(0);
            b.CloseComponent();
        });

        cut.WaitForAssertion(() => Assert.Single(_server.Anfragen, a => a.Pfad == "/api/betrieb/fehler"));
        using var meldung = JsonDocument.Parse(_server.Anfragen.Single(a => a.Pfad == "/api/betrieb/fehler").Koerper!);
        Assert.Equal("InvalidOperationException", meldung.RootElement.GetProperty("fehlerart").GetString());
        Assert.Equal("/app/tief/unten", meldung.RootElement.GetProperty("seite").GetString());
        Assert.Matches("^[0-9a-f]{32}$", meldung.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("geheim", meldung.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Erika", meldung.RootElement.GetRawText(), StringComparison.Ordinal);

        var fehler = cut.Find("section.elkw-alert--fehler");
        Assert.Equal("alert", fehler.GetAttribute("role"));
        Assert.Equal("Fehler:", fehler.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Contains("Die Ansicht konnte nicht angezeigt werden.", fehler.TextContent, StringComparison.Ordinal);
        fehler.QuerySelector("button")!.Click();
        Assert.Equal(1, _browser.NeuGeladen);
    }

    [Fact]
    public void Fehlergrenze_erholt_sich_beim_Seitenwechsel()
    {
        var werfen = true;
        var cut = Layout(b =>
        {
            if (werfen)
            {
                b.OpenComponent<Werfer>(0);
                b.CloseComponent();
            }
            else
            {
                b.AddMarkupContent(1, "<h1>Andere Seite</h1>");
            }
        });
        cut.WaitForAssertion(() => Assert.Contains("Die Ansicht konnte nicht angezeigt werden.", cut.Markup, StringComparison.Ordinal));

        werfen = false;
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/app/andere");

        cut.WaitForAssertion(() => Assert.Equal("Andere Seite", cut.Find("main h1").TextContent));
        Assert.DoesNotContain("Die Ansicht konnte nicht angezeigt werden.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Fehlergrenze_verwirft_eine_gescheiterte_Meldung()
    {
        _server.Bei(HttpMethod.Post, "/api/betrieb/fehler", () => throw new HttpRequestException("Kein Netz"));

        var cut = Layout(b =>
        {
            b.OpenComponent<Werfer>(0);
            b.CloseComponent();
        });

        cut.WaitForAssertion(() => Assert.Contains("Die Ansicht konnte nicht angezeigt werden.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Trace_ID_ist_jedes_Mal_neu()
    {
        Assert.NotEqual(MeldendeFehlergrenze.NeueTraceId(), MeldendeFehlergrenze.NeueTraceId());
        Assert.Equal("FehlerMitGenerik_1", MeldendeFehlergrenze.Fehlerart(new FehlerMitGenerik<int>()));
    }

    [Theory]
    [InlineData("https://lager.example/app/a:b;c@d,e+f=g!h$i'j(k)l*m?x=1#oben", "/app/a_b_c_d_e_f_g_h_i_j_k_l_m")]
    [InlineData("https://lager.example/app/freizeiten/0190a0f0-0000-7000-8000-000000000001/personen%20liste", "/app/freizeiten/0190a0f0-0000-7000-8000-000000000001/personen%20liste")]
    public void Seite_enthaelt_nur_die_Zeichen_die_die_API_annimmt(string adresse, string erwartet) =>
        Assert.Equal(erwartet, MeldendeFehlergrenze.Seite(adresse));

    [Fact]
    public void Seite_und_Fehlerart_bleiben_in_den_Laengengrenzen_der_API()
    {
        var seite = MeldendeFehlergrenze.Seite("https://lager.example/" + new string('a', 3000));

        Assert.Equal(2000, seite.Length);
        Assert.Equal(100, MeldendeFehlergrenze.Fehlerart(new FehlerMitSehrLangemNamenDerDieGrenzeDerApiVonHundertZeichenUeberschreitetUndDeshalbGekuerztWerdenMussAmEnde()).Length);
    }

    // ---------- Story 2-8: Eintrag „Notfall“ in der App-Leiste ----------

    private static readonly Guid FreizeitX = Guid.Parse("0199f800-0000-7000-8000-0000000000a1");
    private static readonly Guid FreizeitY = Guid.Parse("0199f800-0000-7000-8000-0000000000a2");

    private void KontextAntwort(Guid freizeit, string phase) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{freizeit}/kontext", () => NachgebildeterHandler.Json(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { freizeitId = freizeit, name = "Zeltlager", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>() })));

    private int Kontextabrufe(Guid freizeit) => _server.Anfragen.Count(a => a.Pfad == $"/api/freizeiten/{freizeit}/kontext");

    [Fact]
    public void Notfall_Eintrag_nur_auf_Seiten_einer_laufenden_Freizeit_und_neu_geladen_nur_beim_Wechsel_der_Freizeit()
    {
        KontextAntwort(FreizeitX, "Laeuft");
        KontextAntwort(FreizeitY, "InVorbereitung");
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = Layout();
        Assert.Empty(cut.FindAll("a.zl-notfall-eintrag"));
        Assert.Equal(0, Kontextabrufe(FreizeitX));

        navigation.NavigateTo($"freizeiten/{FreizeitX}");
        cut.WaitForAssertion(() => Assert.Equal($"freizeiten/{FreizeitX}/notfall", cut.Find("header a.zl-notfall-eintrag").GetAttribute("href")));
        Assert.Equal("Notfall", cut.Find("header a.zl-notfall-eintrag").TextContent);

        navigation.NavigateTo($"freizeiten/{FreizeitX}/freizeitpaesse");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("a.zl-notfall-eintrag")));
        Assert.Equal(1, Kontextabrufe(FreizeitX));

        navigation.NavigateTo($"freizeiten/{FreizeitY}");
        cut.WaitForAssertion(() => Assert.Equal(1, Kontextabrufe(FreizeitY)));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("a.zl-notfall-eintrag")));

        navigation.NavigateTo("konto");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("a.zl-notfall-eintrag")));

        navigation.NavigateTo($"freizeiten/{FreizeitX}/notfall");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("a.zl-notfall-eintrag")));
        Assert.Equal(2, Kontextabrufe(FreizeitX));
    }

    [Fact]
    public void Ohne_Kontext_kein_Notfall_Eintrag()
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitX}/kontext", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"freizeiten/{FreizeitX}");

        var cut = Layout();

        cut.WaitForAssertion(() => Assert.Equal(1, Kontextabrufe(FreizeitX)));
        Assert.Empty(cut.FindAll("a.zl-notfall-eintrag"));
        Assert.Equal("Zeltlotse", cut.Find("main h1").TextContent);
    }

    private sealed class Werfer : ComponentBase
    {
        protected override void OnInitialized() => throw new InvalidOperationException("Erika Muster hat Erdnussallergie");
    }

#pragma warning disable CA1032, CA1064, S3376 // Nur für den Test: Typname mit Backtick.
    private sealed class FehlerMitGenerik<T> : Exception;

    private sealed class FehlerMitSehrLangemNamenDerDieGrenzeDerApiVonHundertZeichenUeberschreitetUndDeshalbGekuerztWerdenMussAmEnde : Exception;
#pragma warning restore CA1032, CA1064, S3376
}
