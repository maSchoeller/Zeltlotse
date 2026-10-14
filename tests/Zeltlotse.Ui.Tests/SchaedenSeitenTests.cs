using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Inventar;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using WuenscheSeite = Zeltlotse.App.Features.Inventar.Anschaffungswuensche;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 3-4 in der Web-App mit nachgebildetem Server: der Client und die Texte, die Seite „Schäden &amp; Verluste“ der Freizeit (Eintragen mit
/// Art und Feldfehler, Liste mit Autosave, Entnahme mit dem Text aus dem Vorfall-Protokoll, Entfernen, leer, abgeschlossen), der Knopf auf der
/// Startseite, die Seite „Anschaffungswünsche“ der Organisation (Org-Admin erledigt, Org-Überblick liest nur, leer, nicht verfügbar) und die Card
/// der Organisation.
/// </summary>
public sealed class SchaedenSeitenTests : BunitContext
{
    private static readonly Guid OrgId = Guid.Parse("0199f900-0000-7000-8000-0000000000c1");
    private static readonly Guid Freizeit = Guid.Parse("0199f900-0000-7000-8000-0000000001d1");
    private static readonly Guid Kbt = Guid.Parse("0199f900-0000-7000-8000-0000000001d2");
    private static readonly Guid Zelt = Guid.Parse("0199f900-0000-7000-8000-0000000002a1");
    private static readonly Guid Lampe = Guid.Parse("0199f900-0000-7000-8000-0000000002a2");
    private static readonly Guid Kasten = Guid.Parse("0199f900-0000-7000-8000-0000000002a3");
    private static readonly Guid Bibel = Guid.Parse("0199f900-0000-7000-8000-0000000002a4");
    private static readonly Guid Vorfall = Guid.Parse("0199f900-0000-7000-8000-0000000003b1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public SchaedenSeitenTests()
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
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero)));
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string Pfad => $"/api/freizeiten/{Freizeit}/inventar/schaeden";

    private static string WuenschePfad => $"/api/organisationen/{OrgId}/inventar/anschaffungswuensche";

    private static string VorfaellePfad => $"/api/freizeiten/{Freizeit}/sicherheit/vorfaelle";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Eintrag(Guid id, string art, string bezeichnung, string erfasstAm, Guid? vorfall = null) =>
        new { id, version = 4u, art, bezeichnung, erfasstAm, vorfallEintragId = vorfall };

    private static object[] Vorgabe =>
    [
        Eintrag(Kasten, "Entnahme", "Erste-Hilfe-Kasten", "2027-08-05T10:00:00+00:00", Vorfall),
        Eintrag(Lampe, "Verlust", "Taschenlampe", "2027-08-04T18:00:00+00:00"),
        Eintrag(Zelt, "Schaden", "Großzelt: Stange verbogen", "2027-08-03T09:00:00+00:00"),
    ];

    private void Kontext(string phase = "Laeuft") =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>(),
        }));

    private void Vorfaelle(string? text = "2 Pflaster, 1 sterile Kompresse") =>
        _server.Bei(HttpMethod.Get, VorfaellePfad, () => Json(new[]
        {
            new
            {
                id = Vorfall, personId = Guid.NewGuid(), vorname = "Mia", nachname = "Probst", art = "Entnahme", text, erfasstAm = "2027-08-05T10:00:00+00:00",
                verfasserVorname = "Lea", verfasserNachname = "Feuerstein",
            },
        }));

    private void Organisation(params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" }, erlaubt }));

    private static object[] WuenscheVorgabe =>
    [
        new { freizeitId = Freizeit, name = "Zeltlager Sommer 2027", eintraege = Vorgabe },
        new { freizeitId = Kbt, name = "Kinderbibeltage 2027", eintraege = new[] { Eintrag(Bibel, "Verlust", "Bibel", "2027-08-11T12:00:00+00:00") } },
    ];

    private IRenderedComponent<SchaedenVerluste> FreizeitRendern()
    {
        var cut = Render<SchaedenVerluste>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<WuenscheSeite> WuenscheRendern()
    {
        var cut = Render<WuenscheSeite>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Card<T>(IRenderedComponent<T> cut, string titel)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == titel);

    private static AngleSharp.Dom.IElement Zeile(IRenderedComponent<SchaedenVerluste> cut, string titel) =>
        Card(cut, "Liste").QuerySelectorAll("li").Single(l => l.QuerySelector("h3")!.TextContent == titel);

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_403_404_heissen_nicht_verfuegbar_und_Aktionen_ordnen_Antworten_zu()
    {
        _server.BeiDerReihe(HttpMethod.Get, Pfad, () => Json(Vorgabe), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.BeiDerReihe(HttpMethod.Get, WuenschePfad, () => Json(WuenscheVorgabe), () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.BeiDerReihe(
            HttpMethod.Post, Pfad,
            () => Json(Eintrag(Lampe, "Verlust", "Taschenlampe", "2027-08-04T18:00:00+00:00"), HttpStatusCode.Created),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"art":["Art fehlt."]}}""", "application/problem+json"));
        _server.BeiDerReihe(HttpMethod.Delete, $"{Pfad}/{Zelt}", () => new HttpResponseMessage(HttpStatusCode.NoContent), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.BeiDerReihe(HttpMethod.Delete, $"{WuenschePfad}/{Zelt}", () => new HttpResponseMessage(HttpStatusCode.NoContent), () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = Services.GetRequiredService<InventarApiClient>();

        var liste = await client.SchaedenAsync(Freizeit, Abbruch);
        Assert.Equal((SchadenArt.Entnahme, (Guid?)Vorfall), (liste![0].Art, liste[0].VorfallEintragId));
        Assert.Null(await client.SchaedenAsync(Freizeit, Abbruch));
        var wuensche = await client.AnschaffungswuenscheAsync(OrgId, Abbruch);
        Assert.Equal(["Zeltlager Sommer 2027", "Kinderbibeltage 2027"], wuensche!.Select(w => w.Name));
        Assert.Null(await client.AnschaffungswuenscheAsync(OrgId, Abbruch));
        Assert.Equal(Lampe, (await client.SchadenEintragenAsync(Freizeit, new SchadenNeu(SchadenArt.Verlust, "Taschenlampe"), Abbruch)).Eintrag!.Id);
        Assert.Equal("Art fehlt.", (await client.SchadenEintragenAsync(Freizeit, new SchadenNeu(null, "x"), Abbruch)).Feldfehler["art"]);
        Assert.Equal(InventarAktionErgebnis.Erfolg, await client.SchadenEntfernenAsync(Freizeit, Zelt, Abbruch));
        Assert.Equal(InventarAktionErgebnis.Fehler, await client.SchadenEntfernenAsync(Freizeit, Zelt, Abbruch));
        Assert.Equal(InventarAktionErgebnis.Erfolg, await client.ErledigtAsync(OrgId, Zelt, Abbruch));
        Assert.Equal(InventarAktionErgebnis.Fehler, await client.ErledigtAsync(OrgId, Zelt, Abbruch));
        Assert.Equal($"{Pfad}/{Zelt}", InventarApiClient.SchadenPfad(Freizeit, Zelt));
        var koerper = _server.Anfragen.First(a => a.Methode == HttpMethod.Post).Koerper!;
        Assert.Contains("\"art\":\"Verlust\"", koerper, StringComparison.Ordinal);
    }

    [Fact]
    public void Texte_der_Arten()
    {
        Assert.Equal(["Schaden", "Verlust", "Erste-Hilfe-Entnahme"], Enum.GetValues<SchadenArt>().Select(Anzeige.SchadenArt));
    }

    // ---------- Schäden & Verluste der Freizeit ----------

    [Fact]
    public void Freizeit_zeigt_Dachzeile_Formular_mit_Vorgabe_Schaden_und_die_Liste_mit_Autosave_und_Text_aus_dem_Vorfall_Protokoll()
    {
        Kontext();
        Vorfaelle();
        _server.Bei(HttpMethod.Get, Pfad, () => Json(Vorgabe));

        var cut = FreizeitRendern();

        Assert.Equal(("Ablage", "Schäden & Verluste"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var eintragen = Card(cut, "Eintragen");
        Assert.Equal(["Schaden", "Verlust", "Erste-Hilfe-Entnahme"], eintragen.QuerySelectorAll("label.elkw-choice").Select(l => l.TextContent.Trim()));
        Assert.Equal("Schaden", eintragen.QuerySelectorAll("input[type=radio]").Single(r => r.HasAttribute("checked")).GetAttribute("value"));
        Assert.NotNull(eintragen.QuerySelector("input#schaden-bezeichnung"));
        Assert.Equal("Was?", eintragen.QuerySelector("label[for=schaden-bezeichnung]")!.TextContent.Trim());
        Assert.Equal(
            ["Erste-Hilfe-Entnahme · Erste-Hilfe-Kasten", "Verlust · Taschenlampe", "Schaden · Großzelt: Stange verbogen"],
            Card(cut, "Liste").QuerySelectorAll("h3").Select(h => h.TextContent));
        var kasten = Zeile(cut, "Erste-Hilfe-Entnahme · Erste-Hilfe-Kasten");
        Assert.Contains("Laut Vorfall-Protokoll: 2 Pflaster, 1 sterile Kompresse", kasten.TextContent, StringComparison.Ordinal);
        Assert.Contains("Eingetragen am 05.08.2027", kasten.TextContent, StringComparison.Ordinal);
        Assert.Equal("Erste-Hilfe-Kasten", kasten.QuerySelector($"input#schaden-{Kasten}-bezeichnung")!.GetAttribute("value"));
        Assert.DoesNotContain("Laut Vorfall-Protokoll", Zeile(cut, "Verlust · Taschenlampe").TextContent, StringComparison.Ordinal);
        Assert.Contains(Zeile(cut, "Verlust · Taschenlampe").QuerySelectorAll("button"), b => b.TextContent == "Entfernen");
        Assert.DoesNotContain("Anschaffungswünsche an die Organisation.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Text_im_Vorfall_Protokoll_entfaellt_die_Zeile_ohne_Vorfall_Eintrag_wird_das_Protokoll_nicht_geladen()
    {
        Kontext();
        Vorfaelle(text: null);
        _server.Bei(HttpMethod.Get, Pfad, () => Json(Vorgabe));
        var cut = FreizeitRendern();
        Assert.DoesNotContain("Laut Vorfall-Protokoll", cut.Markup, StringComparison.Ordinal);

        _server.Bei(HttpMethod.Get, VorfaellePfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.DoesNotContain("Laut Vorfall-Protokoll", FreizeitRendern().Markup, StringComparison.Ordinal);

        var vorher = _server.Anfragen.Count(a => a.Pfad == VorfaellePfad);
        _server.Bei(HttpMethod.Get, Pfad, () => Json(new[] { Eintrag(Lampe, "Verlust", "Taschenlampe", "2027-08-04T18:00:00+00:00") }));
        FreizeitRendern();
        Assert.Equal(vorher, _server.Anfragen.Count(a => a.Pfad == VorfaellePfad));
    }

    [Fact]
    public void Eintragen_sendet_Art_und_Bezeichnung_zeigt_Feldfehler_und_laedt_danach_neu()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, Pfad, () => Json(Array.Empty<object>()), () => Json(new[] { Eintrag(Lampe, "Verlust", "Taschenlampe", "2027-08-04T18:00:00+00:00") }));
        _server.BeiDerReihe(
            HttpMethod.Post, Pfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"bezeichnung":["Bezeichnung fehlt."]}}""", "application/problem+json"),
            () => Json(Eintrag(Lampe, "Verlust", "Taschenlampe", "2027-08-04T18:00:00+00:00"), HttpStatusCode.Created));
        var cut = FreizeitRendern();
        Assert.Contains("Noch nichts eingetragen.", cut.Markup, StringComparison.Ordinal);

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Fehler: Bezeichnung fehlt.", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("input[type=radio]").Single(r => r.GetAttribute("value") == "Verlust").Change("Verlust");
        cut.Find("#schaden-bezeichnung").Input("Taschenlampe");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(Zeile(cut, "Verlust · Taschenlampe")));
        var koerper = _server.Anfragen.Where(a => a.Methode == HttpMethod.Post).Select(a => JsonDocument.Parse(a.Koerper!).RootElement).ToList();
        Assert.Equal(("Schaden", JsonValueKind.Null), (koerper[0].GetProperty("art").GetString(), koerper[0].GetProperty("bezeichnung").ValueKind));
        Assert.Equal(("Verlust", "Taschenlampe"), (koerper[1].GetProperty("art").GetString(), koerper[1].GetProperty("bezeichnung").GetString()));
        Assert.Equal("", cut.Find("#schaden-bezeichnung").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Bezeichnung_speichert_per_Autosave_und_die_Ueberschrift_folgt()
    {
        Kontext();
        Vorfaelle();
        _server.Bei(HttpMethod.Get, Pfad, () => Json(Vorgabe));
        _server.Bei(HttpMethod.Patch, $"{Pfad}/{Lampe}", () => Json(new { version = 5u, bezeichnung = "Stirnlampe" }));
        var cut = FreizeitRendern();

        var eingabe = cut.Find($"#schaden-{Lampe}-bezeichnung");
        eingabe.Focus();
        eingabe.Input("Stirnlampe");
        cut.Find($"#schaden-{Lampe}-bezeichnung").Blur();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Erste-Hilfe-Entnahme · Erste-Hilfe-Kasten", "Verlust · Stirnlampe", "Schaden · Großzelt: Stange verbogen"],
            Card(cut, "Liste").QuerySelectorAll("h3").Select(h => h.TextContent)));
        var koerper = JsonDocument.Parse(Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Patch).Koerper!).RootElement;
        Assert.Equal(("Stirnlampe", 4u, "Taschenlampe"), (
            koerper.GetProperty("bezeichnung").GetString(), koerper.GetProperty("version").GetUInt32(), koerper.GetProperty("alterWert").GetString()));
    }

    [Fact]
    public void Entfernen_fragt_inline_nach_und_nimmt_den_Eintrag_aus_der_Liste()
    {
        Kontext();
        Vorfaelle();
        _server.Bei(HttpMethod.Get, Pfad, () => Json(Vorgabe));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/{Lampe}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = FreizeitRendern();

        Zeile(cut, "Verlust · Taschenlampe").QuerySelectorAll("button").Single(b => b.TextContent == "Entfernen").Click();
        Assert.Contains("„Taschenlampe“ entfernen?", Zeile(cut, "Verlust · Taschenlampe").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
        Zeile(cut, "Verlust · Taschenlampe").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, Card(cut, "Liste").QuerySelectorAll("h3").Length));
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == $"{Pfad}/{Lampe}");
    }

    [Fact]
    public void Abgeschlossen_zeigt_den_Hinweis_Anschaffungswuensche_und_bleibt_beschreibbar_nicht_verfuegbar_bei_404()
    {
        Kontext("Gelaufen");
        Vorfaelle();
        _server.Bei(HttpMethod.Get, Pfad, () => Json(Vorgabe));
        var cut = FreizeitRendern();

        Assert.Contains("Die Freizeit ist abgeschlossen. Die Einträge sind jetzt Anschaffungswünsche an die Organisation.", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(Card(cut, "Eintragen").QuerySelector("form"));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Entfernen");

        _server.Bei(HttpMethod.Get, Pfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Contains("Diese Seite ist nicht verfügbar.", FreizeitRendern().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Startseite_der_Freizeit_hat_den_Knopf_nach_Inventar()
    {
        Kontext("InVorbereitung");
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Contains("Schäden & Verluste", cut.Markup.Replace("&amp;", "&", StringComparison.Ordinal), StringComparison.Ordinal));
        var links = cut.FindAll("a").Select(a => (Text: a.TextContent.Trim(), Href: a.GetAttribute("href"))).ToList();
        var inventar = links.FindIndex(l => l.Text == "Inventar");
        Assert.Equal(("Schäden & Verluste", $"freizeiten/{Freizeit}/schaeden"), links[inventar + 1]);
    }

    // ---------- Anschaffungswünsche der Organisation ----------

    [Fact]
    public void Org_Admin_sieht_je_Freizeit_eine_Card_mit_Art_Bezeichnung_Datum_und_Erledigt()
    {
        Organisation(Aktionsschluessel.InventarSehen, Aktionsschluessel.InventarVerwalten);
        _server.Bei(HttpMethod.Get, WuenschePfad, () => Json(WuenscheVorgabe));

        var cut = WuenscheRendern();

        Assert.Equal(("Organisation", "Anschaffungswünsche"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        Assert.Equal(["Zeltlager Sommer 2027", "Kinderbibeltage 2027"], cut.FindAll("section.elkw-card h2").Select(h => h.TextContent));
        var zeilen = Card(cut, "Zeltlager Sommer 2027").QuerySelectorAll("li").ToList();
        Assert.StartsWith("Erste-Hilfe-Entnahme · Erste-Hilfe-Kasten · 05.08.2027", zeilen[0].TextContent.Trim(), StringComparison.Ordinal);
        Assert.StartsWith("Schaden · Großzelt: Stange verbogen · 03.08.2027", zeilen[2].TextContent.Trim(), StringComparison.Ordinal);
        Assert.All(zeilen, z => Assert.Contains(z.QuerySelectorAll("button"), b => b.TextContent == "Erledigt"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("vorfaelle", StringComparison.Ordinal));
    }

    [Fact]
    public void Org_Ueberblick_sieht_die_Eintraege_ohne_Erledigt_und_ohne_Vorfall_Text()
    {
        Organisation(Aktionsschluessel.InventarSehen);
        _server.Bei(HttpMethod.Get, WuenschePfad, () => Json(WuenscheVorgabe));

        var cut = WuenscheRendern();

        Assert.Equal(4, cut.FindAll("li").Count);
        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("Pflaster", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Laut Vorfall-Protokoll", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Erledigt_fragt_inline_nach_und_nimmt_den_Eintrag_und_eine_leere_Freizeit_heraus()
    {
        Organisation(Aktionsschluessel.InventarSehen, Aktionsschluessel.InventarVerwalten);
        _server.Bei(HttpMethod.Get, WuenschePfad, () => Json(WuenscheVorgabe));
        _server.Bei(HttpMethod.Delete, $"{WuenschePfad}/{Bibel}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Delete, $"{WuenschePfad}/{Lampe}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = WuenscheRendern();

        void Erledigen(string freizeit, string bezeichnung)
        {
            AngleSharp.Dom.IElement Li() => Card(cut, freizeit).QuerySelectorAll("li").Single(l => l.TextContent.Contains(bezeichnung, StringComparison.Ordinal));
            Li().QuerySelectorAll("button").Single(b => b.TextContent == "Erledigt").Click();
            Assert.Contains($"„{bezeichnung}“ als erledigt entfernen?", Li().TextContent, StringComparison.Ordinal);
            Li().QuerySelectorAll("button").Single(b => b.TextContent == "Ja, erledigt").Click();
        }

        Erledigen("Zeltlager Sommer 2027", "Taschenlampe");
        cut.WaitForAssertion(() => Assert.Equal(2, Card(cut, "Zeltlager Sommer 2027").QuerySelectorAll("li").Length));
        Erledigen("Kinderbibeltage 2027", "Bibel");
        cut.WaitForAssertion(() => Assert.Equal(["Zeltlager Sommer 2027"], cut.FindAll("section.elkw-card h2").Select(h => h.TextContent)));
    }

    [Fact]
    public async Task Zwei_Erledigt_auf_derselben_Card_gleichzeitig_nehmen_beide_Eintraege_heraus()
    {
        Organisation(Aktionsschluessel.InventarSehen, Aktionsschluessel.InventarVerwalten);
        _server.Bei(HttpMethod.Get, WuenschePfad, () => Json(WuenscheVorgabe));
        var erste = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Bei(a => a.Method == HttpMethod.Delete && a.RequestUri!.AbsolutePath == $"{WuenschePfad}/{Lampe}", _ => erste.Task);
        _server.Bei(HttpMethod.Delete, $"{WuenschePfad}/{Zelt}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = WuenscheRendern();

        void Bestaetigen(string bezeichnung)
        {
            AngleSharp.Dom.IElement Li() => Card(cut, "Zeltlager Sommer 2027").QuerySelectorAll("li").Single(l => l.TextContent.Contains(bezeichnung, StringComparison.Ordinal));
            Li().QuerySelectorAll("button").Single(b => b.TextContent == "Erledigt").Click();
            Li().QuerySelectorAll("button").Single(b => b.TextContent == "Ja, erledigt").Click();
        }

        // Das erste „Erledigt“ hängt noch am Server, während das zweite schon fertig ist.
        Bestaetigen("Taschenlampe");
        Bestaetigen("Großzelt");
        cut.WaitForAssertion(() => Assert.Equal(2, Card(cut, "Zeltlager Sommer 2027").QuerySelectorAll("li").Length));
        await cut.InvokeAsync(() => erste.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        cut.WaitForAssertion(() => Assert.Equal(
            ["Erste-Hilfe-Kasten"],
            Card(cut, "Zeltlager Sommer 2027").QuerySelectorAll("li").Select(l => l.TextContent.Split(" · ")[1])));
        Assert.DoesNotContain("Fehler", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Wuensche_der_Leer_Zustand_ohne_inventar_sehen_nicht_verfuegbar()
    {
        Organisation(Aktionsschluessel.InventarSehen);
        _server.Bei(HttpMethod.Get, WuenschePfad, () => Json(Array.Empty<object>()));
        Assert.Contains(
            "Noch keine Anschaffungswünsche. Nach dem Abschluss einer Freizeit stehen ihre Schäden und Verluste hier.", WuenscheRendern().Markup, StringComparison.Ordinal);

        Organisation(Aktionsschluessel.OrgRollenVergeben);
        Assert.Contains("Diese Seite ist nicht verfügbar.", WuenscheRendern().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Organisation_zeigt_die_Card_Anschaffungswuensche_nur_mit_inventar_sehen()
    {
        Organisation(Aktionsschluessel.InventarSehen);
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.Contains("Anschaffungswünsche öffnen", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(
            $"organisationen/{OrgId}/anschaffungswuensche",
            cut.FindAll("a").Single(a => a.TextContent.Contains("Anschaffungswünsche öffnen", StringComparison.Ordinal)).GetAttribute("href"));

        Organisation(Aktionsschluessel.OrgRollenVergeben);
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/rollen", () => Json(new { personen = Array.Empty<object>(), einladungen = Array.Empty<object>() }));
        var ohne = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        ohne.WaitForAssertion(() => Assert.Contains("Jugendwerk Musterstadt", ohne.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Anschaffungswünsche öffnen", ohne.Markup, StringComparison.Ordinal);
    }
}
