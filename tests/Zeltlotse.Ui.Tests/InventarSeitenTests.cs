using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.App.Features.Inventar;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Features.Programm;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using FreizeitSeite = Zeltlotse.App.Features.Inventar.FreizeitInventar;
using OrgSeite = Zeltlotse.App.Features.Inventar.OrgInventar;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 3-3 in der Web-App mit nachgebildetem Server: der Client und die Texte, das Inventar der Organisation (Org-Admin pflegt, Org-Überblick
/// liest nur, Konflikt als Status, leer, nicht verfügbar), die Card „Inventar“ der Organisation, das Inventar in der Freizeit (Konflikthinweis,
/// Reservieren mit vorbelegtem Zeitraum und Feldfehler, Aufheben, leer, nur lesbar) und „Aus dem Inventar“ am Material des Programmpunkts.
/// </summary>
public sealed class InventarSeitenTests : BunitContext
{
    private static readonly Guid OrgId = Guid.Parse("0199f800-0000-7000-8000-0000000000c1");
    private static readonly Guid Freizeit = Guid.Parse("0199f800-0000-7000-8000-0000000001d1");
    private static readonly Guid Kbt = Guid.Parse("0199f800-0000-7000-8000-0000000001d2");
    private static readonly Guid Fahnen = Guid.Parse("0199f800-0000-7000-8000-0000000002a1");
    private static readonly Guid Grosszelt = Guid.Parse("0199f800-0000-7000-8000-0000000002a2");
    private static readonly Guid ResFahnen = Guid.Parse("0199f800-0000-7000-8000-0000000003b1");
    private static readonly Guid ResZelt = Guid.Parse("0199f800-0000-7000-8000-0000000003b2");
    private static readonly Guid ResKbt = Guid.Parse("0199f800-0000-7000-8000-0000000003b3");
    private static readonly Guid Gelaendespiel = Guid.Parse("0199f800-0000-7000-8000-0000000004a1");
    private static readonly Guid MatFahnen = Guid.Parse("0199f800-0000-7000-8000-0000000005b1");
    private static readonly Guid MatSeile = Guid.Parse("0199f800-0000-7000-8000-0000000005b2");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public InventarSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten", () => Json(new { freizeiten = Array.Empty<object>(), erlaubt = Array.Empty<string>() }));
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero)));
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string OrgPfad => $"/api/organisationen/{OrgId}/inventar";

    private static string FreizeitPfad => $"/api/freizeiten/{Freizeit}/inventar";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Reservierung(Guid id, Guid freizeit, string name, string von, string bis, Guid? material = null, params string[] konflikte) =>
        new { id, freizeitId = freizeit, freizeit = name, von, bis, materialId = material, konflikte };

    private static object Artikel(Guid id, string bezeichnung, string? menge, params object[] reservierungen) =>
        new { id, version = 7u, bezeichnung, menge, reservierungen };

    private static object[] OrgVorgabe =>
    [
        Artikel(Fahnen, "Fahnen", "8 Stück", Reservierung(ResFahnen, Freizeit, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13")),
        Artikel(
            Grosszelt, "Großzelt", "1 Stück",
            Reservierung(ResZelt, Freizeit, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", null, "Kinderbibeltage 2027"),
            Reservierung(ResKbt, Kbt, "Kinderbibeltage 2027", "2027-08-10", "2027-08-12", null, "Zeltlager Sommer 2027")),
    ];

    private void Organisation(params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" }, erlaubt }));

    private IRenderedComponent<OrgSeite> OrgRendern()
    {
        var cut = Render<OrgSeite>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private void Kontext(string phase = "InVorbereitung") =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>(),
        }));

    private void FreizeitInventar(params object[] artikel) =>
        _server.Bei(HttpMethod.Get, FreizeitPfad, () => Json(new { ersterTag = "2027-08-02", letzterTag = "2027-08-13", artikel }));

    private static object[] FreizeitVorgabe =>
    [
        Artikel(Fahnen, "Fahnen", "8 Stück"),
        Artikel(Grosszelt, "Großzelt", "1 Stück", Reservierung(ResZelt, Freizeit, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", null, "Kinderbibeltage 2027")),
    ];

    private IRenderedComponent<FreizeitSeite> FreizeitRendern()
    {
        var cut = Render<FreizeitSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<OrgSeite> cut, string titel) =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == titel);

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<FreizeitSeite> cut, string titel) =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == titel);

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_403_404_heissen_nicht_verfuegbar_und_Aktionen_ordnen_Antworten_zu()
    {
        _server.BeiDerReihe(HttpMethod.Get, OrgPfad, () => Json(new { artikel = OrgVorgabe }), () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.Bei(HttpMethod.Get, FreizeitPfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.BeiDerReihe(
            HttpMethod.Post, OrgPfad,
            () => Json(Artikel(Fahnen, "Fahnen", null), HttpStatusCode.Created),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"bezeichnung":["Bezeichnung fehlt."]}}""", "application/problem+json"));
        _server.BeiDerReihe(
            HttpMethod.Delete, $"{OrgPfad}/{Grosszelt}",
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.BeiDerReihe(
            HttpMethod.Post, $"{FreizeitPfad}/{Fahnen}/reservierungen",
            () => Json(Reservierung(ResFahnen, Freizeit, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", null, "Kinderbibeltage 2027"), HttpStatusCode.Created),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"bis":["Bis liegt vor Von."]}}""", "application/problem+json"),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));
        _server.Bei(HttpMethod.Delete, $"{FreizeitPfad}/reservierungen/{ResFahnen}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Services.GetRequiredService<InventarApiClient>();

        var org = await client.OrgInventarAsync(OrgId, Abbruch);
        Assert.Equal(["Kinderbibeltage 2027"], org!.Artikel[1].Reservierungen[0].Konflikte);
        Assert.Null(await client.OrgInventarAsync(OrgId, Abbruch));
        Assert.Null(await client.FreizeitInventarAsync(Freizeit, Abbruch));
        Assert.Equal(Fahnen, (await client.ArtikelAnlegenAsync(OrgId, new InventarArtikelNeu("Fahnen", null), Abbruch)).Artikel!.Id);
        Assert.Equal("Bezeichnung fehlt.", (await client.ArtikelAnlegenAsync(OrgId, new InventarArtikelNeu("", null), Abbruch)).Feldfehler["bezeichnung"]);
        Assert.Equal(InventarAktionErgebnis.Konflikt, await client.ArtikelLoeschenAsync(OrgId, Grosszelt, Abbruch));
        Assert.Equal(InventarAktionErgebnis.Erfolg, await client.ArtikelLoeschenAsync(OrgId, Grosszelt, Abbruch));
        var reserviert = await client.ReservierenAsync(Freizeit, Fahnen, new ReservierungNeu(null, null, null), Abbruch);
        Assert.Equal((InventarAktionErgebnis.Erfolg, "Kinderbibeltage 2027"), (reserviert.Ergebnis, reserviert.Reservierung!.Konflikte[0]));
        Assert.Equal("Bis liegt vor Von.", (await client.ReservierenAsync(Freizeit, Fahnen, new ReservierungNeu(null, null, null), Abbruch)).Feldfehler["bis"]);
        Assert.Equal(InventarAktionErgebnis.Abgeschlossen, (await client.ReservierenAsync(Freizeit, Fahnen, new ReservierungNeu(null, null, null), Abbruch)).Ergebnis);
        Assert.Equal(InventarAktionErgebnis.Erfolg, await client.AufhebenAsync(Freizeit, ResFahnen, Abbruch));
        Assert.Equal($"{OrgPfad}/{Fahnen}", InventarApiClient.ArtikelPfad(OrgId, Fahnen));
    }

    [Fact]
    public void Texte_kurzer_Zeitraum_und_Konflikthinweis()
    {
        Assert.Equal("02.08.–13.08.2027", Anzeige.ZeitraumKurz(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13)));
        Assert.Equal("03.08.2027", Anzeige.ZeitraumKurz(new DateOnly(2027, 8, 3), new DateOnly(2027, 8, 3)));
        Assert.Equal("30.12.2027–02.01.2028", Anzeige.ZeitraumKurz(new DateOnly(2027, 12, 30), new DateOnly(2028, 1, 2)));
        Assert.Equal(
            "Zur gleichen Zeit auch für Kinderbibeltage 2027 reserviert. Absprache mit der anderen Leitung nötig.",
            Anzeige.Konflikt(["Kinderbibeltage 2027"]));
        Assert.Equal(
            "Zur gleichen Zeit auch für Bibelwoche, Freizeit B und Kinderbibeltage 2027 reserviert. Absprache mit der anderen Leitung nötig.",
            Anzeige.Konflikt(["Bibelwoche", "Freizeit B", "Kinderbibeltage 2027"]));
    }

    // ---------- Inventar der Organisation ----------

    [Fact]
    public void Org_Admin_sieht_je_Artikel_eine_Card_mit_Autosave_Reservierungen_Konflikt_Loeschen_und_das_Formular()
    {
        Organisation(Aktionsschluessel.InventarSehen, Aktionsschluessel.InventarVerwalten);
        _server.Bei(HttpMethod.Get, OrgPfad, () => Json(new { artikel = OrgVorgabe }));

        var cut = OrgRendern();

        Assert.Equal("Inventar", cut.Find("h1").TextContent);
        var zelt = Card(cut, "Großzelt");
        Assert.Equal("Großzelt", zelt.QuerySelector("input#artikel-" + Grosszelt + "-bezeichnung")!.GetAttribute("value"));
        Assert.Equal("1 Stück", zelt.QuerySelector("input#artikel-" + Grosszelt + "-menge")!.GetAttribute("value"));
        var zeilen = zelt.QuerySelectorAll("li").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(["Zeltlager Sommer 2027 · 02.08.–13.08.2027 Konflikt", "Kinderbibeltage 2027 · 10.08.–12.08.2027 Konflikt"], zeilen);
        Assert.Equal("Konflikt", zelt.QuerySelector(".elkw-status--warnung")!.TextContent);
        Assert.DoesNotContain("Konflikt", Card(cut, "Fahnen").TextContent, StringComparison.Ordinal);
        Assert.Contains(zelt.QuerySelectorAll("button"), b => b.TextContent == "Artikel löschen");
        Assert.NotNull(Card(cut, "Artikel hinzufügen").QuerySelector("form"));
    }

    [Fact]
    public void Org_Ueberblick_sieht_das_Inventar_mit_Reservierungen_ohne_Formular_und_ohne_Loeschen()
    {
        Organisation(Aktionsschluessel.InventarSehen);
        _server.Bei(HttpMethod.Get, OrgPfad, () => Json(new { artikel = OrgVorgabe }));

        var cut = OrgRendern();

        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("form"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Artikel löschen");
        Assert.Contains("Menge: 8 Stück", Card(cut, "Fahnen").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, Card(cut, "Großzelt").QuerySelectorAll("li").Length);
    }

    [Fact]
    public void Ohne_Artikel_Noch_kein_Inventar_ohne_inventar_sehen_nicht_verfuegbar()
    {
        Organisation(Aktionsschluessel.InventarSehen);
        _server.Bei(HttpMethod.Get, OrgPfad, () => Json(new { artikel = Array.Empty<object>() }));
        Assert.Contains("Noch kein Inventar.", OrgRendern().Markup, StringComparison.Ordinal);

        Organisation(Aktionsschluessel.OrgRollenVergeben);
        Assert.Contains("Diese Seite ist nicht verfügbar.", OrgRendern().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Artikel_hinzufuegen_sendet_Bezeichnung_und_Menge_zeigt_Feldfehler_und_laedt_danach_neu()
    {
        Organisation(Aktionsschluessel.InventarSehen, Aktionsschluessel.InventarVerwalten);
        _server.BeiDerReihe(HttpMethod.Get, OrgPfad, () => Json(new { artikel = Array.Empty<object>() }), () => Json(new { artikel = new[] { Artikel(Fahnen, "Fahnen", "8 Stück") } }));
        _server.BeiDerReihe(
            HttpMethod.Post, OrgPfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"bezeichnung":["Bezeichnung fehlt."]}}""", "application/problem+json"),
            () => Json(Artikel(Fahnen, "Fahnen", "8 Stück"), HttpStatusCode.Created));
        var cut = OrgRendern();

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Fehler: Bezeichnung fehlt.", cut.Markup, StringComparison.Ordinal));
        cut.Find("#artikel-bezeichnung").Input("Fahnen");
        cut.Find("#artikel-menge").Input("8 Stück");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Fahnen")));
        var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Post).Koerper!).RootElement;
        Assert.Equal(("Fahnen", "8 Stück"), (koerper.GetProperty("bezeichnung").GetString(), koerper.GetProperty("menge").GetString()));
    }

    [Fact]
    public void Artikel_loeschen_mit_Reservierungen_zeigt_Noch_reserviert_sonst_verschwindet_er()
    {
        Organisation(Aktionsschluessel.InventarSehen, Aktionsschluessel.InventarVerwalten);
        _server.Bei(HttpMethod.Get, OrgPfad, () => Json(new { artikel = OrgVorgabe }));
        _server.Bei(HttpMethod.Delete, $"{OrgPfad}/{Grosszelt}", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Delete, $"{OrgPfad}/{Fahnen}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = OrgRendern();

        LoeschenBestaetigen(cut, "Großzelt");
        cut.WaitForAssertion(() => Assert.Contains(OrgSeite.NochReserviertText, cut.Markup, StringComparison.Ordinal));
        LoeschenBestaetigen(cut, "Fahnen");

        cut.WaitForAssertion(() => Assert.DoesNotContain(cut.FindAll("section.elkw-card h2"), h => h.TextContent == "Fahnen"));
    }

    private static void LoeschenBestaetigen(IRenderedComponent<OrgSeite> cut, string titel)
    {
        Card(cut, titel).QuerySelectorAll("button").Single(b => b.TextContent == "Artikel löschen").Click();
        cut.WaitForAssertion(() => Card(cut, titel).QuerySelectorAll("button").Single(b => b.TextContent == "Ja, löschen").Click());
    }

    [Fact]
    public void Organisation_zeigt_die_Card_Inventar_nur_mit_inventar_sehen()
    {
        Organisation(Aktionsschluessel.InventarSehen);
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.Contains("Inventar öffnen", cut.Markup, StringComparison.Ordinal));
        Assert.Equal($"organisationen/{OrgId}/inventar", cut.FindAll("a").Single(a => a.TextContent.Contains("Inventar öffnen", StringComparison.Ordinal)).GetAttribute("href"));

        Organisation(Aktionsschluessel.OrgRollenVergeben);
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/rollen", () => Json(new { personen = Array.Empty<object>(), einladungen = Array.Empty<object>() }));
        var ohne = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        ohne.WaitForAssertion(() => Assert.Contains("Jugendwerk Musterstadt", ohne.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Inventar öffnen", ohne.Markup, StringComparison.Ordinal);
    }

    // ---------- Inventar in der Freizeit ----------

    [Fact]
    public void Freizeit_zeigt_Dachzeile_Konflikthinweis_eigene_Reservierungen_und_das_vorbelegte_Formular()
    {
        Kontext();
        FreizeitInventar(FreizeitVorgabe);

        var cut = FreizeitRendern();

        Assert.Equal(("Planen", "Inventar"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var zelt = Card(cut, "Großzelt");
        Assert.Equal(
            "Zur gleichen Zeit auch für Kinderbibeltage 2027 reserviert. Absprache mit der anderen Leitung nötig.",
            zelt.QuerySelector(".elkw-alert--warnung p")!.TextContent);
        Assert.StartsWith("02.08.–13.08.2027", zelt.QuerySelector("li")!.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Contains(zelt.QuerySelectorAll("button"), b => b.TextContent == "Reservierung aufheben");
        var fahnen = Card(cut, "Fahnen");
        Assert.Contains("Für diese Freizeit nicht reserviert.", fahnen.TextContent, StringComparison.Ordinal);
        Assert.Null(fahnen.QuerySelector(".elkw-alert--warnung"));
        Assert.Equal(("2027-08-02", "2027-08-13"), (
            fahnen.QuerySelector($"#reservieren-{Fahnen}-von")!.GetAttribute("value"), fahnen.QuerySelector($"#reservieren-{Fahnen}-bis")!.GetAttribute("value")));
    }

    [Fact]
    public void Reservieren_sendet_Von_und_Bis_zeigt_den_Fehler_am_Feld_Bis_und_laedt_danach_neu()
    {
        Kontext();
        _server.BeiDerReihe(
            HttpMethod.Get, FreizeitPfad,
            () => Json(new { ersterTag = "2027-08-02", letzterTag = "2027-08-13", artikel = FreizeitVorgabe }),
            () => Json(new
            {
                ersterTag = "2027-08-02", letzterTag = "2027-08-13",
                artikel = new[] { Artikel(Fahnen, "Fahnen", "8 Stück", Reservierung(ResFahnen, Freizeit, "Zeltlager Sommer 2027", "2027-08-03", "2027-08-05")) },
            }));
        _server.BeiDerReihe(
            HttpMethod.Post, $"{FreizeitPfad}/{Fahnen}/reservierungen",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"bis":["Bis liegt vor Von."]}}""", "application/problem+json"),
            () => Json(Reservierung(ResFahnen, Freizeit, "Zeltlager Sommer 2027", "2027-08-03", "2027-08-05"), HttpStatusCode.Created));
        var cut = FreizeitRendern();
        var fahnen = Card(cut, "Fahnen");

        fahnen.QuerySelector("form")!.Submit();
        cut.WaitForAssertion(() => Assert.Contains("Fehler: Bis liegt vor Von.", Card(cut, "Fahnen").TextContent, StringComparison.Ordinal));
        cut.Find($"#reservieren-{Fahnen}-von").Input("2027-08-03");
        cut.Find($"#reservieren-{Fahnen}-bis").Input("2027-08-05");
        Card(cut, "Fahnen").QuerySelector("form")!.Submit();

        cut.WaitForAssertion(() => Assert.StartsWith("03.08.–05.08.2027", Card(cut, "Fahnen").QuerySelector("li")!.TextContent.Trim(), StringComparison.Ordinal));
        var koerper = _server.Anfragen.Where(a => a.Methode == HttpMethod.Post).Select(a => a.Koerper).ToList();
        Assert.Contains("\"von\":\"2027-08-02\",\"bis\":\"2027-08-13\"", koerper[0], StringComparison.Ordinal);
        Assert.Contains("\"von\":\"2027-08-03\",\"bis\":\"2027-08-05\"", koerper[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Reservierung_aufheben_fragt_inline_nach_und_laedt_danach_neu()
    {
        Kontext();
        _server.BeiDerReihe(
            HttpMethod.Get, FreizeitPfad,
            () => Json(new { ersterTag = "2027-08-02", letzterTag = "2027-08-13", artikel = FreizeitVorgabe }),
            () => Json(new { ersterTag = "2027-08-02", letzterTag = "2027-08-13", artikel = new[] { Artikel(Grosszelt, "Großzelt", "1 Stück") } }));
        _server.Bei(HttpMethod.Delete, $"{FreizeitPfad}/reservierungen/{ResZelt}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = FreizeitRendern();

        Card(cut, "Großzelt").QuerySelectorAll("button").Single(b => b.TextContent == "Reservierung aufheben").Click();
        Assert.Contains("Reservierung aufheben?", Card(cut, "Großzelt").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
        Card(cut, "Großzelt").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, aufheben").Click();

        cut.WaitForAssertion(() => Assert.Contains("Für diese Freizeit nicht reserviert.", Card(cut, "Großzelt").TextContent, StringComparison.Ordinal));
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == $"{FreizeitPfad}/reservierungen/{ResZelt}");
    }

    [Fact]
    public void Freizeit_ohne_Inventar_und_abgeschlossen_nur_lesbar_nicht_verfuegbar_bei_404()
    {
        Kontext();
        FreizeitInventar();
        Assert.Contains("Die Organisation hat noch kein Inventar.", FreizeitRendern().Markup, StringComparison.Ordinal);

        Kontext("Gelaufen");
        FreizeitInventar(FreizeitVorgabe);
        var gelaufen = FreizeitRendern();
        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", gelaufen.Markup, StringComparison.Ordinal);
        Assert.Empty(gelaufen.FindAll("form"));
        Assert.Empty(gelaufen.FindAll("button"));
        Assert.Contains("Kinderbibeltage 2027", gelaufen.Markup, StringComparison.Ordinal);

        _server.Bei(HttpMethod.Get, FreizeitPfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Contains("Diese Seite ist nicht verfügbar.", FreizeitRendern().Markup, StringComparison.Ordinal);
    }

    // ---------- Aus dem Inventar am Programmpunkt ----------

    private void Programmpunkt(object inventar, string phase = "InVorbereitung")
    {
        Kontext(phase);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/programm", () => Json(new
        {
            ersterTag = "2027-08-02", letzterTag = "2027-08-13",
            punkte = new object[]
            {
                new { id = Gelaendespiel, version = 3u, tag = "2027-08-03", beginn = "14:30", titel = "Geländespiel", mahlzeit = (string?)null, uebernommenVon = (Guid?)null, meins = false },
            },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/programm/material", () => Json(new
        {
            material = new object[]
            {
                new { id = MatFahnen, version = 5u, programmpunktId = Gelaendespiel, programmpunkt = "Geländespiel", tag = "2027-08-03", beginn = "14:30", bezeichnung = "Fahnen", menge = "8 Stück", status = "Vorhanden", bringtMit = (Guid?)null, meins = false, reservierungId = ResFahnen },
                new { id = MatSeile, version = 5u, programmpunktId = Gelaendespiel, programmpunkt = "Geländespiel", tag = "2027-08-03", beginn = "14:30", bezeichnung = "Seile", menge = "4 × 10 m", status = "Gebraucht", bringtMit = (Guid?)null, meins = false, reservierungId = (Guid?)null },
            },
        }));
        _server.Bei(HttpMethod.Get, FreizeitPfad, () => Json(inventar));
    }

    private static object InventarMitFahnen(bool seileReserviert = false) => new
    {
        ersterTag = "2027-08-02", letzterTag = "2027-08-13",
        artikel = new[]
        {
            Artikel(Fahnen, "Fahnen", "8 Stück", Reservierung(ResFahnen, Freizeit, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", MatFahnen, "Kinderbibeltage 2027")),
            Artikel(Grosszelt, "Großzelt", "1 Stück", seileReserviert ? [Reservierung(ResZelt, Freizeit, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", MatSeile)] : []),
        },
    };

    private IRenderedComponent<ProgrammpunktSeite> PunktRendern()
    {
        var cut = Render<ProgrammpunktSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PunktId, Gelaendespiel));
        cut.WaitForAssertion(() => Assert.Contains("Seile", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Eintrag(IRenderedComponent<ProgrammpunktSeite> cut, string bezeichnung) =>
        cut.FindAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == bezeichnung);

    [Fact]
    public void Material_mit_Reservierung_zeigt_Aus_dem_Inventar_und_den_Konflikt_ohne_Reservierung_die_Auswahl()
    {
        Programmpunkt(InventarMitFahnen());

        var cut = PunktRendern();

        var fahnen = Eintrag(cut, "Fahnen");
        Assert.Contains("Aus dem Inventar: Fahnen", fahnen.TextContent, StringComparison.Ordinal);
        Assert.Contains(Anzeige.Konflikt(["Kinderbibeltage 2027"]), fahnen.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(fahnen.QuerySelectorAll("button"), b => b.TextContent == "Reservieren");
        var seile = Eintrag(cut, "Seile");
        Assert.Equal("Aus dem Inventar", seile.QuerySelector("fieldset legend")!.TextContent);
        Assert.Equal(["Fahnen (8 Stück)", "Großzelt (1 Stück)"], seile.QuerySelectorAll("fieldset label").Select(l => l.TextContent.Trim()));
        Assert.NotNull(seile.QuerySelectorAll("button").Single(b => b.TextContent == "Reservieren").GetAttribute("disabled"));
    }

    [Fact]
    public void Reservieren_am_Material_sendet_die_materialId_ohne_Zeitraum_und_laedt_Material_und_Inventar_neu()
    {
        Programmpunkt(InventarMitFahnen());
        _server.Bei(HttpMethod.Post, $"{FreizeitPfad}/{Grosszelt}/reservierungen", () =>
        {
            _server.Bei(HttpMethod.Get, FreizeitPfad, () => Json(InventarMitFahnen(seileReserviert: true)));
            return Json(Reservierung(ResZelt, Freizeit, "Zeltlager Sommer 2027", "2027-08-02", "2027-08-13", MatSeile), HttpStatusCode.Created);
        });
        var cut = PunktRendern();

        Eintrag(cut, "Seile").QuerySelectorAll("input[type=radio]").Single(i => i.GetAttribute("value") == Grosszelt.ToString()).Change(Grosszelt.ToString());
        Eintrag(cut, "Seile").QuerySelectorAll("button").Single(b => b.TextContent == "Reservieren").Click();

        cut.WaitForAssertion(() => Assert.Contains("Aus dem Inventar: Großzelt", Eintrag(cut, "Seile").TextContent, StringComparison.Ordinal));
        var koerper = _server.Anfragen.Single(a => a.Methode == HttpMethod.Post).Koerper!;
        Assert.Contains($"\"materialId\":\"{MatSeile}\"", koerper, StringComparison.Ordinal);
        Assert.Contains("\"von\":null,\"bis\":null", koerper, StringComparison.Ordinal);
        Assert.True(_server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == $"/api/freizeiten/{Freizeit}/programm/material") >= 2);
    }

    [Fact]
    public void Abgeschlossen_zeigt_am_Material_die_Reservierung_aber_keine_Auswahl_aus_dem_Inventar()
    {
        Programmpunkt(InventarMitFahnen(), phase: "Gelaufen");

        var cut = PunktRendern();

        Assert.Contains("Aus dem Inventar: Fahnen", Eintrag(cut, "Fahnen").TextContent, StringComparison.Ordinal);
        var seile = Eintrag(cut, "Seile");
        Assert.Null(seile.QuerySelector("fieldset"));
        Assert.DoesNotContain(seile.QuerySelectorAll("button"), b => b.TextContent == "Reservieren");
    }

    [Fact]
    public void Ohne_ladbares_Inventar_zeigt_das_Material_keinen_Abschnitt_aus_dem_Inventar()
    {
        Programmpunkt(new { });
        _server.Bei(HttpMethod.Get, FreizeitPfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = PunktRendern();

        Assert.DoesNotContain("Aus dem Inventar", cut.Markup, StringComparison.Ordinal);
    }
}
