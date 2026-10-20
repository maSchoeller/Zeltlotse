using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.App.Features.Finanzen;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using BeitragsrechnerSeite = Zeltlotse.App.Features.Finanzen.Beitragsrechner;
using KalkulationSeite = Zeltlotse.App.Features.Finanzen.Kalkulation;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 5-1 in der Web-App mit nachgebildetem Server: die Kalkulation (Reiter, Ergebnis mit Teilnehmerbeitrag als Zahl und Balken, Anteile der
/// Kategorien, Planzahlen und Beträge als Autosave-Felder mit Neuladen des Ergebnisses, Einnahme anlegen und löschen, 0 Teilnehmende, nur lesbar,
/// Ladefehler, nicht verfügbar), der Beitragsrechner (Regler ändern den Beitrag sofort ohne Serveraufruf, Zurücksetzen, 0 Teilnehmende, nach dem
/// Abschluss bedienbar), der Knopf „Finanzen“ auf der Startseite nur mit dem Modul Finanzen, der Client und der Euro-Text.
/// </summary>
public sealed class FinanzenSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000007d1");
    private static readonly Guid Zuschuss = Guid.Parse("0199f700-0000-7000-8000-0000000007e1");
    private static readonly Guid Rabatt = Guid.Parse("0199f700-0000-7000-8000-0000000007e2");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public FinanzenSeitenTests()
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

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string Pfad => $"/api/freizeiten/{Freizeit}/finanzen/kalkulation";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void Kontext(string phase = "InVorbereitung", string[]? schalter = null) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = schalter ?? ["Verpflegung", "Finanzen"],
            erlaubt = Array.Empty<string>(),
        }));

    private static object Kategorie(string kategorie, decimal fix, decimal pro, decimal kosten, uint version = 0) =>
        new { kategorie, version, fixkosten = fix, proPersonUndTag = pro, kosten };

    /// <summary>Die Rechnung der Matrix: 40 TN, 10 MA, 12 Tage, Unterkunft 2.000 € + 10 €, Verpflegung 7 €, Zuschuss 1.200 € → 275,00 €.</summary>
    private static object KalkulationJson(int teilnehmende = 40, decimal unterkunftFix = 2000m, decimal zuschuss = 1200m, bool mitEinnahmen = true, decimal tagessatz = 7m)
    {
        var personen = teilnehmende + 10;
        var unterkunft = unterkunftFix + 10m * personen * 12;
        var verpflegung = tagessatz * personen * 12;
        var einnahmen = mitEinnahmen ? zuschuss : 0m;
        var ergebnis = Beitragsrechnung.Rechnen(
            teilnehmende, 10, 12, [new Kostenansatz(unterkunftFix, 10m), new Kostenansatz(0m, tagessatz), new Kostenansatz(0m, 0m), new Kostenansatz(0m, 0m), new Kostenansatz(0m, 0m), new Kostenansatz(0m, 0m)],
            mitEinnahmen ? [zuschuss] : []);
        return new
        {
            version = 3u,
            teilnehmende,
            mitarbeitende = 10,
            tage = 12,
            kategorien = new[]
            {
                Kategorie("Unterkunft", unterkunftFix, 10m, unterkunft, 4),
                Kategorie("Verpflegung", 0m, tagessatz, verpflegung, 5),
                Kategorie("Fahrt", 0m, 0m, 0m),
                Kategorie("ProgrammUndMaterial", 0m, 0m, 0m),
                Kategorie("Mitarbeitende", 0m, 0m, 0m),
                Kategorie("Sonstiges", 0m, 0m, 0m),
            },
            einnahmen = mitEinnahmen ? new object[] { new { id = Zuschuss, version = 6u, bezeichnung = "Zuschuss Landesjugendplan", betrag = zuschuss } } : [],
            ergebnis = new { gesamtkosten = ergebnis.Gesamtkosten, einnahmen = einnahmen, teilnehmerbeitrag = ergebnis.Teilnehmerbeitrag },
        };
    }

    private void Antworten(string phase = "InVorbereitung", object? kalkulation = null)
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, Pfad, () => Json(kalkulation ?? KalkulationJson()));
    }

    private IRenderedComponent<KalkulationSeite> KalkulationRendern()
    {
        var cut = Render<KalkulationSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<BeitragsrechnerSeite> RechnerRendern()
    {
        var cut = Render<BeitragsrechnerSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Balken<T>(IRenderedComponent<T> cut, string bezeichnung)
        where T : IComponent =>
        cut.FindAll(".zl-balken").Single(b => b.QuerySelector(".zl-balken__kopf strong")!.TextContent == bezeichnung);

    private static string Zahl(AngleSharp.Dom.IElement balken) => balken.QuerySelector(".zl-zahl")!.TextContent;

    private static string Breite(AngleSharp.Dom.IElement balken) => balken.QuerySelector("rect")!.GetAttribute("width")!;

    // ---------- Kalkulation ----------

    [Fact]
    public void Kalkulation_zeigt_Reiter_Ergebnis_mit_Beitrag_als_Zahl_und_Balken_Planzahlen_sechs_Kategorien_und_Einnahmen()
    {
        Antworten();

        var cut = KalkulationRendern();

        Assert.Equal("Finanzen", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Kalkulation", cut.Find("h1").TextContent);
        var reiter = cut.FindAll("nav[aria-label=Finanzen] a");
        Assert.Equal(["Kalkulation", "Beitragsrechner"], reiter.Select(a => a.TextContent));
        Assert.Equal(($"freizeiten/{Freizeit}/finanzen/kalkulation", "page"), (reiter[0].GetAttribute("href"), reiter[0].GetAttribute("aria-current")));
        Assert.Equal($"freizeiten/{Freizeit}/finanzen/beitragsrechner", reiter[1].GetAttribute("href"));

        var ergebnis = cut.Find("#ergebnis");
        var beitrag = Balken(cut, "Teilnehmerbeitrag");
        Assert.Equal("275,00 €", Zahl(beitrag));
        // Der Balken zeigt den Anteil der Kosten, der über die Beiträge läuft: 275 € × 40 = 11.000 € von 12.200 €.
        Assert.Equal("90.16%", Breite(beitrag));
        Assert.Equal("90 % der Kosten über Beiträge", beitrag.QuerySelector(".elkw-status")!.TextContent);
        Assert.Contains("Gesamtkosten", ergebnis.TextContent, StringComparison.Ordinal);
        Assert.Contains("12.200,00 €", ergebnis.TextContent, StringComparison.Ordinal);
        Assert.Contains("1.200,00 €", ergebnis.TextContent, StringComparison.Ordinal);
        // Je Kategorie ein Balken mit ihrem Anteil an den Gesamtkosten: Unterkunft 8.000 € von 12.200 €.
        var unterkunft = Balken(cut, "Unterkunft");
        Assert.Equal(("8.000,00 €", "65.57%"), (Zahl(unterkunft), Breite(unterkunft)));
        Assert.Equal("66 % der Kosten", unterkunft.QuerySelector(".elkw-status")!.TextContent);
        Assert.Equal(("0,00 €", "0%"), (Zahl(Balken(cut, "Fahrt")), Breite(Balken(cut, "Fahrt"))));

        Assert.Equal("40", cut.Find("#kalkulation-teilnehmende").GetAttribute("value"));
        Assert.Equal("10", cut.Find("#kalkulation-mitarbeitende").GetAttribute("value"));
        Assert.Contains("12 Tage", cut.Find("#planzahlen").TextContent, StringComparison.Ordinal);

        Assert.Equal(
            ["Unterkunft", "Verpflegung", "Fahrt", "Programm und Material", "Mitarbeitende", "Sonstiges"],
            cut.FindAll("section.elkw-card[id^=kategorie-] h2").Select(h => h.TextContent));
        Assert.Equal("2000", cut.Find("#kategorie-Unterkunft-fixkosten").GetAttribute("value"));
        Assert.Equal("Tagessatz pro Person und Tag in €", cut.Find("label[for=kategorie-Verpflegung-pro-person-und-tag]").TextContent);
        Assert.Equal("Kosten pro Person und Tag in €", cut.Find("label[for=kategorie-Fahrt-pro-person-und-tag]").TextContent);
        Assert.Equal("7", cut.Find("#kategorie-Verpflegung-pro-person-und-tag").GetAttribute("value"));
        Assert.Contains("Kosten: 4.200,00 €", cut.Find("#kategorie-Verpflegung").TextContent, StringComparison.Ordinal);

        Assert.Equal("Zuschuss Landesjugendplan", cut.Find($"#einnahme-{Zuschuss}-bezeichnung").GetAttribute("value"));
        Assert.Equal("1200", cut.Find($"#einnahme-{Zuschuss}-betrag").GetAttribute("value"));
        Assert.NotNull(cut.Find("#einnahme-hinzufuegen form"));
    }

    [Fact]
    public void Betrag_speichern_laedt_das_Ergebnis_neu_und_das_Feld_behaelt_Gespeichert()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, Pfad, () => Json(KalkulationJson()), () => Json(KalkulationJson(unterkunftFix: 2400m)));
        _server.Bei(HttpMethod.Patch, $"{Pfad}/kategorien/Unterkunft", () => Json(new { version = 8u, fixkosten = "2400" }));
        var cut = KalkulationRendern();

        var feld = cut.Find("#kategorie-Unterkunft-fixkosten");
        feld.Focus();
        feld.Input("2400");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Equal("285,00 €", Zahl(Balken(cut, "Teilnehmerbeitrag"))));
        Assert.Contains("Gespeichert", cut.Find("#kategorie-Unterkunft-fixkosten").ParentElement!.TextContent, StringComparison.Ordinal);
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal((4u, "2000", "2400"), (
            koerper.RootElement.GetProperty("version").GetUInt32(), koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("fixkosten").GetString()));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad));
    }

    [Fact]
    public void Planzahl_speichern_schickt_den_PATCH_an_die_Kalkulation()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, Pfad, () => Json(KalkulationJson()), () => Json(KalkulationJson(teilnehmende: 50)));
        _server.Bei(HttpMethod.Patch, Pfad, () => Json(new { version = 9u, teilnehmende = "50" }));
        var cut = KalkulationRendern();

        var feld = cut.Find("#kalkulation-teilnehmende");
        feld.Focus();
        feld.Input("50");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad)));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal(("40", "50"), (koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("teilnehmende").GetString()));
    }

    [Fact]
    public void Einnahme_hinzufuegen_mit_Feldfehler_dann_erfolgreich_und_entfernen_per_Inline_Bestaetigung()
    {
        Kontext();
        _server.BeiDerReihe(
            HttpMethod.Get, Pfad, () => Json(KalkulationJson()), () => Json(KalkulationJson()), () => Json(KalkulationJson(mitEinnahmen: false)));
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"{Pfad}/einnahmen",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"errors":{"betrag":["Nur ein Betrag von 0 bis 1000000 Euro mit höchstens 2 Nachkommastellen, keine Tausenderpunkte."]}}""", "application/problem+json"),
            () => Json(new { id = Rabatt, version = 7u, bezeichnung = "Rabatt der Organisation", betrag = 300m }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/einnahmen/{Zuschuss}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = KalkulationRendern();

        cut.Find("#einnahme-bezeichnung").Input("Rabatt der Organisation");
        cut.Find("#einnahme-betrag").Input("1.250");
        cut.Find("#einnahme-hinzufuegen form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Betrag: Nur ein Betrag von 0 bis 1000000 Euro mit höchstens 2 Nachkommastellen, keine Tausenderpunkte."], cut.FindAll(".zl-fehleruebersicht li a").Select(a => a.TextContent)));

        cut.Find("#einnahme-betrag").Input("300");
        cut.Find("#einnahme-hinzufuegen form").Submit();
        cut.WaitForAssertion(() => Assert.Null(cut.Find("#einnahme-bezeichnung").GetAttribute("value")));
        using (var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Post).Koerper!))
        {
            Assert.Equal(("Rabatt der Organisation", "300"), (koerper.RootElement.GetProperty("bezeichnung").GetString(), koerper.RootElement.GetProperty("betrag").GetString()));
        }

        cut.Find("#einnahmen").QuerySelectorAll("button").Single(b => b.TextContent == "Entfernen").Click();
        Assert.Contains("„Zuschuss Landesjugendplan“ entfernen?", cut.Markup, StringComparison.Ordinal);
        cut.Find("#einnahmen").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Einnahmen.", cut.Find("#einnahmen").TextContent, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
    }

    [Fact]
    public void Ohne_Teilnehmende_steht_der_Satz_statt_eines_Beitrags()
    {
        Antworten(kalkulation: KalkulationJson(teilnehmende: 0));

        var cut = KalkulationRendern();

        Assert.Contains(FinanzenTexte.KeinBeitragText, cut.Find("#ergebnis").TextContent, StringComparison.Ordinal);
        Assert.Equal("–", Zahl(Balken(cut, "Teilnehmerbeitrag")));
        Assert.Equal("Kein Beitrag", Balken(cut, "Teilnehmerbeitrag").QuerySelector(".elkw-status")!.TextContent);
    }

    [Fact]
    public void Gelaufen_nur_lesbar_ohne_Eingaben_die_Werte_als_Text()
    {
        Antworten("Gelaufen");

        var cut = KalkulationRendern();

        Assert.Contains(FinanzenTexte.AbgeschlossenText, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("form"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Entfernen");
        Assert.Equal("275,00 €", Zahl(Balken(cut, "Teilnehmerbeitrag")));
        Assert.Contains("Teilnehmende: 40", cut.Find("#planzahlen").TextContent, StringComparison.Ordinal);
        Assert.Contains("Tagessatz pro Person und Tag: 7,00 €", cut.Find("#kategorie-Verpflegung").TextContent, StringComparison.Ordinal);
        Assert.Contains("Zuschuss Landesjugendplan: 1.200,00 €", cut.Find("#einnahmen").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Ladefehler_zeigt_die_Meldung_statt_einer_leeren_Kalkulation()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, Pfad, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = KalkulationRendern();

        Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#ergebnis"));
        var rechner = RechnerRendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, rechner.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(rechner.FindAll("input"));
    }

    [Fact]
    public void Ohne_Modul_Finanzen_sind_beide_Seiten_nicht_verfuegbar()
    {
        Kontext(schalter: ["Verpflegung"]);
        _server.Bei(HttpMethod.Get, Pfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal("Diese Seite ist nicht verfügbar.", KalkulationRendern().Find("h1").TextContent);
        Assert.Equal("Diese Seite ist nicht verfügbar.", RechnerRendern().Find("h1").TextContent);
    }

    // ---------- Beitragsrechner ----------

    [Fact]
    public void Beitragsrechner_startet_auf_den_gespeicherten_Werten_und_zeigt_beide_Balken_gleich()
    {
        Antworten();

        var cut = RechnerRendern();

        Assert.Equal("Beitragsrechner", cut.Find("h1").TextContent);
        Assert.Equal("page", cut.FindAll("nav[aria-label=Finanzen] a")[1].GetAttribute("aria-current"));
        var regler = cut.FindAll("input[type=range]");
        Assert.Equal(["rechner-teilnehmende", "rechner-mitarbeitende", "rechner-tagessatz"], regler.Select(r => r.Id!));
        Assert.Equal(("0", "200", "1", "40"), (regler[0].GetAttribute("min"), regler[0].GetAttribute("max"), regler[0].GetAttribute("step"), regler[0].GetAttribute("value")));
        Assert.Equal(("100", "10"), (regler[1].GetAttribute("max"), regler[1].GetAttribute("value")));
        Assert.Equal(("30", "0.5", "7"), (regler[2].GetAttribute("max"), regler[2].GetAttribute("step"), regler[2].GetAttribute("value")));
        Assert.Equal("7,00 €", cut.Find("#rechner-tagessatz-wert").TextContent);
        Assert.Equal(("275,00 €", "275,00 €"), (Zahl(Balken(cut, "Laut Kalkulation")), Zahl(Balken(cut, "Mit Reglern"))));
        Assert.Equal(("100%", "100%"), (Breite(Balken(cut, "Laut Kalkulation")), Breite(Balken(cut, "Mit Reglern"))));
        Assert.Equal("Gleich", Balken(cut, "Mit Reglern").QuerySelector(".elkw-status")!.TextContent);
        Assert.Contains("Differenz: 0,00 €", cut.Find("#vergleich").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Regler_reichen_bis_zu_gespeicherten_Werten_ueber_den_Standardgrenzen()
    {
        Antworten(kalkulation: KalkulationJson(teilnehmende: 250, tagessatz: 35.25m));

        var cut = RechnerRendern();

        Assert.Equal(("250", "250"), (cut.Find("#rechner-teilnehmende").GetAttribute("max"), cut.Find("#rechner-teilnehmende").GetAttribute("value")));
        Assert.Equal(("35.25", "35.25"), (cut.Find("#rechner-tagessatz").GetAttribute("max"), cut.Find("#rechner-tagessatz").GetAttribute("value")));
        Assert.Equal("Gleich", Balken(cut, "Mit Reglern").QuerySelector(".elkw-status")!.TextContent);
    }

    [Fact]
    public void Regler_aendern_Zahl_Differenz_und_Balken_sofort_ohne_Serveraufruf_und_Zuruecksetzen_stellt_die_Kalkulation_her()
    {
        Antworten();
        var cut = RechnerRendern();
        var anfragen = _server.Anfragen.Count;

        // 50 statt 40 Teilnehmende: (60 × 12 × 17 € + 2.000 € − 1.200 €) ÷ 50 = 260,80 €.
        cut.Find("#rechner-teilnehmende").Input("50");

        Assert.Equal("260,80 €", Zahl(Balken(cut, "Mit Reglern")));
        Assert.Equal("Günstiger", Balken(cut, "Mit Reglern").QuerySelector(".elkw-status")!.TextContent);
        Assert.Contains("Differenz: −14,20 €", cut.Find("#vergleich").TextContent, StringComparison.Ordinal);
        Assert.Equal(("100%", "94.84%"), (Breite(Balken(cut, "Laut Kalkulation")), Breite(Balken(cut, "Mit Reglern"))));

        // Tagessatz 8 €: 50 TN + 10 MA, 12 Tage, +1 € → +720 € → 275,20 €; größerer Wert ist 100 %.
        cut.Find("#rechner-tagessatz").Input("8");
        Assert.Equal("275,20 €", Zahl(Balken(cut, "Mit Reglern")));
        Assert.Equal("Teurer", Balken(cut, "Mit Reglern").QuerySelector(".elkw-status")!.TextContent);
        Assert.Contains("Differenz: +0,20 €", cut.Find("#vergleich").TextContent, StringComparison.Ordinal);
        Assert.Equal(("99.93%", "100%"), (Breite(Balken(cut, "Laut Kalkulation")), Breite(Balken(cut, "Mit Reglern"))));
        Assert.Equal(anfragen, _server.Anfragen.Count);

        cut.FindAll("button").Single(b => b.TextContent == "Zurücksetzen").Click();

        Assert.Equal("275,00 €", Zahl(Balken(cut, "Mit Reglern")));
        Assert.Equal("40", cut.Find("#rechner-teilnehmende").GetAttribute("value"));
        Assert.Equal("7,00 €", cut.Find("#rechner-tagessatz-wert").TextContent);
        Assert.Equal(anfragen, _server.Anfragen.Count);
    }

    [Fact]
    public void Regler_auf_0_Teilnehmende_nennt_den_Satz_statt_eines_Beitrags()
    {
        Antworten();
        var cut = RechnerRendern();

        cut.Find("#rechner-teilnehmende").Input("0");

        Assert.Equal("–", Zahl(Balken(cut, "Mit Reglern")));
        Assert.Contains(FinanzenTexte.KeinBeitragText, cut.Find("#vergleich").TextContent, StringComparison.Ordinal);
        // Kein Vergleich gegen 0 €: kein „Günstiger“, keine Differenz.
        Assert.Equal("–", Balken(cut, "Mit Reglern").QuerySelector(".elkw-status")!.TextContent);
        Assert.DoesNotContain("Differenz", cut.Find("#vergleich").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Gespeichert_0_Teilnehmende_und_Regler_darueber_vergleicht_nicht_gegen_0_Euro()
    {
        Antworten(kalkulation: KalkulationJson(teilnehmende: 0));
        var cut = RechnerRendern();
        Assert.Equal(("–", "Gleich"), (Zahl(Balken(cut, "Laut Kalkulation")), Balken(cut, "Mit Reglern").QuerySelector(".elkw-status")!.TextContent));

        cut.Find("#rechner-teilnehmende").Input("40");

        var mitReglern = Balken(cut, "Mit Reglern");
        Assert.Equal(("275,00 €", "–"), (Zahl(mitReglern), mitReglern.QuerySelector(".elkw-status")!.TextContent));
        Assert.DoesNotContain("zl-balken--warnung", mitReglern.ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("Differenz", cut.Find("#vergleich").TextContent, StringComparison.Ordinal);
        Assert.Contains(FinanzenTexte.KeinBeitragText, cut.Find("#vergleich").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Tagessatz_neben_dem_Raster_verschiebt_das_Raster_damit_gespeicherter_Wert_und_Maximum_darauf_liegen()
    {
        Antworten(kalkulation: KalkulationJson(tagessatz: 7.25m));
        var cut = RechnerRendern();

        var regler = cut.Find("#rechner-tagessatz");
        Assert.Equal(("0.25", "7.25", "30.25", "0.5"), (regler.GetAttribute("min"), regler.GetAttribute("value"), regler.GetAttribute("max"), regler.GetAttribute("step")));
        var (min, wert, max) = (0.25m, 7.25m, 30.25m);
        Assert.Equal(0m, (wert - min) % 0.5m);
        Assert.Equal(0m, (max - min) % 0.5m);
        Assert.Equal("7,25 €", cut.Find("#rechner-tagessatz-wert").TextContent);
        Assert.Equal("Gleich", Balken(cut, "Mit Reglern").QuerySelector(".elkw-status")!.TextContent);

        regler.Input("7.75");
        Assert.Equal("7,75 €", cut.Find("#rechner-tagessatz-wert").TextContent);
    }

    // ---------- Kalkulation: Konflikt und Fehlerwege ----------

    [Fact]
    public void Konflikt_Neu_laden_zeigt_den_neuen_Wert_und_der_Hinweis_verschwindet()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, Pfad, () => Json(KalkulationJson()), () => Json(KalkulationJson(teilnehmende: 45)));
        _server.Bei(HttpMethod.Patch, Pfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var cut = KalkulationRendern();

        var feld = cut.Find("#kalkulation-teilnehmende");
        feld.Focus();
        feld.Input("50");
        feld.Blur();
        cut.WaitForAssertion(() => Assert.Contains("Inzwischen geändert", cut.Find("#planzahlen").TextContent, StringComparison.Ordinal));

        cut.FindAll("button").Single(b => b.TextContent == "Neu laden").Click();

        cut.WaitForAssertion(() => Assert.Equal("45", cut.Find("#kalkulation-teilnehmende").GetAttribute("value")));
        Assert.DoesNotContain("Inzwischen geändert", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Einnahme_schon_von_anderen_entfernt_404_laedt_neu_ohne_Fehlermeldung()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, Pfad, () => Json(KalkulationJson()), () => Json(KalkulationJson(mitEinnahmen: false)));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/einnahmen/{Zuschuss}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = KalkulationRendern();

        EinnahmeEntfernen(cut);

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Einnahmen.", cut.Find("#einnahmen").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll(".elkw-alert"), a => a.TextContent.Contains(AnmeldungAbschluss.FehlerText, StringComparison.Ordinal));
    }

    [Fact]
    public void Einnahme_entfernen_scheitert_und_der_Posten_ist_noch_da_zeigt_die_Fehlermeldung()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, Pfad, () => Json(KalkulationJson()));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/einnahmen/{Zuschuss}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = KalkulationRendern();

        EinnahmeEntfernen(cut);

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal));
        Assert.NotNull(cut.Find($"#einnahme-{Zuschuss}-betrag"));
    }

    [Fact]
    public void Einnahme_anlegen_und_entfernen_bei_abgeschlossener_Freizeit_nennen_den_Abschluss()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, Pfad, () => Json(KalkulationJson()));
        _server.Bei(HttpMethod.Post, $"{Pfad}/einnahmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/einnahmen/{Zuschuss}", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = KalkulationRendern();

        cut.Find("#einnahme-bezeichnung").Input("Spende");
        cut.Find("#einnahme-hinzufuegen form").Submit();
        cut.WaitForAssertion(() => Assert.Contains(FinanzenTexte.AbgeschlossenText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".zl-fehleruebersicht"));

        EinnahmeEntfernen(cut);
        cut.WaitForAssertion(() => Assert.Contains(FinanzenTexte.AbgeschlossenText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
    }

    private static void EinnahmeEntfernen(IRenderedComponent<KalkulationSeite> cut)
    {
        cut.Find("#einnahmen").QuerySelectorAll("button").Single(b => b.TextContent == "Entfernen").Click();
        cut.Find("#einnahmen").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();
    }

    [Fact]
    public void Beitragsrechner_nach_dem_Abschluss_nur_lesbar_die_Regler_bleiben_bedienbar()
    {
        Antworten("Gelaufen");
        var cut = RechnerRendern();

        Assert.Contains(FinanzenTexte.AbgeschlossenText, cut.Markup, StringComparison.Ordinal);
        Assert.All(cut.FindAll("input[type=range]"), r => Assert.False(r.HasAttribute("disabled")));
        cut.Find("#rechner-mitarbeitende").Input("0");
        Assert.NotEqual("275,00 €", Zahl(Balken(cut, "Mit Reglern")));
    }

    // ---------- Startseite ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Startseite_zeigt_den_Knopf_Finanzen_nur_mit_dem_Modul_Finanzen(bool finanzen)
    {
        Kontext(schalter: finanzen ? ["Verpflegung", "Finanzen"] : ["Verpflegung"]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        var knopf = cut.FindAll("a").Where(a => a.TextContent == "Finanzen").ToList();
        if (finanzen)
        {
            Assert.Equal($"freizeiten/{Freizeit}/finanzen/kalkulation", Assert.Single(knopf).GetAttribute("href"));
        }
        else
        {
            Assert.Empty(knopf);
        }
    }

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_die_Kalkulation_403_404_heissen_nicht_verfuegbar_und_Aktionen_ordnen_Antworten_zu()
    {
        Antworten();
        _server.Bei(HttpMethod.Delete, $"{Pfad}/einnahmen/{Zuschuss}", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/einnahmen/{Rabatt}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Post, $"{Pfad}/einnahmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var client = Services.GetRequiredService<FinanzenApiClient>();

        var kalkulation = await client.KalkulationLadenAsync(Freizeit, Abbruch);

        Assert.Equal((40, 6, Kostenkategorie.ProgrammUndMaterial, 275m), (kalkulation!.Teilnehmende, kalkulation.Kategorien.Count, kalkulation.Kategorien[3].Kategorie, kalkulation.Ergebnis.Teilnehmerbeitrag!.Value));
        Assert.Null(await client.KalkulationLadenAsync(Guid.CreateVersion7(), Abbruch));
        Assert.Equal(FinanzenAktionErgebnis.Abgeschlossen, await client.EinnahmeLoeschenAsync(Freizeit, Zuschuss, Abbruch));
        Assert.Equal(FinanzenAktionErgebnis.Fehler, await client.EinnahmeLoeschenAsync(Freizeit, Rabatt, Abbruch));
        Assert.Equal(FinanzenAktionErgebnis.Abgeschlossen, (await client.EinnahmeAnlegenAsync(Freizeit, new EinnahmepostenNeu("Spende", null), Abbruch)).Ergebnis);
        Assert.Equal($"{Pfad}/kategorien/Verpflegung", FinanzenApiClient.KategoriePfad(Freizeit, Kostenkategorie.Verpflegung));
    }

    [Fact]
    public void Texte_Euro_mit_Tausenderpunkt_und_Cent()
    {
        Assert.Equal(("1.234,50 €", "0,00 €", "275,00 €", "−25,00 €"), (Anzeige.Euro(1234.5m), Anzeige.Euro(0m), Anzeige.Euro(275m), Anzeige.Euro(-25m)));
        Assert.Equal("1.000.000,00 €", Anzeige.Euro(1_000_000m));
    }
}
