using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using KetteSeite = Zeltlotse.App.Features.Sicherheit.Sicherheitskette;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-8 in der Web-App mit nachgebildetem Server: die Seite „Sicherheitskette“ (eingerichtet, nicht eingerichtet, Schnellsuche mit
/// und ohne Treffer, nicht verfügbar, Personenliste scheitert), die Startseite der Freizeit (Notfall-Box und Schnellsuche nur während der
/// Freizeit, Knopf „Sicherheitskette“), die Card „Sicherheitskette“ in den Einstellungen und der Client.
/// </summary>
public sealed class SicherheitsketteSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000000d1");
    private static readonly Guid Emma = Guid.Parse("0199f700-0000-7000-8000-0000000000e1");
    private static readonly Guid Lea = Guid.Parse("0199f700-0000-7000-8000-0000000000e2");

    private const string WasTun = "Ruhe bewahren und bei der verletzten Person bleiben.\nBei Lebensgefahr sofort den Notruf 112 wählen.\n\nLeitung anrufen und Bescheid geben.\nErste Hilfe leisten.";

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public SicherheitsketteSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
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

    private static string KettePfad => $"/api/freizeiten/{Freizeit}/sicherheit/sicherheitskette";

    private static string ListePfad => $"/api/freizeiten/{Freizeit}/personenliste";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Eingerichtet() => new
    {
        wasTun = WasTun, leitungTelefon = "0123 456782", heimatName = "Petra Heimann (Gemeindebüro)", heimatTelefon = "0123 456783", eingerichtet = true, version = 7u,
    };

    private static object Leer() => new { wasTun = (string?)null, leitungTelefon = (string?)null, heimatName = (string?)null, heimatTelefon = (string?)null, eingerichtet = false, version = 0u };

    private void Antworten(string phase = "InVorbereitung", string[]? erlaubt = null, Func<HttpResponseMessage>? kette = null, Func<HttpResponseMessage>? liste = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(),
            erlaubt = erlaubt ?? [Aktionsschluessel.FreizeitpaesseSehen],
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase, freigabeBis = "2027-09-12",
            leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner" }, new { vorname = "Miriam", nachname = "Zeltner" } },
        }));
        _server.Bei(HttpMethod.Get, KettePfad, kette ?? (() => Json(Eingerichtet())));
        _server.Bei(HttpMethod.Get, ListePfad, liste ?? (() => Json(new object[]
        {
            new { anmeldungId = Guid.CreateVersion7(), personId = Emma, vorname = "Emma", nachname = "Muster", art = "Teilnehmende", status = "Bestaetigt" },
            new { anmeldungId = Guid.CreateVersion7(), personId = Lea, vorname = "Lea", nachname = "Feuerstein", art = "Mitarbeitende", status = "Bestaetigt" },
        })));
    }

    private IRenderedComponent<KetteSeite> Seite()
    {
        var cut = Render<KetteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement? Card(IRenderedComponent<KetteSeite> cut, string titel) =>
        cut.FindAll(".elkw-card").SingleOrDefault(c => c.QuerySelector(".elkw-card__title")?.TextContent == titel);

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_die_Kette_und_403_404_heissen_nicht_verfuegbar()
    {
        Antworten();
        var client = Services.GetRequiredService<SicherheitApiClient>();

        var kette = await client.SicherheitsketteLadenAsync(Freizeit, Abbruch);

        Assert.Equal(new Sicherheitskette(WasTun, "0123 456782", "Petra Heimann (Gemeindebüro)", "0123 456783", true, 7), kette);
        Assert.Equal(KettePfad, SicherheitApiClient.SicherheitskettePfad(Freizeit));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, KettePfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.SicherheitsketteLadenAsync(Freizeit, Abbruch));
        }

        Assert.Equal("tel:0123456782", Anzeige.TelefonHref("0123 456782"));
        Assert.Equal("tel:+49123456782", Anzeige.TelefonHref("+49 (123) 456-782"));
        Assert.Equal(("EM", "L", "?"), (Anzeige.Initialen("Emma", "Muster"), Anzeige.Initialen(" lea ", null), Anzeige.Initialen(null, " ")));
    }

    // ---------- Seite „Sicherheitskette“ ----------

    [Fact]
    public void Eingerichtet_zeigt_Suche_oben_die_Schritte_als_Liste_und_wen_anrufen_mit_Telefon_Links()
    {
        Antworten();

        var cut = Seite();

        Assert.Equal("Notfall", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Sicherheitskette", cut.Find("h1").TextContent);
        var suche = cut.Find("input[type=search]");
        Assert.Equal("Betroffene Person suchen", cut.Find($"label[for='{suche.Id}']").TextContent.Trim());
        Assert.Equal("off", suche.GetAttribute("autocomplete"));
        // Das Suchfeld steht vor den Cards.
        Assert.True(cut.Markup.IndexOf("type=\"search\"", StringComparison.Ordinal) < cut.Markup.IndexOf("Was jetzt zu tun ist", StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-alert"));

        var schritte = Card(cut, "Was jetzt zu tun ist")!.QuerySelectorAll("ol li").Select(l => l.TextContent).ToList();
        Assert.Equal(
            [
                "Ruhe bewahren und bei der verletzten Person bleiben.", "Bei Lebensgefahr sofort den Notruf 112 wählen.",
                "Leitung anrufen und Bescheid geben.", "Erste Hilfe leisten.",
            ],
            schritte);

        var anrufen = Card(cut, "Wen anrufen")!;
        var links = anrufen.QuerySelectorAll("a").Select(a => (a.TextContent, a.GetAttribute("href"))).ToList();
        Assert.Equal(
            [("Notruf 112", "tel:112"), ("Leitung anrufen: 0123 456782", "tel:0123456782"), ("Ansprechperson anrufen: 0123 456783", "tel:0123456783")],
            links);
        Assert.All(anrufen.QuerySelectorAll("a"), a => Assert.Contains("zl-gross", a.ClassList));
        var texte = anrufen.QuerySelectorAll("p").Select(p => p.TextContent.Trim()).ToList();
        Assert.Contains("Leitung: Jonas Hüttner, Miriam Zeltner", texte);
        Assert.Contains("Ansprechperson am Heimatort: Petra Heimann (Gemeindebüro)", texte);
    }

    [Fact]
    public void Nicht_eingerichtet_zeigt_den_Hinweis_Suchfeld_Leitung_und_Notruf_112()
    {
        Antworten(kette: () => Json(Leer()));

        var cut = Seite();

        var hinweis = cut.Find(".elkw-alert");
        Assert.Equal("Hinweis:", hinweis.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Contains("Die Sicherheitskette ist noch nicht eingerichtet.", hinweis.TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("input[type=search]"));
        Assert.Null(Card(cut, "Was jetzt zu tun ist"));
        var anrufen = Card(cut, "Wen anrufen")!;
        Assert.Equal([("Notruf 112", "tel:112")], anrufen.QuerySelectorAll("a").Select(a => (a.TextContent, a.GetAttribute("href"))));
        Assert.Contains("Leitung: Jonas Hüttner, Miriam Zeltner", anrufen.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Heimatort", anrufen.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Schnellsuche_trifft_ab_dem_ersten_Zeichen_und_der_Treffer_oeffnet_den_Freizeitpass()
    {
        Antworten();
        var cut = Seite();
        Assert.Empty(cut.FindAll(".elkw-people"));

        cut.Find("input[type=search]").Input("em");

        var treffer = cut.Find(".elkw-people");
        var link = treffer.QuerySelector("a")!;
        Assert.Equal(("Emma Muster", $"freizeiten/{Freizeit}/freizeitpaesse/{Emma}"), (link.TextContent, link.GetAttribute("href")));
        Assert.Equal("EM", treffer.QuerySelector(".elkw-avatar")!.TextContent);
        Assert.Equal("Teilnehmende", treffer.QuerySelector(".elkw-people__who span")!.TextContent);
        Assert.Single(treffer.QuerySelectorAll("li"));
        Assert.Equal("1 Person gefunden.", cut.Find("p[role=status]").TextContent);

        cut.Find("input[type=search]").Input("e");
        Assert.Equal("2 Personen gefunden.", cut.Find("p[role=status]").TextContent);
        Assert.Equal(2, cut.FindAll(".elkw-people li").Count);

        cut.Find("input[type=search]").Input("FEUER");
        Assert.Equal("Mitarbeitende", cut.Find(".elkw-people .elkw-people__who span").TextContent);

        cut.Find("input[type=search]").Input("xy");
        Assert.Empty(cut.FindAll(".elkw-people"));
        Assert.Equal("Keine Person gefunden.", cut.Find("p[role=status]").TextContent);

        cut.Find("input[type=search]").Input("");
        Assert.Empty(cut.FindAll(".elkw-people"));
        Assert.Empty(cut.FindAll("p[role=status]"));
    }

    [Fact]
    public void Scheitert_die_Personenliste_entfaellt_nur_die_Suche()
    {
        Antworten(liste: () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = Seite();

        Assert.Equal("Sicherheitskette", cut.Find("h1").TextContent);
        Assert.Empty(cut.FindAll("input[type=search]"));
        Assert.NotNull(Card(cut, "Was jetzt zu tun ist"));
        Assert.NotNull(Card(cut, "Wen anrufen"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Ohne_Kontext_oder_ohne_Kette_ist_die_Seite_nicht_verfuegbar(bool kontextFehlt, bool ketteFehlt)
    {
        Antworten(kette: ketteFehlt ? () => new HttpResponseMessage(HttpStatusCode.NotFound) : null);
        if (kontextFehlt)
        {
            _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var cut = Render<KetteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }

    [Fact]
    public void Scheitert_das_Laden_zeigt_die_Seite_die_Fehlermeldung_und_trotzdem_Notruf_112()
    {
        Antworten(kette: () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = Render<KetteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Sicherheitskette", cut.Find("h1").TextContent);
        Assert.Equal("Notruf 112", cut.Find("a[href='tel:112']").TextContent);
        Assert.Empty(cut.FindAll(".elkw-loading"));
    }

    [Fact]
    public void Ansprechperson_am_Heimatort_steht_auch_ohne_Telefon_da_aber_ohne_Link()
    {
        Antworten(kette: () => Json(new
        {
            wasTun = (string?)null, leitungTelefon = (string?)null, heimatName = (string?)"Petra Heimann (Gemeindebüro)", heimatTelefon = (string?)null,
            eingerichtet = false, version = 2u,
        }));

        var cut = Seite();

        var anrufen = Card(cut, "Wen anrufen")!;
        Assert.Contains("Ansprechperson am Heimatort: Petra Heimann (Gemeindebüro)", anrufen.QuerySelectorAll("p").Select(p => p.TextContent.Trim()));
        Assert.Equal(["tel:112"], anrufen.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unter_Wen_anrufen_steht_der_Knopf_Notfallordner_drucken_nur_mit_freizeitpaesse_sehen(bool erlaubt)
    {
        Antworten(erlaubt: erlaubt ? [Aktionsschluessel.FreizeitpaesseSehen] : []);

        var cut = Seite();

        var knopf = cut.FindAll("a.elkw-btn").Where(a => a.TextContent == "Notfallordner drucken").ToList();
        Assert.Equal(erlaubt ? 1 : 0, knopf.Count);
        if (erlaubt)
        {
            Assert.Equal($"freizeiten/{Freizeit}/notfallordner", knopf[0].GetAttribute("href"));
            Assert.DoesNotContain("elkw-btn--noarrow", knopf[0].ClassName, StringComparison.Ordinal);
            Assert.True(cut.Markup.IndexOf("Wen anrufen", StringComparison.Ordinal) < cut.Markup.IndexOf("Notfallordner drucken", StringComparison.Ordinal));
        }
    }

    // ---------- Startseite der Freizeit ----------

    private IRenderedComponent<FreizeitStart> Startseite()
    {
        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        return cut;
    }

    [Fact]
    public void Startseite_waehrend_der_Freizeit_zeigt_die_Notfall_Box_direkt_unter_der_Ueberschrift_und_die_Schnellsuche()
    {
        Antworten(phase: "Laeuft");

        var cut = Startseite();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("input[type=search]")));
        var box = cut.Find(".zl-notfall-box");
        Assert.Equal("h1", box.PreviousElementSibling!.LocalName);
        Assert.Contains("Was jetzt zu tun ist, wen anrufen und die Freizeitpässe – ein Tipp entfernt.", box.TextContent, StringComparison.Ordinal);
        var knopf = box.QuerySelector("a.elkw-btn")!;
        Assert.Equal(("Notfallhilfe öffnen", $"freizeiten/{Freizeit}/notfall"), (knopf.TextContent, knopf.GetAttribute("href")));
        Assert.Equal("Notruf 112", box.QuerySelector("a[href='tel:112']")!.TextContent);
        var suche = cut.Find("input[type=search]");
        Assert.Equal("Schnellsuche", cut.Find($"label[for='{suche.Id}']").TextContent.Trim());
        Assert.True(cut.Markup.IndexOf("zl-notfall-box", StringComparison.Ordinal) < cut.Markup.IndexOf("type=\"search\"", StringComparison.Ordinal));

        suche.Input("em");
        Assert.Equal($"freizeiten/{Freizeit}/freizeitpaesse/{Emma}", cut.Find(".elkw-people a").GetAttribute("href"));
        Assert.Contains(cut.FindAll("a"), a => a.TextContent == "Sicherheitskette" && a.GetAttribute("href") == $"freizeiten/{Freizeit}/notfall");
    }

    [Theory]
    [InlineData("InVorbereitung")]
    [InlineData("Nachbereitung")]
    [InlineData("Gelaufen")]
    public void Startseite_ausserhalb_der_Freizeit_ohne_Notfall_Box_und_Suche_aber_mit_dem_Knopf_Sicherheitskette(string phase)
    {
        Antworten(phase: phase);

        var cut = Startseite();

        Assert.Empty(cut.FindAll(".zl-notfall-box"));
        Assert.Empty(cut.FindAll("input[type=search]"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == ListePfad);
        var knopf = cut.FindAll("a").Single(a => a.TextContent == "Sicherheitskette");
        Assert.Equal($"freizeiten/{Freizeit}/notfall", knopf.GetAttribute("href"));
        // Das Team lädt die Kette nicht (kein Cockpit).
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == KettePfad);
    }

    // ---------- Einstellungen ----------

    [Fact]
    public void Einstellungen_haben_die_Card_Sicherheitskette_mit_vier_Autosave_Feldern()
    {
        Antworten(erlaubt: [Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/freizeitseite", () => Json(new { version = 3u, anmeldelinks = Array.Empty<object>() }));

        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll(".elkw-card").SingleOrDefault(c => c.Id == "sicherheitskette")));
        var card = cut.Find("#sicherheitskette");
        Assert.Equal("Sicherheitskette", card.QuerySelector(".elkw-card__title")!.TextContent);
        Assert.Contains("Erscheint im Notfall bei Leitung und Team.", card.TextContent, StringComparison.Ordinal);
        var felder = card.QuerySelectorAll(".elkw-field").Select(f => (f.QuerySelector("label")!.TextContent.Trim(), (f.QuerySelector("textarea") ?? f.QuerySelector("input"))!)).ToList();
        Assert.Equal(
            ["Was jetzt zu tun ist", "Telefon der Leitung im Notfall", "Ansprechperson am Heimatort", "Telefon der Ansprechperson am Heimatort"],
            felder.Select(f => f.Item1));
        Assert.Equal("textarea", felder[0].Item2.LocalName);
        Assert.Equal(WasTun, felder[0].Item2.GetAttribute("value") ?? felder[0].Item2.TextContent);
        Assert.Contains("Ein Schritt je Zeile.", card.TextContent, StringComparison.Ordinal);
        Assert.Equal(("tel", "tel"), (felder[1].Item2.GetAttribute("type"), felder[3].Item2.GetAttribute("type")));
        Assert.Equal("Petra Heimann (Gemeindebüro)", felder[2].Item2.GetAttribute("value"));
    }

    [Fact]
    public void Einstellungen_speichern_ein_Feld_der_Kette_per_PATCH_mit_Version_und_altem_Wert()
    {
        Antworten(erlaubt: [Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/freizeitseite", () => Json(new { version = 3u, anmeldelinks = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Patch, KettePfad, () => Json(new { version = 8u, heimatName = "Petra Heimann" }));
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("#sicherheitskette").SingleOrDefault()));

        var feld = cut.FindAll("#sicherheitskette input").Single(i => i.GetAttribute("value") == "Petra Heimann (Gemeindebüro)");
        feld.Input("Petra Heimann");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Patch && a.Pfad == KettePfad));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal(7u, koerper.RootElement.GetProperty("version").GetUInt32());
        Assert.Equal("Petra Heimann (Gemeindebüro)", koerper.RootElement.GetProperty("alterWert").GetString());
        Assert.Equal("Petra Heimann", koerper.RootElement.GetProperty("heimatName").GetString());
    }

    [Fact]
    public void Einstellungen_in_einer_abgeschlossenen_Freizeit_melden_beim_Speichern_nur_lesbar()
    {
        Antworten(phase: "Gelaufen", erlaubt: [Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/freizeitseite", () => Json(new { version = 3u, anmeldelinks = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Patch, KettePfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("#sicherheitskette-heimat-name").SingleOrDefault()));

        var feld = cut.Find("#sicherheitskette-heimat-name");
        feld.Input("Petra Heimann");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Patch && a.Pfad == KettePfad));
        cut.WaitForAssertion(() => Assert.Contains(
            "Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Find("#sicherheitskette").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("Inzwischen geändert", cut.Markup, StringComparison.Ordinal);
    }
}
