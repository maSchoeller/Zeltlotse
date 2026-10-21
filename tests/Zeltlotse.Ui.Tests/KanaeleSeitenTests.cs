using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Kanaele;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;
using KanaeleSeite = Zeltlotse.App.Features.Kanaele.Kanaele;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 6-1 in der Web-App mit nachgebildetem Server und Browser: die Kanal-Seite (Segmente „Team / Alle“, Nachrichten mit Namen, neueste unten, die Seite
/// öffnet am Ende), Senden in „Team“ direkt, in „Alle“ erst nach der Inline-Bestätigung „An N Personen senden?“, Feldfehler, der Knopf „Benachrichtigungen
/// einschalten“ (Erlaubnis erst nach dem Tipp), die Hinweise „iPhone ohne Home-Bildschirm“, „abgelehnt“ und „nicht möglich“, Neuladen bei einem Push,
/// nach dem Abschluss nur lesbar, nicht verfügbar, der Knopf „Kanäle“ auf der Startseite und der Client.
/// </summary>
public sealed class KanaeleSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000006d1");
    private static readonly Guid Jonas = Guid.Parse("0199f700-0000-7000-8000-0000000006a1");
    private static readonly Guid Lea = Guid.Parse("0199f700-0000-7000-8000-0000000006a2");
    private static readonly Guid Fremd = Guid.Parse("0199f700-0000-7000-8000-0000000006a3");

    private readonly NachgebildeterHandler _server = new();
    private readonly NachgebildeterBrowser _browser = new();
    private int _tokens;

    public KanaeleSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddSingleton<IBrowser>(_browser);
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

    private static string Pfad(string kanal) => $"/api/freizeiten/{Freizeit}/kanaele/{kanal}/nachrichten";

    private static string AboPfad => $"/api/freizeiten/{Freizeit}/kanaele/push/abo";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Nachricht(Guid autor, string text, string gesendetAm = "2027-08-03T06:05:00+00:00") =>
        new { id = Guid.CreateVersion7(), autorPersonId = autor, text, gesendetAm };

    private static object Ansicht(string kanal, params object[] nachrichten) => new { kanal, nachrichten, personenAlle = 42 };

    /// <summary>Kontext, Freizeit (Leitung Jonas), Personenliste (Lea Team) und beide Kanäle mit je einer Nachricht.</summary>
    private void Antworten(string phase = "Laeuft")
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase, freigabeBis = "2027-09-12",
            leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner", personId = Jonas } },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(new object[]
        {
            new { anmeldungId = Guid.CreateVersion7(), personId = Lea, vorname = "Lea", nachname = "Feuerstein", art = "Mitarbeitende", status = "Bestaetigt" },
        }));
        _server.Bei(HttpMethod.Get, Pfad("team"), () => Json(Ansicht(
            "Team",
            Nachricht(Jonas, "Feuerholz liegt am Küchenzelt."),
            Nachricht(KanalFelder.GeloeschtePerson, "Alte Nachricht"),
            Nachricht(Fremd, "Wer bin ich?"),
            Nachricht(Lea, "Danke!", "2027-08-03T06:10:00+00:00"))));
        _server.Bei(HttpMethod.Get, Pfad("alle"), () => Json(Ansicht("Alle", Nachricht(Jonas, "Essen 13:30 statt 13:00"))));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kanaele/push/schluessel", () => Json(new { oeffentlicherSchluessel = "BSchluessel" }));
        _server.Bei(HttpMethod.Post, AboPfad, () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, $"{AboPfad}/abmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private IRenderedComponent<KanaeleSeite> Rendern(string? kanal = null)
    {
        Navigation.NavigateTo(kanal is null ? $"freizeiten/{Freizeit}/kanaele" : $"freizeiten/{Freizeit}/kanaele?kanal={kanal}");
        var cut = Render<KanaeleSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IEnumerable<Aufgezeichnet> Posts(string pfad) => _server.Anfragen.Where(a => a.Methode == HttpMethod.Post && a.Pfad == pfad);

    private static AngleSharp.Dom.IElement Knopf<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    // ---------- Lesen ----------

    [Fact]
    public void Team_Kanal_zeigt_Segmente_Nachrichten_mit_Namen_neueste_unten_und_oeffnet_am_Ende_ohne_nach_der_Erlaubnis_zu_fragen()
    {
        Antworten();

        var cut = Rendern();

        Assert.Equal(("Kanäle", "Team"), (cut.Find(".elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var segmente = cut.FindAll(".elkw-segments button");
        Assert.Equal(["Team", "Alle"], segmente.Select(s => s.TextContent));
        Assert.Equal(["true", "false"], segmente.Select(s => s.GetAttribute("aria-pressed")));
        Assert.Equal(
            ["Jonas Hüttner", "Gelöschte Person", "Ohne Namen", "Lea Feuerstein"],
            cut.FindAll("ol.zl-kanal strong").Select(s => s.TextContent));
        Assert.Equal(["JH", "?", "?", "LF"], cut.FindAll("ol.zl-kanal .elkw-avatar").Select(a => a.TextContent));
        Assert.Equal("Danke!", cut.FindAll(".zl-kanal__text")[^1].TextContent);
        Assert.Equal("03.08. 08:10", cut.FindAll("ol.zl-kanal time")[^1].TextContent);
        Assert.Contains("kanal-ende", _browser.AnsEnde);
        Assert.Equal("Senden", Knopf(cut, "Senden").TextContent);
        Assert.Equal("Benachrichtigungen einschalten", Knopf(cut, "Benachrichtigungen einschalten").TextContent);
        Assert.Equal(0, _browser.ErlaubnisGefragt);
        Assert.Empty(cut.FindAll(".elkw-badge, .zl-zaehler"));
    }

    [Fact]
    public void Segment_Alle_wechselt_auf_die_Adresse_mit_kanal_alle_und_zeigt_den_Kanal_Alle()
    {
        Antworten();
        var cut = Rendern();

        cut.FindAll(".elkw-segments button")[1].Click();

        Assert.EndsWith($"freizeiten/{Freizeit}/kanaele?kanal=alle", Navigation.Uri, StringComparison.Ordinal);
        var alle = Rendern("alle");
        Assert.Equal("Alle", alle.Find("h1").TextContent);
        Assert.Equal(["Essen 13:30 statt 13:00"], alle.FindAll(".zl-kanal__text").Select(t => t.TextContent));
        Assert.Equal(["false", "true"], alle.FindAll(".elkw-segments button").Select(s => s.GetAttribute("aria-pressed")));
    }

    [Fact]
    public async Task Ein_Entwurf_bleibt_beim_Neuladen_im_selben_Kanal_geht_aber_beim_Wechsel_nach_Alle_verloren()
    {
        Antworten();
        var cut = Rendern();
        cut.Find("textarea").Input("Nur fürs Team");

        await cut.InvokeAsync(_browser.PushEingangAsync);
        cut.WaitForAssertion(() => Assert.Equal("Nur fürs Team", cut.Find("textarea").GetAttribute("value")));

        cut.FindAll(".elkw-segments button")[1].Click();

        Assert.Equal("", cut.Find("textarea").GetAttribute("value") ?? "");
        // Auch wenn der Kanal ohne Segment wechselt (z. B. „Zurück“ im Browser), gehört der Entwurf zum alten Kanal.
        Navigation.NavigateTo($"freizeiten/{Freizeit}/kanaele");
        cut.Render(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Equal("Team", cut.Find("h1").TextContent));
        cut.Find("textarea").Input("Wieder fürs Team");
        Navigation.NavigateTo($"freizeiten/{Freizeit}/kanaele?kanal=alle");
        cut.Render(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Equal("Alle", cut.Find("h1").TextContent));
        Assert.Equal("", cut.Find("textarea").GetAttribute("value") ?? "");
        Assert.Empty(Posts(Pfad("alle")));
    }

    // ---------- Senden ----------

    [Fact]
    public void Senden_im_Team_geht_direkt_hinaus_leert_das_Feld_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, Pfad("team"), () => Json(Nachricht(Lea, "Bin unterwegs"), HttpStatusCode.Created));
        var cut = Rendern();
        var geladen = _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad("team"));

        cut.Find("textarea").Input("Bin unterwegs");
        Knopf(cut, "Senden").Click();

        cut.WaitForAssertion(() => Assert.Single(Posts(Pfad("team"))));
        using var koerper = JsonDocument.Parse(Posts(Pfad("team")).Single().Koerper!);
        Assert.Equal("Bin unterwegs", koerper.RootElement.GetProperty("text").GetString());
        cut.WaitForAssertion(() => Assert.Equal(geladen + 1, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad("team"))));
        Assert.Equal("", cut.Find("textarea").GetAttribute("value") ?? "");
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
    }

    [Fact]
    public void Senden_in_Alle_klappt_erst_die_Inline_Bestaetigung_auf_Nein_sendet_nichts_Ja_senden_schickt_mit_Bestaetigung()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, Pfad("alle"), () => Json(Nachricht(Lea, "Morgen Wandertag"), HttpStatusCode.Created));
        var cut = Rendern("alle");
        cut.Find("textarea").Input("Morgen Wandertag");

        Knopf(cut, "Senden").Click();

        Assert.Equal("An 42 Personen senden?", cut.Find(".zl-bestaetigung__satz").TextContent);
        Assert.Equal(["Ja, senden", "Nein"], cut.FindAll(".zl-bestaetigung button").Select(b => b.TextContent.Trim()));
        Assert.Empty(Posts(Pfad("alle")));

        Knopf(cut, "Nein").Click();
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
        Assert.Empty(Posts(Pfad("alle")));

        Knopf(cut, "Senden").Click();
        Knopf(cut, "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Single(Posts(Pfad("alle"))));
        using var koerper = JsonDocument.Parse(Posts(Pfad("alle")).Single().Koerper!);
        Assert.Equal(("Morgen Wandertag", true), (koerper.RootElement.GetProperty("text").GetString(), koerper.RootElement.GetProperty("bestaetigt").GetBoolean()));
    }

    [Fact]
    public void Feldfehler_steht_am_Feld_der_Text_bleibt()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, Pfad("team"), () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"errors":{"text":["Text fehlt."]}}""", "application/problem+json"));
        var cut = Rendern();
        cut.Find("textarea").Input("   ");

        Knopf(cut, "Senden").Click();

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Text fehlt.", cut.Find(".elkw-field-error").TextContent));
        Assert.Equal("true", cut.Find("textarea").GetAttribute("aria-invalid"));
    }

    // ---------- Push ----------

    [Fact]
    public void Benachrichtigungen_einschalten_fragt_erst_nach_dem_Tipp_speichert_das_Abo_und_ausschalten_meldet_es_ab()
    {
        Antworten();
        var cut = Rendern();

        Knopf(cut, "Benachrichtigungen einschalten").Click();

        cut.WaitForAssertion(() => Assert.Single(Posts(AboPfad)));
        Assert.Equal((1, "BSchluessel"), (_browser.ErlaubnisGefragt, _browser.Schluessel));
        using (var koerper = JsonDocument.Parse(Posts(AboPfad).Single().Koerper!))
        {
            Assert.Equal(
                ("https://fcm.googleapis.com/fcm/send/geraet", "p256dh-geraet", "auth-geraet"),
                (koerper.RootElement.GetProperty("endpunkt").GetString(), koerper.RootElement.GetProperty("p256dh").GetString(), koerper.RootElement.GetProperty("auth").GetString()));
        }

        cut.WaitForAssertion(() => Assert.Contains(KanaeleTexte.PushAnText, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal));
        Knopf(cut, "Benachrichtigungen ausschalten").Click();

        cut.WaitForAssertion(() => Assert.Single(Posts($"{AboPfad}/abmelden")));
        using (var koerper = JsonDocument.Parse(Posts($"{AboPfad}/abmelden").Single().Koerper!))
        {
            Assert.Equal("https://fcm.googleapis.com/fcm/send/geraet", koerper.RootElement.GetProperty("endpunkt").GetString());
        }

        cut.WaitForAssertion(() => Knopf(cut, "Benachrichtigungen einschalten"));
    }

    [Fact]
    public void Verweigert_jemand_die_Erlaubnis_erscheint_der_Hinweis_abgelehnt_ohne_Abo()
    {
        Antworten();
        _browser.ErlaubnisGeben = false;
        var cut = Rendern();

        Knopf(cut, "Benachrichtigungen einschalten").Click();

        cut.WaitForAssertion(() => Assert.Contains(KanaeleTexte.PushAbgelehntText, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal));
        Assert.Empty(Posts(AboPfad));
    }

    [Fact]
    public void Ist_das_Geraet_schon_angemeldet_wird_das_Abo_fuer_diese_Freizeit_still_erneuert()
    {
        Antworten();
        _browser.Push = PushStatus.An;
        _browser.Abo = new PushAboDaten("https://fcm.googleapis.com/fcm/send/alt", "p", "a");

        var cut = Rendern();

        cut.WaitForAssertion(() => Assert.Single(Posts(AboPfad)));
        Assert.Contains("fcm/send/alt", Posts(AboPfad).Single().Koerper!, StringComparison.Ordinal);
        Assert.Equal(0, _browser.ErlaubnisGefragt);
        Assert.Contains(KanaeleTexte.PushAnText, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Beim_Wechsel_auf_eine_andere_Freizeit_wird_das_Abo_still_fuer_die_neue_Freizeit_erneuert()
    {
        var andere = Guid.Parse("0199f700-0000-7000-8000-0000000006d2");
        Antworten();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{andere}/kontext", () => Json(new
        {
            freizeitId = andere, name = "Herbstlager 2027", phase = "Laeuft", rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{andere}/kanaele/team/nachrichten", () => Json(Ansicht("Team")));
        _server.Bei(HttpMethod.Post, $"/api/freizeiten/{andere}/kanaele/push/abo", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _browser.Push = PushStatus.An;
        _browser.Abo = new PushAboDaten("https://fcm.googleapis.com/fcm/send/alt", "p", "a");
        var cut = Rendern();
        cut.WaitForAssertion(() => Assert.Single(Posts(AboPfad)));

        Navigation.NavigateTo($"freizeiten/{andere}/kanaele");
        cut.Render(ps => ps.Add(p => p.FreizeitId, andere));

        cut.WaitForAssertion(() => Assert.Single(Posts($"/api/freizeiten/{andere}/kanaele/push/abo")));
        Assert.Contains("fcm/send/alt", Posts($"/api/freizeiten/{andere}/kanaele/push/abo").Single().Koerper!, StringComparison.Ordinal);
        Assert.Single(Posts(AboPfad));
        Assert.Equal(0, _browser.ErlaubnisGefragt);
    }

    [Theory]
    [InlineData(PushStatus.Abgelehnt, KanaeleTexte.PushAbgelehntText)]
    [InlineData(PushStatus.NurMitHomeBildschirm, KanaeleTexte.PushHomeBildschirmText)]
    [InlineData(PushStatus.NichtUnterstuetzt, KanaeleTexte.PushNichtMoeglichText)]
    public void Ohne_Push_erscheint_der_passende_Hinweis_und_der_Kanal_bleibt_les_und_beschreibbar(PushStatus status, string hinweis)
    {
        Antworten();
        _browser.Push = status;

        var cut = Rendern();

        Assert.Contains(hinweis, cut.Find("#benachrichtigungen").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Benachrichtigungen einschalten");
        Assert.Equal(4, cut.FindAll("ol.zl-kanal li").Count);
        Assert.Single(cut.FindAll("textarea"));
        Knopf(cut, "Senden");
        if (status == PushStatus.NurMitHomeBildschirm)
        {
            Assert.Contains("Zum Home-Bildschirm", cut.Find("#benachrichtigungen ol").TextContent, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Ein_eingehender_Push_laedt_den_Kanal_neu()
    {
        Antworten();
        var cut = Rendern();
        var geladen = _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad("team"));
        Assert.True(_browser.PushBeobachtet);

        await cut.InvokeAsync(_browser.PushEingangAsync);

        cut.WaitForAssertion(() => Assert.Equal(geladen + 1, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad("team"))));
    }

    // ---------- Abgeschlossen, nicht verfügbar, Startseite ----------

    [Fact]
    public void Gelaufen_nur_lesbar_ohne_Eingabefeld_und_ohne_Benachrichtigungs_Knopf()
    {
        Antworten("Gelaufen");

        var cut = Rendern();

        Assert.Equal(4, cut.FindAll("ol.zl-kanal li").Count);
        Assert.Empty(cut.FindAll("textarea"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() is "Senden" or "Benachrichtigungen einschalten");
        Assert.Contains(KanaeleTexte.AbgeschlossenText, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nicht_verfuegbar_wenn_der_Kanal_404_liefert()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, Pfad("team"), () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Rendern();

        Assert.Contains("Diese Seite ist nicht verfügbar.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Startseite_zeigt_den_Knopf_Kanaele()
    {
        Antworten();

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/kanaele", Assert.Single(cut.FindAll("a"), a => a.TextContent == "Kanäle").GetAttribute("href"));
    }

    [Fact]
    public async Task Client_ordnet_Antworten_zu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, Pfad("alle"), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        _server.Bei(HttpMethod.Post, AboPfad, () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"errors":{"endpunkt":["Nur die Adresse eines bekannten Push-Dienstes (https)."]}}""", "application/problem+json"));
        var client = Services.GetRequiredService<KanaeleApiClient>();

        var team = await client.LadenAsync(Freizeit, Kanal.Team, Abbruch);

        Assert.Equal((Kanal.Team, 4, 42), (team!.Kanal, team.Nachrichten.Count, team.PersonenAlle));
        Assert.Null(await client.LadenAsync(Guid.CreateVersion7(), Kanal.Alle, Abbruch));
        Assert.Equal(KanalAktionErgebnis.Abgeschlossen, (await client.SendenAsync(Freizeit, Kanal.Alle, new NachrichtNeu("x", true), Abbruch)).Ergebnis);
        Assert.Equal(KanalAktionErgebnis.Fehler, await client.AboSpeichernAsync(Freizeit, new PushAboNeu("http://x", "p", "a"), Abbruch));
        Assert.Equal(KanalAktionErgebnis.Erfolg, await client.AbmeldenAsync(Freizeit, "https://fcm.googleapis.com/x", Abbruch));
        Assert.Equal("BSchluessel", await client.SchluesselLadenAsync(Freizeit, Abbruch));
        Assert.Equal($"/api/freizeiten/{Freizeit}/kanaele/alle/nachrichten", KanaeleApiClient.NachrichtenPfad(Freizeit, Kanal.Alle));
    }
}
