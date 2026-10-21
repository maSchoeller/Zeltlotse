using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Features.Kanaele;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Teilnehmer;
using Zeltlotse.App.Layout;
using Zeltlotse.App.Sitzung;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 6-4 in der Web-App mit nachgebildetem Server und Browser: die Seite hinter dem QR-Code (erst „Freizeit öffnen“ löst ein), die Startseite des
/// Teilnehmer-Zugangs mit „Woche / Infos / Alle“ (Termine ohne Link und Status, Dokumente, Nachrichten ohne Eingabefeld mit der Card „Benachrichtigungen“),
/// „Diese Freizeit ist zu Ende.“ bei 404, keine Umleitung zur Anmeldung, das Layout ohne Konto-Link, die Card auf der Anmelden-Seite und der Client.
/// </summary>
public sealed class TeilnehmerSeitenTests : BunitContext
{
    private static readonly Guid Infobrief = Guid.Parse("0199f700-0000-7000-8000-0000000006e1");
    private const string Token = "AbcdefghijAbcdefghijAbcdefghijAbcdefghij123";

    private readonly NachgebildeterHandler _server = new();
    private readonly NachgebildeterBrowser _browser = new();
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2027, 8, 3, 10, 0, 0, TimeSpan.Zero));
    private int _tokens;

    public TeilnehmerSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddSingleton<TimeProvider>(_uhr);
        Services.AddSingleton<IBrowser>(_browser);
        Services.AddScoped<AppVerbindung>();
        Services.AddScoped<IVerbindung>(sp => sp.GetRequiredService<AppVerbindung>());
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private IEnumerable<Aufgezeichnet> Posts(string pfad) => _server.Anfragen.Where(a => a.Methode == HttpMethod.Post && a.Pfad == pfad);

    private static AngleSharp.Dom.IElement Knopf<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    /// <summary>Eine gültige Gast-Sitzung: Freizeit, Woche (zwei Tage), Infos und Kanal „Alle“.</summary>
    private void Antworten()
    {
        _server.Bei(HttpMethod.Get, "/api/teilnehmer", () => Json(new { name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-03" }));
        _server.Bei(HttpMethod.Get, "/api/teilnehmer/programm", () => Json(new
        {
            ersterTag = "2027-08-02",
            letzterTag = "2027-08-03",
            punkte = new object[]
            {
                new { tag = "2027-08-02", beginn = "08:00", titel = "Frühstück" },
                new { tag = "2027-08-03", beginn = "14:30", titel = "Geländespiel" },
                new { tag = "2027-08-03", beginn = (string?)null, titel = "Nachtwanderung" },
            },
        }));
        _server.Bei(HttpMethod.Get, "/api/teilnehmer/infos", () => Json(new { dokumente = new[] { new { id = Infobrief, titel = "Infobrief", dateiart = "PDF" } } }));
        _server.Bei(HttpMethod.Get, "/api/teilnehmer/kanal", () => Json(new
        {
            nachrichten = new[] { new { id = Guid.CreateVersion7(), text = "Essen 13:30 statt 13:00", gesendetAm = "2027-08-03T09:05:00+00:00" } },
        }));
        _server.Bei(HttpMethod.Get, "/api/teilnehmer/push/schluessel", () => Json(new { oeffentlicherSchluessel = "BGast" }));
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/push/abo", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/push/abo/abmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private IRenderedComponent<TeilnehmerStart> Start(string? abfrage = null)
    {
        Navigation.NavigateTo(abfrage is null ? "teilnehmer" : $"teilnehmer?{abfrage}");
        var cut = Render<TeilnehmerStart>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    // ---------- Einlösen ----------

    [Fact]
    public void Der_Aufruf_zeigt_nur_die_Seite_erst_Freizeit_oeffnen_loest_ein_und_fuehrt_zur_Startseite()
    {
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Navigation.NavigateTo($"teilnehmer/{Token}");

        var cut = Render<TeilnehmerEinloesen>(ps => ps.Add(p => p.Token, Token));

        Assert.Equal(TeilnehmerTexte.Ueberschrift, cut.Find("h1").TextContent);
        Assert.Contains(TeilnehmerTexte.Erklaerung, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.StartsWith("/api/teilnehmer", StringComparison.Ordinal));

        Knopf(cut, "Freizeit öffnen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/teilnehmer", Navigation.Uri, StringComparison.Ordinal));
        using var koerper = JsonDocument.Parse(Assert.Single(Posts("/api/teilnehmer/einloesen")).Koerper!);
        Assert.Equal(Token, koerper.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public void Gilt_das_Token_nicht_steht_Diese_Freizeit_ist_zu_Ende_mit_dem_Hinweis_auf_einen_neuen_Scan()
    {
        var cut = Render<TeilnehmerEinloesen>(ps => ps.Add(p => p.Token, Token));

        Knopf(cut, "Freizeit öffnen").Click();

        cut.WaitForAssertion(() => Assert.Equal(TeilnehmerTexte.EndeText, cut.Find("h1").TextContent));
        Assert.Contains("Wurde der QR-Code erneuert, hilft ein neuer Scan am Infobrett. Sonst hat die Leitung den Zugang ausgeschaltet.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("anmelden", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Bei_429_bittet_die_Seite_gleich_noch_einmal_zu_tippen_und_bleibt_bereit()
    {
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/einloesen", () => NachgebildeterHandler.Problem(HttpStatusCode.TooManyRequests, "zugriffsbegrenzt"));
        var cut = Render<TeilnehmerEinloesen>(ps => ps.Add(p => p.Token, Token));

        Knopf(cut, "Freizeit öffnen").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            "Gerade wollen viele gleichzeitig hinein. Bitte in einer Minute noch einmal tippen.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal(TeilnehmerTexte.Ueberschrift, cut.Find("h1").TextContent);
        Assert.False(Knopf(cut, "Freizeit öffnen").HasAttribute("disabled"));

        // Ein zweiter Tipp nach der Minute klappt.
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Knopf(cut, "Freizeit öffnen").Click();
        cut.WaitForAssertion(() => Assert.EndsWith("/teilnehmer", Navigation.Uri, StringComparison.Ordinal));
    }

    // ---------- Startseite ----------

    [Theory]
    [InlineData("tag=2027-08-02", "Montag, 2. August 2027", "Frühstück")]
    [InlineData("tag=2027-08-03", "Dienstag, 3. August 2027", "Geländespiel")]
    [InlineData("tag=2027-09-01", "Dienstag, 3. August 2027", "Geländespiel")]
    [InlineData("tag=kein-datum", "Dienstag, 3. August 2027", "Geländespiel")]
    public void Der_Tag_aus_der_Adresse_wird_gezeigt_ein_ungueltiger_oder_fremder_Tag_faellt_auf_heute_zurueck(string abfrage, string ueberschrift, string ersterTermin)
    {
        Antworten();

        var cut = Start(abfrage);

        Assert.Equal(ueberschrift, cut.Find(".zl-nur-handy h2").TextContent);
        Assert.Equal(ersterTermin, cut.FindAll(".zl-nur-handy .zl-termin h3")[0].TextContent.Trim());
        var aktiv = Assert.Single(cut.FindAll("nav[aria-label=Tage] a"), a => a.GetAttribute("aria-current") == "page");
        Assert.Equal(ueberschrift.StartsWith("Montag", StringComparison.Ordinal) ? "teilnehmer?tag=2027-08-02" : "teilnehmer?tag=2027-08-03", aktiv.GetAttribute("href"));
    }

    [Fact]
    public void Woche_zeigt_Reiter_Woche_Infos_Alle_und_Termine_nur_mit_Uhrzeit_und_Titel_ohne_Link_und_Status()
    {
        Antworten();

        var cut = Start();

        Assert.Equal(("Teilnehmer-Zugang", "Zeltlager Sommer 2027"), (cut.Find(".elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var bereiche = cut.FindAll("nav[aria-label=Bereiche] a");
        Assert.Equal(["Woche", "Infos", "Alle"], bereiche.Select(a => a.TextContent));
        Assert.Equal(["teilnehmer", "teilnehmer?bereich=infos", "teilnehmer?bereich=alle"], bereiche.Select(a => a.GetAttribute("href")));
        Assert.Equal(["page", null, null], bereiche.Select(a => a.GetAttribute("aria-current")));
        // Handy: heute (03.08.) als Tag gewählt; Laptop: beide Tage nebeneinander.
        Assert.Equal(["teilnehmer?tag=2027-08-02", "teilnehmer?tag=2027-08-03"], cut.FindAll("nav[aria-label=Tage] a").Select(a => a.GetAttribute("href")));
        Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-nur-handy h2").TextContent);
        Assert.Equal(["Geländespiel", "Nachtwanderung"], cut.FindAll(".zl-nur-handy .zl-termin h3").Select(h => h.TextContent.Trim()));
        Assert.Equal(2, cut.FindAll(".zl-tagesspalte").Count);
        // Am Handy die zwei Termine des Tages, am Laptop alle drei.
        Assert.Equal(5, cut.FindAll(".zl-termin").Count);
        Assert.Empty(cut.FindAll(".zl-termin a"));
        Assert.Empty(cut.FindAll(".zl-termin__status, .zl-termin button"));
        Assert.Equal("14:30", cut.FindAll(".zl-nur-handy .zl-termin__zeit")[0].TextContent);
    }

    [Fact]
    public void Infos_listet_die_Dokumente_als_Links_auf_die_Datei()
    {
        Antworten();

        var cut = Start("bereich=infos");

        var link = Assert.Single(cut.FindAll("main a, .zl-reiter__inhalt a"), a => a.TextContent == "Infobrief (PDF)");
        Assert.Equal($"/api/teilnehmer/infos/{Infobrief}", link.GetAttribute("href"));
        Assert.Equal("page", cut.FindAll("nav[aria-label=Bereiche] a")[1].GetAttribute("aria-current"));
    }

    [Fact]
    public void Alle_zeigt_die_Nachrichten_mit_Team_als_Absender_ohne_Eingabefeld_und_die_Card_Benachrichtigungen()
    {
        Antworten();

        var cut = Start("bereich=alle");

        Assert.Equal(["Team"], cut.FindAll("ol.zl-kanal strong").Select(s => s.TextContent));
        Assert.Equal("Essen 13:30 statt 13:00", cut.Find(".zl-kanal__text").TextContent);
        Assert.Empty(cut.FindAll("textarea"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Senden");
        Assert.Contains(TeilnehmerTexte.PushAusText, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal);
        Assert.Equal(0, _browser.ErlaubnisGefragt);
    }

    [Fact]
    public void Benachrichtigungen_einschalten_speichert_das_Gast_Abo_ausschalten_meldet_es_ab()
    {
        Antworten();
        var cut = Start("bereich=alle");

        Knopf(cut, "Benachrichtigungen einschalten").Click();

        cut.WaitForAssertion(() => Assert.Single(Posts("/api/teilnehmer/push/abo")));
        Assert.Equal((1, "BGast"), (_browser.ErlaubnisGefragt, _browser.Schluessel));
        Assert.Contains("fcm/send/geraet", Posts("/api/teilnehmer/push/abo").Single().Koerper!, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Contains(KanaeleTexte.PushAnText, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal));

        Knopf(cut, "Benachrichtigungen ausschalten").Click();

        cut.WaitForAssertion(() => Assert.Single(Posts("/api/teilnehmer/push/abo/abmelden")));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("/kanaele/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PushStatus.NurMitHomeBildschirm, KanaeleTexte.PushHomeBildschirmText)]
    [InlineData(PushStatus.Abgelehnt, KanaeleTexte.PushAbgelehntText)]
    [InlineData(PushStatus.NichtUnterstuetzt, KanaeleTexte.PushNichtMoeglichText)]
    public void Ohne_Push_erscheint_der_passende_Hinweis_und_die_Nachrichten_bleiben_lesbar(PushStatus status, string hinweis)
    {
        Antworten();
        _browser.Push = status;

        var cut = Start("bereich=alle");

        Assert.Contains(hinweis, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal);
        Assert.Contains(TeilnehmerTexte.NachrichtenBleibenText, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("ol.zl-kanal li"));
    }

    [Fact]
    public async Task Hat_das_Geraet_schon_ein_Abo_wird_es_still_fuer_den_Gast_erneuert_ein_Push_laedt_die_Nachrichten_neu()
    {
        Antworten();
        _browser.Push = PushStatus.An;
        _browser.Abo = new PushAboDaten("https://fcm.googleapis.com/fcm/send/alt", "p", "a");

        var cut = Start("bereich=alle");

        cut.WaitForAssertion(() => Assert.Single(Posts("/api/teilnehmer/push/abo")));
        var geladen = _server.Anfragen.Count(a => a.Pfad == "/api/teilnehmer/kanal");
        await cut.InvokeAsync(_browser.PushEingangAsync);
        cut.WaitForAssertion(() => Assert.Equal(geladen + 1, _server.Anfragen.Count(a => a.Pfad == "/api/teilnehmer/kanal")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bereich=infos")]
    [InlineData("bereich=alle")]
    public void Ohne_gueltige_Gast_Sitzung_steht_Diese_Freizeit_ist_zu_Ende_ohne_Umleitung_zur_Anmeldung(string? abfrage)
    {
        var cut = Start(abfrage);

        Assert.Equal(TeilnehmerTexte.EndeText, cut.Find("h1").TextContent);
        Assert.Contains(TeilnehmerTexte.EndeZusatz, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("anmelden", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Faellt_der_Zugang_zwischen_zwei_Bereichen_weg_steht_ebenfalls_zu_Ende()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, "/api/teilnehmer/infos", () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Start("bereich=infos");

        Assert.Equal(TeilnehmerTexte.EndeText, cut.Find("h1").TextContent);
    }

    [Fact]
    public async Task Eine_Antwort_401_fuehrt_im_Teilnehmer_Zugang_nie_zur_Anmeldung_sonst_schon()
    {
        var sitzung = Services.GetRequiredService<AppSitzung>();

        foreach (var pfad in new[] { "teilnehmer", "teilnehmer?bereich=alle", $"teilnehmer/{Token}" })
        {
            Navigation.NavigateTo(pfad);
            await sitzung.AbgelaufenAsync();
            Assert.EndsWith($"/{pfad}", Navigation.Uri, StringComparison.Ordinal);
        }

        Navigation.NavigateTo("teilnehmerliste");
        await sitzung.AbgelaufenAsync();
        Assert.EndsWith("/anmelden", Navigation.Uri, StringComparison.Ordinal);
        Assert.True(AppSitzung.IstTeilnehmerPfad("Teilnehmer"));
        Assert.False(AppSitzung.IstTeilnehmerPfad("freizeiten/x/teilnehmer"));
    }

    // ---------- Layout und Anmelden ----------

    [Fact]
    public void Layout_zeigt_auf_der_Startseite_den_Namen_der_Freizeit_ohne_Konto_Link_und_ohne_Navigation()
    {
        Antworten();
        Navigation.NavigateTo("teilnehmer");

        var cut = Render<TeilnehmerLayout>(ps => ps.Add(p => p.Body, b => b.AddMarkupContent(0, "<h1>Inhalt</h1>")));

        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("header .zl-freizeitname").TextContent));
        Assert.Equal("teilnehmer", cut.Find("header a.zl-wortmarke").GetAttribute("href"));
        Assert.Empty(cut.FindAll("a.zl-konto-link, .zl-notfall-eintrag, .elkw-app__nav, .zl-app__unten"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.StartsWith("/api/ich", StringComparison.Ordinal) || a.Pfad.StartsWith("/api/freizeiten", StringComparison.Ordinal));
    }

    [Fact]
    public void Layout_laedt_auf_der_Seite_mit_dem_Token_keinen_Namen()
    {
        Antworten();
        Navigation.NavigateTo($"teilnehmer/{Token}");

        var cut = Render<TeilnehmerLayout>(ps => ps.Add(p => p.Body, b => b.AddMarkupContent(0, "<h1>Inhalt</h1>")));

        Assert.Empty(cut.FindAll(".zl-freizeitname"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == "/api/teilnehmer");
    }

    [Fact]
    public void Anmelden_zeigt_mit_gueltiger_Gast_Sitzung_die_Card_Zum_Teilnehmer_Zugang_sonst_nicht()
    {
        var ohne = Render<Anmelden>();
        Assert.Empty(ohne.FindAll("#teilnehmer-zugang"));

        Antworten();
        var mit = Render<Anmelden>();

        mit.WaitForAssertion(() => Assert.NotNull(mit.Find("#teilnehmer-zugang")));
        Assert.Contains("Zeltlager Sommer 2027", mit.Find("#teilnehmer-zugang").TextContent, StringComparison.Ordinal);
        var knopf = mit.Find("#teilnehmer-zugang a");
        Assert.Equal(("Zum Teilnehmer-Zugang", "teilnehmer"), (knopf.TextContent.Trim(), knopf.GetAttribute("href")));
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_ordnet_Antworten_zu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/push/abo", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"errors":{"endpunkt":["Nur die Adresse eines bekannten Push-Dienstes (https)."]}}""", "application/problem+json"));
        var client = Services.GetRequiredService<TeilnehmerApiClient>();

        Assert.Equal(TeilnehmerEinloesung.Eingeloest, await client.EinloesenAsync(Token, Abbruch));
        Assert.Equal("Zeltlager Sommer 2027", (await client.LadenAsync(Abbruch))!.Name);
        Assert.Equal(3, (await client.WocheLadenAsync(Abbruch))!.Punkte.Count);
        Assert.Equal("Infobrief", (await client.InfosLadenAsync(Abbruch))!.Dokumente.Single().Titel);
        Assert.Equal("Essen 13:30 statt 13:00", (await client.KanalLadenAsync(Abbruch))!.Nachrichten.Single().Text);
        Assert.Equal("BGast", await client.SchluesselLadenAsync(Abbruch));
        Assert.Equal(KanalAktionErgebnis.Fehler, await client.AboSpeichernAsync(new PushAboNeu("http://x", "p", "a"), Abbruch));
        Assert.Equal(KanalAktionErgebnis.Erfolg, await client.AbmeldenAsync("https://fcm.googleapis.com/x", Abbruch));
        Assert.Equal($"/api/teilnehmer/infos/{Infobrief}", TeilnehmerApiClient.InfoDateiPfad(Infobrief));

        _server.Bei(HttpMethod.Post, "/api/teilnehmer/einloesen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Get, "/api/teilnehmer", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal(TeilnehmerEinloesung.NichtGefunden, await client.EinloesenAsync(Token, Abbruch));
        _server.Bei(HttpMethod.Post, "/api/teilnehmer/einloesen", () => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        Assert.Equal(TeilnehmerEinloesung.ZuVieleAnfragen, await client.EinloesenAsync(Token, Abbruch));
        Assert.Null(await client.LadenAsync(Abbruch));
    }
}
