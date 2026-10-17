using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Kueche;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using RezeptSeite = Zeltlotse.App.Features.Kueche.Rezept;
using RezepteSeite = Zeltlotse.App.Features.Kueche.Rezepte;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 4-1 in der Web-App mit nachgebildetem Server: die Rezepte (Cards mit Basis und Allergenen, Personenzahl, leer, Ladefehler, nicht verfügbar,
/// Anlegen mit Fehlerübersicht), die Rezeptseite (Tabelle mit hochgerechneter Spalte, Hinweis bei 0 Personen, Autosave mit Neuladen, Zutat hinzufügen
/// und entfernen, Rezept löschen, Ladefehler), nur lesbar nach dem Abschluss, der Knopf „Küche“ auf der Startseite nur mit Verpflegung und der Client.
/// </summary>
public sealed class RezeptSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000004d1");
    private static readonly Guid Pfannkuchen = Guid.Parse("0199f700-0000-7000-8000-0000000004a1");
    private static readonly Guid Chili = Guid.Parse("0199f700-0000-7000-8000-0000000004a2");
    private static readonly Guid Mehl = Guid.Parse("0199f700-0000-7000-8000-0000000004b1");
    private static readonly Guid Milch = Guid.Parse("0199f700-0000-7000-8000-0000000004b2");
    private static readonly Guid Salz = Guid.Parse("0199f700-0000-7000-8000-0000000004b3");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public RezeptSeitenTests()
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

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string ListePfad => $"/api/freizeiten/{Freizeit}/kueche/rezepte";

    private static string RezeptPfad(Guid rezept) => $"{ListePfad}/{rezept}";

    private static string ZutatPfad(Guid rezept, Guid zutat) => $"{RezeptPfad(rezept)}/zutaten/{zutat}";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Zutat(Guid id, string bezeichnung, decimal? menge, string? einheit, string? allergene, decimal? hoch, uint version = 3) =>
        new { id, version, bezeichnung, menge, einheit, allergene, hochgerechneteMenge = hoch };

    private static object PfannkuchenJson(int personen = 6, decimal mehlMenge = 1.5m, decimal? mehlHoch = 2.25m, uint version = 7, bool mitZutaten = true) => new
    {
        id = Pfannkuchen, version, name = "Pfannkuchen", basisPersonen = 4, zubereitung = "Alles verrühren.\nIn der Pfanne ausbacken.", personenzahl = personen,
        zutaten = mitZutaten
            ? new[]
            {
                Zutat(Mehl, "Mehl", mehlMenge, "kg", "gluten", mehlHoch),
                Zutat(Milch, "Milch", 0.75m, "l", "milch", personen == 0 ? null : 1.13m),
                Zutat(Salz, "Salz", null, null, null, null),
            }
            : [],
    };

    private void Kontext(string phase = "InVorbereitung", string[]? schalter = null) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = schalter ?? ["Verpflegung"],
            erlaubt = Array.Empty<string>(),
        }));

    private void ListeAntworten(string phase = "InVorbereitung", object[]? rezepte = null, int personen = 6)
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, ListePfad, () => Json(new
        {
            personenzahl = personen,
            rezepte = rezepte ??
            [
                new { id = Chili, name = "Chili sin Carne", basisPersonen = 4, zutaten = 7, allergene = "sellerie" },
                new { id = Pfannkuchen, name = "Pfannkuchen", basisPersonen = 1, zutaten = 1, allergene = (string?)null },
            ],
        }));
    }

    private void RezeptAntworten(string phase = "InVorbereitung", int personen = 6)
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson(personen)));
    }

    private IRenderedComponent<RezepteSeite> ListeRendern()
    {
        var cut = Render<RezepteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<RezeptSeite> RezeptRendern()
    {
        var cut = Render<RezeptSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.RezeptId, Pfannkuchen));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static List<List<string>> Tabelle(IRenderedComponent<RezeptSeite> cut) =>
        [.. cut.FindAll("#zutaten tbody tr").Select(z => z.QuerySelectorAll("td").Select(t => t.TextContent.Trim()).ToList())];

    // ---------- Rezepte ----------

    [Fact]
    public void Rezepte_zeigen_je_Rezept_eine_Card_mit_Link_Basis_Zutaten_und_Allergenen_und_die_Personenzahl()
    {
        ListeAntworten();

        var cut = ListeRendern();

        Assert.Equal("Küche", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Rezepte", cut.Find("h1").TextContent);
        Assert.Contains("Hochgerechnet auf 6 Personen der Freizeit.", cut.FindAll("p").Select(p => p.TextContent));
        var cards = cut.FindAll("section.elkw-card").Where(c => c.QuerySelector("h2 a") is not null).ToList();
        Assert.Equal(["Chili sin Carne", "Pfannkuchen"], cards.Select(c => c.QuerySelector("h2")!.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/kueche/rezepte/{Chili}", cards[0].QuerySelector("h2 a")!.GetAttribute("href"));
        Assert.Equal(["für 4 Personen · 7 Zutaten", "Allergene: Sellerie"], cards[0].QuerySelectorAll("p").Select(p => p.TextContent));
        Assert.Equal(["für 1 Person · 1 Zutat", "Allergene: keine"], cards[1].QuerySelectorAll("p").Select(p => p.TextContent));
        Assert.NotNull(cut.Find("#rezept-anlegen"));
    }

    [Fact]
    public void Rezepte_leer_und_ohne_Personen()
    {
        ListeAntworten(rezepte: [], personen: 0);

        var cut = ListeRendern();

        Assert.Equal("Noch keine Rezepte.", cut.Find(".elkw-empty p").TextContent);
        Assert.Contains(KuecheTexte.KeinePersonenText, cut.FindAll("p").Select(p => p.TextContent));
    }

    [Fact]
    public void Rezepte_Ladefehler_zeigt_die_Meldung_und_nicht_leer()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = ListeRendern();

        Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Noch keine Rezepte.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#rezept-anlegen"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Rezepte_ohne_Verpflegung_oder_Recht_sind_nicht_verfuegbar(HttpStatusCode status)
    {
        Kontext(schalter: []);
        _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(status));
        _server.Bei(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => new HttpResponseMessage(status));

        var liste = ListeRendern();
        var rezept = RezeptRendern();

        Assert.Equal("Diese Seite ist nicht verfügbar.", liste.Find("h1").TextContent);
        Assert.Equal("Diese Seite ist nicht verfügbar.", rezept.Find("h1").TextContent);
    }

    [Fact]
    public void Rezept_anlegen_mit_Feldfehlern_zeigt_Fehleruebersicht_und_Feld_danach_oeffnet_sich_das_Rezept()
    {
        ListeAntworten();
        _server.BeiDerReihe(
            HttpMethod.Post,
            ListePfad,
            () => NachgebildeterHandler.Json(
                HttpStatusCode.BadRequest,
                """{"status":400,"errors":{"name":["Name fehlt."],"basisPersonen":["Nur eine ganze Zahl von 1 bis 1000."]}}""",
                "application/problem+json"),
            () => Json(PfannkuchenJson(mitZutaten: false), HttpStatusCode.Created));
        var cut = ListeRendern();

        cut.Find("#rezept-basis").Input("abc");
        cut.Find("#rezept-anlegen form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Name: Name fehlt.", "Basis-Personenzahl: Nur eine ganze Zahl von 1 bis 1000."],
            cut.FindAll(".zl-fehleruebersicht li a").Select(a => a.TextContent)));
        Assert.Equal("#rezept-name", cut.FindAll(".zl-fehleruebersicht li a")[0].GetAttribute("href"));
        Assert.Contains("Name fehlt.", cut.Markup, StringComparison.Ordinal);

        cut.Find("#rezept-name").Input("Pfannkuchen");
        cut.Find("#rezept-basis").Input("4");
        cut.Find("#rezept-anlegen form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/kueche/rezepte/{Pfannkuchen}", Navigation.Uri, StringComparison.Ordinal));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Post && a.Pfad == ListePfad).Koerper!);
        Assert.Equal(("Pfannkuchen", "4"), (koerper.RootElement.GetProperty("name").GetString(), koerper.RootElement.GetProperty("basisPersonen").GetString()));
    }

    [Fact]
    public void Rezepte_nach_dem_Abschluss_nur_lesbar()
    {
        ListeAntworten("Gelaufen");

        var cut = ListeRendern();

        Assert.Contains(KuecheTexte.AbgeschlossenText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#rezept-anlegen"));
        Assert.Empty(cut.FindAll("form"));
    }

    // ---------- Rezept ----------

    [Fact]
    public void Rezept_zeigt_Tabelle_mit_hochgerechneter_Spalte_Zubereitung_und_Felder()
    {
        RezeptAntworten();

        var cut = RezeptRendern();

        Assert.Equal("Pfannkuchen", cut.Find("h1").TextContent);
        Assert.Contains("für 4 Personen", cut.FindAll("p").Select(p => p.TextContent));
        Assert.Contains("Hochgerechnet auf 6 Personen der Freizeit.", cut.Find("#zutaten").TextContent, StringComparison.Ordinal);
        Assert.Equal(["Menge", "Einheit", "Zutat", "Allergene", "Für 6 Personen"], cut.FindAll("#zutaten th").Select(t => t.TextContent));
        Assert.Equal("Zutaten für 6 Personen", cut.Find("#zutaten caption").TextContent);
        Assert.Equal(
            [
                ["1,5", "kg", "Mehl", "Glutenhaltiges Getreide", "2,25 kg"],
                ["0,75", "l", "Milch", "Milch (inkl. Laktose)", "1,13 l"],
                ["", "", "Salz", "", ""],
            ],
            Tabelle(cut));
        Assert.Equal(["Alles verrühren.", "In der Pfanne ausbacken."], cut.FindAll("#zubereitung p").Select(p => p.TextContent));
        Assert.Equal("Pfannkuchen", cut.Find("#rezept-name").GetAttribute("value"));
        Assert.Equal("4", cut.Find("#rezept-basis").GetAttribute("value"));
        Assert.Equal("1,5", cut.Find($"#zutat-{Mehl}-menge").GetAttribute("value"));
        Assert.Equal(14, cut.FindAll($"#zutat-{Mehl}-allergene input[type=checkbox]").Count);
        Assert.True(cut.FindAll($"#zutat-{Mehl}-allergene input[type=checkbox]").Single(k => k.GetAttribute("value") == "gluten").HasAttribute("checked"));
        Assert.Contains("Rezept löschen", cut.FindAll("button").Select(b => b.TextContent));
    }

    [Fact]
    public void Rezept_ohne_Personen_zeigt_den_Hinweis_und_keine_hochgerechnete_Spalte()
    {
        RezeptAntworten(personen: 0);
        _server.Bei(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson(0, mehlHoch: null)));

        var cut = RezeptRendern();

        Assert.Contains(KuecheTexte.KeinePersonenText, cut.Find("#zutaten").TextContent, StringComparison.Ordinal);
        Assert.Equal(["Menge", "Einheit", "Zutat", "Allergene"], cut.FindAll("#zutaten th").Select(t => t.TextContent));
    }

    [Fact]
    public void Rezept_nach_dem_Abschluss_nur_lesbar_ohne_Felder_Formular_und_Loeschen()
    {
        RezeptAntworten("Gelaufen");

        var cut = RezeptRendern();

        Assert.Contains(KuecheTexte.AbgeschlossenText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Equal(3, Tabelle(cut).Count);
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("textarea"));
        Assert.Empty(cut.FindAll("form"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent is "Rezept löschen" or "Entfernen");
    }

    [Fact]
    public void Rezept_Ladefehler_zeigt_die_Meldung()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = RezeptRendern();

        Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Menge_speichern_laedt_das_Rezept_neu_und_das_Feld_behaelt_Gespeichert()
    {
        Kontext();
        _server.BeiDerReihe(
            HttpMethod.Get,
            RezeptPfad(Pfannkuchen),
            () => Json(PfannkuchenJson()),
            () => Json(PfannkuchenJson(mehlMenge: 2m, mehlHoch: 3m, version: 8)));
        _server.Bei(HttpMethod.Patch, ZutatPfad(Pfannkuchen, Mehl), () => Json(new { version = 4u, menge = "2" }));
        var cut = RezeptRendern();

        var feld = cut.Find($"#zutat-{Mehl}-menge");
        feld.Focus();
        feld.Input("2");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Equal(["2", "kg", "Mehl", "Glutenhaltiges Getreide", "3 kg"], Tabelle(cut)[0]));
        Assert.Contains("Gespeichert", cut.Find($"#zutat-{Mehl}-menge").ParentElement!.ParentElement!.TextContent, StringComparison.Ordinal);
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal((3u, "1,5", "2"), (
            koerper.RootElement.GetProperty("version").GetUInt32(), koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("menge").GetString()));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == RezeptPfad(Pfannkuchen)));
    }

    [Fact]
    public void Allergen_ankreuzen_speichert_die_Komma_Liste_und_laedt_neu()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson()));
        _server.Bei(HttpMethod.Patch, ZutatPfad(Pfannkuchen, Milch), () => Json(new { version = 4u, allergene = "eier,milch" }));
        var cut = RezeptRendern();

        cut.FindAll($"#zutat-{Milch}-allergene input[type=checkbox]").Single(k => k.GetAttribute("value") == "eier").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == RezeptPfad(Pfannkuchen))));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal(("milch", "eier,milch"), (koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("allergene").GetString()));
    }

    [Fact]
    public void Zutat_hinzufuegen_mit_Feldfehler_dann_erfolgreich_leert_das_Formular_und_laedt_neu()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson()), () => Json(PfannkuchenJson()));
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"{RezeptPfad(Pfannkuchen)}/zutaten",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"errors":{"menge":["Nur eine Zahl größer 0 bis 1000000 mit höchstens 3 Nachkommastellen."]}}""", "application/problem+json"),
            () => Json(Zutat(Guid.CreateVersion7(), "Eier", 4m, "Stück", null, 6m), HttpStatusCode.Created));
        var cut = RezeptRendern();

        cut.Find("#zutat-bezeichnung").Input("Eier");
        cut.Find("#zutat-menge").Input("x");
        cut.Find("#zutat-hinzufuegen form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(["Menge: Nur eine Zahl größer 0 bis 1000000 mit höchstens 3 Nachkommastellen."], cut.FindAll(".zl-fehleruebersicht li a").Select(a => a.TextContent)));

        cut.Find("#zutat-menge").Input("4");
        cut.Find("#zutat-einheit").Input("Stück");
        cut.Find("#zutat-hinzufuegen form").Submit();

        cut.WaitForAssertion(() => Assert.Null(cut.Find("#zutat-bezeichnung").GetAttribute("value")));
        Assert.Empty(cut.FindAll(".zl-fehleruebersicht"));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Post && a.Pfad.EndsWith("/zutaten", StringComparison.Ordinal)).Koerper!);
        Assert.Equal(("Eier", "4", "Stück"), (koerper.RootElement.GetProperty("bezeichnung").GetString(), koerper.RootElement.GetProperty("menge").GetString(), koerper.RootElement.GetProperty("einheit").GetString()));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == RezeptPfad(Pfannkuchen)));
    }

    [Fact]
    public void Zutat_entfernen_per_Inline_Bestaetigung_und_Rezept_loeschen_fuehrt_zu_den_Rezepten()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson()), () => Json(new
        {
            id = Pfannkuchen, version = 9u, name = "Pfannkuchen", basisPersonen = 4, zubereitung = (string?)null, personenzahl = 6,
            zutaten = new[] { Zutat(Mehl, "Mehl", 1.5m, "kg", "gluten", 2.25m), Zutat(Milch, "Milch", 0.75m, "l", "milch", 1.13m) },
        }));
        _server.Bei(HttpMethod.Delete, ZutatPfad(Pfannkuchen, Salz), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Delete, RezeptPfad(Pfannkuchen), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RezeptRendern();

        var salzCard = cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Zutat: Salz");
        salzCard.QuerySelectorAll("button").Single(b => b.TextContent == "Entfernen").Click();
        Assert.Contains("„Salz“ entfernen?", cut.Markup, StringComparison.Ordinal);
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Zutat: Salz").QuerySelectorAll("button")
            .Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, Tabelle(cut).Count));
        Assert.DoesNotContain(cut.FindAll("section.elkw-card h2"), h => h.TextContent == "Zutat: Salz");

        cut.FindAll("button").Single(b => b.TextContent == "Rezept löschen").Click();
        Assert.Contains("Rezept „Pfannkuchen“ mit allen Zutaten löschen?", cut.Markup, StringComparison.Ordinal);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/kueche/rezepte", Navigation.Uri, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == RezeptPfad(Pfannkuchen));
    }

    [Fact]
    public void Rezept_fuer_eine_Person_nennt_die_Spalte_und_Beschriftung_in_der_Einzahl()
    {
        RezeptAntworten(personen: 1);
        _server.Bei(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson(1, mehlHoch: 0.38m)));

        var cut = RezeptRendern();

        Assert.Equal("Für 1 Person", cut.FindAll("#zutaten th").Last().TextContent);
        Assert.Equal("Zutaten für 1 Person", cut.Find("#zutaten caption").TextContent);
        Assert.Contains("Hochgerechnet auf 1 Person der Freizeit.", cut.Find("#zutaten").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Zutat_entfernen_die_schon_weg_ist_laedt_neu_und_die_veraltete_Card_verschwindet()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson()), () => Json(new
        {
            id = Pfannkuchen, version = 9u, name = "Pfannkuchen", basisPersonen = 4, zubereitung = (string?)null, personenzahl = 6,
            zutaten = new[] { Zutat(Mehl, "Mehl", 1.5m, "kg", "gluten", 2.25m), Zutat(Milch, "Milch", 0.75m, "l", "milch", 1.13m) },
        }));
        _server.Bei(HttpMethod.Delete, ZutatPfad(Pfannkuchen, Salz), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = RezeptRendern();

        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Zutat: Salz").QuerySelectorAll("button")
            .Single(b => b.TextContent == "Entfernen").Click();
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Zutat: Salz").QuerySelectorAll("button")
            .Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain(cut.FindAll("section.elkw-card h2"), h => h.TextContent == "Zutat: Salz"));
        Assert.Equal(2, Tabelle(cut).Count);
    }

    [Fact]
    public void Rezept_loeschen_das_schon_weg_ist_fuehrt_zu_den_Rezepten()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, RezeptPfad(Pfannkuchen), () => Json(PfannkuchenJson()), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Delete, RezeptPfad(Pfannkuchen), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = RezeptRendern();

        cut.FindAll("button").Single(b => b.TextContent == "Rezept löschen").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/kueche/rezepte", Navigation.Uri, StringComparison.Ordinal));
    }

    // ---------- Startseite ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Startseite_zeigt_den_Knopf_Kueche_nur_mit_Verpflegung(bool verpflegung)
    {
        Kontext(schalter: verpflegung ? ["Verpflegung", "Finanzen"] : ["Finanzen"]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        var kueche = cut.FindAll("a").Where(a => a.TextContent == "Küche").ToList();
        if (verpflegung)
        {
            Assert.Equal($"freizeiten/{Freizeit}/kueche/rezepte", Assert.Single(kueche).GetAttribute("href"));
        }
        else
        {
            Assert.Empty(kueche);
        }
    }

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_Rezepte_403_404_heissen_nicht_verfuegbar_und_Aktionen_ordnen_Antworten_zu()
    {
        ListeAntworten();
        _server.Bei(HttpMethod.Get, RezeptPfad(Chili), () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.Bei(HttpMethod.Delete, RezeptPfad(Chili), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        _server.Bei(HttpMethod.Delete, ZutatPfad(Chili, Mehl), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        _server.Bei(HttpMethod.Post, $"{RezeptPfad(Chili)}/zutaten", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var client = Services.GetRequiredService<KuecheApiClient>();

        var liste = await client.RezepteLadenAsync(Freizeit, Abbruch);

        Assert.Equal((6, 2), (liste!.Personenzahl, liste.Rezepte.Count));
        Assert.Null(await client.RezeptLadenAsync(Freizeit, Chili, Abbruch));
        Assert.Null(await client.RezeptLadenAsync(Freizeit, Guid.CreateVersion7(), Abbruch));
        Assert.Equal(KuecheAktionErgebnis.Abgeschlossen, await client.RezeptLoeschenAsync(Freizeit, Chili, Abbruch));
        Assert.Equal(KuecheAktionErgebnis.Fehler, await client.ZutatLoeschenAsync(Freizeit, Chili, Mehl, Abbruch));
        Assert.Equal(KuecheAktionErgebnis.Abgeschlossen, (await client.ZutatAnlegenAsync(Freizeit, Chili, new ZutatNeu("Mehl", null, null), Abbruch)).Ergebnis);
        Assert.Equal($"/api/freizeiten/{Freizeit}/kueche/rezepte/{Chili}/zutaten/{Mehl}", KuecheApiClient.ZutatPfad(Freizeit, Chili, Mehl));
    }

    [Fact]
    public void Texte_Menge_Personen_und_Allergene()
    {
        Assert.Equal(("2,25", "6000", ""), (Anzeige.Menge(2.25m), Anzeige.Menge(6000.00m), Anzeige.Menge(null)));
        Assert.Equal(("für 1 Person", "für 4 Personen"), (Anzeige.FuerPersonen(1), Anzeige.FuerPersonen(4)));
        Assert.Equal(("Glutenhaltiges Getreide, Eier", "keine"), (Anzeige.Allergene("eier,gluten"), Anzeige.Allergene(null)));
        Assert.Equal(("Hochgerechnet auf 1 Person der Freizeit.", KuecheTexte.KeinePersonenText), (KuecheTexte.Hochgerechnet(1), KuecheTexte.Hochgerechnet(0)));
    }
}
