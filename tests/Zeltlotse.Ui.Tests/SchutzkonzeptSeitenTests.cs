using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Autosave;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Transport;
using KonzeptSeite = Zeltlotse.App.Features.Sicherheit.Schutzkonzept;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-9 in der Web-App mit nachgebildetem Server: die Seite „Schutzkonzept“ (leer mit und ohne Einstellungsrecht, mit Dokument, Bestätigen
/// mit Erfolg und Konflikt, schon gelesen, abgeschlossen, nicht verfügbar), die Card und der Knopf auf der Startseite, die Card in den
/// Einstellungen, die Autosave-Auswahlgruppe mit leerer Option und der Client.
/// </summary>
public sealed class SchutzkonzeptSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f900-0000-7000-8000-0000000000d1");
    private static readonly Guid Dokument = Guid.Parse("0199f900-0000-7000-8000-0000000000a1");
    private static readonly Guid Packliste = Guid.Parse("0199f900-0000-7000-8000-0000000000a2");
    private static readonly Guid Jonas = Guid.Parse("0199f900-0000-7000-8000-0000000000e1");
    private static readonly Guid Miriam = Guid.Parse("0199f900-0000-7000-8000-0000000000e2");
    private static readonly Guid Lea = Guid.Parse("0199f900-0000-7000-8000-0000000000e3");
    private static readonly DateTimeOffset Gelesen = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public SchutzkonzeptSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        // Story 3-5: Die Startseite lädt die Aufgaben mit; ohne Aufgaben weder Zeile noch eigene Aufgaben.
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/programm/aufgaben", StringComparison.Ordinal),
            _ => Task.FromResult(NachgebildeterHandler.Json(HttpStatusCode.OK, "[]")));
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

    private static string KonzeptPfad => $"/api/freizeiten/{Freizeit}/sicherheit/schutzkonzept";

    private static string GelesenPfad => $"{KonzeptPfad}/gelesen";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Konzept(DateTimeOffset? selbst = null, bool mitDokument = true, bool leaGelesen = false) => new
    {
        dokumentId = mitDokument ? Dokument : (Guid?)null,
        titel = mitDokument ? "Schutzkonzept Jugendwerk Musterstadt" : null,
        dateiart = mitDokument ? "PDF" : null,
        selbstGelesenAm = selbst,
        team = new object[]
        {
            new { personId = Lea, vorname = "Lea", nachname = "Feuerstein", leitung = false, gelesenAm = leaGelesen ? Gelesen : (DateTimeOffset?)null },
            new { personId = Jonas, vorname = "Jonas", nachname = "Hüttner", leitung = true, gelesenAm = mitDokument ? Gelesen : (DateTimeOffset?)null },
            new { personId = Miriam, vorname = "Miriam", nachname = "Zeltner", leitung = true, gelesenAm = (DateTimeOffset?)null },
        },
        version = 5u,
    };

    private void Antworten(string phase = "InVorbereitung", string[]? erlaubt = null, Func<HttpResponseMessage>? konzept = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(),
            erlaubt = erlaubt ?? [Aktionsschluessel.FreizeitpaesseSehen],
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase, freigabeBis = "2027-09-12",
            leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner" } },
        }));
        _server.Bei(HttpMethod.Get, KonzeptPfad, konzept ?? (() => Json(Konzept())));
    }

    private IRenderedComponent<KonzeptSeite> Seite()
    {
        var cut = Render<KonzeptSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement? Card(IRenderedComponent<KonzeptSeite> cut, string titel) =>
        cut.FindAll(".elkw-card").SingleOrDefault(c => c.QuerySelector(".elkw-card__title")?.TextContent == titel);

    // ---------- Client ----------

    [Fact]
    public async Task Client_liest_das_Schutzkonzept_403_404_heissen_nicht_verfuegbar_und_Bestaetigen_unterscheidet_Konflikt_und_Fehler()
    {
        Antworten();
        var client = Services.GetRequiredService<SicherheitApiClient>();

        var konzept = await client.SchutzkonzeptLadenAsync(Freizeit, Abbruch);

        Assert.Equal((Dokument, "Schutzkonzept Jugendwerk Musterstadt", "PDF", 5u), (konzept!.DokumentId, konzept.Titel, konzept.Dateiart, konzept.Version));
        Assert.Equal([Lea, Jonas, Miriam], konzept.Team.Select(l => l.PersonId));
        Assert.Equal((true, Gelesen), (konzept.Team[1].Leitung, konzept.Team[1].GelesenAm!.Value));
        Assert.Equal(KonzeptPfad, SicherheitApiClient.SchutzkonzeptPfad(Freizeit));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, KonzeptPfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.SchutzkonzeptLadenAsync(Freizeit, Abbruch));
        }

        var antworten = new Func<HttpResponseMessage>[]
        {
            () => new HttpResponseMessage(HttpStatusCode.NoContent),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen),
            () => new HttpResponseMessage(HttpStatusCode.NotFound),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        _server.BeiDerReihe(HttpMethod.Post, GelesenPfad, antworten);
        var ergebnisse = new List<SchutzkonzeptGelesenErgebnis>();
        foreach (var _ in antworten)
        {
            ergebnisse.Add(await client.GelesenAsync(Freizeit, Dokument, Abbruch));
        }

        Assert.Equal(
            [
                SchutzkonzeptGelesenErgebnis.Erfolg, SchutzkonzeptGelesenErgebnis.Konflikt, SchutzkonzeptGelesenErgebnis.Fehler,
                SchutzkonzeptGelesenErgebnis.Fehler, SchutzkonzeptGelesenErgebnis.Fehler,
            ],
            ergebnisse);
        Assert.Equal($$"""{"dokumentId":"{{Dokument}}"}""", _server.Anfragen.Last(a => a.Methode == HttpMethod.Post).Koerper);
    }

    // ---------- Seite „Schutzkonzept“ ----------

    [Fact]
    public void Mit_Dokument_zeigt_Link_auf_die_Datei_Knopf_Balken_und_die_Team_Liste()
    {
        Antworten();

        var cut = Seite();

        Assert.Equal("Sicherheit", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Schutzkonzept", cut.Find("h1").TextContent);
        var dokument = Card(cut, "Dokument")!;
        var link = dokument.QuerySelector("a")!;
        Assert.Equal(
            ("Schutzkonzept Jugendwerk Musterstadt (PDF)", $"/api/freizeiten/{Freizeit}/dokumente/{Dokument}/datei"),
            (link.TextContent, link.GetAttribute("href")));
        Assert.Contains("Nach dem Lesen hier bestätigen.", dokument.TextContent, StringComparison.Ordinal);
        var knopf = dokument.QuerySelector("button")!;
        Assert.Equal("Als gelesen bestätigen", knopf.TextContent.Trim());
        // Primär: der Grundknopf ohne Abwandlung.
        Assert.DoesNotContain(knopf.ClassList, k => k is "elkw-btn--outline" or "elkw-btn--text");

        var team = Card(cut, "Team")!;
        Assert.Equal("1 von 3 gelesen", team.QuerySelector(".zl-balken .zl-zahl")!.TextContent);
        var personen = team.QuerySelectorAll(".elkw-people li").Select(l => (
            l.QuerySelector(".elkw-avatar")!.TextContent, l.QuerySelector("strong")!.TextContent, l.QuerySelector(".elkw-people__who span")!.TextContent)).ToList();
        Assert.Equal(
            [
                ("LF", "Lea Feuerstein", "Team · noch nicht gelesen"), ("JH", "Jonas Hüttner", "Leitung · gelesen am 12.10.2026"),
                ("MZ", "Miriam Zeltner", "Leitung · noch nicht gelesen"),
            ],
            personen);
    }

    [Fact]
    public void Bestaetigen_mit_Erfolg_meldet_es_und_laedt_neu()
    {
        Antworten();
        _server.BeiDerReihe(HttpMethod.Get, KonzeptPfad, () => Json(Konzept()), () => Json(Konzept(selbst: Gelesen, leaGelesen: true)));
        _server.Bei(HttpMethod.Post, GelesenPfad, () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Seite();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Als gelesen bestätigen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Als gelesen bestätigt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal($$"""{"dokumentId":"{{Dokument}}"}""", _server.Anfragen.Single(a => a.Methode == HttpMethod.Post && a.Pfad == GelesenPfad).Koerper);
        cut.WaitForAssertion(() => Assert.Contains("Gelesen am 12.10.2026.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Als gelesen bestätigen");
        Assert.Equal("2 von 3 gelesen", Card(cut, "Team")!.QuerySelector(".zl-zahl")!.TextContent);
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == KonzeptPfad));
    }

    [Fact]
    public void Bestaetigen_mit_Konflikt_meldet_den_Austausch_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, GelesenPfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = Seite();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Als gelesen bestätigen").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            "Das Schutzkonzept wurde inzwischen ausgetauscht oder entfernt.", cut.Markup, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == KonzeptPfad)));
    }

    [Fact]
    public void Scheitert_das_Bestaetigen_steht_die_Fehlermeldung_da()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, GelesenPfad, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = Seite();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Als gelesen bestätigen").Click();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Schon_gelesen_zeigt_Gelesen_am_und_keinen_Knopf()
    {
        Antworten(konzept: () => Json(Konzept(selbst: Gelesen)));

        var cut = Seite();

        var meldung = Card(cut, "Dokument")!.QuerySelector(".elkw-alert")!;
        Assert.Contains("elkw-alert--erfolg", meldung.ClassList);
        Assert.Contains("Gelesen am 12.10.2026.", meldung.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("Nach dem Lesen hier bestätigen.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void In_einer_abgeschlossenen_Freizeit_gibt_es_keinen_Knopf()
    {
        Antworten(phase: "Gelaufen");

        var cut = Seite();

        Assert.NotNull(Card(cut, "Dokument"));
        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("Nach dem Lesen hier bestätigen.", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(Card(cut, "Team"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ohne_Dokument_der_Leer_Zustand_mit_Link_in_die_Einstellungen_nur_mit_Einstellungsrecht(bool einstellungen)
    {
        Antworten(
            erlaubt: einstellungen ? [Aktionsschluessel.FreizeitEinstellungen] : [Aktionsschluessel.FreizeitpaesseSehen],
            konzept: () => Json(Konzept(mitDokument: false)));

        var cut = Seite();

        var leer = cut.Find(".elkw-empty");
        Assert.Contains("Noch kein Schutzkonzept angehängt.", leer.TextContent, StringComparison.Ordinal);
        var links = leer.QuerySelectorAll("a").Select(a => (a.TextContent.Trim(), a.GetAttribute("href"))).ToList();
        Assert.Equal(einstellungen ? [("In den Einstellungen anhängen", $"freizeiten/{Freizeit}/einstellungen#schutzkonzept")] : [], links);
        Assert.Null(Card(cut, "Dokument"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Ohne_Kontext_oder_ohne_Schutzkonzept_ist_die_Seite_nicht_verfuegbar(bool kontextFehlt, bool konzeptFehlt)
    {
        Antworten(konzept: konzeptFehlt ? () => new HttpResponseMessage(HttpStatusCode.NotFound) : null);
        if (kontextFehlt)
        {
            _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var cut = Render<KonzeptSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }

    // ---------- Startseite ----------

    private IRenderedComponent<FreizeitStart> Startseite()
    {
        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        return cut;
    }

    private static AngleSharp.Dom.IElement? HinweisCard(IRenderedComponent<FreizeitStart> cut) =>
        cut.FindAll(".elkw-card").SingleOrDefault(c => c.QuerySelector(".elkw-card__title")?.TextContent == "Schutzkonzept");

    [Theory]
    [InlineData("InVorbereitung", false)]
    [InlineData("Laeuft", true)]
    [InlineData("Nachbereitung", false)]
    public void Startseite_zeigt_die_Card_Schutzkonzept_solange_nicht_selbst_bestaetigt(string phase, bool leitung)
    {
        Antworten(phase: phase, erlaubt: leitung ? [Aktionsschluessel.FreizeitEinstellungen] : null);

        var cut = Startseite();

        cut.WaitForAssertion(() => Assert.NotNull(HinweisCard(cut)));
        var card = HinweisCard(cut)!;
        Assert.Contains("Das Schutzkonzept ist noch nicht als gelesen bestätigt.", card.TextContent, StringComparison.Ordinal);
        var lesen = card.QuerySelector("a")!;
        Assert.Equal(("Schutzkonzept lesen", $"freizeiten/{Freizeit}/schutzkonzept"), (lesen.TextContent.Trim(), lesen.GetAttribute("href")));
        Assert.Contains(cut.FindAll("a"), a => a.TextContent.Trim() == "Schutzkonzept" && a.GetAttribute("href") == $"freizeiten/{Freizeit}/schutzkonzept");
    }

    [Fact]
    public void Startseite_ohne_Card_wenn_bestaetigt_ohne_Dokument_abgeschlossen_oder_der_Abruf_scheitert_der_Knopf_bleibt()
    {
        var faelle = new (string Phase, Func<HttpResponseMessage> Antwort)[]
        {
            ("InVorbereitung", () => Json(Konzept(selbst: Gelesen))),
            ("InVorbereitung", () => Json(Konzept(mitDokument: false))),
            ("Gelaufen", () => Json(Konzept())),
            ("InVorbereitung", () => new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            ("InVorbereitung", () => new HttpResponseMessage(HttpStatusCode.NotFound)),
        };

        foreach (var (phase, antwort) in faelle)
        {
            Antworten(phase: phase, konzept: antwort);
            var cut = Startseite();

            cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("a"), a => a.TextContent.Trim() == "Schutzkonzept"));
            Assert.Null(HinweisCard(cut));
            Assert.DoesNotContain(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Die_Card_ist_kein_Warnhinweis_des_Cockpits()
    {
        Antworten(erlaubt: [Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/sicherheit/sicherheitskette", () => Json(new
        {
            wasTun = "x", leitungTelefon = "1", heimatName = (string?)null, heimatTelefon = "2", eingerichtet = true, version = 1u,
        }));

        var cut = Startseite();

        cut.WaitForAssertion(() => Assert.NotNull(HinweisCard(cut)));
        var warnungen = cut.FindAll(".elkw-card").Single(c => c.QuerySelector(".elkw-card__title")?.TextContent == "Warnhinweise");
        Assert.DoesNotContain("Schutzkonzept", warnungen.TextContent, StringComparison.Ordinal);
        Assert.Contains("Keine offenen Punkte.", warnungen.TextContent, StringComparison.Ordinal);
    }

    // ---------- Einstellungen ----------

    private void EinstellungenAntworten(string phase = "InVorbereitung")
    {
        Antworten(phase: phase, erlaubt: [Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/freizeitseite", () => Json(new { version = 3u, anmeldelinks = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/dokumente", () => Json(new
        {
            dokumente = new object[]
            {
                new
                {
                    id = Packliste, titel = "Packliste", sichtbarkeit = "ElternUndTeilnehmende", dateiart = "PDF", groesse = 10L,
                    hochgeladenAm = Gelesen, ausOrganisation = false, version = 1u,
                },
                new
                {
                    id = Dokument, titel = "Schutzkonzept Jugendwerk Musterstadt", sichtbarkeit = "Team", dateiart = "PDF", groesse = 10L,
                    hochgeladenAm = Gelesen, ausOrganisation = true, version = 1u,
                },
            },
        }));
    }

    private IRenderedComponent<Einstellungen> EinstellungenSeite()
    {
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("#schutzkonzept").SingleOrDefault()));
        return cut;
    }

    [Fact]
    public void Einstellungen_haben_die_Card_Schutzkonzept_nach_der_Sicherheitskette_mit_den_Dokumenten_als_Optionen()
    {
        EinstellungenAntworten();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/sicherheit/sicherheitskette", () => Json(new
        {
            wasTun = (string?)null, leitungTelefon = (string?)null, heimatName = (string?)null, heimatTelefon = (string?)null, eingerichtet = false, version = 0u,
        }));

        var cut = EinstellungenSeite();

        var card = cut.Find("#schutzkonzept");
        Assert.Equal("Schutzkonzept", card.QuerySelector(".elkw-card__title")!.TextContent);
        Assert.True(cut.Markup.IndexOf("id=\"sicherheitskette\"", StringComparison.Ordinal) < cut.Markup.IndexOf("id=\"schutzkonzept\"", StringComparison.Ordinal));
        Assert.Contains("Jedes Teammitglied bestätigt es als gelesen. Wird es ausgetauscht, bestätigen alle neu.", card.TextContent, StringComparison.Ordinal);
        Assert.Equal("Dokument", card.QuerySelector("legend")!.TextContent);
        var optionen = card.QuerySelectorAll("label.elkw-choice").Select(l => (l.TextContent.Trim(), l.QuerySelector("input")!)).ToList();
        Assert.Equal(["Kein Schutzkonzept", "Packliste", "Schutzkonzept Jugendwerk Musterstadt"], optionen.Select(o => o.Item1));
        Assert.All(optionen, o => Assert.Equal("radio", o.Item2.GetAttribute("type")));
        Assert.Equal([false, false, true], optionen.Select(o => o.Item2.HasAttribute("checked")));
        Assert.Contains("Neue Datei unter „Dokumente“ hochladen oder aus der Organisation einbinden.", card.TextContent, StringComparison.Ordinal);
        var links = card.QuerySelectorAll("a").Select(a => (a.TextContent.Trim(), a.GetAttribute("href"))).ToList();
        Assert.Contains(("Dokumente", $"freizeiten/{Freizeit}/dokumente"), links);
        Assert.Contains(("Wer hat gelesen?", $"freizeiten/{Freizeit}/schutzkonzept"), links);
    }

    [Fact]
    public void Einstellungen_waehlen_ein_Dokument_und_Kein_Schutzkonzept_per_PATCH()
    {
        EinstellungenAntworten();
        _server.BeiDerReihe(
            HttpMethod.Patch,
            KonzeptPfad,
            () => Json(new { version = 6u, dokumentId = Packliste }),
            () => Json(new { version = 7u, dokumentId = (Guid?)null }));
        var cut = EinstellungenSeite();

        cut.Find($"#schutzkonzept input[value='{Packliste}']").Change(true);

        cut.WaitForAssertion(() => Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Patch));
        Assert.Equal($$"""{"version":5,"alterWert":"{{Dokument}}","dokumentId":"{{Packliste}}"}""", _server.Anfragen.Last(a => a.Methode == HttpMethod.Patch).Koerper);

        cut.Find("#schutzkonzept input[value='']").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Patch)));
        Assert.Equal($$"""{"version":6,"alterWert":"{{Packliste}}","dokumentId":null}""", _server.Anfragen.Last(a => a.Methode == HttpMethod.Patch).Koerper);
        cut.WaitForAssertion(() => Assert.True(cut.Find("#schutzkonzept input[value='']").HasAttribute("checked")));
    }

    [Fact]
    public void Einstellungen_ohne_Schutzkonzept_haben_Kein_Schutzkonzept_gewaehlt()
    {
        EinstellungenAntworten();
        _server.Bei(HttpMethod.Get, KonzeptPfad, () => Json(Konzept(mitDokument: false)));

        var cut = EinstellungenSeite();

        Assert.True(cut.Find("#schutzkonzept input[value='']").HasAttribute("checked"));
        Assert.Single(cut.FindAll("#schutzkonzept input[checked]"));
    }

    [Fact]
    public void Einstellungen_in_einer_abgeschlossenen_Freizeit_melden_beim_Waehlen_nur_lesbar()
    {
        EinstellungenAntworten(phase: "Gelaufen");
        _server.Bei(HttpMethod.Patch, KonzeptPfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));
        var cut = EinstellungenSeite();

        cut.Find($"#schutzkonzept input[value='{Packliste}']").Change(true);

        cut.WaitForAssertion(() => Assert.Contains(
            "Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Find("#schutzkonzept").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("Inzwischen geändert", cut.Find("#schutzkonzept").TextContent, StringComparison.Ordinal);
    }

    // ---------- Autosave-Auswahlgruppe ----------

    [Fact]
    public void Autosave_Auswahlgruppe_mit_Optionsknoepfen_waehlt_die_leere_Option_solange_nichts_gewaehlt_ist()
    {
        IReadOnlyList<AuswahlOption> optionen = [new("", "Keins"), new("a", "A")];
        var cut = Render<AutosaveAuswahlgruppe>(ps => ps
            .Add(p => p.Legende, "Dokument")
            .Add(p => p.Feldname, "wert")
            .Add(p => p.Pfad, "/api/x")
            .Add(p => p.Optionen, optionen)
            .Add(p => p.Mehrfach, false)
            .Add(p => p.Wert, (string?)null)
            .Add(p => p.Version, 1u));

        Assert.True(cut.Find("input[value='']").HasAttribute("checked"));
        Assert.False(cut.Find("input[value=a]").HasAttribute("checked"));

        // Die leere Option erneut wählen sendet nichts.
        cut.Find("input[value='']").Change(true);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Patch);
    }
}
