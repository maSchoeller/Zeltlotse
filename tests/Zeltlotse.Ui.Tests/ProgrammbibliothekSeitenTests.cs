using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Features.Programm;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using BibliothekSeite = Zeltlotse.App.Features.Programm.Programmbibliothek;
using RasterSeite = Zeltlotse.App.Features.Programm.Wochenraster;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 3-6 in der Web-App mit nachgebildetem Server: die Card „Programmbibliothek“ am Programmpunkt („In Bibliothek ablegen“ mit einem Tipp, nicht
/// bei Mahlzeiten, nicht nach dem Abschluss), der Link im Wochenraster, die Seite „Programmbibliothek“ der Freizeit (Einträge mit Material und
/// Aufgaben, Übernehmen mit Tag und Uhrzeit, Feldfehler, Leer-Zustand, nur lesbar nach dem Abschluss), die Organisation und ihre Seite
/// „Programmbibliothek“ (Entfernen per Inline-Bestätigung) sowie der Client und die Texte.
/// </summary>
public sealed class ProgrammbibliothekSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000004d1");
    private static readonly Guid OrgId = Guid.Parse("0199f700-0000-7000-8000-0000000004c1");
    private static readonly Guid Lagerfeuer = Guid.Parse("0199f700-0000-7000-8000-0000000004a1");
    private static readonly Guid Fruehstueck = Guid.Parse("0199f700-0000-7000-8000-0000000004a2");
    private static readonly Guid Neu = Guid.Parse("0199f700-0000-7000-8000-0000000004a3");
    private static readonly Guid Gelaendespiel = Guid.Parse("0199f700-0000-7000-8000-0000000004b1");
    private static readonly Guid LagerfeuerEintrag = Guid.Parse("0199f700-0000-7000-8000-0000000004b2");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public ProgrammbibliothekSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero)));
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string ProgrammPfad => $"/api/freizeiten/{Freizeit}/programm";

    private static string BibliothekPfad => $"{ProgrammPfad}/bibliothek";

    private static string OrgPfad => $"/api/organisationen/{OrgId}/programmbibliothek";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Punkt(Guid id, string titel, string? mahlzeit = null, bool inBibliothek = false, string tag = "2027-08-02", string beginn = "20:00") =>
        new { id, version = 3u, tag, beginn, titel, mahlzeit, uebernommenVon = (Guid?)null, meins = false, inBibliothek };

    /// <summary>Zwei Einträge: „Geländespiel“ mit zwei Materialien und einer Aufgabe, „Lagerfeuer“ ohne Material und Aufgaben.</summary>
    private static object[] Eintraege() =>
    [
        new
        {
            id = Gelaendespiel, titel = "Geländespiel", abgelegtAm = "2026-10-13T22:30:00Z",
            material = new object[] { new { bezeichnung = "Fahnen", menge = (string?)null }, new { bezeichnung = "Seile", menge = "4 × 10 m" } },
            aufgaben = new object[] { new { titel = "Fahnen verstecken", phase = "Waehrend" } },
        },
        new { id = LagerfeuerEintrag, titel = "Lagerfeuer", abgelegtAm = "2026-08-01T10:00:00Z", material = Array.Empty<object>(), aufgaben = Array.Empty<object>() },
    ];

    private void Antworten(string phase = "InVorbereitung", bool lagerfeuerInBibliothek = false, Func<HttpResponseMessage>? bibliothek = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>(),
        }));
        _server.Bei(HttpMethod.Get, ProgrammPfad, () => Json(new
        {
            ersterTag = "2027-08-02",
            letzterTag = "2027-08-04",
            punkte = new[] { Punkt(Fruehstueck, "Frühstück", "Fruehstueck", beginn: "08:00"), Punkt(Lagerfeuer, "Lagerfeuer", inBibliothek: lagerfeuerInBibliothek) },
        }));
        _server.Bei(HttpMethod.Get, $"{ProgrammPfad}/material", () => Json(new { material = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, $"{ProgrammPfad}/aufgaben", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, BibliothekPfad, bibliothek ?? (() => Json(Eintraege())));
    }

    private IRenderedComponent<ProgrammpunktSeite> PunktRendern(Guid punkt)
    {
        var cut = Render<ProgrammpunktSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PunktId, punkt));
        cut.WaitForAssertion(() => Assert.Contains("Zurück zum Programm", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<BibliothekSeite> BibliothekRendern()
    {
        var cut = Render<BibliothekSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Card<T>(IRenderedComponent<T> cut, string titel)
        where T : IComponent =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")?.TextContent == titel);

    private static AngleSharp.Dom.IElement Knopf(AngleSharp.Dom.IParentNode bereich, string text) =>
        bereich.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_Bibliothek_403_404_heissen_nicht_verfuegbar_und_die_Aktionen_ordnen_Antworten_zu()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, OrgPfad, () => Json(Eintraege()));
        var client = Services.GetRequiredService<ProgrammApiClient>();

        var eintraege = await client.BibliothekAsync(Freizeit, Abbruch);

        Assert.NotNull(eintraege);
        Assert.Equal(("Geländespiel", 2, "Seile", "4 × 10 m", "Fahnen verstecken", AufgabenPhase.Waehrend),
            (eintraege[0].Titel, eintraege[0].Material.Count, eintraege[0].Material[1].Bezeichnung, eintraege[0].Material[1].Menge, eintraege[0].Aufgaben[0].Titel, eintraege[0].Aufgaben[0].Phase));
        Assert.Equal(2, (await client.OrgBibliothekAsync(OrgId, Abbruch))!.Count);
        Assert.Equal(BibliothekPfad, ProgrammApiClient.BibliothekPfad(Freizeit));
        Assert.Equal(OrgPfad, ProgrammApiClient.OrgBibliothekPfad(OrgId));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, BibliothekPfad, () => new HttpResponseMessage(status));
            _server.Bei(HttpMethod.Get, OrgPfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.BibliothekAsync(Freizeit, Abbruch));
            Assert.Null(await client.OrgBibliothekAsync(OrgId, Abbruch));
        }

        _server.Bei(HttpMethod.Post, $"{ProgrammPfad}/punkte/{Lagerfeuer}/bibliothek", () => Json(Punkt(Lagerfeuer, "Lagerfeuer", inBibliothek: true)));
        _server.Bei(HttpMethod.Post, $"{ProgrammPfad}/punkte/{Fruehstueck}/bibliothek", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Post, $"{BibliothekPfad}/{Gelaendespiel}/uebernehmen", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"tag":["Tag fehlt."]}}""", "application/problem+json"));
        _server.Bei(HttpMethod.Post, $"{BibliothekPfad}/{LagerfeuerEintrag}/uebernehmen", () => Json(Punkt(Neu, "Lagerfeuer"), HttpStatusCode.Created));
        _server.Bei(HttpMethod.Delete, $"{OrgPfad}/{Gelaendespiel}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Delete, $"{OrgPfad}/{LagerfeuerEintrag}", () => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.True((await client.AblegenAsync(Freizeit, Lagerfeuer, Abbruch)).Punkt!.InBibliothek);
        Assert.Equal(ProgrammAktionErgebnis.Konflikt, (await client.AblegenAsync(Freizeit, Fruehstueck, Abbruch)).Ergebnis);
        Assert.Equal("Tag fehlt.", (await client.BibliothekUebernehmenAsync(Freizeit, Gelaendespiel, new BibliotheksUebernahme(null, null), Abbruch)).Feldfehler["tag"]);
        Assert.Equal(Neu, (await client.BibliothekUebernehmenAsync(Freizeit, LagerfeuerEintrag, new BibliotheksUebernahme(new DateOnly(2027, 8, 3), "19:30"), Abbruch)).Punkt!.Id);
        Assert.Equal(ProgrammAktionErgebnis.Erfolg, await client.EintragLoeschenAsync(OrgId, Gelaendespiel, Abbruch));
        Assert.Equal(ProgrammAktionErgebnis.Fehler, await client.EintragLoeschenAsync(OrgId, LagerfeuerEintrag, Abbruch));
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{BibliothekPfad}/{LagerfeuerEintrag}/uebernehmen"
                                               && a.Koerper == """{"tag":"2027-08-03","beginn":"19:30"}""");

        Assert.Equal("Abgelegt am 14.10.2026", Anzeige.AbgelegtAm(new DateTimeOffset(2026, 10, 13, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(("Seile · 4 × 10 m", "Fahnen"), (Anzeige.BibliotheksMaterial(new BibliotheksMaterial("Seile", "4 × 10 m")), Anzeige.BibliotheksMaterial(new BibliotheksMaterial("Fahnen", null))));
        Assert.Equal("Fahnen verstecken · Während der Freizeit", Anzeige.BibliotheksAufgabe(new BibliotheksAufgabe("Fahnen verstecken", AufgabenPhase.Waehrend)));
    }

    // ---------- Programmpunkt ----------

    [Fact]
    public void Programmpunkt_In_Bibliothek_ablegen_mit_einem_Tipp_danach_steht_der_Satz_und_der_Knopf_fehlt()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{ProgrammPfad}/punkte/{Lagerfeuer}/bibliothek", () => Json(Punkt(Lagerfeuer, "Lagerfeuer", inBibliothek: true)));
        var cut = PunktRendern(Lagerfeuer);

        var card = cut.Find("#programmbibliothek");
        Assert.Equal("Programmbibliothek", card.QuerySelector("h2")!.TextContent);
        Assert.Contains("Titel, Material und Aufgaben lassen sich für andere Freizeiten der Organisation ablegen.", card.TextContent, StringComparison.Ordinal);
        Knopf(card, "In Bibliothek ablegen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Steht in der Programmbibliothek der Organisation.", cut.Find("#programmbibliothek").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "In Bibliothek ablegen");
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{ProgrammPfad}/punkte/{Lagerfeuer}/bibliothek");
    }

    [Fact]
    public void Programmpunkt_schon_abgelegt_zeigt_den_Satz_Mahlzeit_hat_keine_Card_abgeschlossen_keinen_Knopf()
    {
        Antworten(lagerfeuerInBibliothek: true);
        var abgelegt = PunktRendern(Lagerfeuer);
        Assert.Contains("Steht in der Programmbibliothek der Organisation.", abgelegt.Find("#programmbibliothek").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(abgelegt.FindAll("button"), b => b.TextContent.Trim() == "In Bibliothek ablegen");

        var mahlzeit = PunktRendern(Fruehstueck);
        Assert.Empty(mahlzeit.FindAll("#programmbibliothek"));

        Antworten(phase: "Gelaufen");
        var gelaufen = PunktRendern(Lagerfeuer);
        Assert.DoesNotContain(gelaufen.FindAll("button"), b => b.TextContent.Trim() == "In Bibliothek ablegen");
        Assert.Empty(gelaufen.FindAll("#programmbibliothek"));

        Antworten(phase: "Gelaufen", lagerfeuerInBibliothek: true);
        var gelaufenAbgelegt = PunktRendern(Lagerfeuer);
        Assert.Contains("Steht in der Programmbibliothek der Organisation.", gelaufenAbgelegt.Find("#programmbibliothek").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(gelaufenAbgelegt.FindAll("button"), b => b.TextContent.Trim() == "In Bibliothek ablegen");
    }

    // ---------- Wochenraster ----------

    [Fact]
    public void Wochenraster_hat_neben_der_Materialliste_den_Link_Programmbibliothek()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Render<RasterSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Contains("Materialliste", cut.Markup, StringComparison.Ordinal));

        var link = cut.FindAll("a").Single(a => a.TextContent.Trim() == "Programmbibliothek");

        Assert.Equal($"freizeiten/{Freizeit}/programm/bibliothek", link.GetAttribute("href"));
        Assert.Same(cut.FindAll("a").Single(a => a.TextContent.Trim() == "Materialliste").ParentElement, link.ParentElement);
    }

    // ---------- Seite Programmbibliothek ----------

    [Fact]
    public void Bibliothek_zeigt_je_Eintrag_Abgelegt_am_Material_und_Aufgaben_und_das_Formular_mit_den_Tagen_der_Freizeit()
    {
        Antworten();

        var cut = BibliothekRendern();

        Assert.Equal(("Programm", "Programmbibliothek"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var card = Card(cut, "Geländespiel");
        Assert.Contains("Abgelegt am 14.10.2026", card.TextContent, StringComparison.Ordinal);
        Assert.Equal(["Fahnen", "Seile · 4 × 10 m", "Fahnen verstecken · Während der Freizeit"], card.QuerySelectorAll("li").Select(l => l.TextContent.Trim()));
        var tag = card.QuerySelector("input[type=date]")!;
        Assert.Equal(("2027-08-02", "2027-08-04"), (tag.GetAttribute("min"), tag.GetAttribute("max")));
        Assert.NotNull(card.QuerySelector("input[type=time]"));
        Assert.Contains("In diese Freizeit übernehmen", card.TextContent, StringComparison.Ordinal);
        var leer = Card(cut, "Lagerfeuer");
        Assert.Contains("Kein Material.", leer.TextContent, StringComparison.Ordinal);
        Assert.Contains("Keine Aufgaben.", leer.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Uebernehmen_schickt_Tag_und_Uhrzeit_und_oeffnet_den_neuen_Programmpunkt()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{BibliothekPfad}/{Gelaendespiel}/uebernehmen", () => Json(Punkt(Neu, "Geländespiel", tag: "2027-08-03", beginn: "10:00"), HttpStatusCode.Created));
        var cut = BibliothekRendern();

        cut.Find($"#uebernahme-{Gelaendespiel}-tag").Input("2027-08-03");
        cut.Find($"#uebernahme-{Gelaendespiel}-beginn").Input("10:00");
        cut.FindAll("form").Single(f => f.QuerySelector($"#uebernahme-{Gelaendespiel}-tag") is not null).Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/programm/{Neu}", Navigation.Uri, StringComparison.Ordinal));
        var anfrage = Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post);
        Assert.Equal("""{"tag":"2027-08-03","beginn":"10:00"}""", anfrage.Koerper);
    }

    [Fact]
    public void Uebernehmen_zeigt_Feldfehler_am_Feld_des_Eintrags()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{BibliothekPfad}/{Gelaendespiel}/uebernehmen", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"tag":["Tag fehlt."],"beginn":["Uhrzeit im Format HH:mm, z. B. 14:30."]}}""", "application/problem+json"));
        var cut = BibliothekRendern();

        cut.FindAll("form").Single(f => f.QuerySelector($"#uebernahme-{Gelaendespiel}-tag") is not null).Submit();

        cut.WaitForAssertion(() => Assert.Contains("Tag fehlt.", Card(cut, "Geländespiel").TextContent, StringComparison.Ordinal));
        Assert.Contains("Uhrzeit im Format HH:mm", Card(cut, "Geländespiel").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag fehlt.", Card(cut, "Lagerfeuer").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("/programm/", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Bibliothek_leer_zeigt_den_Leer_Zustand_abgeschlossen_ohne_Formular_nicht_verfuegbar_bei_404()
    {
        Antworten(bibliothek: () => Json(Array.Empty<object>()));
        var leer = BibliothekRendern();
        Assert.Contains(
            "Noch nichts in der Programmbibliothek. Programmpunkte lassen sich an ihrer Seite mit „In Bibliothek ablegen“ hier ablegen.",
            leer.Markup,
            StringComparison.Ordinal);

        Antworten(phase: "Gelaufen");
        var gelaufen = BibliothekRendern();
        Assert.NotNull(Card(gelaufen, "Geländespiel"));
        Assert.Empty(gelaufen.FindAll("form"));
        Assert.DoesNotContain("In diese Freizeit übernehmen", gelaufen.Markup, StringComparison.Ordinal);

        Antworten(bibliothek: () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var weg = BibliothekRendern();
        Assert.Contains("Diese Seite ist nicht verfügbar.", weg.Markup, StringComparison.Ordinal);
    }

    // ---------- Organisation ----------

    private void Organisation(params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" }, erlaubt }));

    private IRenderedComponent<OrgProgrammbibliothek> OrgRendern()
    {
        var cut = Render<OrgProgrammbibliothek>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    [Fact]
    public void Organisation_zeigt_die_Card_Programmbibliothek_nur_mit_programmbibliothek_verwalten()
    {
        Organisation(Aktionsschluessel.ProgrammbibliothekVerwalten);
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.Contains("Programmbibliothek öffnen", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(
            $"organisationen/{OrgId}/programmbibliothek",
            cut.FindAll("a").Single(a => a.TextContent.Contains("Programmbibliothek öffnen", StringComparison.Ordinal)).GetAttribute("href"));

        Organisation(Aktionsschluessel.InventarSehen);
        var ueberblick = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        ueberblick.WaitForAssertion(() => Assert.Contains("Inventar öffnen", ueberblick.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Programmbibliothek", ueberblick.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Org_Seite_zeigt_die_Eintraege_und_entfernt_einen_per_Inline_Bestaetigung()
    {
        Organisation(Aktionsschluessel.ProgrammbibliothekVerwalten);
        _server.Bei(HttpMethod.Get, OrgPfad, () => Json(Eintraege()));
        _server.Bei(HttpMethod.Delete, $"{OrgPfad}/{Gelaendespiel}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = OrgRendern();

        Assert.Equal(("Organisation", "Programmbibliothek"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var card = Card(cut, "Geländespiel");
        Assert.Contains("Seile · 4 × 10 m", card.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("form input[type=date]"));
        Knopf(card, "Aus der Bibliothek entfernen").Click();
        Assert.Contains("„Geländespiel“ aus der Bibliothek entfernen?", Card(cut, "Geländespiel").TextContent, StringComparison.Ordinal);
        Knopf(Card(cut, "Geländespiel"), "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain(cut.FindAll("section.elkw-card h2"), h => h.TextContent == "Geländespiel"));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == $"{OrgPfad}/{Gelaendespiel}");
        Assert.NotNull(Card(cut, "Lagerfeuer"));
    }

    [Fact]
    public void Org_Seite_leer_zeigt_den_Leer_Zustand_ohne_Aktionsschluessel_nicht_verfuegbar()
    {
        Organisation(Aktionsschluessel.ProgrammbibliothekVerwalten);
        _server.Bei(HttpMethod.Get, OrgPfad, () => Json(Array.Empty<object>()));
        var leer = OrgRendern();
        Assert.Contains("Noch nichts in der Programmbibliothek. Das Team legt gelungene Programmpunkte in seiner Freizeit ab.", leer.Markup, StringComparison.Ordinal);

        Organisation(Aktionsschluessel.InventarSehen);
        var ohne = OrgRendern();
        Assert.Contains("Diese Seite ist nicht verfügbar.", ohne.Markup, StringComparison.Ordinal);
    }
}
