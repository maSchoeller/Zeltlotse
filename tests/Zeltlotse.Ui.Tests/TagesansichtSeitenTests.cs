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
using EssensplanSeite = Zeltlotse.App.Features.Kueche.Essensplan;
using TagesansichtSeite = Zeltlotse.App.Features.Kueche.Tagesansicht;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 4-4 in der Web-App mit nachgebildetem Server: die Küchen-Tagesansicht (Küche-Reiter mit fünf Einträgen, Tag-Reiter mit der Wahl des Servers,
/// Mahlzeiten mit Gerichten und Ersatzportionen, Übersicht Allergien und Sonderkost samt der Sätze „fragt keine … ab“, Restetag als Autosave-Ankreuzfeld
/// mit Hinweis, nur lesbar, Leer-Zustand ohne Gerichte, Ladefehler, nicht verfügbar), der Restetag im Essensplan und der neue Zweck im
/// Zugriffsprotokoll.
/// </summary>
public sealed class TagesansichtSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000006d2");
    private static readonly Guid Pfannkuchen = Guid.Parse("0199f700-0000-7000-8000-0000000006a1");
    private static readonly Guid FruehstueckDi = Guid.Parse("0199f700-0000-7000-8000-0000000006c1");
    private static readonly Guid MittagDi = Guid.Parse("0199f700-0000-7000-8000-0000000006c2");
    private static readonly Guid AbendDi = Guid.Parse("0199f700-0000-7000-8000-0000000006c3");
    private static readonly Guid Gericht = Guid.Parse("0199f700-0000-7000-8000-0000000006e1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public TagesansichtSeitenTests()
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

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void Kontext(string phase = "InVorbereitung") =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = new[] { "Verpflegung" }, erlaubt = Array.Empty<string>(),
        }));

    private static object Mahlzeit(Guid id, string beginn, string titel, string art, bool restetag = false, uint version = 0, params object[] gerichte) =>
        new { programmpunktId = id, beginn, titel, art, restetag, version, gerichte };

    private static object AnsichtJson(
        bool allergienAbgefragt = true, bool sonderkostAbgefragt = true, bool mitAnzahlen = true, bool abendRestetag = false, bool mitGericht = true) => new
    {
        tag = "2027-08-03",
        tage = new[] { "2027-08-02", "2027-08-03", "2027-08-04" },
        personenzahl = 8,
        allergienAbgefragt,
        allergene = allergienAbgefragt && mitAnzahlen ? new object[] { new { allergen = "gluten", anzahl = 1 } } : [],
        sonderkostAbgefragt,
        sonderkost = sonderkostAbgefragt && mitAnzahlen ? new object[] { new { sonderkost = "vegetarisch", anzahl = 1 }, new { sonderkost = "vegan", anzahl = 2 } } : [],
        mahlzeiten = new[]
        {
            Mahlzeit(
                FruehstueckDi, "08:00", "Frühstück", "Fruehstueck", gerichte: mitGericht
                    ? [new { id = Gericht, version = 5u, rezeptId = Pfannkuchen, rezeptName = "Pfannkuchen", ersatzportionen = new { anzahl = 1, allergene = new[] { "gluten" } } }]
                    : []),
            Mahlzeit(MittagDi, "12:30", "Mittagessen", "Mittagessen"),
            Mahlzeit(AbendDi, "18:30", "Abendessen", "Abendessen", abendRestetag, abendRestetag ? 7u : 0u),
        },
    };

    private void Ansicht(Func<HttpResponseMessage>? antwort = null, string phase = "InVorbereitung")
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, $"{Kueche}/tagesansicht", antwort ?? (() => Json(AnsichtJson())));
    }

    private IRenderedComponent<TagesansichtSeite> Rendern(string? tag = null)
    {
        Navigation.NavigateTo(tag is null ? $"freizeiten/{Freizeit}/kueche/tagesansicht" : $"freizeiten/{Freizeit}/kueche/tagesansicht?tag={tag}");
        var cut = Render<TagesansichtSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<TagesansichtSeite> cut, string titel) =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h3")?.TextContent == titel);

    [Fact]
    public void Tagesansicht_zeigt_fuenf_Kueche_Reiter_Tag_Reiter_vom_Server_und_je_Mahlzeit_eine_Card_mit_Gericht_und_Ersatzportion()
    {
        Ansicht();

        var cut = Rendern();

        Assert.Equal("Küche", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Tagesansicht", cut.Find("h1").TextContent);
        var kueche = cut.FindAll("nav[aria-label=Küche] a");
        Assert.Equal(["Tagesansicht", "Rezepte", "Essensplan", "Einkaufsliste", "Einkauf"], kueche.Select(a => a.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/kueche/tagesansicht", kueche[0].GetAttribute("href"));
        Assert.Equal("page", kueche[0].GetAttribute("aria-current"));
        var tage = cut.FindAll("nav[aria-label=Tage] a");
        Assert.Equal(["Mo 02.08.", "Di 03.08.", "Mi 04.08."], tage.Select(a => a.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/kueche/tagesansicht?tag=2027-08-02", tage[0].GetAttribute("href"));
        // Ohne Wahl in der Adresse wählt der Server den Tag.
        Assert.Equal("page", tage[1].GetAttribute("aria-current"));
        Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Equal(
            ["Allergien und Sonderkost", "Frühstück", "Mittagessen", "Abendessen"],
            cut.FindAll(".zl-reiter__inhalt section.elkw-card h3").Select(h => h.TextContent));
        var fruehstueck = Card(cut, "Frühstück");
        Assert.Contains("08:00", fruehstueck.TextContent, StringComparison.Ordinal);
        var rezept = fruehstueck.QuerySelectorAll("a").Single(a => a.TextContent == "Pfannkuchen");
        Assert.Equal($"freizeiten/{Freizeit}/kueche/rezepte/{Pfannkuchen}", rezept.GetAttribute("href"));
        Assert.Contains("1 Ersatzportion einplanen (enthält Gluten)", fruehstueck.TextContent, StringComparison.Ordinal);
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/tagesansicht");
    }

    [Fact]
    public void Tag_aus_der_Adresse_geht_an_den_Server()
    {
        Ansicht();

        var cut = Rendern("2027-08-03");

        Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/tagesansicht?tag=2027-08-03");
    }

    [Fact]
    public void Ungueltiger_Tag_in_der_Adresse_zeigt_den_Tag_des_Servers()
    {
        Ansicht();
        _server.Bei(
            r => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery == $"{Kueche}/tagesansicht?tag=2027-09-01",
            _ => Task.FromResult(NachgebildeterHandler.Problem(HttpStatusCode.BadRequest, "validierung")));

        var cut = Rendern("2027-09-01");

        Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Empty(cut.FindAll(".elkw-alert"));
        Assert.Equal(
            [$"{Kueche}/tagesansicht?tag=2027-09-01", $"{Kueche}/tagesansicht"],
            _server.Anfragen.Where(a => a.Methode == HttpMethod.Get && a.Pfad.StartsWith($"{Kueche}/tagesansicht", StringComparison.Ordinal)).Select(a => a.Pfad));
    }

    [Fact]
    public void Uebersicht_zeigt_Allergien_und_Sonderkost_nur_als_Anzahlen()
    {
        Ansicht();

        var uebersicht = Card(Rendern(), "Allergien und Sonderkost");

        Assert.Equal(["Gluten: 1 Person", "Vegetarisch: 1 Person", "Vegan: 2 Personen"], uebersicht.QuerySelectorAll("li").Select(l => l.TextContent));
        Assert.Contains("ohne Namen", uebersicht.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Uebersicht_nennt_nicht_abgefragte_Fragen_und_leere_Antworten_mit_eigenen_Saetzen()
    {
        Ansicht(() => Json(AnsichtJson(allergienAbgefragt: false, sonderkostAbgefragt: false)));
        var nicht = Card(Rendern(), "Allergien und Sonderkost");
        Assert.Contains("Diese Freizeit fragt keine Allergien ab.", nicht.TextContent, StringComparison.Ordinal);
        Assert.Contains("Diese Freizeit fragt keine Sonderkost ab.", nicht.TextContent, StringComparison.Ordinal);
        Assert.Empty(nicht.QuerySelectorAll("li"));

        Ansicht(() => Json(AnsichtJson(mitAnzahlen: false)));
        var leer = Card(Rendern(), "Allergien und Sonderkost");
        Assert.Contains("Keine Allergien in den bestätigten Freizeitpässen.", leer.TextContent, StringComparison.Ordinal);
        Assert.Contains("Keine Sonderkost in den bestätigten Freizeitpässen.", leer.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Gericht_Kein_Gericht_vorgemerkt_mit_Link_zum_Essensplan_am_Tag()
    {
        Ansicht(() => Json(AnsichtJson(mitGericht: false)));

        var mittag = Card(Rendern(), "Mittagessen");

        Assert.Contains("Kein Gericht vorgemerkt.", mittag.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            $"freizeiten/{Freizeit}/kueche/essensplan?tag=2027-08-03",
            mittag.QuerySelectorAll("a").Single(a => a.TextContent == "Im Essensplan vormerken").GetAttribute("href"));
    }

    [Fact]
    public void Restetag_antippen_schickt_PATCH_restetag_zeigt_Gespeichert_Rueckgaengig_und_den_Hinweis()
    {
        Ansicht();
        var pfad = $"{Kueche}/mahlzeiten/{AbendDi}";
        _server.BeiDerReihe(HttpMethod.Patch, pfad, () => Json(new { version = 9u, restetag = "true" }), () => Json(new { version = 10u, restetag = "false" }));
        var cut = Rendern();
        var abend = Card(cut, "Abendessen");
        Assert.Equal("Restetag", abend.QuerySelector("label.elkw-choice")!.TextContent.Trim());
        Assert.DoesNotContain("Reste verwerten", abend.TextContent, StringComparison.Ordinal);

        cut.Find($"#restetag-{AbendDi}").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find($"#mahlzeit-{AbendDi} [role=status]").TextContent, StringComparison.Ordinal));
        Assert.Contains(cut.Find($"#mahlzeit-{AbendDi} [role=status]").QuerySelectorAll("button"), b => b.TextContent == "Rückgängig");
        using (var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch && a.Pfad == pfad).Koerper!))
        {
            Assert.Equal((0u, "false", "true"), (koerper.RootElement.GetProperty("version").GetUInt32(), koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("restetag").GetString()));
        }

        var abendKarte = Card(cut, "Abendessen");
        Assert.Contains("Restetag: Reste verwerten.", abendKarte.TextContent, StringComparison.Ordinal);
        // Wie im Essensplan steht der Hinweis als erste Zeile der Card.
        Assert.Equal("Restetag: Reste verwerten.", abendKarte.QuerySelector("h3 + p")!.TextContent);
        Assert.True(cut.Find($"#restetag-{AbendDi}").HasAttribute("checked"));

        // Rückgängig: zweiter PATCH mit dem vorigen Wert, danach ohne Hinweis und ohne Haken.
        cut.Find($"#mahlzeit-{AbendDi} [role=status]").QuerySelectorAll("button").Single(b => b.TextContent == "Rückgängig").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Patch && a.Pfad == pfad)));
        using (var zurueck = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Patch && a.Pfad == pfad).Koerper!))
        {
            Assert.Equal((9u, "true", "false"), (zurueck.RootElement.GetProperty("version").GetUInt32(), zurueck.RootElement.GetProperty("alterWert").GetString(), zurueck.RootElement.GetProperty("restetag").GetString()));
        }

        cut.WaitForAssertion(() => Assert.DoesNotContain("Reste verwerten", Card(cut, "Abendessen").TextContent, StringComparison.Ordinal));
        Assert.False(cut.Find($"#restetag-{AbendDi}").HasAttribute("checked"));
    }

    [Fact]
    public void Tag_Reiter_wechseln_laedt_und_zeigt_den_anderen_Tag()
    {
        var mittwoch = Guid.Parse("0199f700-0000-7000-8000-0000000006c9");
        Kontext();
        _server.Bei(
            r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == $"{Kueche}/tagesansicht",
            r => Task.FromResult(r.RequestUri!.Query == "?tag=2027-08-04"
                ? Json(new
                {
                    tag = "2027-08-04",
                    tage = new[] { "2027-08-02", "2027-08-03", "2027-08-04" },
                    personenzahl = 8,
                    allergienAbgefragt = true,
                    allergene = Array.Empty<object>(),
                    sonderkostAbgefragt = true,
                    sonderkost = Array.Empty<object>(),
                    mahlzeiten = new[] { Mahlzeit(mittwoch, "08:00", "Frühstück", "Fruehstueck") },
                })
                : Json(AnsichtJson())));
        var cut = Rendern();
        Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent);

        var mi = cut.FindAll("nav[aria-label=Tage] a")[2];
        Assert.Equal($"freizeiten/{Freizeit}/kueche/tagesansicht?tag=2027-08-04", mi.GetAttribute("href"));
        Navigation.NavigateTo(mi.GetAttribute("href")!);

        cut.WaitForAssertion(() => Assert.Equal("Mittwoch, 4. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent));
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Get && a.Pfad == $"{Kueche}/tagesansicht?tag=2027-08-04");
        Assert.Equal("page", cut.FindAll("nav[aria-label=Tage] a")[2].GetAttribute("aria-current"));
        Assert.NotNull(cut.Find($"#mahlzeit-{mittwoch}"));
        Assert.Empty(cut.FindAll($"#mahlzeit-{AbendDi}"));
    }

    [Fact]
    public void Gesetzter_Restetag_zeigt_Haken_und_Hinweis()
    {
        Ansicht(() => Json(AnsichtJson(abendRestetag: true)));

        var cut = Rendern();

        Assert.True(cut.Find($"#restetag-{AbendDi}").HasAttribute("checked"));
        Assert.False(cut.Find($"#restetag-{MittagDi}").HasAttribute("checked"));
        Assert.Contains("Restetag: Reste verwerten.", Card(cut, "Abendessen").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Reste verwerten", Card(cut, "Mittagessen").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Nach_dem_Abschluss_nur_lesbar_ohne_Ankreuzfeld_Restetag_als_Text()
    {
        Ansicht(() => Json(AnsichtJson(abendRestetag: true)), phase: "Gelaufen");

        var cut = Rendern();

        Assert.Contains("nur lesbar", cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("Restetag: Reste verwerten.", Card(cut, "Abendessen").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Restetag", Card(cut, "Mittagessen").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Im Essensplan vormerken", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ladefehler_ist_keine_leere_Seite_nicht_verfuegbar_ohne_Verpflegung()
    {
        Ansicht(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var fehler = Rendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, fehler.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(fehler.FindAll("section.elkw-card"));

        Ansicht(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal("Diese Seite ist nicht verfügbar.", Rendern().Find("h1").TextContent);
    }

    // ---------- Essensplan und Texte ----------

    [Fact]
    public void Essensplan_zeigt_Restetag_als_Text_an_der_Mahlzeit()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, $"{Kueche}/essensplan", () => Json(new
        {
            personenzahl = 8,
            allergienAbgefragt = true,
            tage = new[]
            {
                new
                {
                    tag = "2027-08-03",
                    mahlzeiten = new[]
                    {
                        new { programmpunktId = MittagDi, beginn = "12:30", titel = "Mittagessen", art = "Mittagessen", gerichte = Array.Empty<object>(), restetag = false },
                        new { programmpunktId = AbendDi, beginn = "18:30", titel = "Abendessen", art = "Abendessen", gerichte = Array.Empty<object>(), restetag = true },
                    },
                },
            },
            rezepte = Array.Empty<object>(),
        }));
        Navigation.NavigateTo($"freizeiten/{Freizeit}/kueche/essensplan?tag=2027-08-03");

        var cut = Render<EssensplanSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));

        Assert.Equal(["Tagesansicht", "Rezepte", "Essensplan", "Einkaufsliste", "Einkauf"], cut.FindAll("nav[aria-label=Küche] a").Select(a => a.TextContent));
        Assert.Contains("Restetag", cut.Find($"#mahlzeit-{AbendDi}").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Restetag", cut.Find($"#mahlzeit-{MittagDi}").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Texte_der_Kueche_fuer_Sonderkost_und_Zugriffsprotokoll()
    {
        Assert.Equal("Vegetarisch: 1 Person", KuecheTexte.SonderkostZeile(new Zeltlotse.Api.Contracts.Kueche.SonderkostAnzahl("vegetarisch", 1)));
        Assert.Equal("Kein Schweinefleisch: 3 Personen", KuecheTexte.SonderkostZeile(new Zeltlotse.Api.Contracts.Kueche.SonderkostAnzahl("kein-schweinefleisch", 3)));
        Assert.Equal($"freizeiten/{Freizeit}/kueche/tagesansicht", KuecheTexte.TagesansichtZiel(Freizeit));
        Assert.Equal($"freizeiten/{Freizeit}/kueche/tagesansicht?tag=2027-08-03", KuecheTexte.TagesansichtZiel(Freizeit, new DateOnly(2027, 8, 3)));
        Assert.Equal("Sonderkost-Abgleich der Küche", Zeltlotse.Ui.Texte.Anzeige.Zweck(Zeltlotse.Api.Contracts.Personen.ZugriffZweck.SonderkostFuerKueche));
    }
}
