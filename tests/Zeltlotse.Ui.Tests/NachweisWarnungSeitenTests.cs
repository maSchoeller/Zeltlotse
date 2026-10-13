using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Personen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using FreizeitenArt = Zeltlotse.Api.Contracts.Freizeiten.NachweisArt;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 1-6 in der Web-App mit nachgebildetem Server: Warnhinweise und „Erinnern“ in der Prüfliste der Freizeit (Erfolg,
/// <c>409</c>, Warnhinweise nicht ladbar), keine Warnhinweise auf der Org-Route, Meldung auf der Freizeit-Startseite (Einzahl,
/// Mehrzahl, ohne Warnung, Abruf gescheitert).
/// </summary>
public sealed class NachweisWarnungSeitenTests : BunitContext
{
    private static readonly Guid Jonas = Guid.Parse("0199f100-0000-7000-8000-0000000000c1");
    private static readonly Guid Tobias = Guid.Parse("0199f100-0000-7000-8000-0000000000c2");
    private static readonly Guid Lea = Guid.Parse("0199f100-0000-7000-8000-0000000000c3");
    private static readonly Guid Freizeit = Guid.Parse("0199f100-0000-7000-8000-0000000000d1");
    private static readonly Guid Org = Guid.Parse("0199f100-0000-7000-8000-0000000000e1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public NachweisWarnungSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static string ListenPfad => $"/api/freizeiten/{Freizeit}/personen/nachweise";

    private static string WarnPfad => $"/api/freizeiten/{Freizeit}/nachweis-warnungen";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Person(Guid id, string vorname, string nachname, string? erinnertAm = null) => new
    {
        personId = id,
        vorname,
        nachname,
        anfragbar = false,
        einsicht = new { id = Guid.CreateVersion7(), status = "angefragt", angefragtAm = "2026-10-01T08:00:00+00:00", freigegebenBis = (string?)null, abgeschlossenAm = (string?)null, pruefbar = true },
        erinnertAm,
    };

    private static object Warnungen() => new
    {
        personen = new object[]
        {
            new
            {
                personId = Jonas,
                vorname = "Jonas",
                nachname = "Hüttner",
                warnungen = new object[]
                {
                    new { art = "fuehrungszeugnis", grund = "fehlt" },
                    new { art = "ersteHilfe", grund = "laeuftAb", gueltigBis = "2027-04-15" },
                },
            },
            new
            {
                personId = Lea,
                vorname = "Lea",
                nachname = "Feuerstein",
                warnungen = new object[] { new { art = "fuehrungszeugnis", grund = "abgelaufen", gueltigBis = "2026-10-11" } },
            },
        },
    };

    private void Liste(Func<HttpResponseMessage>? warnungen = null)
    {
        _server.Bei(HttpMethod.Get, ListenPfad, () => Json(new[]
        {
            Person(Lea, "Lea", "Feuerstein"),
            Person(Jonas, "Jonas", "Hüttner", erinnertAm: "2026-10-12T08:00:00+00:00"),
            Person(Tobias, "Tobias", "Wanderer"),
        }));
        _server.Bei(HttpMethod.Get, WarnPfad, warnungen ?? (() => Json(Warnungen())));
    }

    private static AngleSharp.Dom.IElement Zeile(IRenderedComponent<NachweisePruefen> cut, string name) =>
        cut.FindAll("tbody tr").Single(z => z.QuerySelector("td")!.TextContent.Trim() == name);

    private static List<string> Warnhinweise(AngleSharp.Dom.IElement zeile) =>
        [.. zeile.QuerySelectorAll("td[data-label='Warnhinweise'] .elkw-status--warnung").Select(s => s.TextContent.Trim())];

    [Fact]
    public void Pruefliste_zeigt_je_Person_die_Warnhinweise_und_Erinnern_nur_bei_Warnung()
    {
        Liste();

        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.NotEmpty(Warnhinweise(Zeile(cut, "Jonas Hüttner"))));
        Assert.Contains("Warnhinweise", cut.FindAll("thead th").Select(t => t.TextContent));
        Assert.Equal(["Führungszeugnis nicht bestätigt", "Erste Hilfe läuft am 15.04.2027 ab"], Warnhinweise(Zeile(cut, "Jonas Hüttner")));
        Assert.Equal(["Führungszeugnis abgelaufen am 11.10.2026"], Warnhinweise(Zeile(cut, "Lea Feuerstein")));
        Assert.Empty(Warnhinweise(Zeile(cut, "Tobias Wanderer")));
        Assert.Contains("Erinnert am 12.10.2026", Zeile(cut, "Jonas Hüttner").TextContent, StringComparison.Ordinal);
        Assert.Contains("Erinnern", Zeile(cut, "Jonas Hüttner").QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Contains("Erinnern", Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.DoesNotContain("Erinnern", Zeile(cut, "Tobias Wanderer").QuerySelectorAll("button").Select(b => b.TextContent));
    }

    [Fact]
    public void Erinnern_mit_Inline_Bestaetigung_meldet_Gesendet_und_laedt_die_Liste_neu()
    {
        Liste();
        _server.Bei(HttpMethod.Post, $"{ListenPfad}/erinnerungen", () => Json(new { erinnertAm = "2026-10-12T09:00:00+00:00" }));
        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.NotEmpty(Warnhinweise(Zeile(cut, "Jonas Hüttner"))));

        Zeile(cut, "Jonas Hüttner").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();

        var zeile = Zeile(cut, "Jonas Hüttner");
        Assert.Equal("Erinnerung an Jonas Hüttner senden? Dabei wird die Einsicht in die Nachweise angefragt.", zeile.QuerySelector(".zl-bestaetigung__satz")!.TextContent);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad.EndsWith("/erinnerungen", StringComparison.Ordinal));
        zeile.QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Contains("Die Erinnerung an Jonas Hüttner geht per Mail hinaus.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal));
        Assert.Equal("Gesendet:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Equal($$"""{"personId":"{{Jonas}}"}""", _server.Anfragen.Single(a => a.Methode == HttpMethod.Post && a.Pfad == $"{ListenPfad}/erinnerungen").Koerper);
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == ListenPfad));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == WarnPfad));
    }

    [Fact]
    public void Erinnern_zum_zweiten_Mal_am_Tag_meldet_den_Fehler()
    {
        Liste();
        _server.Bei(HttpMethod.Post, $"{ListenPfad}/erinnerungen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.NotEmpty(Warnhinweise(Zeile(cut, "Lea Feuerstein"))));

        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Contains("Lea Feuerstein wurde heute schon erinnert.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal("Fehler:", cut.Find(".elkw-alert--fehler .elkw-alert__title").TextContent);
    }

    [Fact]
    public void Erinnern_bei_404_meldet_dass_die_Erinnerung_nicht_gesendet_wurde()
    {
        Liste();
        _server.Bei(HttpMethod.Post, $"{ListenPfad}/erinnerungen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.NotEmpty(Warnhinweise(Zeile(cut, "Jonas Hüttner"))));

        Zeile(cut, "Jonas Hüttner").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Jonas Hüttner").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Contains("Die Erinnerung an Jonas Hüttner ließ sich nicht senden.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-alert--erfolg"));
    }

    [Fact]
    public void Scheitern_die_Warnhinweise_bleibt_die_Liste_ohne_Warnhinweise_nutzbar()
    {
        Liste(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));
        Assert.Empty(cut.FindAll(".elkw-status--warnung"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Erinnern");
        Assert.Empty(cut.FindAll(".elkw-alert--fehler"));
        Assert.Contains("Erinnert am 12.10.2026", Zeile(cut, "Jonas Hüttner").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Auf_der_Org_Route_keine_Warnhinweise_und_kein_Erinnern()
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}/personen/nachweise", () => Json(new[] { Person(Jonas, "Jonas", "Hüttner") }));

        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.OrgId, Org));

        cut.WaitForElement("table");
        Assert.DoesNotContain("Warnhinweise", cut.FindAll("thead th").Select(t => t.TextContent));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Erinnern");
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("nachweis-warnungen", StringComparison.Ordinal));
    }

    // ---------- Freizeit-Startseite ----------

    private IRenderedComponent<FreizeitStart> Startseite(string[] erlaubt, Func<HttpResponseMessage>? warnungen = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new { freizeitId = Freizeit, name = "Zeltlager", phase = "InVorbereitung", rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(), erlaubt }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new { id = Freizeit, name = "Zeltlager", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12", leitung = Array.Empty<object>() }));
        if (warnungen is not null)
        {
            _server.Bei(HttpMethod.Get, WarnPfad, warnungen);
        }

        var start = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        start.WaitForAssertion(() => Assert.Equal("Zeltlager", start.Find("h1").TextContent));
        return start;
    }

    private static object WarnungenFuer(params Guid[] personen) => new
    {
        personen = personen.Select(p => new { personId = p, vorname = "X", nachname = "Y", warnungen = new object[] { new { art = "fuehrungszeugnis", grund = "fehlt" } } }).ToArray(),
    };

    [Fact]
    public void Startseite_meldet_Personen_mit_Warnung_in_Mehrzahl_und_Einzahl_ueber_dem_Knopf()
    {
        // Story 2-6: Die Meldung steht wörtlich als Zeile in der Card „Warnhinweise“ über dem Knopf; darüber „Achtung: 1 Punkt ist offen.“
        var mehrere = Startseite([Aktionsschluessel.NachweisePruefen], () => Json(WarnungenFuer(Jonas, Lea)));
        mehrere.WaitForElement(".elkw-alert--warnung");
        Assert.Equal("Achtung:", mehrere.Find(".elkw-alert--warnung .elkw-alert__title").TextContent);
        Assert.Contains("1 Punkt ist offen.", mehrere.Find(".elkw-alert--warnung").TextContent, StringComparison.Ordinal);
        Assert.Equal("Bei 2 Personen im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab.", mehrere.Find(".elkw-card li").TextContent.Trim());
        var markup = mehrere.Markup;
        Assert.True(markup.IndexOf("Bei 2 Personen", StringComparison.Ordinal) < markup.IndexOf($"freizeiten/{Freizeit}/nachweise", StringComparison.Ordinal));

        var eine = Startseite([Aktionsschluessel.NachweisePruefen], () => Json(WarnungenFuer(Jonas)));
        eine.WaitForElement(".elkw-alert--warnung");
        Assert.Equal("Bei 1 Person im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab.", eine.Find(".elkw-card li").TextContent.Trim());
    }

    [Fact]
    public void Startseite_ohne_Warnung_mit_gescheitertem_Abruf_oder_ohne_erlaubte_Aktion_zeigt_keine_Meldung()
    {
        // Story 2-6: Ohne Warnung meldet die Card „Keine offenen Punkte.“; scheitert der Abruf, gibt es keine Meldung.
        var ohne = Startseite([Aktionsschluessel.NachweisePruefen], () => Json(WarnungenFuer()));
        ohne.WaitForElement($"a[href='freizeiten/{Freizeit}/nachweise']");
        Assert.Empty(ohne.FindAll(".elkw-alert--warnung"));
        Assert.Empty(ohne.FindAll(".elkw-card li"));
        Assert.Contains("Keine offenen Punkte.", ohne.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal);

        var gescheitert = Startseite([Aktionsschluessel.NachweisePruefen], () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        gescheitert.WaitForElement($"a[href='freizeiten/{Freizeit}/nachweise']");
        Assert.Empty(gescheitert.FindAll(".elkw-alert"));

        var anfragenVorher = _server.Anfragen.Count(a => a.Pfad == WarnPfad);
        var team = Startseite([], () => Json(WarnungenFuer(Jonas)));
        Assert.Empty(team.FindAll(".elkw-alert"));
        Assert.Equal(anfragenVorher, _server.Anfragen.Count(a => a.Pfad == WarnPfad));
    }

    [Fact]
    public void Anzeige_nennt_die_Nachweisart_der_Warnhinweise()
    {
        Assert.Equal("Erste Hilfe", Anzeige.Nachweis(FreizeitenArt.ErsteHilfe));
        Assert.Equal("JuLeiCa", Anzeige.Nachweis(FreizeitenArt.Juleica));
    }
}
