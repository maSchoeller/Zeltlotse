using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Kueche;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Autosave;
using Zeltlotse.Ui.Transport;
using EinkaufSeite = Zeltlotse.App.Features.Kueche.Einkauf;
using EinkaufslisteSeite = Zeltlotse.App.Features.Kueche.Einkaufsliste;
using EssensplanSeite = Zeltlotse.App.Features.Kueche.Essensplan;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 4-3 in der Web-App mit nachgebildetem Server: die Seite „Einkauf“ der Einkaufenden (Küche-Reiter mit vier Einträgen, Tage mit Ankreuzfeldern,
/// Abhaken mit einem Tipp, Leer-Zustand, nur lesbar, Ladefehler, nicht verfügbar), die Einkaufsliste mit der Spalte „Freigegeben“, der Zählzeile und den
/// Tages-Knöpfen, die Tausch-Vorschau mit „schon gekauft“ und das Autosave-Ankreuzfeld mit „Gespeichert“.
/// </summary>
public sealed class EinkaufSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000005d2");
    private static readonly Guid Pfannkuchen = Guid.Parse("0199f700-0000-7000-8000-0000000005a1");
    private static readonly Guid Chili = Guid.Parse("0199f700-0000-7000-8000-0000000005a2");
    private static readonly Guid Mehl = Guid.Parse("0199f700-0000-7000-8000-0000000005b1");
    private static readonly Guid Milch = Guid.Parse("0199f700-0000-7000-8000-0000000005b2");
    private static readonly Guid FruehstueckDi = Guid.Parse("0199f700-0000-7000-8000-0000000005c1");
    private static readonly Guid Gericht = Guid.Parse("0199f700-0000-7000-8000-0000000005e1");
    private static readonly Guid Aepfel = Guid.Parse("0199f700-0000-7000-8000-0000000005f1");
    private static readonly Guid Kaffee = Guid.Parse("0199f700-0000-7000-8000-0000000005f2");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public EinkaufSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero)));
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static string Kueche => $"/api/freizeiten/{Freizeit}/kueche";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void Kontext(string phase = "InVorbereitung") =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = new[] { "Verpflegung" }, erlaubt = Array.Empty<string>(),
        }));

    private static object Berechnet(Guid zutat, string bezeichnung, decimal? menge, string? einheit, bool freigegeben, bool gekauft, uint version = 0) => new
    {
        gerichtId = Gericht, zutatId = zutat, postenId = (Guid?)null, bezeichnung, menge, einheit, rezeptName = "Pfannkuchen", mahlzeitName = "Frühstück",
        vonHand = version > 0, version, freigegeben, gekauft,
    };

    private static object Hand(Guid posten, string bezeichnung, decimal? menge, string? einheit, bool freigegeben, bool gekauft) => new
    {
        gerichtId = (Guid?)null, zutatId = (Guid?)null, postenId = posten, bezeichnung, menge, einheit, rezeptName = (string?)null, mahlzeitName = (string?)null,
        vonHand = true, version = 4u, freigegeben, gekauft,
    };

    private static object Tag(string? tag, params object[] posten) => new { tag, posten, ersatzportionen = Array.Empty<object>() };

    // ---------- Seite „Einkauf“ ----------

    private static object EinkaufJson(bool mehlGekauft = false) => new
    {
        tage = new[]
        {
            Tag("2027-08-03", Berechnet(Mehl, "Mehl", 500m, "g", true, mehlGekauft, 7), Hand(Aepfel, "Äpfel", 3m, "kg", true, true)),
            Tag(null, Hand(Kaffee, "Kaffee", null, null, true, false)),
        },
    };

    private void Einkauf(Func<HttpResponseMessage>? antwort = null, string phase = "InVorbereitung")
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, $"{Kueche}/einkauf", antwort ?? (() => Json(EinkaufJson())));
    }

    private IRenderedComponent<EinkaufSeite> EinkaufRendern()
    {
        var cut = Render<EinkaufSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    [Fact]
    public void Einkauf_zeigt_den_Kueche_Reiter_und_je_Tag_eine_Card_mit_einem_Ankreuzfeld_je_freigegebenem_Posten()
    {
        Einkauf();

        var cut = EinkaufRendern();

        Assert.Equal("Küche", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Einkauf", cut.Find("h1").TextContent);
        var reiter = cut.FindAll("nav[aria-label=Küche] a");
        Assert.Equal(["Rezepte", "Essensplan", "Einkaufsliste", "Einkauf"], reiter.Select(a => a.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/kueche/einkauf", reiter[3].GetAttribute("href"));
        Assert.Equal("page", reiter[3].GetAttribute("aria-current"));
        var dienstag = cut.Find("#tag-2027-08-03");
        Assert.Equal("Dienstag, 3. August 2027", dienstag.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Mehl 500 g · Pfannkuchen · Frühstück", "Äpfel 3 kg"], dienstag.QuerySelectorAll("label.elkw-choice").Select(l => l.TextContent.Trim()));
        var mehl = dienstag.QuerySelector($"#gekauft-{Gericht}-{Mehl}")!;
        Assert.False(mehl.HasAttribute("checked"));
        Assert.True(dienstag.QuerySelector($"#gekauft-{Aepfel}")!.HasAttribute("checked"));
        var ohneTag = cut.Find("#tag-ohne");
        Assert.Equal("Ohne Tag", ohneTag.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Kaffee"], ohneTag.QuerySelectorAll("label.elkw-choice").Select(l => l.TextContent.Trim()));
        Assert.DoesNotContain("Allergen", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Ersatzportion", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Abhaken_mit_einem_Tipp_schickt_PATCH_gekauft_und_zeigt_Gespeichert_Rueckgaengig()
    {
        Einkauf();
        var pfad = $"{Kueche}/einkaufsliste/gerichte/{Gericht}/zutaten/{Mehl}";
        _server.Bei(HttpMethod.Patch, pfad, () => Json(new { version = 9u, gekauft = "true" }));
        _server.Bei(HttpMethod.Patch, $"{Kueche}/einkaufsliste/posten/{Aepfel}", () => Json(new { version = 5u, gekauft = "false" }));
        var cut = EinkaufRendern();

        cut.Find($"#gekauft-{Gericht}-{Mehl}").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("#tag-2027-08-03 [role=status]").TextContent, StringComparison.Ordinal));
        Assert.Contains(cut.Find("#tag-2027-08-03 [role=status]").QuerySelectorAll("button"), b => b.TextContent == "Rückgängig");
        using (var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch && a.Pfad == pfad).Koerper!))
        {
            Assert.Equal(("false", "true"), (koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("gekauft").GetString()));
        }

        Assert.True(cut.Find($"#gekauft-{Gericht}-{Mehl}").HasAttribute("checked"));

        cut.Find($"#gekauft-{Aepfel}").Change(false);
        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Patch && a.Pfad == $"{Kueche}/einkaufsliste/posten/{Aepfel}"));
        using var hand = JsonDocument.Parse(_server.Anfragen.Single(a => a.Pfad == $"{Kueche}/einkaufsliste/posten/{Aepfel}").Koerper!);
        Assert.Equal(("true", "false"), (hand.RootElement.GetProperty("alterWert").GetString(), hand.RootElement.GetProperty("gekauft").GetString()));
    }

    [Fact]
    public void Konflikt_beim_Abhaken_Neu_laden_holt_den_Einkauf_neu_und_das_Kaestchen_zeigt_den_Stand_des_Servers()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, $"{Kueche}/einkauf", () => Json(EinkaufJson()), () => Json(EinkaufJson()));
        _server.Bei(HttpMethod.Patch, $"{Kueche}/einkaufsliste/gerichte/{Gericht}/zutaten/{Mehl}", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var cut = EinkaufRendern();

        cut.Find($"#gekauft-{Gericht}-{Mehl}").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("Inzwischen geändert", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("button").Single(b => b.TextContent == "Neu laden").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/einkauf")));
        // Gleicher Wert und gleiche Version wie beim ersten Laden: das Feld entsteht trotzdem neu und zeigt den Stand des Servers.
        cut.WaitForAssertion(() => Assert.False(cut.Find($"#gekauft-{Gericht}-{Mehl}").HasAttribute("checked")));
        Assert.DoesNotContain("Inzwischen geändert", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Einkauf_ohne_Freigegebenes_zeigt_den_Leer_Zustand_mit_dem_Weg_zur_Einkaufsliste()
    {
        Einkauf(() => Json(new { tage = Array.Empty<object>() }));

        var cut = EinkaufRendern();

        var leer = cut.Find(".elkw-empty");
        Assert.Equal("Noch nichts zum Einkaufen freigegeben.", leer.QuerySelector("p")!.TextContent);
        Assert.Equal($"freizeiten/{Freizeit}/kueche/einkaufsliste", leer.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll("input"));
    }

    [Fact]
    public void Einkauf_nach_dem_Abschluss_nur_lesbar_ohne_Ankreuzfelder()
    {
        Einkauf(phase: "Gelaufen");

        var cut = EinkaufRendern();

        Assert.Contains("nur lesbar", cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("button"));
        Assert.Equal(
            ["Mehl 500 g · Pfannkuchen · Frühstück: offen", "Äpfel 3 kg: gekauft"],
            cut.Find("#tag-2027-08-03").QuerySelectorAll("li").Select(l => l.TextContent.Trim()));
    }

    [Fact]
    public void Einkauf_Ladefehler_ist_keine_leere_Seite_nicht_verfuegbar_ohne_Verpflegung()
    {
        Einkauf(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var fehler = EinkaufRendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, fehler.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(fehler.FindAll(".elkw-empty"));

        Einkauf(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal("Diese Seite ist nicht verfügbar.", EinkaufRendern().Find("h1").TextContent);
    }

    // ---------- Einkaufsliste: Freigabe ----------

    private static object ListeJson(bool mehlFreigegeben = true, bool milchFreigegeben = false) => new
    {
        personenzahl = 8,
        allergienAbgefragt = true,
        allergene = Array.Empty<object>(),
        tage = new[]
        {
            Tag("2027-08-03", Berechnet(Mehl, "Mehl", 800m, "g", mehlFreigegeben, true), Berechnet(Milch, "Milch", 1.5m, "l", milchFreigegeben, false)),
            Tag(null, Hand(Kaffee, "Kaffee", null, null, false, false)),
        },
    };

    private IRenderedComponent<EinkaufslisteSeite> ListeRendern()
    {
        var cut = Render<EinkaufslisteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static List<List<string>> Tabelle(AngleSharp.Dom.IElement card) =>
        [.. card.QuerySelectorAll("tbody tr").Select(z => z.QuerySelectorAll("td").Select(t => t.TextContent.Trim()).ToList())];

    private int Listenabrufe => _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/einkaufsliste");

    [Fact]
    public void Einkaufsliste_hat_die_Spalten_Freigegeben_und_Gekauft_und_je_Tag_die_Zaehlzeile()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()));

        var cut = ListeRendern();

        var dienstag = cut.Find("#tag-2027-08-03");
        Assert.Equal(["Zutat", "Menge", "Einheit", "Gericht · Mahlzeit", "von Hand", "Freigegeben", "Gekauft"], dienstag.QuerySelectorAll("th").Select(t => t.TextContent));
        Assert.True(dienstag.QuerySelector($"#freigegeben-{Gericht}-{Mehl}")!.HasAttribute("checked"));
        Assert.False(dienstag.QuerySelector($"#freigegeben-{Gericht}-{Milch}")!.HasAttribute("checked"));
        var zeilen = Tabelle(dienstag);
        Assert.Equal(("gekauft", ""), (zeilen[0][6], zeilen[1][6]));
        Assert.Contains("1 von 2 freigegeben · 1 gekauft", dienstag.QuerySelectorAll("p").Select(p => p.TextContent));
        Assert.Equal(["Ganzen Tag freigeben", "Freigabe zurücknehmen"], dienstag.QuerySelectorAll("button").Select(b => b.TextContent).Where(t => t is "Ganzen Tag freigeben" or "Freigabe zurücknehmen"));
        var ohneTag = cut.Find("#tag-ohne");
        Assert.NotNull(ohneTag.QuerySelector($"#freigegeben-{Kaffee}"));
        Assert.Contains("0 von 1 freigegeben · 0 gekauft", ohneTag.QuerySelectorAll("p").Select(p => p.TextContent));
        Assert.Equal(["Ganzen Tag freigeben"], ohneTag.QuerySelectorAll("button").Select(b => b.TextContent).Where(t => t is "Ganzen Tag freigeben" or "Freigabe zurücknehmen"));
    }

    [Fact]
    public void Tages_Knoepfe_schicken_POST_freigabe_mit_Tag_und_laden_die_Liste_neu()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()), () => Json(ListeJson(milchFreigegeben: true)), () => Json(ListeJson()));
        _server.Bei(HttpMethod.Post, $"{Kueche}/einkaufsliste/freigabe", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = ListeRendern();

        cut.Find("#tag-2027-08-03").QuerySelectorAll("button").Single(b => b.TextContent == "Ganzen Tag freigeben").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, Listenabrufe));
        cut.WaitForAssertion(() => Assert.True(cut.Find($"#freigegeben-{Gericht}-{Milch}").HasAttribute("checked")));
        Assert.Contains("2 von 2 freigegeben · 1 gekauft", cut.Find("#tag-2027-08-03").QuerySelectorAll("p").Select(p => p.TextContent));
        Assert.DoesNotContain(cut.Find("#tag-2027-08-03").QuerySelectorAll("button"), b => b.TextContent == "Ganzen Tag freigeben");
        using (var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Post).Koerper!))
        {
            Assert.Equal(("2027-08-03", true), (koerper.RootElement.GetProperty("tag").GetString(), koerper.RootElement.GetProperty("freigegeben").GetBoolean()));
        }

        cut.Find("#tag-ohne").QuerySelectorAll("button").Single(b => b.TextContent == "Ganzen Tag freigeben").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, Listenabrufe));
        using var ohneTag = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Post).Koerper!);
        Assert.Equal((JsonValueKind.Null, true), (ohneTag.RootElement.GetProperty("tag").ValueKind, ohneTag.RootElement.GetProperty("freigegeben").GetBoolean()));
    }

    [Fact]
    public void Freigabe_zuruecknehmen_schickt_freigegeben_false_nach_dem_Abschluss_meldet_der_Knopf_nur_lesbar()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()));
        _server.Bei(HttpMethod.Post, $"{Kueche}/einkaufsliste/freigabe", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = ListeRendern();

        cut.Find("#tag-2027-08-03").QuerySelectorAll("button").Single(b => b.TextContent == "Freigabe zurücknehmen").Click();

        cut.WaitForAssertion(() => Assert.Contains(KuecheTexte.AbgeschlossenText, cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Post).Koerper!);
        Assert.False(koerper.RootElement.GetProperty("freigegeben").GetBoolean());
    }

    [Fact]
    public void Nach_einzelnem_Freigeben_und_Freigabe_zuruecknehmen_zeigt_das_Kaestchen_wieder_den_Stand_des_Servers()
    {
        Kontext();
        _server.BeiDerReihe(
            HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()), () => Json(ListeJson(milchFreigegeben: true)), () => Json(ListeJson(mehlFreigegeben: false)));
        _server.Bei(HttpMethod.Patch, $"{Kueche}/einkaufsliste/gerichte/{Gericht}/zutaten/{Milch}", () => Json(new { version = 3u, freigegeben = "true" }));
        _server.Bei(HttpMethod.Post, $"{Kueche}/einkaufsliste/freigabe", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = ListeRendern();

        cut.Find($"#freigegeben-{Gericht}-{Milch}").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(2, Listenabrufe));
        cut.Find("#tag-2027-08-03").QuerySelectorAll("button").Single(b => b.TextContent == "Freigabe zurücknehmen").Click();

        cut.WaitForAssertion(() => Assert.Equal(3, Listenabrufe));
        // Der Server liefert für Milch wieder (false, Version 0) wie beim ersten Laden; das Kästchen folgt trotzdem dem Server.
        cut.WaitForAssertion(() => Assert.False(cut.Find($"#freigegeben-{Gericht}-{Milch}").HasAttribute("checked")));
        Assert.False(cut.Find($"#freigegeben-{Gericht}-{Mehl}").HasAttribute("checked"));
    }

    [Fact]
    public void Freigegeben_Beschriftung_nennt_Posten_Gericht_und_Mahlzeit_Hand_Posten_speichern_am_Feld_des_Postens()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()));
        _server.Bei(HttpMethod.Patch, $"{Kueche}/einkaufsliste/posten/{Kaffee}", () => Json(new { version = 5u, freigegeben = "true" }));
        var cut = ListeRendern();

        Assert.Equal("Mehl 800 g · Pfannkuchen · Frühstück freigegeben", cut.Find($"#freigegeben-{Gericht}-{Mehl}").ParentElement!.TextContent.Trim());
        cut.Find($"#freigegeben-{Kaffee}").Change(true);

        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Patch && a.Pfad == $"{Kueche}/einkaufsliste/posten/{Kaffee}"));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal("true", koerper.RootElement.GetProperty("freigegeben").GetString());
    }

    [Fact]
    public void Freigegeben_ankreuzen_speichert_per_PATCH_und_laedt_die_Liste_fuer_die_Zaehlzeile_neu()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()), () => Json(ListeJson(milchFreigegeben: true)));
        var pfad = $"{Kueche}/einkaufsliste/gerichte/{Gericht}/zutaten/{Milch}";
        _server.Bei(HttpMethod.Patch, pfad, () => Json(new { version = 3u, freigegeben = "true" }));
        var cut = ListeRendern();

        cut.Find($"#freigegeben-{Gericht}-{Milch}").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("2 von 2 freigegeben · 1 gekauft", cut.Find("#tag-2027-08-03").QuerySelectorAll("p").Select(p => p.TextContent)));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal(("false", "true"), (koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("freigegeben").GetString()));
        Assert.Contains("Gespeichert", cut.Find($"#freigegeben-{Gericht}-{Milch}").ParentElement!.ParentElement!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Einkaufsliste_nach_dem_Abschluss_zeigt_Freigabe_und_Gekauft_als_Text_ohne_Knoepfe()
    {
        Kontext("Gelaufen");
        _server.Bei(HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()));

        var cut = ListeRendern();

        Assert.Equal(["Mehl", "800", "g", "Pfannkuchen · Frühstück", "", "freigegeben", "gekauft"], Tabelle(cut.Find("#tag-2027-08-03"))[0]);
        Assert.Equal("", Tabelle(cut.Find("#tag-2027-08-03"))[1][5]);
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("1 von 2 freigegeben · 1 gekauft", cut.Find("#tag-2027-08-03").QuerySelectorAll("p").Select(p => p.TextContent));
    }

    // ---------- Tausch-Vorschau ----------

    [Theory]
    [InlineData(1, "1 schon gekaufter Posten steht nicht im Vergleich.")]
    [InlineData(2, "2 schon gekaufte Posten stehen nicht im Vergleich.")]
    public void Tausch_Vorschau_nennt_die_schon_gekauften_Posten(int schonGekauft, string satz)
    {
        Kontext();
        _server.Bei(HttpMethod.Get, $"{Kueche}/essensplan", () => Json(new
        {
            personenzahl = 8,
            allergienAbgefragt = false,
            tage = new[]
            {
                new
                {
                    tag = "2027-08-03",
                    mahlzeiten = new[]
                    {
                        new
                        {
                            programmpunktId = FruehstueckDi, beginn = "08:00", titel = "Frühstück", art = "Fruehstueck",
                            gerichte = new[] { new { id = Gericht, version = 5u, rezeptId = Pfannkuchen, rezeptName = "Pfannkuchen", ersatzportionen = new { anzahl = 0, allergene = Array.Empty<string>() } } },
                        },
                    },
                },
            },
            rezepte = new[] { new { id = Chili, name = "Chili sin Carne" }, new { id = Pfannkuchen, name = "Pfannkuchen" } },
        }));
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == $"{Kueche}/gerichte/{Gericht}/tausch", _ => Task.FromResult(Json(new
        {
            gerichtId = Gericht,
            version = 5u,
            rezept = new { id = Pfannkuchen, name = "Pfannkuchen" },
            neuesRezept = new { id = Chili, name = "Chili sin Carne" },
            vorher = new[] { new { bezeichnung = "Salz", menge = (decimal?)null, einheit = (string?)null, vonHand = false, geaendert = true } },
            nachher = new[] { new { bezeichnung = "Kidneybohnen", menge = (decimal?)1000m, einheit = (string?)"g", vonHand = false, geaendert = true } },
            schonGekauft,
        })));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"freizeiten/{Freizeit}/kueche/essensplan?tag=2027-08-03");
        var cut = Render<EssensplanSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button").Single(b => b.TextContent == "Tauschen").Click();
        cut.Find("input[name^=tausch]").Change(Chili.ToString());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".zl-vergleich")));
        Assert.Contains(satz, cut.FindAll("p").Select(p => p.TextContent));
    }

    [Fact]
    public void Tausch_Vorschau_ohne_gekaufte_Posten_nennt_keine()
    {
        Assert.Null(KuecheTexte.SchonGekauft(0));
    }

    // ---------- Autosave-Ankreuzfeld ----------

    [Fact]
    public void Autosave_Ankreuzfeld_meldet_den_gespeicherten_Wert_an_Gespeichert_ein_Konflikt_nicht()
    {
        const string pfad = "/api/freizeiten/x/kueche/einkaufsliste/posten/y";
        var gemeldet = new List<string?>();
        var server = new NachgebildeterHandler();
        server.BeiDerReihe(
            HttpMethod.Patch,
            pfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":8,"gekauft":"true"}"""),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        Services.AddScoped(_ => new AutosaveDienst(server.Client()));
        var cut = Render<AutosaveAnkreuzfeld>(ps => ps
            .Add(p => p.Label, "Äpfel")
            .Add(p => p.Feldname, "gekauft")
            .Add(p => p.Pfad, pfad)
            .Add(p => p.Version, 7u)
            .Add(p => p.Gespeichert, (string? wert) => gemeldet.Add(wert)));

        cut.Find("input").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("[role=status]").TextContent, StringComparison.Ordinal));
        cut.Find("input").Change(false);
        cut.WaitForAssertion(() => Assert.Contains("Inzwischen geändert", cut.Markup, StringComparison.Ordinal));

        Assert.Equal(["true"], gemeldet);
    }
}
