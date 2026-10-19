using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Kueche;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Transport;
using EinkaufslisteSeite = Zeltlotse.App.Features.Kueche.Einkaufsliste;
using EssensplanSeite = Zeltlotse.App.Features.Kueche.Essensplan;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 4-2 in der Web-App mit nachgebildetem Server: der Essensplan (Küche-Reiter, Tag-Reiter, Mahlzeiten mit Gerichten und Ersatzportionen,
/// Vormerken, Entfernen, Tauschen mit Vorschau, Übernehmen, Anpassen, Abbrechen und Konflikt, ohne Rezepte, nur lesbar, nicht verfügbar, Ladefehler)
/// und die Einkaufsliste (Allergen-Abgleich, „fragt keine Allergien ab“, Tage mit Posten und Ersatzportionen, Hand-Menge, Hand-Posten, nur lesbar,
/// leer, Ladefehler).
/// </summary>
public sealed class EssensplanSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000004d2");
    private static readonly Guid Pfannkuchen = Guid.Parse("0199f700-0000-7000-8000-0000000004a1");
    private static readonly Guid Chili = Guid.Parse("0199f700-0000-7000-8000-0000000004a2");
    private static readonly Guid Mehl = Guid.Parse("0199f700-0000-7000-8000-0000000004b1");
    private static readonly Guid FruehstueckDi = Guid.Parse("0199f700-0000-7000-8000-0000000004c1");
    private static readonly Guid MittagDi = Guid.Parse("0199f700-0000-7000-8000-0000000004c2");
    private static readonly Guid AbendDi = Guid.Parse("0199f700-0000-7000-8000-0000000004c3");
    private static readonly Guid FruehstueckMo = Guid.Parse("0199f700-0000-7000-8000-0000000004c4");
    private static readonly Guid Gericht = Guid.Parse("0199f700-0000-7000-8000-0000000004e1");
    private static readonly Guid Aepfel = Guid.Parse("0199f700-0000-7000-8000-0000000004f1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public EssensplanSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero)));
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static string Kueche => $"/api/freizeiten/{Freizeit}/kueche";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void Kontext(string phase = "InVorbereitung") =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = new[] { "Verpflegung" }, erlaubt = Array.Empty<string>(),
        }));

    private static object Mahlzeit(Guid id, string beginn, string titel, string art, params object[] gerichte) =>
        new { programmpunktId = id, beginn, titel, art, gerichte };

    private static object PlanJson(bool mitRezepten = true, bool abgefragt = true, uint version = 5, string rezept = "Pfannkuchen") => new
    {
        personenzahl = 8,
        allergienAbgefragt = abgefragt,
        tage = new object[]
        {
            new { tag = "2027-08-02", mahlzeiten = new[] { Mahlzeit(FruehstueckMo, "08:00", "Frühstück", "Fruehstueck") } },
            new
            {
                tag = "2027-08-03",
                mahlzeiten = new[]
                {
                    Mahlzeit(
                        FruehstueckDi, "08:00", "Frühstück", "Fruehstueck",
                        new
                        {
                            id = Gericht, version, rezeptId = rezept == "Pfannkuchen" ? Pfannkuchen : Chili, rezeptName = rezept,
                            ersatzportionen = new { anzahl = abgefragt ? 1 : 0, allergene = abgefragt ? new[] { "gluten" } : [] },
                        }),
                    Mahlzeit(MittagDi, "12:30", "Mittagessen", "Mittagessen"),
                    Mahlzeit(AbendDi, "18:30", "Abendessen", "Abendessen"),
                },
            },
        },
        rezepte = mitRezepten ? new object[] { new { id = Chili, name = "Chili sin Carne" }, new { id = Pfannkuchen, name = "Pfannkuchen" } } : [],
    };

    private void Plan(Func<HttpResponseMessage>? antwort = null, string phase = "InVorbereitung")
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, $"{Kueche}/essensplan", antwort ?? (() => Json(PlanJson())));
    }

    private IRenderedComponent<EssensplanSeite> PlanRendern(string? tag = "2027-08-03")
    {
        Navigation.NavigateTo(tag is null ? $"freizeiten/{Freizeit}/kueche/essensplan" : $"freizeiten/{Freizeit}/kueche/essensplan?tag={tag}");
        var cut = Render<EssensplanSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<EssensplanSeite> cut, string titel) =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h3")?.TextContent == titel || c.QuerySelector("h2")?.TextContent == titel);

    private static object VorschauJson() => new
    {
        gerichtId = Gericht,
        version = 5u,
        rezept = new { id = Pfannkuchen, name = "Pfannkuchen" },
        neuesRezept = new { id = Chili, name = "Chili sin Carne" },
        vorher = new[] { new { bezeichnung = "Mehl", menge = 1m, einheit = "kg", vonHand = true, geaendert = true } },
        nachher = new[] { new { bezeichnung = "Kidneybohnen", menge = 1000m, einheit = "g", vonHand = false, geaendert = true } },
    };

    // ---------- Essensplan ----------

    [Fact]
    public void Essensplan_zeigt_Kueche_Reiter_Tag_Reiter_und_je_Mahlzeit_eine_Card_mit_Gericht_und_Ersatzportion()
    {
        Plan();

        var cut = PlanRendern();

        Assert.Equal("Küche", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Essensplan", cut.Find("h1").TextContent);
        var kueche = cut.FindAll("nav[aria-label=Küche] a");
        Assert.Equal(["Rezepte", "Essensplan", "Einkaufsliste"], kueche.Select(a => a.TextContent));
        Assert.Equal([$"freizeiten/{Freizeit}/kueche/rezepte", $"freizeiten/{Freizeit}/kueche/essensplan", $"freizeiten/{Freizeit}/kueche/einkaufsliste"], kueche.Select(a => a.GetAttribute("href")));
        Assert.Equal("page", kueche[1].GetAttribute("aria-current"));
        var tage = cut.FindAll("nav[aria-label=Tage] a");
        Assert.Equal(["Mo 02.08.", "Di 03.08."], tage.Select(a => a.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/kueche/essensplan?tag=2027-08-02", tage[0].GetAttribute("href"));
        Assert.Equal("page", tage[1].GetAttribute("aria-current"));
        Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Equal(["Frühstück", "Mittagessen", "Abendessen"], cut.FindAll(".zl-reiter__inhalt section.elkw-card h3").Select(h => h.TextContent));
        var fruehstueck = Card(cut, "Frühstück");
        Assert.Contains("Pfannkuchen", fruehstueck.TextContent, StringComparison.Ordinal);
        Assert.Contains("1 Ersatzportion einplanen (enthält Gluten)", fruehstueck.TextContent, StringComparison.Ordinal);
        Assert.Contains(fruehstueck.QuerySelectorAll("button"), b => b.TextContent == "Tauschen");
        Assert.Contains(fruehstueck.QuerySelectorAll("button"), b => b.TextContent == "Entfernen");
        Assert.Contains("Noch nichts vorgemerkt.", Card(cut, "Mittagessen").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, Card(cut, "Mittagessen").QuerySelectorAll("input[type=radio]").Length);
    }

    [Fact]
    public void Ohne_Tag_in_der_Adresse_ist_der_erste_Tag_gewaehlt()
    {
        Plan();

        var cut = PlanRendern(tag: null);

        Assert.Equal("Montag, 2. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Equal("page", cut.FindAll("nav[aria-label=Tage] a")[0].GetAttribute("aria-current"));
    }

    [Fact]
    public void Gericht_vormerken_schickt_Mahlzeit_und_Rezept_und_laedt_neu()
    {
        Plan();
        _server.Bei(HttpMethod.Post, $"{Kueche}/gerichte", () => Json(new { id = Guid.CreateVersion7(), version = 3u, programmpunktId = MittagDi, rezeptId = Chili }, HttpStatusCode.Created));
        var cut = PlanRendern();

        var mittag = Card(cut, "Mittagessen");
        mittag.QuerySelectorAll("input[type=radio]").Single(r => r.GetAttribute("value") == Chili.ToString()).Change(Chili.ToString());
        Card(cut, "Mittagessen").QuerySelectorAll("button").Single(b => b.TextContent == "Gericht vormerken").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/essensplan")));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Post).Koerper!);
        Assert.Equal((MittagDi, Chili), (koerper.RootElement.GetProperty("programmpunktId").GetGuid(), koerper.RootElement.GetProperty("rezeptId").GetGuid()));
    }

    [Fact]
    public void Ohne_Rezepte_zeigt_der_Leer_Zustand_den_Weg_zu_den_Rezepten()
    {
        Plan(() => Json(PlanJson(mitRezepten: false)));

        var cut = PlanRendern();

        var leer = cut.Find(".elkw-empty");
        Assert.Equal("Noch keine Rezepte.", leer.QuerySelector("p")!.TextContent);
        Assert.Equal($"freizeiten/{Freizeit}/kueche/rezepte", leer.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll("input[type=radio]"));
    }

    [Fact]
    public void Entfernen_per_Inline_Bestaetigung_schickt_DELETE_und_laedt_neu()
    {
        Plan();
        _server.Bei(HttpMethod.Delete, $"{Kueche}/gerichte/{Gericht}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = PlanRendern();

        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Entfernen").Click();
        Assert.Contains("„Pfannkuchen“ entfernen?", Card(cut, "Frühstück").TextContent, StringComparison.Ordinal);
        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == $"{Kueche}/gerichte/{Gericht}"));
        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/essensplan")));
    }

    [Fact]
    public void Tauschen_zeigt_nach_der_Auswahl_die_Vorschau_und_Uebernehmen_tauscht_mit_der_Version()
    {
        Plan();
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == $"{Kueche}/gerichte/{Gericht}/tausch", _ => Task.FromResult(Json(VorschauJson())));
        _server.Bei(HttpMethod.Post, $"{Kueche}/gerichte/{Gericht}/tauschen", () => Json(new { id = Gericht, version = 6u, programmpunktId = FruehstueckDi, rezeptId = Chili }));
        var cut = PlanRendern();

        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Tauschen").Click();
        var auswahl = Card(cut, "Frühstück").QuerySelectorAll("input[type=radio]").Where(r => r.GetAttribute("name")!.StartsWith("tausch", StringComparison.Ordinal)).ToList();
        Assert.Equal([Chili.ToString()], auswahl.Select(r => r.GetAttribute("value")));
        auswahl[0].Change(Chili.ToString());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".zl-vergleich")));
        Assert.True(Card(cut, "Frühstück").QuerySelector("input[name^=tausch]")!.HasAttribute("checked"));
        Assert.Contains(_server.Anfragen, a => a.Pfad == $"{Kueche}/gerichte/{Gericht}/tausch?rezeptId={Chili}");
        Assert.Equal(["Pfannkuchen", "Chili sin Carne"], cut.FindAll(".zl-vergleich__spalte").Select(s => s.QuerySelector("p")!.TextContent));
        Assert.Equal("1 kg Mehl (von Hand)", cut.Find(".zl-vergleich__spalte li strong").TextContent);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post);
        Assert.Equal(
            ["Übernehmen", "Anpassen", "Abbrechen"],
            Card(cut, "Frühstück").QuerySelectorAll("button").Select(b => b.TextContent).Where(t => t is "Übernehmen" or "Anpassen" or "Abbrechen"));
        Assert.DoesNotContain(Card(cut, "Frühstück").QuerySelectorAll("button"), b => b.TextContent == "Tauschen");

        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Übernehmen").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-vergleich")));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Post).Koerper!);
        Assert.Equal((5u, Chili), (koerper.RootElement.GetProperty("version").GetUInt32(), koerper.RootElement.GetProperty("rezeptId").GetGuid()));
        Assert.Contains("Pfannkuchen gegen Chili sin Carne getauscht.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/essensplan"));
    }

    [Fact]
    public void Anpassen_tauscht_und_oeffnet_die_Einkaufsliste_am_Tag_Abbrechen_tauscht_nicht()
    {
        Plan();
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == $"{Kueche}/gerichte/{Gericht}/tausch", _ => Task.FromResult(Json(VorschauJson())));
        _server.Bei(HttpMethod.Post, $"{Kueche}/gerichte/{Gericht}/tauschen", () => Json(new { id = Gericht, version = 6u, programmpunktId = FruehstueckDi, rezeptId = Chili }));
        var cut = PlanRendern();

        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Tauschen").Click();
        Card(cut, "Frühstück").QuerySelector("input[name^=tausch]")!.Change(Chili.ToString());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".zl-vergleich")));
        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Abbrechen").Click();
        Assert.Empty(cut.FindAll(".zl-vergleich"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post);

        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Tauschen").Click();
        Card(cut, "Frühstück").QuerySelector("input[name^=tausch]")!.Change(Chili.ToString());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".zl-vergleich")));
        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Anpassen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/kueche/einkaufsliste#tag-2027-08-03", Navigation.Uri, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post);
    }

    [Fact]
    public void Tauschen_mit_veralteter_Version_zeigt_den_Konflikt_mit_Neu_laden()
    {
        Plan();
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == $"{Kueche}/gerichte/{Gericht}/tausch", _ => Task.FromResult(Json(VorschauJson())));
        _server.Bei(HttpMethod.Post, $"{Kueche}/gerichte/{Gericht}/tauschen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var cut = PlanRendern();

        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Tauschen").Click();
        Card(cut, "Frühstück").QuerySelector("input[name^=tausch]")!.Change(Chili.ToString());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".zl-vergleich")));
        Card(cut, "Frühstück").QuerySelectorAll("button").Single(b => b.TextContent == "Übernehmen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Inzwischen geändert – neu laden?", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("button").Single(b => b.TextContent == "Neu laden").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/essensplan")));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Inzwischen geändert", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Essensplan_nach_dem_Abschluss_nur_lesbar()
    {
        Plan(phase: "Gelaufen");

        var cut = PlanRendern();

        Assert.Contains(KuecheTexte.AbgeschlossenText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Contains("Pfannkuchen", Card(cut, "Frühstück").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("input"));
    }

    [Fact]
    public void Essensplan_nicht_verfuegbar_und_Ladefehler()
    {
        Plan(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        var weg = PlanRendern();
        Assert.Equal("Diese Seite ist nicht verfügbar.", weg.Find("h1").TextContent);

        Plan(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var fehler = PlanRendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, fehler.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Noch nichts vorgemerkt.", fehler.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Essensplan_ohne_Allergie_Frage_nennt_das()
    {
        Plan(() => Json(PlanJson(abgefragt: false)));

        var cut = PlanRendern();

        Assert.Contains(KuecheTexte.KeineAllergienText, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Ersatzportion", cut.Markup, StringComparison.Ordinal);
    }

    // ---------- Einkaufsliste ----------

    private static object ListeJson(bool abgefragt = true, uint version = 0, bool vonHand = false, string menge = "800") => new
    {
        personenzahl = 8,
        allergienAbgefragt = abgefragt,
        allergene = abgefragt ? new[] { new { allergen = "gluten", anzahl = 1 }, new { allergen = "milch", anzahl = 2 } } : [],
        tage = new object[]
        {
            new
            {
                tag = "2027-08-03",
                posten = new object[]
                {
                    new
                    {
                        gerichtId = Gericht, zutatId = Mehl, postenId = (Guid?)null, bezeichnung = "Mehl", menge = decimal.Parse(menge, System.Globalization.CultureInfo.InvariantCulture),
                        einheit = "g", rezeptName = "Pfannkuchen", mahlzeitName = "Frühstück", vonHand, version,
                    },
                    new
                    {
                        gerichtId = (Guid?)null, zutatId = (Guid?)null, postenId = Aepfel, bezeichnung = "Äpfel", menge = 3m, einheit = "kg", rezeptName = (string?)null,
                        mahlzeitName = (string?)null, vonHand = true, version = 4u,
                    },
                },
                ersatzportionen = abgefragt
                    ? new[] { new { gerichtId = Gericht, rezeptName = "Pfannkuchen", mahlzeitName = "Frühstück", ersatzportionen = new { anzahl = 1, allergene = new[] { "gluten" } } } }
                    : [],
            },
            new
            {
                tag = (string?)null,
                posten = new object[]
                {
                    new
                    {
                        gerichtId = (Guid?)null, zutatId = (Guid?)null, postenId = Guid.Parse("0199f700-0000-7000-8000-0000000004f2"), bezeichnung = "Kaffee",
                        menge = (decimal?)null, einheit = (string?)null, rezeptName = (string?)null, mahlzeitName = (string?)null, vonHand = true, version = 2u,
                    },
                },
                ersatzportionen = Array.Empty<object>(),
            },
        },
    };

    private void Liste(Func<HttpResponseMessage>? antwort = null, string phase = "InVorbereitung")
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, $"{Kueche}/einkaufsliste", antwort ?? (() => Json(ListeJson())));
    }

    private IRenderedComponent<EinkaufslisteSeite> ListeRendern()
    {
        var cut = Render<EinkaufslisteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static List<List<string>> Tabelle(AngleSharp.Dom.IElement card) =>
        [.. card.QuerySelectorAll("tbody tr").Select(z => z.QuerySelectorAll("td").Select(t => t.TextContent.Trim()).ToList())];

    [Fact]
    public void Einkaufsliste_zeigt_Abgleich_Tage_mit_Posten_Ersatzportionen_und_Hand_Posten()
    {
        Liste();

        var cut = ListeRendern();

        Assert.Equal("Einkaufsliste", cut.Find("h1").TextContent);
        Assert.Equal("page", cut.FindAll("nav[aria-label=Küche] a")[2].GetAttribute("aria-current"));
        Assert.Contains("Berechnet für 8 Personen der Freizeit.", cut.FindAll("p").Select(p => p.TextContent));
        var abgleich = cut.Find("#allergen-abgleich");
        Assert.Equal(["Gluten: 1 Person", "Milch: 2 Personen"], abgleich.QuerySelectorAll("li").Select(l => l.TextContent));
        var dienstag = cut.Find("#tag-2027-08-03");
        Assert.Equal("Dienstag, 3. August 2027", dienstag.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Zutat", "Menge", "Einheit", "Gericht · Mahlzeit", "von Hand"], dienstag.QuerySelectorAll("th").Select(t => t.TextContent));
        var zeilen = Tabelle(dienstag);
        Assert.Equal(("Mehl", "g", "Pfannkuchen · Frühstück", ""), (zeilen[0][0], zeilen[0][2], zeilen[0][3], zeilen[0][4]));
        Assert.Equal("800", dienstag.QuerySelector($"#menge-{Gericht}-{Mehl}")!.GetAttribute("value"));
        Assert.Equal(["Äpfel", "3", "kg", "–", "von Hand"], zeilen[1]);
        Assert.Contains("Pfannkuchen · Frühstück: 1 Ersatzportion einplanen (enthält Gluten)", dienstag.TextContent, StringComparison.Ordinal);
        var ohneTag = cut.Find("#tag-ohne");
        Assert.Equal("Ohne Tag", ohneTag.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Kaffee", "", "", "–", "von Hand"], Tabelle(ohneTag)[0]);
        var handPosten = cut.Find("#hand-posten");
        Assert.Equal("Äpfel", handPosten.QuerySelector($"#posten-{Aepfel}-bezeichnung")!.GetAttribute("value"));
        Assert.Equal("2027-08-03", handPosten.QuerySelector($"#posten-{Aepfel}-tag")!.GetAttribute("value"));
        Assert.Equal(2, handPosten.QuerySelectorAll("button").Count(b => b.TextContent == "Entfernen"));
        Assert.NotNull(cut.Find("#posten-ergaenzen form"));
    }

    [Fact]
    public void Einkaufsliste_ohne_Allergie_Frage_nennt_das_statt_der_Uebersicht()
    {
        Liste(() => Json(ListeJson(abgefragt: false)));

        var cut = ListeRendern();

        Assert.Contains(KuecheTexte.KeineAllergienText, cut.Find("#allergen-abgleich").TextContent, StringComparison.Ordinal);
        Assert.Equal("Diese Freizeit fragt keine Allergien ab.", KuecheTexte.KeineAllergienText);
        Assert.DoesNotContain("Ersatzportion", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Hand_Menge_speichern_schickt_den_PATCH_und_laedt_die_Liste_neu()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, $"{Kueche}/einkaufsliste", () => Json(ListeJson()), () => Json(ListeJson(version: 9, vonHand: true, menge: "1")));
        var pfad = $"{Kueche}/einkaufsliste/gerichte/{Gericht}/zutaten/{Mehl}";
        _server.Bei(HttpMethod.Patch, pfad, () => Json(new { version = 9u, menge = "1" }));
        var cut = ListeRendern();

        var feld = cut.Find($"#menge-{Gericht}-{Mehl}");
        feld.Focus();
        feld.Input("1");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Equal("von Hand", Tabelle(cut.Find("#tag-2027-08-03"))[0][4]));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal(("800", "1"), (koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("menge").GetString()));
        Assert.Contains("Gespeichert", cut.Find($"#menge-{Gericht}-{Mehl}").ParentElement!.ParentElement!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Posten_ergaenzen_mit_Feldfehlern_danach_leer_und_neu_geladen_Entfernen_schickt_DELETE()
    {
        Liste();
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"{Kueche}/einkaufsliste/posten",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"errors":{"bezeichnung":["Bezeichnung fehlt."],"tag":["Nur ein Tag der Freizeit."]}}""", "application/problem+json"),
            () => Json(new { gerichtId = (Guid?)null, zutatId = (Guid?)null, postenId = Guid.CreateVersion7(), bezeichnung = "Brot", menge = 2m, einheit = "Laib", rezeptName = (string?)null, mahlzeitName = (string?)null, vonHand = true, version = 1u }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Delete, $"{Kueche}/einkaufsliste/posten/{Aepfel}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = ListeRendern();

        cut.Find("#posten-ergaenzen form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Bezeichnung: Bezeichnung fehlt.", "Tag: Nur ein Tag der Freizeit."], cut.FindAll(".zl-fehleruebersicht li a").Select(a => a.TextContent)));

        cut.Find("#posten-bezeichnung").Input("Brot");
        cut.Find("#posten-menge").Input("2");
        cut.Find("#posten-einheit").Input("Laib");
        cut.Find("#posten-tag").Input("2027-08-03");
        cut.Find("#posten-ergaenzen form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/einkaufsliste")));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Post).Koerper!);
        Assert.Equal(("Brot", "2", "Laib", "2027-08-03"), (
            koerper.RootElement.GetProperty("bezeichnung").GetString(), koerper.RootElement.GetProperty("menge").GetString(),
            koerper.RootElement.GetProperty("einheit").GetString(), koerper.RootElement.GetProperty("tag").GetString()));
        Assert.Equal("", cut.Find("#posten-bezeichnung").GetAttribute("value") ?? "");

        var handPosten = cut.Find("#hand-posten");
        handPosten.QuerySelectorAll("button").First(b => b.TextContent == "Entfernen").Click();
        cut.Find("#hand-posten").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();
        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == $"{Kueche}/einkaufsliste/posten/{Aepfel}"));
    }

    [Fact]
    public void Einkaufsliste_nach_dem_Abschluss_nur_lesbar()
    {
        Liste(phase: "Gelaufen");

        var cut = ListeRendern();

        Assert.Contains(KuecheTexte.AbgeschlossenText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Equal(["Mehl", "800", "g", "Pfannkuchen · Frühstück", ""], Tabelle(cut.Find("#tag-2027-08-03"))[0]);
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Einkaufsliste_leer_Ladefehler_und_nicht_verfuegbar()
    {
        Liste(() => Json(new { personenzahl = 2, allergienAbgefragt = true, allergene = Array.Empty<object>(), tage = Array.Empty<object>() }));
        var leer = ListeRendern();
        Assert.Contains("Noch nichts auf der Einkaufsliste.", leer.Find(".elkw-empty").TextContent, StringComparison.Ordinal);
        Assert.Contains("Keine Allergien in den bestätigten Freizeitpässen.", leer.Find("#allergen-abgleich").TextContent, StringComparison.Ordinal);

        Liste(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var fehler = ListeRendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, fehler.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(fehler.FindAll(".elkw-empty"));

        Liste(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal("Diese Seite ist nicht verfügbar.", ListeRendern().Find("h1").TextContent);
    }

    [Fact]
    public void Rezepte_zeigen_den_Kueche_Reiter_mit_Rezepte_aktiv()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, $"{Kueche}/rezepte", () => Json(new { personenzahl = 2, rezepte = Array.Empty<object>() }));

        var cut = Render<Rezepte>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));

        var reiter = cut.FindAll("nav[aria-label=Küche] a");
        Assert.Equal(["Rezepte", "Essensplan", "Einkaufsliste"], reiter.Select(a => a.TextContent));
        Assert.Equal("page", reiter[0].GetAttribute("aria-current"));
    }
}
