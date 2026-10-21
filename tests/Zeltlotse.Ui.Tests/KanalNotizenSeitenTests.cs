using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Features.Kanaele;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;
using KanaeleSeite = Zeltlotse.App.Features.Kanaele.Kanaele;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 6-3 in der Web-App mit nachgebildetem Server: die Seiten „Ansagen-Zettel“ und „Besprechung“ (offene Punkte als Kästchen, darunter „Abgehakt“ mit
/// gesetzten Kästchen), Hinzufügen leert das Feld und lädt neu, ein Feldfehler steht am Feld, ein Tipp hakt ab oder öffnet wieder, der Leer-Zustand, nach
/// dem Abschluss ohne Eingabe und mit deaktivierten Kästchen, die Reiter „Nachrichten · Ansagen-Zettel · Besprechung“ (auch auf der Kanal-Seite), eine
/// unbekannte Liste und der Client.
/// </summary>
public sealed class KanalNotizenSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000007d1");
    private static readonly Guid Taschenlampen = Guid.Parse("0199f700-0000-7000-8000-0000000007b1");
    private static readonly Guid Spuelplan = Guid.Parse("0199f700-0000-7000-8000-0000000007b2");
    private static readonly Guid Nachtwache = Guid.Parse("0199f700-0000-7000-8000-0000000007b3");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public KanalNotizenSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddSingleton<IBrowser>(new NachgebildeterBrowser());
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

    private static string Pfad(string art) => $"/api/freizeiten/{Freizeit}/kanaele/notizen/{art}";

    private static string UebergangPfad(Guid notiz, string uebergang) => $"/api/freizeiten/{Freizeit}/kanaele/notizen/{notiz}/{uebergang}";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Notiz(Guid id, string art, string text, string? erledigtAm = null) =>
        new { id, art, text, angelegtAm = "2027-08-03T06:05:00+00:00", erledigtAm };

    /// <summary>Kontext, Kanal „Team“ (für die Reiter auf der Kanal-Seite), Ansagen-Zettel mit einem offenen Punkt und Besprechung mit einem offenen und einem abgehakten.</summary>
    private void Antworten(string phase = "Laeuft")
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kanaele/team/nachrichten", () => Json(new { kanal = "Team", nachrichten = Array.Empty<object>(), personenAlle = 3 }));
        _server.Bei(HttpMethod.Get, Pfad("ansagen"), () => Json(new
        {
            art = "Ansage", offen = new[] { Notiz(Taschenlampen, "Ansage", "Taschenlampen mitbringen") }, abgehakt = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Get, Pfad("besprechung"), () => Json(new
        {
            art = "Besprechung",
            offen = new[] { Notiz(Nachtwache, "Besprechung", "Nachtwache einteilen") },
            abgehakt = new[] { Notiz(Spuelplan, "Besprechung", "Spülplan besprechen", "2027-08-03T19:00:00+00:00") },
        }));
    }

    private IRenderedComponent<KanalNotizen> Rendern(string art = "ansagen")
    {
        Navigation.NavigateTo($"freizeiten/{Freizeit}/kanaele/{art}");
        var cut = Render<KanalNotizen>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.Art, art));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IEnumerable<Aufgezeichnet> Posts(string pfad) => _server.Anfragen.Where(a => a.Methode == HttpMethod.Post && a.Pfad == pfad);

    private int Geladen(string art) => _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad(art));

    private static AngleSharp.Dom.IElement Knopf<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    /// <summary>Die Beschriftungen der Kästchen in einer Card.</summary>
    private static IReadOnlyList<string> Punkte<T>(IRenderedComponent<T> cut, string card)
        where T : IComponent =>
        [.. cut.FindAll($"#{card} label.elkw-choice").Select(l => l.TextContent.Trim())];

    // ---------- Lesen ----------

    [Fact]
    public void Besprechung_zeigt_offene_Punkte_als_Kaestchen_und_darunter_Abgehakt_mit_gesetzten_Kaestchen()
    {
        Antworten();

        var cut = Rendern("besprechung");

        Assert.Equal(("Kanäle", "Besprechung"), (cut.Find(".elkw-kicker").TextContent, cut.Find("h1").TextContent));
        Assert.Equal(["Nachtwache einteilen"], Punkte(cut, "offen"));
        Assert.Equal(["Spülplan besprechen"], Punkte(cut, "abgehakt"));
        Assert.Equal([false], cut.FindAll("#offen input[type=checkbox]").Select(i => i.HasAttribute("checked")));
        Assert.Equal([true], cut.FindAll("#abgehakt input[type=checkbox]").Select(i => i.HasAttribute("checked")));
        Assert.DoesNotContain(cut.FindAll("input[type=checkbox]"), i => i.HasAttribute("disabled"));
        Assert.Equal("Hinzufügen", Knopf(cut, "Hinzufügen").TextContent.Trim());
    }

    [Fact]
    public void Ansagen_Zettel_ohne_abgehakte_Punkte_zeigt_keinen_Bereich_Abgehakt()
    {
        Antworten();

        var cut = Rendern();

        Assert.Equal("Ansagen-Zettel", cut.Find("h1").TextContent);
        Assert.Equal(["Taschenlampen mitbringen"], Punkte(cut, "offen"));
        Assert.Empty(cut.FindAll("#abgehakt"));
    }

    [Fact]
    public void Leer_zeigt_den_Leer_Zustand_und_das_Eingabefeld()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, Pfad("ansagen"), () => Json(new { art = "Ansage", offen = Array.Empty<object>(), abgehakt = Array.Empty<object>() }));

        var cut = Rendern();

        Assert.Equal(KanaeleTexte.KeineNotizenText(NotizArt.Ansage), cut.Find(".elkw-empty p").TextContent);
        Assert.Single(cut.FindAll("#notiz-text"));
        Assert.Empty(cut.FindAll("input[type=checkbox]"));
    }

    [Fact]
    public void Reiter_mit_aria_current_auf_der_Notiz_Seite_und_auf_der_Kanal_Seite()
    {
        Antworten();

        var cut = Rendern("besprechung");

        var reiter = cut.FindAll("nav[aria-label=Kanäle] a");
        Assert.Equal(["Nachrichten", "Ansagen-Zettel", "Besprechung"], reiter.Select(a => a.TextContent));
        Assert.Equal(
            [$"freizeiten/{Freizeit}/kanaele", $"freizeiten/{Freizeit}/kanaele/ansagen", $"freizeiten/{Freizeit}/kanaele/besprechung"],
            reiter.Select(a => a.GetAttribute("href")));
        Assert.Equal([null, null, "page"], reiter.Select(a => a.GetAttribute("aria-current")));

        Navigation.NavigateTo($"freizeiten/{Freizeit}/kanaele");
        var kanal = Render<KanaeleSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        kanal.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", kanal.Markup, StringComparison.Ordinal));
        Assert.Equal(["page", null, null], kanal.FindAll("nav[aria-label=Kanäle] a").Select(a => a.GetAttribute("aria-current")));
    }

    [Fact]
    public void Unbekannte_Liste_ist_nicht_verfuegbar()
    {
        Antworten();

        var cut = Rendern("foo");

        Assert.Contains("Diese Seite ist nicht verfügbar.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("/notizen/", StringComparison.Ordinal));
    }

    [Fact]
    public void Nicht_verfuegbar_wenn_die_Liste_404_liefert()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, Pfad("ansagen"), () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Rendern();

        Assert.Contains("Diese Seite ist nicht verfügbar.", cut.Markup, StringComparison.Ordinal);
    }

    // ---------- Hinzufügen ----------

    [Fact]
    public void Hinzufuegen_sendet_den_Text_leert_das_Feld_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, Pfad("besprechung"), () => Json(Notiz(Guid.CreateVersion7(), "Besprechung", "Wandertag planen"), HttpStatusCode.Created));
        var cut = Rendern("besprechung");
        var geladen = Geladen("besprechung");

        cut.Find("#notiz-text").Input("Wandertag planen");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(Posts(Pfad("besprechung"))));
        using var koerper = JsonDocument.Parse(Posts(Pfad("besprechung")).Single().Koerper!);
        Assert.Equal("Wandertag planen", koerper.RootElement.GetProperty("text").GetString());
        cut.WaitForAssertion(() => Assert.Equal(geladen + 1, Geladen("besprechung")));
        Assert.Equal("", cut.Find("#notiz-text").GetAttribute("value") ?? "");
        Assert.Empty(cut.FindAll(".elkw-field-error"));
    }

    [Fact]
    public void Feldfehler_steht_am_Feld_der_Text_bleibt()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, Pfad("ansagen"), () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"errors":{"text":["Text fehlt."]}}""", "application/problem+json"));
        var cut = Rendern();
        cut.Find("#notiz-text").Input("   ");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Text fehlt.", cut.Find("#notiz-text-fehler").TextContent));
        Assert.Equal("true", cut.Find("#notiz-text").GetAttribute("aria-invalid"));
        Assert.Equal("   ", cut.Find("#notiz-text").GetAttribute("value"));
    }

    // ---------- Abhaken ----------

    [Fact]
    public void Ein_Tipp_auf_einen_offenen_Punkt_hakt_ihn_ab_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, UebergangPfad(Nachtwache, "erledigt"), () => Json(Notiz(Nachtwache, "Besprechung", "Nachtwache einteilen", "2027-08-03T19:30:00+00:00")));
        var cut = Rendern("besprechung");
        var geladen = Geladen("besprechung");

        cut.Find("#offen input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() => Assert.Single(Posts(UebergangPfad(Nachtwache, "erledigt"))));
        cut.WaitForAssertion(() => Assert.Equal(geladen + 1, Geladen("besprechung")));
        Assert.Empty(Posts(UebergangPfad(Nachtwache, "offen")));
    }

    [Fact]
    public void Ein_Tipp_auf_einen_abgehakten_Punkt_oeffnet_ihn_wieder()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, UebergangPfad(Spuelplan, "offen"), () => Json(Notiz(Spuelplan, "Besprechung", "Spülplan besprechen")));
        var cut = Rendern("besprechung");

        cut.Find("#abgehakt input[type=checkbox]").Change(false);

        cut.WaitForAssertion(() => Assert.Single(Posts(UebergangPfad(Spuelplan, "offen"))));
        Assert.Empty(Posts(UebergangPfad(Spuelplan, "erledigt")));
    }

    [Fact]
    public void Scheitert_das_Abhaken_steht_der_Fehler_am_Punkt()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, UebergangPfad(Taschenlampen, "erledigt"), () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = Rendern();

        cut.Find("#offen input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() => Assert.Equal($"Fehler: {KanaeleTexte.NichtGespeichertText}", cut.Find("#offen .elkw-field-error").TextContent));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public void Gelaufen_mit_Meldung_ohne_Eingabefeld_und_mit_deaktivierten_Kaestchen()
    {
        Antworten("Gelaufen");

        var cut = Rendern("besprechung");

        Assert.Contains(KanaeleTexte.NotizenAbgeschlossenText, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#notiz-text"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Hinzufügen");
        var kaestchen = cut.FindAll("input[type=checkbox]");
        Assert.Equal(2, kaestchen.Count);
        Assert.All(kaestchen, k => Assert.True(k.HasAttribute("disabled")));
    }

    /// <summary>Der Kontext meldet beim ersten Laden „läuft“, danach „gelaufen“: Die Freizeit wird abgeschlossen, während die Seite offen ist.</summary>
    private void WirdAbgeschlossen()
    {
        var abrufe = 0;
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase = ++abrufe == 1 ? "Laeuft" : "Gelaufen", rollen = new[] { "Team" },
            schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>(),
        }));
    }

    [Fact]
    public void Hinzufuegen_nach_dem_Abschluss_zeigt_den_Hinweis_einmal_und_blendet_die_Eingabe_aus()
    {
        Antworten();
        WirdAbgeschlossen();
        _server.Bei(HttpMethod.Post, Pfad("ansagen"), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = Rendern();
        cut.Find("#notiz-text").Input("Taschenlampen");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#notiz-text")));
        Assert.Single(cut.FindAll("p"), p => p.TextContent == KanaeleTexte.NotizenAbgeschlossenText);
        Assert.All(cut.FindAll("input[type=checkbox]"), k => Assert.True(k.HasAttribute("disabled")));
    }

    [Fact]
    public void Abhaken_nach_dem_Abschluss_zeigt_den_Hinweis_einmal_und_deaktiviert_die_Kaestchen()
    {
        Antworten();
        WirdAbgeschlossen();
        _server.Bei(HttpMethod.Post, UebergangPfad(Nachtwache, "erledigt"), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = Rendern("besprechung");

        cut.Find("#offen input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#notiz-text")));
        Assert.Single(cut.FindAll("p"), p => p.TextContent == KanaeleTexte.NotizenAbgeschlossenText);
        Assert.Empty(cut.FindAll(".elkw-field-error"));
        Assert.All(cut.FindAll("input[type=checkbox]"), k => Assert.True(k.HasAttribute("disabled")));
    }

    // ---------- Reiterwechsel ----------

    [Fact]
    public void Reiterwechsel_auf_derselben_Seite_leert_den_Entwurf_und_laedt_die_andere_Liste()
    {
        Antworten();
        var cut = Rendern();
        cut.Find("#notiz-text").Input("Taschenlampen");

        cut.Render(ps => ps.Add(p => p.Art, "besprechung"));

        cut.WaitForAssertion(() => Assert.Equal(["Nachtwache einteilen"], Punkte(cut, "offen")));
        Assert.Equal("Besprechung", cut.Find("h1").TextContent);
        Assert.Equal("", cut.Find("#notiz-text").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Alles_abgehakt_zeigt_den_eigenen_Leer_Zustand_und_die_abgehakten_Punkte()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, Pfad("besprechung"), () => Json(new
        {
            art = "Besprechung", offen = Array.Empty<object>(),
            abgehakt = new[] { Notiz(Spuelplan, "Besprechung", "Spülplan besprechen", "2027-08-03T19:00:00+00:00") },
        }));

        var cut = Rendern("besprechung");

        Assert.Equal(KanaeleTexte.AllesAbgehaktText, cut.Find("#offen .elkw-empty p").TextContent);
        Assert.Equal(["Spülplan besprechen"], Punkte(cut, "abgehakt"));
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_ordnet_Antworten_zu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, Pfad("ansagen"), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        _server.Bei(HttpMethod.Post, Pfad("besprechung"), () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"errors":{"text":["Höchstens 500 Zeichen."]}}""", "application/problem+json"));
        _server.Bei(HttpMethod.Post, UebergangPfad(Spuelplan, "offen"), () => Json(Notiz(Spuelplan, "Besprechung", "Spülplan besprechen")));
        var client = Services.GetRequiredService<KanaeleApiClient>();

        var besprechung = await client.NotizenLadenAsync(Freizeit, NotizArt.Besprechung, Abbruch);

        Assert.Equal((NotizArt.Besprechung, 1, 1), (besprechung!.Art, besprechung.Offen.Count, besprechung.Abgehakt.Count));
        Assert.NotNull(besprechung.Abgehakt[0].ErledigtAm);
        Assert.Null(await client.NotizenLadenAsync(Guid.CreateVersion7(), NotizArt.Ansage, Abbruch));
        Assert.Equal(KanalAktionErgebnis.Abgeschlossen, (await client.NotizAnlegenAsync(Freizeit, NotizArt.Ansage, "x", Abbruch)).Ergebnis);
        var fehler = await client.NotizAnlegenAsync(Freizeit, NotizArt.Besprechung, "x", Abbruch);
        Assert.Equal((KanalAktionErgebnis.Fehler, "Höchstens 500 Zeichen."), (fehler.Ergebnis, fehler.Feldfehler[KanalFelder.Text]));
        var offen = await client.NotizUmschaltenAsync(Freizeit, Spuelplan, erledigt: false, Abbruch);
        Assert.Equal((KanalAktionErgebnis.Erfolg, (DateTimeOffset?)null), (offen.Ergebnis, offen.Notiz!.ErledigtAm));
        Assert.Equal($"/api/freizeiten/{Freizeit}/kanaele/notizen/{Spuelplan}/erledigt", KanaeleApiClient.NotizUebergangPfad(Freizeit, Spuelplan, erledigt: true));
    }
}
