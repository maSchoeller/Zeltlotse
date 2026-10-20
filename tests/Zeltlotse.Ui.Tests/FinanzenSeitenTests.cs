using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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
using BelegeSeite = Zeltlotse.App.Features.Finanzen.Belege;
using BelegSeite = Zeltlotse.App.Features.Finanzen.BelegSeite;
using SollIstSeite = Zeltlotse.App.Features.Finanzen.SollIst;
using KalkulationSeite = Zeltlotse.App.Features.Finanzen.Kalkulation;
using BeitraegeSeite = Zeltlotse.App.Features.Finanzen.Beitraege;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 5-1 in der Web-App mit nachgebildetem Server: die Kalkulation (Reiter, Ergebnis mit Teilnehmerbeitrag als Zahl und Balken, Anteile der
/// Kategorien, Planzahlen und Beträge als Autosave-Felder mit Neuladen des Ergebnisses, Einnahme anlegen und löschen, 0 Teilnehmende, nur lesbar,
/// Ladefehler, nicht verfügbar), der Beitragsrechner (Regler ändern den Beitrag sofort ohne Serveraufruf, Zurücksetzen, 0 Teilnehmende, nach dem
/// Abschluss bedienbar), der Knopf „Finanzen“ auf der Startseite nur mit dem Modul Finanzen, der Client und der Euro-Text. Story 5-2: die Belege (Liste
/// mit Namen, Datei und Haken „Erstattet“, Anlegen mit und ohne Foto, Upload-Fehler mit „Erneut versuchen“, Feldfehler), die Seite eines Belegs (Autosave,
/// Bild, Löschen), Soll-Ist mit den drei Statuswörtern und den Erstattungen, nach dem Abschluss nur „Erstattet“ bedienbar, nicht verfügbar und Ladefehler.
/// Story 5-3: die Beiträge (Namen, Balken „x von y bezahlt“, Hinweis „Abgehakt von …“, ein Tipp setzt und entfernt den Haken, Fehler am Feld, Leer-Zustand,
/// nach dem Abschluss deaktiviert, nicht verfügbar und Ladefehler).
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
        Assert.Equal(["Kalkulation", "Beitragsrechner", "Belege", "Soll-Ist", "Beiträge"], reiter.Select(a => a.TextContent));
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

    // ---------- Story 5-2: Belege, Soll-Ist und Erstattung ----------

    private static readonly Guid Brot = Guid.Parse("0199f700-0000-7000-8000-0000000008a1");
    private static readonly Guid Bus = Guid.Parse("0199f700-0000-7000-8000-0000000008a2");
    private static readonly Guid Neu = Guid.Parse("0199f700-0000-7000-8000-0000000008a3");
    private static readonly Guid Lea = Guid.Parse("0199f700-0000-7000-8000-0000000008b1");
    private static readonly Guid Jonas = Guid.Parse("0199f700-0000-7000-8000-0000000008b2");
    private static readonly Guid Unbekannt = Guid.Parse("0199f700-0000-7000-8000-0000000008b3");

    private static string BelegePfad => $"/api/freizeiten/{Freizeit}/finanzen/belege";

    private static object BelegJson(Guid id, string kategorie, decimal betrag, string? bezeichnung, Guid bezahltVon, bool erstattet = false, object? datei = null, uint version = 5) =>
        new { id, version, kategorie, betrag, bezeichnung, bezahltVon, erstattet, erfasstAm = "2026-10-19T08:00:00+00:00", datei };

    private static object[] Belegliste() =>
    [
        BelegJson(Brot, "Verpflegung", 23.40m, "Brötchen", Lea, datei: new { dateiname = "Kassenbon.png", istBild = true }),
        BelegJson(Bus, "Fahrt", 1950m, null, Jonas, erstattet: true),
        BelegJson(Neu, "Sonstiges", 5m, "Gaffa-Tape", BelegFelder.GeloeschtePerson),
    ];

    /// <summary>Kontext, Freizeit (Leitung Jonas) und Personenliste (Lea bestätigt, Tobias nur eingegangen) und die Belege.</summary>
    private void BelegeAntworten(string phase = "InVorbereitung", Func<HttpResponseMessage>? belege = null)
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase, freigabeBis = "2027-09-12",
            leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner", personId = Jonas } },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(new object[]
        {
            new { anmeldungId = Guid.CreateVersion7(), personId = Lea, vorname = "Lea", nachname = "Feuerstein", art = "Mitarbeitende", status = "Bestaetigt" },
            new { anmeldungId = Guid.CreateVersion7(), personId = Unbekannt, vorname = "Tobias", nachname = "Wanderer", art = "Mitarbeitende", status = "Eingegangen" },
        }));
        _server.Bei(HttpMethod.Get, BelegePfad, belege ?? (() => Json(Belegliste())));
    }

    private IRenderedComponent<BelegeSeite> BelegeRendern()
    {
        var cut = Render<BelegeSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<BelegSeite> BelegRendern(Guid beleg)
    {
        var cut = Render<BelegSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.BelegId, beleg));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<SollIstSeite> SollIstRendern()
    {
        var cut = Render<SollIstSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static List<string> Zeile(AngleSharp.Dom.IElement zeile) => [.. zeile.QuerySelectorAll("td").Select(t => t.TextContent.Trim())];

    private static string Normal(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Belege_Liste_mit_Namen_Datei_Haken_Erstattet_und_Summe()
    {
        BelegeAntworten();

        var cut = BelegeRendern();

        Assert.Equal(("Finanzen", "Belege"), (cut.Find(".elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var reiter = cut.FindAll("nav[aria-label=Finanzen] a");
        Assert.Equal(($"freizeiten/{Freizeit}/finanzen/belege", "page"), (reiter[2].GetAttribute("href"), reiter[2].GetAttribute("aria-current")));
        var zeilen = cut.FindAll("#belege tbody tr");
        Assert.Equal(3, zeilen.Count);
        Assert.Equal(["Brötchen", "Verpflegung", "23,40 €", "Lea Feuerstein", "Foto"], Zeile(zeilen[0]).Take(5));
        Assert.Equal(["Fahrt", "Fahrt", "1.950,00 €", "Jonas Hüttner", "–"], Zeile(zeilen[1]).Take(5));
        Assert.Equal(["Gaffa-Tape", "Sonstiges", "5,00 €", "Gelöschte Person"], Zeile(zeilen[2]).Take(4));
        Assert.Equal($"freizeiten/{Freizeit}/finanzen/belege/{Brot}", zeilen[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal($"/api/freizeiten/{Freizeit}/finanzen/belege/{Brot}/datei", zeilen[0].QuerySelectorAll("a")[1].GetAttribute("href"));
        Assert.False(cut.Find($"#erstattet-{Brot}").HasAttribute("checked"));
        Assert.True(cut.Find($"#erstattet-{Bus}").HasAttribute("checked"));
        Assert.Contains("Summe: 1.978,40 €", cut.Find("#belege").TextContent, StringComparison.Ordinal);
        // „Bezahlt von“: „Ich“ vorgewählt, dazu Leitung und bestätigtes Team, nicht die nur eingegangene Anmeldung.
        Assert.Equal(["Ich", "Jonas Hüttner", "Lea Feuerstein"], cut.FindAll("#beleg-bezahlt-von label").Select(l => l.TextContent.Trim()));
        Assert.True(cut.Find("#beleg-bezahlt-von input[value='']").HasAttribute("checked"));
        Assert.Equal(
            ["Unterkunft", "Verpflegung", "Fahrt", "Programm und Material", "Mitarbeitende", "Sonstiges"],
            cut.FindAll("#beleg-kategorie label").Select(l => l.TextContent.Trim()));
    }

    [Fact]
    public void Ohne_Belege_steht_der_Leer_Zustand_und_ein_unbekannter_Zahler_heisst_Ohne_Namen()
    {
        BelegeAntworten(belege: () => Json(Array.Empty<object>()));
        var leer = BelegeRendern();
        Assert.Contains("Noch keine Belege.", leer.Find("#belege").TextContent, StringComparison.Ordinal);

        Assert.Equal(
            ("Ohne Namen", "Gelöschte Person", "Lea Feuerstein"),
            (FinanzenTexte.Zahler(Unbekannt, new Dictionary<Guid, string>()), FinanzenTexte.Zahler(BelegFelder.GeloeschtePerson, new Dictionary<Guid, string>()),
                FinanzenTexte.Zahler(Lea, new Dictionary<Guid, string> { [Lea] = "Lea Feuerstein" })));
    }

    [Fact]
    public void Erstattet_ankreuzen_speichert_sofort_ueber_den_eigenen_Pfad()
    {
        BelegeAntworten();
        _server.Bei(HttpMethod.Patch, $"{BelegePfad}/{Brot}/erstattet", () => Json(new { version = 6u, erstattet = "true" }));
        var cut = BelegeRendern();

        cut.Find($"#erstattet-{Brot}").Change(true);

        cut.WaitForAssertion(() => Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Patch));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal((5u, "false", "true"), (
            koerper.RootElement.GetProperty("version").GetUInt32(), koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("erstattet").GetString()));
    }

    [Fact]
    public void Beleg_anlegen_ohne_Foto_mit_Vorgabe_Ich_danach_steht_er_in_der_Liste_und_das_Formular_ist_leer()
    {
        BelegeAntworten();
        _server.BeiDerReihe(HttpMethod.Get, BelegePfad, () => Json(Belegliste().Skip(1).ToArray()), () => Json(Belegliste()));
        _server.Bei(HttpMethod.Post, BelegePfad, () => Json(Belegliste()[0], HttpStatusCode.Created));
        var cut = BelegeRendern();

        cut.Find("#beleg-betrag").Input("23,40");
        cut.Find("#beleg-kategorie input[value=Verpflegung]").Change(true);
        cut.Find("#beleg-bezeichnung").Input("Brötchen");
        cut.Find("#beleg-erfassen form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("#belege tbody tr").Count));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Post).Koerper!);
        Assert.Equal(("Verpflegung", "23,40", "Brötchen", JsonValueKind.Null), (koerper.RootElement.GetProperty("kategorie").GetString(),
            koerper.RootElement.GetProperty("betrag").GetString(), koerper.RootElement.GetProperty("bezeichnung").GetString(), koerper.RootElement.GetProperty("bezahltVon").ValueKind));
        Assert.Contains("Brötchen: 23,40 €.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal);
        Assert.Null(cut.Find("#beleg-betrag").GetAttribute("value"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.EndsWith("/datei", StringComparison.Ordinal));
    }

    [Fact]
    public void Beleg_anlegen_mit_Foto_fuer_Lea_laedt_die_Datei_danach_an_den_neuen_Beleg()
    {
        BelegeAntworten();
        _server.Bei(HttpMethod.Post, BelegePfad, () => Json(BelegJson(Brot, "Verpflegung", 23.40m, null, Lea), HttpStatusCode.Created));
        _server.Bei(HttpMethod.Post, $"{BelegePfad}/{Brot}/datei", () => Json(Belegliste()[0]));
        var cut = BelegeRendern();

        cut.Find("#beleg-betrag").Input("23,40");
        cut.Find("#beleg-kategorie input[value=Verpflegung]").Change(true);
        cut.Find($"#beleg-bezahlt-von input[value='{Lea}']").Change(true);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0x89, 0x50, 0x4E, 0x47], "Kassenbon.png", contentType: "image/png"));
        cut.Find("#beleg-erfassen form").Submit();

        cut.WaitForAssertion(() => Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{BelegePfad}/{Brot}/datei"));
        using (var koerper = JsonDocument.Parse(_server.Anfragen.First(a => a.Methode == HttpMethod.Post && a.Pfad == BelegePfad).Koerper!))
        {
            Assert.Equal(Lea.ToString(), koerper.RootElement.GetProperty("bezahltVon").GetString());
        }

        var reihenfolge = _server.Anfragen.Where(a => a.Methode == HttpMethod.Post).Select(a => a.Pfad).ToList();
        Assert.Equal([BelegePfad, $"{BelegePfad}/{Brot}/datei"], reihenfolge);
        Assert.Contains("name=datei", _server.Anfragen.Last(a => a.Methode == HttpMethod.Post).Koerper!, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".elkw-alert--fehler")));
    }

    [Fact]
    public void Upload_scheitert_der_Beleg_bleibt_und_Erneut_versuchen_laedt_an_denselben_Beleg()
    {
        BelegeAntworten();
        _server.Bei(HttpMethod.Post, BelegePfad, () => Json(BelegJson(Brot, "Verpflegung", 23.40m, null, Lea), HttpStatusCode.Created));
        _server.BeiDerReihe(HttpMethod.Post, $"{BelegePfad}/{Brot}/datei", () => throw new HttpRequestException("offline"), () => Json(Belegliste()[0]));
        var cut = BelegeRendern();

        cut.Find("#beleg-betrag").Input("23,40");
        cut.Find("#beleg-kategorie input[value=Verpflegung]").Change(true);
        var feld = cut.FindComponent<InputFile>();
        var feldInstanz = feld.Instance;
        feld.UploadFiles(InputFileContent.CreateFromBinary([0x89, 0x50, 0x4E, 0x47], "Kassenbon.png", contentType: "image/png"));
        cut.Find("#beleg-erfassen form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Fehler: Datei nicht hochgeladen. Der Beleg ist gespeichert.", Normal(cut.Find(".elkw-alert--fehler").TextContent), StringComparison.Ordinal));
        // Solange „Erneut versuchen“ angeboten wird, bleibt das Datei-Feld dasselbe (die Datei wird daraus erneut gelesen).
        Assert.Same(feldInstanz, cut.FindComponent<InputFile>().Instance);
        cut.FindAll("button").Single(b => b.TextContent == "Erneut versuchen").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".elkw-alert--fehler")));
        Assert.NotSame(feldInstanz, cut.FindComponent<InputFile>().Instance);
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == BelegePfad);
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Post && a.Pfad == $"{BelegePfad}/{Brot}/datei"));
    }

    [Fact]
    public void Feldfehler_stehen_in_der_Uebersicht_und_am_Feld_ein_Fehler_der_Datei_am_Datei_Feld()
    {
        BelegeAntworten();
        _server.BeiDerReihe(
            HttpMethod.Post, BelegePfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"errors":{"betrag":["Betrag fehlt."],"kategorie":["Kategorie fehlt."]}}""", "application/problem+json"),
            () => Json(BelegJson(Brot, "Fahrt", 10m, null, Lea), HttpStatusCode.Created));
        _server.Bei(HttpMethod.Post, $"{BelegePfad}/{Brot}/datei", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"errors":{"datei":["Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF."]}}""", "application/problem+json"));
        var cut = BelegeRendern();

        cut.Find("#beleg-erfassen form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Betrag: Betrag fehlt.", "Kategorie: Kategorie fehlt."], cut.FindAll(".zl-fehleruebersicht li a").Select(a => a.TextContent)));
        Assert.Equal(["#beleg-betrag", "#beleg-kategorie"], cut.FindAll(".zl-fehleruebersicht li a").Select(a => a.GetAttribute("href")));
        Assert.Equal("true", cut.Find("#beleg-betrag").GetAttribute("aria-invalid"));

        cut.Find("#beleg-betrag").Input("10");
        cut.Find("#beleg-kategorie input[value=Fahrt]").Change(true);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "text.png", contentType: "image/png"));
        cut.Find("#beleg-erfassen form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(
            "Fehler: Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF. Der Beleg ist ohne Datei gespeichert.", cut.Find("#beleg-erfassen .elkw-field-error").TextContent));
        Assert.Empty(cut.FindAll(".zl-fehleruebersicht"));
    }

    [Fact]
    public void Gelaufen_ohne_Erfassen_der_Haken_Erstattet_bleibt_bedienbar()
    {
        BelegeAntworten("Gelaufen");

        var cut = BelegeRendern();

        Assert.Contains(FinanzenTexte.NurErstattetText, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#beleg-erfassen"));
        Assert.Empty(cut.FindAll("form"));
        Assert.Equal(3, cut.FindAll("#belege input[type=checkbox]").Count);
        Assert.All(cut.FindAll("#belege input[type=checkbox]"), k => Assert.False(k.HasAttribute("disabled")));

        var beleg = BelegRendern(Brot);
        Assert.Empty(beleg.FindAll("input[type=text], input[type=radio], input[type=file]"));
        Assert.Contains("23,40 €", beleg.Find("#beleg dl").TextContent, StringComparison.Ordinal);
        Assert.NotNull(beleg.Find("#beleg-erstattet"));
        Assert.DoesNotContain(beleg.FindAll("button"), b => b.TextContent is "Beleg löschen" or "Datei entfernen");
        Assert.NotNull(beleg.Find("#datei img"));
    }

    [Fact]
    public void Beleg_Seite_zeigt_Autosave_Felder_das_Foto_und_loescht_per_Inline_Bestaetigung()
    {
        BelegeAntworten();
        _server.Bei(HttpMethod.Patch, $"{BelegePfad}/{Brot}", () => Json(new { version = 6u, betrag = "24,5" }));
        _server.Bei(HttpMethod.Delete, $"{BelegePfad}/{Brot}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = BelegRendern(Brot);

        Assert.Equal("Brötchen", cut.Find("h1").TextContent);
        Assert.Contains("Bezahlt von Lea Feuerstein", Normal(cut.Markup), StringComparison.Ordinal);
        Assert.Equal("23,4", cut.Find("#beleg-betrag").GetAttribute("value"));
        Assert.Equal("Brötchen", cut.Find("#beleg-bezeichnung").GetAttribute("value"));
        Assert.True(cut.Find("#beleg-kategorie input[value=Verpflegung]").HasAttribute("checked"));
        Assert.True(cut.Find($"#beleg-bezahlt-von input[value='{Lea}']").HasAttribute("checked"));
        Assert.StartsWith($"/api/freizeiten/{Freizeit}/finanzen/belege/{Brot}/datei", cut.Find("#datei img").GetAttribute("src"), StringComparison.Ordinal);
        Assert.Equal("Foto des Belegs „Brötchen“", cut.Find("#datei img").GetAttribute("alt"));
        Assert.Contains("Datei ersetzen", cut.Find("#datei").TextContent, StringComparison.Ordinal);

        var feld = cut.Find("#beleg-betrag");
        feld.Focus();
        feld.Input("24,50");
        feld.Blur();
        cut.WaitForAssertion(() => Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Patch));
        using (var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!))
        {
            Assert.Equal(("23,4", "24,50"), (koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("betrag").GetString()));
        }

        cut.FindAll("button").Single(b => b.TextContent == "Beleg löschen").Click();
        Assert.Contains("„Brötchen“ löschen? Die Datei wird mitgelöscht.", cut.Markup, StringComparison.Ordinal);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/finanzen/belege", Navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void Beleg_Seite_ohne_Datei_laedt_ein_Foto_hoch_und_ein_unbekannter_Beleg_ist_nicht_verfuegbar()
    {
        BelegeAntworten();
        _server.BeiDerReihe(HttpMethod.Get, BelegePfad, () => Json(Belegliste()), () => Json(new[] { BelegJson(Bus, "Fahrt", 1950m, null, Jonas, true, new { dateiname = "Busrechnung.pdf", istBild = false }, 6) }));
        _server.Bei(HttpMethod.Post, $"{BelegePfad}/{Bus}/datei", () => Json(BelegJson(Bus, "Fahrt", 1950m, null, Jonas, true, new { dateiname = "Busrechnung.pdf", istBild = false }, 6)));
        var cut = BelegRendern(Bus);

        Assert.Equal("Fahrt", cut.Find("h1").TextContent);
        Assert.Contains("Noch keine Datei.", cut.Find("#datei").TextContent, StringComparison.Ordinal);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "Busrechnung.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains("PDF herunterladen (Busrechnung.pdf)", cut.Find("#datei").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#datei img"));

        var fehlt = BelegRendern(Guid.CreateVersion7());
        Assert.Contains("Diese Seite ist nicht verfügbar.", fehlt.Markup, StringComparison.Ordinal);
    }

    private void SollIstAntworten(object? sollIst = null)
    {
        BelegeAntworten();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/finanzen/soll-ist", () => Json(sollIst ?? new
        {
            zeilen = new object[]
            {
                new { kategorie = "Unterkunft", soll = 6300m, ist = 1500m, status = "ImPlan" },
                new { kategorie = "Verpflegung", soll = 4200m, ist = 3900m, status = "Knapp" },
                new { kategorie = "Fahrt", soll = 1800m, ist = 1950m, status = "Ueberschritten" },
                new { kategorie = "ProgrammUndMaterial", soll = 1200m, ist = 0m, status = "ImPlan" },
                new { kategorie = "Mitarbeitende", soll = 500m, ist = 0m, status = "ImPlan" },
                new { kategorie = "Sonstiges", soll = 0m, ist = 0m, status = "ImPlan" },
            },
            gesamt = new { kategorie = (string?)null, soll = 14000m, ist = 7350m, status = "ImPlan" },
            erstattungen = new object[]
            {
                new { personId = Lea, offen = 45.80m, erstattet = 0m },
                new { personId = Jonas, offen = 0m, erstattet = 5804.20m },
            },
        }));
    }

    [Fact]
    public void Soll_Ist_zeigt_Balken_mit_Ist_von_Soll_den_drei_Statuswoertern_und_die_Erstattungen()
    {
        SollIstAntworten();

        var cut = SollIstRendern();

        Assert.Equal("Soll-Ist", cut.Find("h1").TextContent);
        var gesamt = Balken(cut, "Gesamt");
        Assert.Equal(("7.350,00 € von 14.000,00 € (53 %)", "52.5%", "Im Plan"), (Zahl(gesamt), Breite(gesamt), gesamt.QuerySelector(".elkw-status")!.TextContent));
        var verpflegung = Balken(cut, "Verpflegung");
        Assert.Equal(("3.900,00 € von 4.200,00 € (93 %)", "Knapp"), (Zahl(verpflegung), verpflegung.QuerySelector(".elkw-status")!.TextContent));
        Assert.Contains("zl-balken--warnung", verpflegung.ClassName, StringComparison.Ordinal);
        var fahrt = Balken(cut, "Fahrt");
        Assert.Equal(("1.950,00 € von 1.800,00 € (108 %)", "100%", "Überschritten"), (Zahl(fahrt), Breite(fahrt), fahrt.QuerySelector(".elkw-status")!.TextContent));
        Assert.Contains("zl-balken--fehler", fahrt.ClassName, StringComparison.Ordinal);
        Assert.Equal(("0,00 € von 0,00 €", "0%", "Im Plan"), (Zahl(Balken(cut, "Sonstiges")), Breite(Balken(cut, "Sonstiges")), Balken(cut, "Sonstiges").QuerySelector(".elkw-status")!.TextContent));
        Assert.Equal(
            ["Unterkunft", "Verpflegung", "Fahrt", "Programm und Material", "Mitarbeitende", "Sonstiges"],
            cut.FindAll("#soll-ist .zl-balken strong").Skip(1).Select(s => s.TextContent));

        var lea = Balken(cut, "Lea Feuerstein");
        Assert.Equal(("noch 45,80 €", "0%", "Bekommt noch Geld"), (Zahl(lea), Breite(lea), lea.QuerySelector(".elkw-status")!.TextContent));
        Assert.Contains("zl-balken--warnung", lea.ClassName, StringComparison.Ordinal);
        var jonas = Balken(cut, "Jonas Hüttner");
        Assert.Equal(("noch 0,00 €", "100%", "Alles erstattet"), (Zahl(jonas), Breite(jonas), jonas.QuerySelector(".elkw-status")!.TextContent));
        Assert.Equal(["Lea Feuerstein", "Jonas Hüttner"], cut.FindAll("#erstattungen .zl-balken strong").Select(s => s.TextContent));
    }

    [Fact]
    public void Soll_Ist_ohne_Belege_zeigt_den_Leer_Zustand_bei_den_Erstattungen()
    {
        SollIstAntworten(new
        {
            zeilen = Kostenkategorien.Alle.Select(k => new { kategorie = k.ToString(), soll = 0m, ist = 0m, status = "ImPlan" }).ToArray(),
            gesamt = new { kategorie = (string?)null, soll = 0m, ist = 0m, status = "ImPlan" },
            erstattungen = Array.Empty<object>(),
        });

        var cut = SollIstRendern();

        Assert.Contains("Noch keine Belege.", cut.Find("#erstattungen").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#erstattungen .zl-balken"));
    }

    [Fact]
    public void Ohne_Modul_Finanzen_sind_Belege_Beleg_und_Soll_Ist_nicht_verfuegbar_und_ein_Ladefehler_zeigt_die_Meldung()
    {
        BelegeAntworten(belege: () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/finanzen/soll-ist", () => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Contains("Diese Seite ist nicht verfügbar.", BelegeRendern().Markup, StringComparison.Ordinal);
        Assert.Contains("Diese Seite ist nicht verfügbar.", BelegRendern(Brot).Markup, StringComparison.Ordinal);
        Assert.Contains("Diese Seite ist nicht verfügbar.", SollIstRendern().Markup, StringComparison.Ordinal);

        _server.Bei(HttpMethod.Get, BelegePfad, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/finanzen/soll-ist", () => throw new HttpRequestException("offline"));
        var belege = BelegeRendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, belege.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(belege.FindAll("#belege"));
        Assert.Contains(AnmeldungAbschluss.FehlerText, BelegRendern(Brot).Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        var sollIst = SollIstRendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, sollIst.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(sollIst.FindAll("#soll-ist"));
    }

    [Fact]
    public async Task Client_Belege_und_Soll_Ist_403_404_heissen_nicht_verfuegbar_und_der_Upload_ordnet_Antworten_zu()
    {
        SollIstAntworten();
        _server.Bei(HttpMethod.Post, $"{BelegePfad}/{Brot}/datei", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        _server.Bei(HttpMethod.Post, $"{BelegePfad}/{Bus}/datei", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Delete, $"{BelegePfad}/{Brot}/datei", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Services.GetRequiredService<FinanzenApiClient>();

        Assert.Equal(3, (await client.BelegeLadenAsync(Freizeit, Abbruch))!.Count);
        Assert.Null(await client.BelegeLadenAsync(Guid.CreateVersion7(), Abbruch));
        Assert.Equal(SollIstStatus.Knapp, (await client.SollIstLadenAsync(Freizeit, Abbruch))!.Zeilen[1].Status);
        Assert.True((await client.DateiHochladenAsync(Freizeit, Brot, new MemoryStream([1]), "x.png", "image/png", Abbruch)).Abgeschlossen);
        Assert.True((await client.DateiHochladenAsync(Freizeit, Bus, new MemoryStream([1]), "x.png", null, Abbruch)).NichtVerfuegbar);
        Assert.Equal(FinanzenAktionErgebnis.Erfolg, await client.DateiEntfernenAsync(Freizeit, Brot, Abbruch));
        Assert.Equal(
            ($"{BelegePfad}/{Brot}", $"{BelegePfad}/{Brot}/erstattet", $"{BelegePfad}/{Brot}/datei", $"/api/freizeiten/{Freizeit}/finanzen/soll-ist"),
            (FinanzenApiClient.BelegPfad(Freizeit, Brot), FinanzenApiClient.ErstattetPfad(Freizeit, Brot), FinanzenApiClient.DateiPfad(Freizeit, Brot), FinanzenApiClient.SollIstPfad(Freizeit)));
    }

    [Theory]
    [InlineData("Gelaufen")]
    [InlineData("InVorbereitung")]
    public void Beleg_Seite_Erstattet_ankreuzen_speichert_ueber_den_eigenen_Pfad(string phase)
    {
        BelegeAntworten(phase);
        _server.Bei(HttpMethod.Patch, $"{BelegePfad}/{Brot}/erstattet", () => Json(new { version = 6u, erstattet = "true" }));
        var cut = BelegRendern(Brot);

        cut.Find("#beleg-erstattet").Change(true);

        cut.WaitForAssertion(() => Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Patch));
        var anfrage = _server.Anfragen.Single(a => a.Methode == HttpMethod.Patch);
        Assert.Equal($"{BelegePfad}/{Brot}/erstattet", anfrage.Pfad);
        using var koerper = JsonDocument.Parse(anfrage.Koerper!);
        Assert.Equal(("false", "true"), (koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("erstattet").GetString()));
    }

    [Fact]
    public void Beleg_Seite_Datei_entfernen_schickt_DELETE_danach_steht_Noch_keine_Datei()
    {
        BelegeAntworten();
        _server.BeiDerReihe(HttpMethod.Get, BelegePfad, () => Json(Belegliste()), () => Json(new[] { BelegJson(Brot, "Verpflegung", 23.40m, "Brötchen", Lea, version: 6) }));
        _server.Bei(HttpMethod.Delete, $"{BelegePfad}/{Brot}/datei", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = BelegRendern(Brot);
        Assert.NotNull(cut.Find("#datei img"));

        cut.FindAll("button").Single(b => b.TextContent == "Datei entfernen").Click();
        Assert.Contains("Die Datei entfernen? Der Beleg bleibt.", cut.Markup, StringComparison.Ordinal);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Datei.", cut.Find("#datei").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#datei img"));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == $"{BelegePfad}/{Brot}/datei");
    }

    [Fact]
    public void Beleg_Seite_Upload_scheitert_Erneut_versuchen_laedt_an_denselben_Beleg()
    {
        BelegeAntworten();
        _server.BeiDerReihe(
            HttpMethod.Post, $"{BelegePfad}/{Bus}/datei",
            () => throw new HttpRequestException("offline"),
            () => Json(BelegJson(Bus, "Fahrt", 1950m, null, Jonas, true, new { dateiname = "Busrechnung.pdf", istBild = false }, 6)));
        var cut = BelegRendern(Bus);

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "Busrechnung.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains("Datei nicht hochgeladen.", cut.Find("#datei .elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        cut.FindAll("button").Single(b => b.TextContent == "Erneut versuchen").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#datei .elkw-alert--fehler")));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Post && a.Pfad == $"{BelegePfad}/{Bus}/datei"));
    }

    [Fact]
    public void Scheitert_die_Personenliste_stehen_die_Namen_der_Leitung_und_sonst_Ohne_Namen()
    {
        BelegeAntworten();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => throw new HttpRequestException("offline"));

        var cut = BelegeRendern();

        var zeilen = cut.FindAll("#belege tbody tr");
        Assert.Equal(3, zeilen.Count);
        Assert.Equal("Ohne Namen", Zeile(zeilen[0])[3]);
        Assert.Equal("Jonas Hüttner", Zeile(zeilen[1])[3]);
        Assert.Empty(cut.FindAll(".elkw-alert--fehler"));
    }
    // ---------- Story 5-3: Beitrag bezahlt abhaken ----------

    private static readonly Guid Mia = Guid.Parse("0199f700-0000-7000-8000-0000000009a1");
    private static readonly Guid Paul = Guid.Parse("0199f700-0000-7000-8000-0000000009a2");
    private static readonly Guid Emma = Guid.Parse("0199f700-0000-7000-8000-0000000009a3");

    private static string BeitraegePfad => $"/api/freizeiten/{Freizeit}/finanzen/beitraege";

    private static (object Json, bool Bezahlt) BeitragJson(Guid personId, Guid? abgehaktVon = null) =>
        (new { personId, bezahlt = abgehaktVon is not null, abgehaktVon, abgehaktAm = abgehaktVon is null ? null : "2026-10-20T08:00:00+00:00" }, abgehaktVon is not null);

    private static object BeitraegeJson(params (object Json, bool Bezahlt)[] zeilen) =>
        new { zeilen = zeilen.Select(z => z.Json).ToArray(), bezahlt = zeilen.Count(z => z.Bezahlt), gesamt = zeilen.Length };

    private static object KontextJson(string phase) =>
        new { freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = new[] { "Finanzen" }, erlaubt = Array.Empty<string>() };

    /// <summary>
    /// Kontext, Freizeit (Leitung Jonas), Personenliste (Lea Team, Mia und Paul bestätigte Teilnehmende, Emma nur eingegangen) und die Beiträge: Mia von Lea
    /// abgehakt, Paul offen.
    /// </summary>
    private void BeitraegeAntworten(string phase = "InVorbereitung", Func<HttpResponseMessage>? beitraege = null)
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase, freigabeBis = "2027-09-12",
            leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner", personId = Jonas } },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(new object[]
        {
            new { anmeldungId = Guid.CreateVersion7(), personId = Mia, vorname = "Mia", nachname = "Probst", art = "Teilnehmende", status = "Bestaetigt" },
            new { anmeldungId = Guid.CreateVersion7(), personId = Paul, vorname = "Paul", nachname = "Beispiel", art = "Teilnehmende", status = "Bestaetigt" },
            new { anmeldungId = Guid.CreateVersion7(), personId = Emma, vorname = "Emma", nachname = "Muster", art = "Teilnehmende", status = "Eingegangen" },
            new { anmeldungId = Guid.CreateVersion7(), personId = Lea, vorname = "Lea", nachname = "Feuerstein", art = "Mitarbeitende", status = "Bestaetigt" },
        }));
        _server.Bei(HttpMethod.Get, BeitraegePfad, beitraege ?? (() => Json(BeitraegeJson(BeitragJson(Mia, Lea), BeitragJson(Paul)))));
    }

    private IRenderedComponent<BeitraegeSeite> BeitraegeRendern()
    {
        var cut = Render<BeitraegeSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Haken<T>(IRenderedComponent<T> cut, Guid person)
        where T : IComponent =>
        cut.Find($"#beitrag-{person}");

    [Fact]
    public void Beitraege_zeigen_Reiter_Balken_x_von_y_bezahlt_Namen_und_wer_abgehakt_hat()
    {
        BeitraegeAntworten();

        var cut = BeitraegeRendern();

        Assert.Equal(("Finanzen", "Beiträge"), (cut.Find(".elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var reiter = cut.FindAll("nav[aria-label=Finanzen] a");
        Assert.Equal(5, reiter.Count);
        Assert.Equal(("Beiträge", $"freizeiten/{Freizeit}/finanzen/beitraege", "page"), (reiter[4].TextContent, reiter[4].GetAttribute("href"), reiter[4].GetAttribute("aria-current")));
        var balken = cut.Find("#beitraege .zl-balken");
        Assert.Equal(("1 von 2 bezahlt", "50%", "Offen"), (Zahl(balken), Breite(balken), balken.QuerySelector(".elkw-status")!.TextContent));
        Assert.Contains("zl-balken--warnung", balken.ClassName, StringComparison.Ordinal);
        // Sortiert nach Namen; nur bestätigte Teilnehmende (Emma fehlt).
        Assert.Equal(["Mia Probst", "Paul Beispiel"], cut.FindAll("#beitraege label.elkw-choice").Select(l => l.TextContent.Trim()));
        Assert.True(Haken(cut, Mia).HasAttribute("checked"));
        Assert.False(Haken(cut, Paul).HasAttribute("checked"));
        Assert.Equal("Abgehakt von Lea Feuerstein am 20.10.2026", cut.Find($"#beitrag-{Mia}-hinweis").TextContent);
        Assert.Empty(cut.FindAll($"#beitrag-{Paul}-hinweis"));
        Assert.All(cut.FindAll("#beitraege input[type=checkbox]"), k => Assert.False(k.HasAttribute("disabled")));
    }

    [Fact]
    public void Ein_Tipp_setzt_den_Haken_sofort_und_laedt_neu_ein_zweiter_Tipp_macht_ihn_wieder_offen()
    {
        BeitraegeAntworten();
        _server.BeiDerReihe(
            HttpMethod.Get, BeitraegePfad,
            () => Json(BeitraegeJson(BeitragJson(Mia, Lea), BeitragJson(Paul))),
            () => Json(BeitraegeJson(BeitragJson(Mia, Lea), BeitragJson(Paul, Jonas))),
            () => Json(BeitraegeJson(BeitragJson(Mia), BeitragJson(Paul, Jonas))));
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Paul}/bezahlt", () => Json(BeitragJson(Paul, Jonas).Json));
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Mia}/offen", () => Json(BeitragJson(Mia).Json));
        var cut = BeitraegeRendern();

        Haken(cut, Paul).Change(true);

        cut.WaitForAssertion(() => Assert.Equal("Alle bezahlt", cut.Find("#beitraege .zl-balken .elkw-status").TextContent));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{BeitraegePfad}/{Paul}/bezahlt");
        Assert.Equal("2 von 2 bezahlt", Zahl(cut.Find("#beitraege .zl-balken")));
        Assert.DoesNotContain("zl-balken--warnung", cut.Find("#beitraege .zl-balken").ClassName, StringComparison.Ordinal);
        Assert.True(Haken(cut, Paul).HasAttribute("checked"));
        Assert.Equal("Abgehakt von Jonas Hüttner am 20.10.2026", cut.Find($"#beitrag-{Paul}-hinweis").TextContent);

        Haken(cut, Mia).Change(false);

        cut.WaitForAssertion(() => Assert.False(Haken(cut, Mia).HasAttribute("checked")));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{BeitraegePfad}/{Mia}/offen");
        Assert.Equal("1 von 2 bezahlt", Zahl(cut.Find("#beitraege .zl-balken")));
        Assert.Empty(cut.FindAll($"#beitrag-{Mia}-hinweis"));
        Assert.Equal(3, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == BeitraegePfad));
    }

    [Fact]
    public void Ein_Fehler_beim_Speichern_steht_am_Feld_der_Haken_bleibt_wie_vorher()
    {
        BeitraegeAntworten();
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Paul}/bezahlt", () => throw new HttpRequestException("offline"));
        var cut = BeitraegeRendern();

        Haken(cut, Paul).Change(true);

        cut.WaitForAssertion(() => Assert.Equal($"Fehler: {FinanzenTexte.BeitragNichtGespeichertText}", cut.Find($"#beitrag-{Paul}-fehler").TextContent));
        Assert.False(Haken(cut, Paul).HasAttribute("checked"));
        Assert.Equal("true", Haken(cut, Paul).GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll($"#beitrag-{Mia}-fehler"));
        Assert.Empty(cut.FindAll(".elkw-alert--fehler"));
    }

    [Fact]
    public void Die_Zeilen_stehen_nach_Namen_sortiert_auch_wenn_die_API_sie_anders_liefert()
    {
        BeitraegeAntworten(beitraege: () => Json(BeitraegeJson(BeitragJson(Paul), BeitragJson(Mia, Lea))));

        var cut = BeitraegeRendern();

        Assert.Equal(["Mia Probst", "Paul Beispiel"], cut.FindAll("#beitraege label.elkw-choice").Select(l => l.TextContent.Trim()));
    }

    [Fact]
    public void Nicht_gefunden_beim_Speichern_steht_am_Feld_und_die_Liste_laedt_neu()
    {
        BeitraegeAntworten();
        _server.BeiDerReihe(
            HttpMethod.Get, BeitraegePfad,
            () => Json(BeitraegeJson(BeitragJson(Mia, Lea), BeitragJson(Paul))),
            () => Json(BeitraegeJson(BeitragJson(Mia, Lea), BeitragJson(Paul))));
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Paul}/bezahlt", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = BeitraegeRendern();

        Haken(cut, Paul).Change(true);

        cut.WaitForAssertion(() => Assert.Equal($"Fehler: {FinanzenTexte.BeitragNichtGespeichertText}", cut.Find($"#beitrag-{Paul}-fehler").TextContent));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == BeitraegePfad));
        Assert.False(Haken(cut, Paul).HasAttribute("checked"));
    }

    [Fact]
    public void Scheitert_das_Neuladen_nach_dem_Speichern_steht_die_Meldung()
    {
        BeitraegeAntworten();
        _server.BeiDerReihe(
            HttpMethod.Get, BeitraegePfad,
            () => Json(BeitraegeJson(BeitragJson(Mia, Lea), BeitragJson(Paul))),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Paul}/bezahlt", () => Json(BeitragJson(Paul, Jonas).Json));
        var cut = BeitraegeRendern();

        Haken(cut, Paul).Change(true);

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll($"#beitrag-{Paul}-fehler"));
    }

    [Fact]
    public void Abgeschlossen_beim_Speichern_zeigt_den_Hinweis_am_Feld_und_deaktiviert_die_Haken()
    {
        BeitraegeAntworten();
        _server.BeiDerReihe(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(KontextJson("InVorbereitung")), () => Json(KontextJson("Gelaufen")));
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Paul}/bezahlt", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = BeitraegeRendern();

        Haken(cut, Paul).Change(true);

        cut.WaitForAssertion(() => Assert.Equal($"Fehler: {FinanzenTexte.AbgeschlossenText}", cut.Find($"#beitrag-{Paul}-fehler").TextContent));
        cut.WaitForAssertion(() => Assert.All(cut.FindAll("#beitraege input[type=checkbox]"), k => Assert.True(k.HasAttribute("disabled"))));
    }

    [Fact]
    public void Nach_dem_Abschluss_sind_die_Haken_sichtbar_aber_deaktiviert()
    {
        BeitraegeAntworten("Gelaufen");

        var cut = BeitraegeRendern();

        Assert.Contains(FinanzenTexte.AbgeschlossenText, cut.Markup, StringComparison.Ordinal);
        Assert.True(Haken(cut, Mia).HasAttribute("checked"));
        Assert.Equal("Abgehakt von Lea Feuerstein am 20.10.2026", cut.Find($"#beitrag-{Mia}-hinweis").TextContent);
        Assert.All(cut.FindAll("#beitraege input[type=checkbox]"), k => Assert.True(k.HasAttribute("disabled")));
    }

    [Fact]
    public void Ohne_bestaetigte_Teilnehmende_steht_der_Leer_Zustand_unbekannte_Namen_heissen_Ohne_Namen()
    {
        BeitraegeAntworten(beitraege: () => Json(BeitraegeJson()));
        var leer = BeitraegeRendern();
        Assert.Contains("Noch keine bestätigten Teilnehmenden.", leer.Find("#beitraege").TextContent, StringComparison.Ordinal);
        Assert.Empty(leer.FindAll("#beitraege .zl-balken"));

        BeitraegeAntworten(beitraege: () => Json(BeitraegeJson(BeitragJson(Unbekannt, BelegFelder.GeloeschtePerson))));
        var cut = BeitraegeRendern();
        Assert.Equal("Ohne Namen", cut.Find("#beitraege label.elkw-choice").TextContent.Trim());
        Assert.Equal("Abgehakt von Gelöschte Person am 20.10.2026", cut.Find($"#beitrag-{Unbekannt}-hinweis").TextContent);
        Assert.Equal("Alle bezahlt", cut.Find("#beitraege .zl-balken .elkw-status").TextContent);
    }

    [Fact]
    public void Ohne_Modul_Finanzen_nicht_verfuegbar_ein_Ladefehler_zeigt_die_Meldung()
    {
        BeitraegeAntworten(beitraege: () => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Contains("Diese Seite ist nicht verfügbar.", BeitraegeRendern().Markup, StringComparison.Ordinal);

        _server.Bei(HttpMethod.Get, BeitraegePfad, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = BeitraegeRendern();
        Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#beitraege"));
    }

    [Fact]
    public async Task Client_Beitraege_laden_abhaken_und_oeffnen_ordnet_die_Antworten_zu()
    {
        BeitraegeAntworten();
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Paul}/bezahlt", () => Json(BeitragJson(Paul, Jonas).Json));
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Mia}/offen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        _server.Bei(HttpMethod.Post, $"{BeitraegePfad}/{Emma}/bezahlt", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Services.GetRequiredService<FinanzenApiClient>();

        var beitraege = (await client.BeitraegeLadenAsync(Freizeit, Abbruch))!;
        Assert.Equal((1, 2, Lea), (beitraege.Bezahlt, beitraege.Gesamt, beitraege.Zeilen[0].AbgehaktVon!.Value));
        Assert.Null(await client.BeitraegeLadenAsync(Guid.CreateVersion7(), Abbruch));
        var bezahlt = await client.BeitragBezahltAsync(Freizeit, Paul, Abbruch);
        Assert.Equal((FinanzenAktionErgebnis.Erfolg, true, Jonas), (bezahlt.Ergebnis, bezahlt.Zeile!.Bezahlt, bezahlt.Zeile.AbgehaktVon!.Value));
        Assert.Equal(FinanzenAktionErgebnis.Abgeschlossen, (await client.BeitragOffenAsync(Freizeit, Mia, Abbruch)).Ergebnis);
        var fehler = await client.BeitragBezahltAsync(Freizeit, Emma, Abbruch);
        Assert.Equal((FinanzenAktionErgebnis.Fehler, (BeitragZeile?)null), (fehler.Ergebnis, fehler.Zeile));
        Assert.Equal(
            ($"{BeitraegePfad}/{Paul}/bezahlt", $"{BeitraegePfad}/{Paul}/offen"),
            (FinanzenApiClient.BeitragBezahltPfad(Freizeit, Paul), FinanzenApiClient.BeitragOffenPfad(Freizeit, Paul)));
    }
}
