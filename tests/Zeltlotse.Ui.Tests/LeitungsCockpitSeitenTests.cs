using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-6 in der Web-App mit nachgebildetem Server: das Leitungs-Cockpit auf der Startseite der Freizeit. Card „Anmeldungen“ mit Balken und
/// Zahlen je Art, Card „Warnhinweise“ (beide Zeilen, Einzahl und Mehrzahl, „Keine offenen Punkte.“, ein Abruf scheitert), Card
/// „Freizeitpässe unvollständig“ mit „Erinnern“ (Erfolg mit Neuladen, 409, 404) und die Startseite des Teams ohne Cockpit.
/// </summary>
public sealed class LeitungsCockpitSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f600-0000-7000-8000-0000000000d1");
    private static readonly Guid Emma = Guid.Parse("0199f600-0000-7000-8000-0000000000e1");
    private static readonly Guid Lea = Guid.Parse("0199f600-0000-7000-8000-0000000000e2");
    private static readonly Guid Jonas = Guid.Parse("0199f600-0000-7000-8000-0000000000e3");

    private static readonly string[] Leitung = [Aktionsschluessel.AnmeldungenBearbeiten, Aktionsschluessel.NachweisePruefen, Aktionsschluessel.FreizeitpaesseSehen];

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public LeitungsCockpitSeitenTests()
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

    private static string AnmeldungenPfad => $"/api/freizeiten/{Freizeit}/anmeldungen";

    private static string WarnPfad => $"/api/freizeiten/{Freizeit}/nachweis-warnungen";

    private static string StandPfad => $"/api/freizeiten/{Freizeit}/personen/freizeitpass-stand";

    private static string ErinnernPfad => $"/api/freizeiten/{Freizeit}/personen/freizeitpass/erinnerungen";

    private static string KettePfad => $"/api/freizeiten/{Freizeit}/sicherheit/sicherheitskette";

    private static string[] LeitungMitEinstellungen => [.. Leitung, Aktionsschluessel.FreizeitEinstellungen];

    private static HttpResponseMessage Kette(bool eingerichtet) => Json(eingerichtet
        ? new { wasTun = (string?)"Ruhe bewahren.", leitungTelefon = (string?)"0123 456782", heimatName = (string?)null, heimatTelefon = (string?)"0123 456783", eingerichtet = true, version = 4u }
        : new { wasTun = (string?)null, leitungTelefon = (string?)null, heimatName = (string?)null, heimatTelefon = (string?)null, eingerichtet = false, version = 0u });

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Anmeldung(string vorname, string art, string status) => new
    {
        anmeldungId = Guid.CreateVersion7(),
        personId = Guid.CreateVersion7(),
        vorname,
        nachname = "Muster",
        art,
        status,
        eingegangenAm = "2026-10-05T08:00:00+00:00",
        entschiedenAm = status == "Eingegangen" ? null : "2026-10-06T08:00:00+00:00",
    };

    private static HttpResponseMessage Anmeldungen() => Json(new
    {
        hoechstzahlTeilnehmende = 40,
        bestaetigteTeilnehmende = 1,
        anmeldungen = new[]
        {
            Anmeldung("Emma", "Teilnehmende", "Eingegangen"),
            Anmeldung("Mia", "Teilnehmende", "Bestaetigt"),
            Anmeldung("Paul", "Teilnehmende", "Abgesagt"),
            Anmeldung("Lea", "Mitarbeitende", "Bestaetigt"),
            Anmeldung("Tobias", "Mitarbeitende", "Eingegangen"),
            Anmeldung("Ben", "Mitarbeitende", "Eingegangen"),
        },
    });

    private static object StandPerson(Guid id, string vorname, string nachname, int gefragt, int offen, string? erinnertAm = null) =>
        new { personId = id, vorname, nachname, gefragt, offen, erinnertAm };

    private static HttpResponseMessage Stand(params object[] personen) => Json(new { personen });

    private static HttpResponseMessage Warnungen(int personen) => Json(new
    {
        personen = Enumerable.Range(0, personen)
            .Select(_ => new { personId = Guid.CreateVersion7(), vorname = "X", nachname = "Y", warnungen = new object[] { new { art = "fuehrungszeugnis", grund = "fehlt" } } })
            .ToArray(),
    });

    private IRenderedComponent<FreizeitStart> Startseite(
        string[] erlaubt, Func<HttpResponseMessage>? anmeldungen = null, Func<HttpResponseMessage>? stand = null, Func<HttpResponseMessage>? warnungen = null,
        Func<HttpResponseMessage>? kette = null, string phase = "InVorbereitung")
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new { freizeitId = Freizeit, name = "Zeltlager", phase, rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(), erlaubt }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new { id = Freizeit, name = "Zeltlager", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12", leitung = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, AnmeldungenPfad, anmeldungen ?? Anmeldungen);
        _server.Bei(HttpMethod.Get, StandPfad, stand ?? (() => Stand()));
        _server.Bei(HttpMethod.Get, WarnPfad, warnungen ?? (() => Warnungen(0)));
        _server.Bei(HttpMethod.Get, KettePfad, kette ?? (() => Kette(eingerichtet: true)));

        var start = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        start.WaitForAssertion(() => Assert.Equal("Zeltlager", start.Find("h1").TextContent));
        return start;
    }

    private static AngleSharp.Dom.IElement? Card(IRenderedComponent<FreizeitStart> cut, string titel) =>
        cut.FindAll(".elkw-card").SingleOrDefault(c => c.QuerySelector(".elkw-card__title")?.TextContent == titel);

    private static List<string> Zeilen(AngleSharp.Dom.IElement card) => [.. card.QuerySelectorAll("li").Select(l => l.TextContent.Trim())];

    // ---------- Card „Anmeldungen“ ----------

    [Fact]
    public void Card_Anmeldungen_zeigt_den_Plaetze_Balken_und_je_Art_eingegangen_und_bestaetigt_ohne_Absagen()
    {
        var cut = Startseite(Leitung);

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Anmeldungen")));
        var card = Card(cut, "Anmeldungen")!;
        Assert.Equal("Plätze", card.QuerySelector(".zl-balken__kopf strong")!.TextContent);
        Assert.Equal("1 von 40", card.QuerySelector(".zl-zahl")!.TextContent);
        Assert.Equal("Belegt", card.QuerySelector(".zl-balken .elkw-status")!.TextContent);
        var texte = card.QuerySelectorAll("p").Select(p => p.TextContent.Trim()).ToList();
        Assert.Contains("Teilnehmende: 1 eingegangen, 1 bestätigt", texte);
        Assert.Contains("Mitarbeitende: 2 eingegangen, 1 bestätigt", texte);
        Assert.Equal($"freizeiten/{Freizeit}/anmeldungen", card.QuerySelectorAll("a").Single(a => a.TextContent == "Anmeldungen").GetAttribute("href"));
    }

    [Fact]
    public void Scheitert_der_Abruf_der_Anmeldungen_entfaellt_die_Card_bis_auf_den_Knopf()
    {
        var cut = Startseite(Leitung, anmeldungen: () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Warnhinweise")));
        Assert.Null(Card(cut, "Anmeldungen"));
        Assert.Empty(cut.FindAll(".zl-balken"));
        Assert.Equal($"freizeiten/{Freizeit}/anmeldungen", cut.FindAll("a").Single(a => a.TextContent == "Anmeldungen").GetAttribute("href"));
    }

    // ---------- Card „Warnhinweise“ ----------

    [Fact]
    public void Warnhinweise_beide_Zeilen_in_Mehrzahl_mit_der_Meldung_Achtung_und_dem_Link_auf_die_Tabelle()
    {
        var cut = Startseite(
            Leitung,
            stand: () => Stand(StandPerson(Emma, "Emma", "Muster", 8, 5), StandPerson(Lea, "Lea", "Feuerstein", 4, 4)),
            warnungen: () => Warnungen(2));

        cut.WaitForAssertion(() => Assert.Equal(2, Zeilen(Card(cut, "Warnhinweise")!).Count));
        var card = Card(cut, "Warnhinweise")!;
        var meldung = card.QuerySelector(".elkw-alert--warnung")!;
        Assert.Equal("Achtung:", meldung.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Contains("2 Punkte sind offen.", meldung.TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".elkw-alert--warnung"));
        Assert.Equal(
            ["2 Freizeitpässe unvollständig", "Bei 2 Personen im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab."],
            Zeilen(card));
        var link = card.QuerySelectorAll("li a").Single();
        Assert.Equal($"freizeiten/{Freizeit}#freizeitpaesse-offen", link.GetAttribute("href"));
        Assert.Equal($"freizeiten/{Freizeit}/nachweise", card.QuerySelectorAll("a").Single(a => a.TextContent == "Nachweise prüfen").GetAttribute("href"));
        Assert.NotNull(cut.Find("#freizeitpaesse-offen"));
    }

    [Fact]
    public void Warnhinweise_in_Einzahl()
    {
        var cut = Startseite(Leitung, stand: () => Stand(StandPerson(Emma, "Emma", "Muster", 8, 5)));

        cut.WaitForAssertion(() => Assert.Single(Zeilen(Card(cut, "Warnhinweise")!)));
        var card = Card(cut, "Warnhinweise")!;
        Assert.Equal(["1 Freizeitpass unvollständig"], Zeilen(card));
        Assert.Contains("1 Punkt ist offen.", card.QuerySelector(".elkw-alert--warnung")!.TextContent, StringComparison.Ordinal);

        var nachweis = Startseite(Leitung, warnungen: () => Warnungen(1));
        nachweis.WaitForAssertion(() => Assert.Single(Zeilen(Card(nachweis, "Warnhinweise")!)));
        Assert.Equal(["Bei 1 Person im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab."], Zeilen(Card(nachweis, "Warnhinweise")!));
        Assert.Contains("1 Punkt ist offen.", Card(nachweis, "Warnhinweise")!.QuerySelector(".elkw-alert--warnung")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Zeile_und_mit_allen_Abrufen_meldet_die_Card_Keine_offenen_Punkte()
    {
        var cut = Startseite(Leitung);

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Warnhinweise")?.QuerySelector(".elkw-alert--erfolg")));
        var card = Card(cut, "Warnhinweise")!;
        Assert.Contains("Keine offenen Punkte.", card.QuerySelector(".elkw-alert--erfolg")!.TextContent, StringComparison.Ordinal);
        Assert.Empty(Zeilen(card));
        Assert.Empty(cut.FindAll(".elkw-alert--warnung"));
        Assert.Null(Card(cut, "Freizeitpässe unvollständig"));
    }

    [Fact]
    public void Scheitert_ein_Abruf_entfaellt_seine_Zeile_und_es_gibt_kein_Keine_offenen_Punkte()
    {
        var standGescheitert = Startseite(Leitung, stand: () => new HttpResponseMessage(HttpStatusCode.InternalServerError), warnungen: () => Warnungen(1));
        standGescheitert.WaitForAssertion(() => Assert.Single(Zeilen(Card(standGescheitert, "Warnhinweise")!)));
        Assert.Equal(["Bei 1 Person im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab."], Zeilen(Card(standGescheitert, "Warnhinweise")!));
        Assert.Contains("1 Punkt ist offen.", standGescheitert.Find(".elkw-alert--warnung").TextContent, StringComparison.Ordinal);
        Assert.Null(Card(standGescheitert, "Freizeitpässe unvollständig"));

        var warnungenGescheitert = Startseite(Leitung, warnungen: () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        warnungenGescheitert.WaitForAssertion(() => Assert.NotNull(Card(warnungenGescheitert, "Anmeldungen")));
        warnungenGescheitert.WaitForAssertion(() => Assert.NotNull(Card(warnungenGescheitert, "Warnhinweise")));
        var card = Card(warnungenGescheitert, "Warnhinweise")!;
        Assert.Empty(Zeilen(card));
        Assert.Empty(card.QuerySelectorAll(".elkw-alert"));
        Assert.NotNull(card.QuerySelectorAll("a").SingleOrDefault(a => a.TextContent == "Nachweise prüfen"));
    }

    // ---------- Card „Freizeitpässe unvollständig“ ----------

    private void Erinnern(Func<HttpResponseMessage> antwort) => _server.Bei(HttpMethod.Post, ErinnernPfad, antwort);

    private IRenderedComponent<FreizeitStart> MitTabelle() => Startseite(
        Leitung,
        stand: () => Stand(
            StandPerson(Lea, "Lea", "Feuerstein", 4, 4),
            StandPerson(Emma, "Emma", "Muster", 8, 5, erinnertAm: "2026-10-12T08:00:00+00:00")));

    private static AngleSharp.Dom.IElement Zeile(IRenderedComponent<FreizeitStart> cut, string name) =>
        cut.FindAll("#freizeitpaesse-offen tbody tr").Single(z => z.QuerySelector("td")!.TextContent.Trim() == name);

    [Fact]
    public void Tabelle_zeigt_Name_als_Link_den_Stand_als_Status_und_Erinnert_am()
    {
        var cut = MitTabelle();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count));
        Assert.Equal("Freizeitpässe unvollständig", cut.Find("#freizeitpaesse-offen .elkw-card__title").TextContent);
        Assert.Equal(["Name", "Stand", "Erinnerung"], cut.FindAll("#freizeitpaesse-offen thead th").Select(t => t.TextContent));
        var lea = Zeile(cut, "Lea Feuerstein");
        Assert.Equal($"freizeiten/{Freizeit}/freizeitpaesse/{Lea}", lea.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Freizeitpass fehlt noch", lea.QuerySelector(".elkw-status--warnung")!.TextContent);
        Assert.DoesNotContain("Erinnert am", lea.TextContent, StringComparison.Ordinal);
        var emma = Zeile(cut, "Emma Muster");
        Assert.Equal("5 von 8 Angaben offen", emma.QuerySelector(".elkw-status--warnung")!.TextContent);
        Assert.Contains("Erinnert am 12.10.2026", emma.TextContent, StringComparison.Ordinal);
        Assert.Contains("Erinnern", emma.QuerySelectorAll("button").Select(b => b.TextContent));
    }

    [Fact]
    public void Erinnern_mit_Inline_Bestaetigung_meldet_Gesendet_und_laedt_den_Stand_neu()
    {
        Erinnern(() => Json(new { erinnertAm = "2026-10-13T09:00:00+00:00" }));
        var cut = MitTabelle();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count));

        Zeile(cut, "Emma Muster").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Assert.Equal("Erinnerung zum Freizeitpass von Emma Muster senden?", Zeile(cut, "Emma Muster").QuerySelector(".zl-bestaetigung__satz")!.TextContent);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == ErinnernPfad);
        Zeile(cut, "Emma Muster").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            "Die Erinnerung zum Freizeitpass von Emma Muster geht per Mail hinaus.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal));
        Assert.Equal("Gesendet:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Equal($$"""{"personId":"{{Emma}}"}""", Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == ErinnernPfad).Koerper);
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == StandPfad));
    }

    [Fact]
    public void Erinnern_zum_zweiten_Mal_am_Tag_meldet_den_Fehler()
    {
        Erinnern(() => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = MitTabelle();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count));

        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            "Zum Freizeitpass von Lea Feuerstein wurde heute schon erinnert.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal("Fehler:", cut.Find(".elkw-alert--fehler .elkw-alert__title").TextContent);
        Assert.Empty(cut.FindAll(".elkw-alert--erfolg"));
    }

    [Fact]
    public void Erinnern_bei_404_meldet_dass_die_Erinnerung_nicht_gesendet_wurde()
    {
        Erinnern(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = MitTabelle();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count));

        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Contains("Die Erinnerung ließ sich nicht senden.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-alert--erfolg"));
    }

    [Fact]
    public void Kein_doppeltes_Senden_solange_eine_Uebertragung_laeuft()
    {
        var freigabe = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Bei(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == ErinnernPfad, _ => freigabe.Task);
        var cut = MitTabelle();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count));

        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();
        cut.WaitForAssertion(() => Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == ErinnernPfad));
        Zeile(cut, "Emma Muster").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Emma Muster").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();
        freigabe.SetResult(Json(new { erinnertAm = "2026-10-13T09:00:00+00:00" }));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".elkw-alert--erfolg")));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == ErinnernPfad);
    }

    [Fact]
    public void Erinnern_scheitert_in_der_Uebertragung_meldet_den_Fehler_und_ein_zweites_Erinnern_sendet_wieder()
    {
        Erinnern(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = MitTabelle();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count));

        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Lea Feuerstein").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Post && a.Pfad == ErinnernPfad)));
    }

    [Fact]
    public void Scheitert_das_Neuladen_nach_dem_Erinnern_bleibt_die_Tabelle_stehen()
    {
        Erinnern(() => Json(new { erinnertAm = "2026-10-13T09:00:00+00:00" }));
        var cut = MitTabelle();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count));
        _server.Bei(HttpMethod.Get, StandPfad, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Zeile(cut, "Emma Muster").QuerySelectorAll("button").Single(b => b.TextContent == "Erinnern").Click();
        Zeile(cut, "Emma Muster").QuerySelectorAll("button").Single(b => b.TextContent == "Ja, senden").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".elkw-alert--erfolg")));
        Assert.Equal(2, cut.FindAll("#freizeitpaesse-offen tbody tr").Count);
        Assert.Contains("2 Freizeitpässe unvollständig", Zeilen(Card(cut, "Warnhinweise")!));
    }

    // ---------- Story 2-8: Sicherheitskette ----------

    [Fact]
    public void Nicht_eingerichtete_Kette_ist_eine_Zeile_mit_Link_auf_die_Card_in_den_Einstellungen_und_zaehlt_mit()
    {
        var cut = Startseite(LeitungMitEinstellungen, stand: () => Stand(StandPerson(Emma, "Emma", "Muster", 8, 5)), kette: () => Kette(eingerichtet: false));

        cut.WaitForAssertion(() => Assert.Equal(2, Zeilen(Card(cut, "Warnhinweise")!).Count));
        var card = Card(cut, "Warnhinweise")!;
        Assert.Equal(["1 Freizeitpass unvollständig", "Sicherheitskette ist noch nicht eingerichtet"], Zeilen(card));
        Assert.Contains("2 Punkte sind offen.", card.QuerySelector(".elkw-alert--warnung")!.TextContent, StringComparison.Ordinal);
        var link = card.QuerySelectorAll("li a").Single(a => a.TextContent == "Sicherheitskette ist noch nicht eingerichtet");
        Assert.Equal($"freizeiten/{Freizeit}/einstellungen#sicherheitskette", link.GetAttribute("href"));

        var allein = Startseite(LeitungMitEinstellungen, kette: () => Kette(eingerichtet: false));
        allein.WaitForAssertion(() => Assert.Single(Zeilen(Card(allein, "Warnhinweise")!)));
        Assert.Contains("1 Punkt ist offen.", Card(allein, "Warnhinweise")!.QuerySelector(".elkw-alert--warnung")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Eingerichtete_Kette_ergibt_keine_Zeile_und_Keine_offenen_Punkte()
    {
        var cut = Startseite(LeitungMitEinstellungen);

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Warnhinweise")?.QuerySelector(".elkw-alert--erfolg")));
        Assert.Empty(Zeilen(Card(cut, "Warnhinweise")!));
        Assert.Contains(_server.Anfragen, a => a.Pfad == KettePfad);
    }

    [Fact]
    public void Scheitert_der_Abruf_der_Kette_entfaellt_die_Zeile_und_Keine_offenen_Punkte()
    {
        var cut = Startseite(LeitungMitEinstellungen, kette: () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Anmeldungen")));
        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Warnhinweise")));
        var card = Card(cut, "Warnhinweise")!;
        Assert.Empty(Zeilen(card));
        Assert.Empty(card.QuerySelectorAll(".elkw-alert"));
    }

    [Fact]
    public void Nach_dem_Abschluss_ist_eine_nicht_eingerichtete_Kette_keine_Zeile()
    {
        var cut = Startseite(LeitungMitEinstellungen, kette: () => Kette(eingerichtet: false), phase: "Gelaufen");

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Warnhinweise")?.QuerySelector(".elkw-alert--erfolg")));
        Assert.Empty(Zeilen(Card(cut, "Warnhinweise")!));
        Assert.DoesNotContain("Sicherheitskette ist noch nicht eingerichtet", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_freizeit_einstellungen_laedt_das_Cockpit_die_Kette_nicht()
    {
        var cut = Startseite(Leitung, kette: () => Kette(eingerichtet: false));

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Warnhinweise")?.QuerySelector(".elkw-alert--erfolg")));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == KettePfad);
    }

    // ---------- Team ----------

    [Fact]
    public void Team_sieht_kein_Cockpit_und_die_Startseite_laedt_nichts_davon()
    {
        var cut = Startseite(
            [Aktionsschluessel.FreizeitpaesseSehen],
            stand: () => Stand(StandPerson(Emma, "Emma", "Muster", 8, 5)),
            warnungen: () => Warnungen(1));

        cut.WaitForElement($"a[href='freizeiten/{Freizeit}/freizeitpaesse']");
        Assert.Null(Card(cut, "Anmeldungen"));
        Assert.Null(Card(cut, "Warnhinweise"));
        Assert.Null(Card(cut, "Freizeitpässe unvollständig"));
        Assert.Empty(cut.FindAll(".elkw-alert"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == StandPfad || a.Pfad == WarnPfad || a.Pfad == AnmeldungenPfad || a.Pfad == KettePfad);
    }

    // ---------- Texte und Client ----------

    [Fact]
    public void Anzeige_nennt_Stand_Einzahl_und_Mehrzahl()
    {
        Assert.Equal("Freizeitpass fehlt noch", Anzeige.FreizeitpassStand(8, 8));
        Assert.Equal("5 von 8 Angaben offen", Anzeige.FreizeitpassStand(8, 5));
        Assert.Equal(("1 Freizeitpass unvollständig", "4 Freizeitpässe unvollständig"), (Anzeige.FreizeitpaesseUnvollstaendig(1), Anzeige.FreizeitpaesseUnvollstaendig(4)));
        Assert.Equal(("1 Punkt ist offen.", "2 Punkte sind offen."), (Anzeige.OffenePunkte(1), Anzeige.OffenePunkte(2)));
        Assert.Equal("Teilnehmende: 1 eingegangen, 1 bestätigt", Anzeige.Anmeldestand("Teilnehmende", 1, 1));
    }

    [Fact]
    public async Task Client_laedt_den_Stand_und_erinnert_mit_Ergebnis_je_Status()
    {
        _server.Bei(HttpMethod.Get, StandPfad, () => Stand(StandPerson(Jonas, "Jonas", "Hüttner", 4, 2)));
        _server.BeiDerReihe(
            HttpMethod.Post,
            ErinnernPfad,
            () => Json(new { erinnertAm = "2026-10-13T09:00:00+00:00" }),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt),
            () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Services.GetRequiredService<PersonenApiClient>();

        var stand = await client.FreizeitpassStandLadenAsync(Freizeit, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal((Jonas, 4, 2, (DateTimeOffset?)null), (stand!.Personen[0].PersonId, stand.Personen[0].Gefragt, stand.Personen[0].Offen, stand.Personen[0].ErinnertAm));
        Assert.Equal(
            [ErinnerungErgebnis.Gesendet, ErinnerungErgebnis.SchonHeute, ErinnerungErgebnis.NichtGefunden],
            [
                await client.FreizeitpassErinnernAsync(Freizeit, Jonas, Xunit.TestContext.Current.CancellationToken),
                await client.FreizeitpassErinnernAsync(Freizeit, Jonas, Xunit.TestContext.Current.CancellationToken),
                await client.FreizeitpassErinnernAsync(Freizeit, Jonas, Xunit.TestContext.Current.CancellationToken),
            ]);
        Assert.Null(await new PersonenApiClient(new NachgebildeterHandler().Client()).FreizeitpassStandLadenAsync(Freizeit, Xunit.TestContext.Current.CancellationToken));
    }
}
