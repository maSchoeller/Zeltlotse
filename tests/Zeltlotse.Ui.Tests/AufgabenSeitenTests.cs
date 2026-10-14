using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Features.Programm;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using AufgabenListe = Zeltlotse.App.Features.Programm.Aufgaben;
using VorlagenSeite = Zeltlotse.App.Features.Programm.AufgabenVorlagen;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 3-5 in der Web-App mit nachgebildetem Server: die Aufgabenliste (Cards je Phase, Reiter „Alle“ und „Meine Aufgaben“, Namen der
/// Übernehmenden, „Am Programmpunkt“, Erledigt, „Ich übernehme“ und „Erledigt“ ohne Bestätigung, Konflikt, Anlegen mit vorbelegter Phase und
/// Feldfehlern, nur lesbar nach dem Abschluss), die Detailseite (Autosave des Titels, Phase per Auswahl mit Konflikt, „Als Vorlage speichern“,
/// Löschen), die Card am Programmpunkt, die Startseite („Meine Aufgaben“, Warnhinweis nach Phase nur für die Leitung, „Schaden melden“), die
/// Organisation und die Seite „Aufgaben-Vorlagen“ sowie der Client und die Texte.
/// </summary>
public sealed class AufgabenSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000003d1");
    private static readonly Guid OrgId = Guid.Parse("0199f700-0000-7000-8000-0000000003c1");
    private static readonly Guid Lea = Guid.Parse("0199f700-0000-7000-8000-0000000003e2");
    private static readonly Guid Jonas = Guid.Parse("0199f700-0000-7000-8000-0000000003e3");
    private static readonly Guid Ich = Guid.Parse("0199f700-0000-7000-8000-0000000003e4");
    private static readonly Guid Lagerfeuer = Guid.Parse("0199f700-0000-7000-8000-0000000003a1");
    private static readonly Guid Vertrag = Guid.Parse("0199f700-0000-7000-8000-0000000003b1");
    private static readonly Guid Kasten = Guid.Parse("0199f700-0000-7000-8000-0000000003b2");
    private static readonly Guid Andacht = Guid.Parse("0199f700-0000-7000-8000-0000000003b3");
    private static readonly Guid Fotos = Guid.Parse("0199f700-0000-7000-8000-0000000003b4");
    private static readonly Guid Uebergabe = Guid.Parse("0199f700-0000-7000-8000-0000000003b5");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public AufgabenSeitenTests()
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

    private static string AufgabenPfad => $"/api/freizeiten/{Freizeit}/programm/aufgaben";

    private static string AufgabePfad(Guid aufgabe) => $"{AufgabenPfad}/{aufgabe}";

    private static string VorlagenPfad => $"/api/organisationen/{OrgId}/aufgaben-vorlagen";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Aufgabe(
        Guid id, string titel, string phase = "Vorbereitung", Guid? punkt = null, Guid? von = null, bool meins = false, bool erledigt = false,
        bool vorlage = false, uint version = 5) =>
        new { id, version, titel, phase, programmpunktId = punkt, uebernommenVon = von, meins, erledigt, vorlage };

    /// <summary>Fünf Aufgaben: zwei aus Vorlagen in der Vorbereitung (eine erledigt), eine am Lagerfeuer, zwei in der Nachbereitung.</summary>
    private static object[] Liste(params object[] zusaetzlich) =>
    [
        Aufgabe(Vertrag, "Zeltplatz-Vertrag prüfen", von: Jonas, erledigt: true, vorlage: true),
        Aufgabe(Kasten, "Erste-Hilfe-Kasten auffüllen", von: Ich, meins: true, vorlage: true),
        Aufgabe(Andacht, "Andacht am Lagerfeuer vorbereiten", "Waehrend", Lagerfeuer, von: Lea),
        Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung"),
        Aufgabe(Uebergabe, "Zeltplatz übergeben", "Nachbereitung", von: Guid.Parse("0199f700-0000-7000-8000-0000000003ff")),
        .. zusaetzlich,
    ];

    private void Antworten(string phase = "InVorbereitung", string[]? erlaubt = null, Func<HttpResponseMessage>? aufgaben = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { erlaubt is null ? "Team" : "Leitung" },
            schalter = Array.Empty<string>(), erlaubt = erlaubt ?? [],
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-04", phase, freigabeBis = "2027-09-12",
            leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner", personId = Jonas } },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(new object[]
        {
            new { anmeldungId = Guid.CreateVersion7(), personId = Lea, vorname = "Lea", nachname = "Feuerstein", art = "Mitarbeitende", status = "Bestaetigt" },
            new { anmeldungId = Guid.CreateVersion7(), personId = Ich, vorname = "Tobias", nachname = "Wanderer", art = "Mitarbeitende", status = "Bestaetigt" },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/programm", () => Json(new
        {
            ersterTag = "2027-08-02",
            letzterTag = "2027-08-04",
            punkte = new[] { new { id = Lagerfeuer, version = 3u, tag = "2027-08-02", beginn = "20:00", titel = "Lagerfeuer", mahlzeit = (string?)null, uebernommenVon = (Guid?)Lea, meins = false } },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/programm/material", () => Json(new { material = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, AufgabenPfad, aufgaben ?? (() => Json(Liste())));
    }

    private IRenderedComponent<AufgabenListe> ListeRendern(bool meine = false)
    {
        Navigation.NavigateTo(meine ? $"freizeiten/{Freizeit}/aufgaben?meine=true" : $"freizeiten/{Freizeit}/aufgaben");
        var cut = Render<AufgabenListe>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<AufgabeSeite> DetailRendern(Guid aufgabe)
    {
        var cut = Render<AufgabeSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.AufgabeId, aufgabe));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<AufgabenListe> cut, string titel) =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == titel);

    private static AngleSharp.Dom.IElement Zeile(AngleSharp.Dom.IParentNode bereich, string titel) =>
        bereich.QuerySelectorAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == titel);

    private static AngleSharp.Dom.IElement Knopf(AngleSharp.Dom.IParentNode bereich, string text) =>
        bereich.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_Aufgaben_und_Vorlagen_403_404_heissen_nicht_verfuegbar_und_die_Aktionen_ordnen_Antworten_zu()
    {
        Antworten();
        var client = Services.GetRequiredService<ProgrammApiClient>();

        var aufgaben = await client.AufgabenAsync(Freizeit, Abbruch);

        Assert.NotNull(aufgaben);
        Assert.Equal(new Zeltlotse.Api.Contracts.Programm.Aufgabe(Andacht, 5, "Andacht am Lagerfeuer vorbereiten", AufgabenPhase.Waehrend, Lagerfeuer, Lea, false, false, false), aufgaben[2]);
        Assert.Equal(AufgabePfad(Fotos), ProgrammApiClient.AufgabePfad(Freizeit, Fotos));
        Assert.Equal($"{VorlagenPfad}/{Vertrag}", ProgrammApiClient.VorlagePfad(OrgId, Vertrag));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, AufgabenPfad, () => new HttpResponseMessage(status));
            _server.Bei(HttpMethod.Get, VorlagenPfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.AufgabenAsync(Freizeit, Abbruch));
            Assert.Null(await client.VorlagenAsync(OrgId, Abbruch));
        }

        _server.Bei(HttpMethod.Post, $"{AufgabePfad(Fotos)}/uebernehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Post, $"{AufgabePfad(Fotos)}/offen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));
        _server.Bei(HttpMethod.Post, $"{AufgabePfad(Fotos)}/vorlage", () => Json(Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung", vorlage: true)));
        _server.Bei(HttpMethod.Patch, AufgabePfad(Fotos), () => Json(new { version = 9, phase = "Waehrend" }));
        _server.Bei(HttpMethod.Delete, AufgabePfad(Fotos), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, AufgabenPfad, () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"titel":["Titel fehlt."]}}""", "application/problem+json"));
        _server.Bei(HttpMethod.Post, VorlagenPfad, () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"phase":["Unbekannt."]}}""", "application/problem+json"));
        _server.Bei(HttpMethod.Delete, $"{VorlagenPfad}/{Vertrag}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fotos = aufgaben[3];

        Assert.Equal(ProgrammAktionErgebnis.Konflikt, (await client.AufgabeUebergangAsync(Freizeit, Fotos, AufgabenUebergang.Uebernehmen, Abbruch)).Ergebnis);
        Assert.Equal(ProgrammAktionErgebnis.Abgeschlossen, (await client.AufgabeUebergangAsync(Freizeit, Fotos, AufgabenUebergang.Offen, Abbruch)).Ergebnis);
        Assert.True((await client.AufgabeAlsVorlageAsync(Freizeit, Fotos, Abbruch)).Aufgabe!.Vorlage);
        Assert.Equal(new AufgabePhaseAenderung(ProgrammAktionErgebnis.Erfolg, 9), await client.AufgabePhaseAendernAsync(Freizeit, fotos, AufgabenPhase.Waehrend, Abbruch));
        Assert.Equal(ProgrammAktionErgebnis.Erfolg, await client.AufgabeLoeschenAsync(Freizeit, Fotos, Abbruch));
        Assert.Equal("Titel fehlt.", (await client.AufgabeAnlegenAsync(Freizeit, new AufgabeNeu(" ", null, null), Abbruch)).Feldfehler["titel"]);
        Assert.Equal("Unbekannt.", (await client.VorlageAnlegenAsync(OrgId, new AufgabenVorlageNeu("x", null), Abbruch)).Feldfehler["phase"]);
        Assert.Equal(ProgrammAktionErgebnis.Fehler, await client.VorlageLoeschenAsync(OrgId, Vertrag, Abbruch));
        var patch = Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Patch);
        Assert.Equal("""{"version":5,"alterWert":"Nachbereitung","phase":"Waehrend"}""", patch.Koerper);
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{AufgabePfad(Fotos)}/offen");

        Assert.Equal(["Vorbereitung", "Während der Freizeit", "Nachbereitung"], Enum.GetValues<AufgabenPhase>().Select(Anzeige.AufgabenPhase));
        Assert.Equal(("1 Aufgabe ist noch nicht übernommen", "3 Aufgaben sind noch nicht übernommen"), (Anzeige.AufgabenUnbesetzt(1), Anzeige.AufgabenUnbesetzt(3)));
    }

    // ---------- Aufgabenliste ----------

    [Fact]
    public void Liste_zeigt_je_Phase_eine_Card_mit_Namen_Offen_Programmpunkt_Erledigt_und_den_Knoepfen()
    {
        Antworten();

        var cut = ListeRendern();

        Assert.Equal(("Planen", "Aufgaben"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        var reiter = cut.FindAll(".zl-reiter nav a");
        Assert.Equal(["Alle", "Meine Aufgaben"], reiter.Select(a => a.TextContent));
        Assert.Equal([$"freizeiten/{Freizeit}/aufgaben", $"freizeiten/{Freizeit}/aufgaben?meine=true"], reiter.Select(a => a.GetAttribute("href")));
        Assert.Equal(["page", null], reiter.Select(a => a.GetAttribute("aria-current")));
        Assert.Equal(
            ["Vorbereitung", "Während der Freizeit", "Nachbereitung", "Aufgabe anlegen"],
            cut.FindAll("section.elkw-card h2").Select(h => h.TextContent));

        var vorbereitung = Card(cut, "Vorbereitung");
        var vertrag = Zeile(vorbereitung, "Zeltplatz-Vertrag prüfen");
        Assert.Equal($"freizeiten/{Freizeit}/aufgaben/{Vertrag}", vertrag.QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Equal(["Übernommen: Jonas Hüttner", "Erledigt"], vertrag.QuerySelectorAll(".elkw-status").Select(s => s.TextContent));
        Assert.Equal(["Wieder offen"], vertrag.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));
        var kasten = Zeile(vorbereitung, "Erste-Hilfe-Kasten auffüllen");
        Assert.Equal(["Abgeben", "Erledigt"], kasten.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));

        var andacht = Zeile(Card(cut, "Während der Freizeit"), "Andacht am Lagerfeuer vorbereiten");
        Assert.Equal("Übernommen: Lea Feuerstein", andacht.QuerySelector(".elkw-status")!.TextContent);
        var punkt = andacht.QuerySelectorAll("a").Single(a => a.TextContent == "Am Programmpunkt: Lagerfeuer");
        Assert.Equal($"freizeiten/{Freizeit}/programm/{Lagerfeuer}", punkt.GetAttribute("href"));

        var nachbereitung = Card(cut, "Nachbereitung");
        var fotos = Zeile(nachbereitung, "Fotos sammeln");
        Assert.Equal("Offen", fotos.QuerySelector(".elkw-status")!.TextContent);
        Assert.Contains("elkw-status--warnung", fotos.QuerySelector(".elkw-status")!.ClassList);
        Assert.Equal(["Ich übernehme", "Erledigt"], fotos.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));
        Assert.Equal("Übernommen", Zeile(nachbereitung, "Zeltplatz übergeben").QuerySelector(".elkw-status")!.TextContent);
        // Vorbelegt: in der Vorbereitung die Phase Vorbereitung.
        Assert.NotNull(cut.Find("input[name=aufgabe-phase][value=Vorbereitung]").GetAttribute("checked"));
    }

    [Fact]
    public void Reiter_Meine_Aufgaben_zeigt_nur_eigene_ohne_eigene_Keine_Aufgaben_uebernommen()
    {
        Antworten();

        var meine = ListeRendern(meine: true);

        Assert.Equal([null, "page"], meine.FindAll(".zl-reiter nav a").Select(a => a.GetAttribute("aria-current")));
        Assert.Equal(["Erste-Hilfe-Kasten auffüllen"], meine.FindAll(".zl-termin h3").Select(h => h.TextContent.Trim()));
        Assert.Equal(["Vorbereitung", "Aufgabe anlegen"], meine.FindAll("section.elkw-card h2").Select(h => h.TextContent));

        Antworten(aufgaben: () => Json(new[] { Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung") }));
        var keine = ListeRendern(meine: true);
        Assert.Equal("Keine Aufgaben übernommen.", keine.Find(".elkw-empty p").TextContent);

        Antworten(aufgaben: () => Json(Array.Empty<object>()));
        var leer = ListeRendern();
        Assert.Equal("Noch keine Aufgaben.", leer.Find(".elkw-empty p").TextContent);
        Assert.Equal(["Aufgabe anlegen"], leer.FindAll("section.elkw-card h2").Select(h => h.TextContent));
    }

    [Fact]
    public void Ich_uebernehme_und_Erledigt_ohne_Bestaetigung_danach_der_neue_Stand_an_der_Zeile()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{AufgabePfad(Fotos)}/uebernehmen", () => Json(Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung", von: Ich, meins: true, version: 6)));
        _server.Bei(HttpMethod.Post, $"{AufgabePfad(Fotos)}/erledigt", () => Json(Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung", von: Ich, meins: true, erledigt: true, version: 7)));
        var cut = ListeRendern();

        Knopf(Zeile(Card(cut, "Nachbereitung"), "Fotos sammeln"), "Ich übernehme").Click();
        cut.WaitForAssertion(() => Assert.Equal("Übernommen: Tobias Wanderer", Zeile(Card(cut, "Nachbereitung"), "Fotos sammeln").QuerySelector(".elkw-status")?.TextContent));
        Knopf(Zeile(Card(cut, "Nachbereitung"), "Fotos sammeln"), "Erledigt").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Abgeben", "Wieder offen"], Zeile(Card(cut, "Nachbereitung"), "Fotos sammeln").QuerySelectorAll("button").Select(b => b.TextContent.Trim())));
        Assert.Contains(Zeile(Card(cut, "Nachbereitung"), "Fotos sammeln").QuerySelectorAll(".elkw-status"), s => s.TextContent == "Erledigt");
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Post));
    }

    [Fact]
    public void Konflikt_beim_Uebernehmen_meldet_es_an_der_Zeile_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{AufgabePfad(Fotos)}/uebernehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = ListeRendern();
        _server.Bei(HttpMethod.Get, AufgabenPfad, () => Json(Liste().Take(3).Append(Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung", von: Lea)).ToArray()));

        Knopf(Zeile(Card(cut, "Nachbereitung"), "Fotos sammeln"), "Ich übernehme").Click();

        cut.WaitForAssertion(() => Assert.Equal("Übernommen: Lea Feuerstein", Zeile(Card(cut, "Nachbereitung"), "Fotos sammeln").QuerySelector(".elkw-status")?.TextContent));
        Assert.Contains(ProgrammTexte.AufgabeKonfliktText, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Anlegen_waehrend_der_Freizeit_ist_Waehrend_vorbelegt_Feldfehler_am_Feld_danach_steht_sie_in_ihrer_Card()
    {
        Antworten("Laeuft");
        var cut = ListeRendern();
        Assert.NotNull(cut.Find("input[name=aufgabe-phase][value=Waehrend]").GetAttribute("checked"));

        _server.Bei(HttpMethod.Post, AufgabenPfad, () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"titel":["Titel fehlt."]}}""", "application/problem+json"));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Fehler: Titel fehlt.", cut.Markup, StringComparison.Ordinal));

        _server.Bei(HttpMethod.Post, AufgabenPfad, () => Json(Aufgabe(Guid.CreateVersion7(), "Zeltplan drucken", "Nachbereitung"), HttpStatusCode.Created));
        _server.Bei(HttpMethod.Get, AufgabenPfad, () => Json(Liste(Aufgabe(Guid.CreateVersion7(), "Zeltplan drucken", "Nachbereitung"))));
        cut.Find("#aufgabe-titel").Input("Zeltplan drucken");
        cut.Find("input[name=aufgabe-phase][value=Nachbereitung]").Change("Nachbereitung");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Aufgabe „Zeltplan drucken“ angelegt.", cut.Markup, StringComparison.Ordinal));
        Assert.Contains(Card(cut, "Nachbereitung").QuerySelectorAll(".zl-termin h3"), h => h.TextContent.Trim() == "Zeltplan drucken");
        var post = _server.Anfragen.Last(a => a.Methode == HttpMethod.Post);
        Assert.Equal("""{"titel":"Zeltplan drucken","phase":"Nachbereitung","programmpunktId":null}""", post.Koerper);
    }

    [Fact]
    public void Nach_dem_Abschluss_nur_lesbar_und_ohne_Kontext_nicht_verfuegbar()
    {
        Antworten("Gelaufen");
        var cut = ListeRendern();

        Assert.Contains(ProgrammTexte.AbgeschlossenText, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".zl-termin button"));
        Assert.Empty(cut.FindAll("form"));

        _server.Bei(HttpMethod.Get, AufgabenPfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal("Diese Seite ist nicht verfügbar.", ListeRendern().Find("h1").TextContent);
    }

    // ---------- Detailseite ----------

    [Fact]
    public void Detail_zeigt_Status_Programmpunkt_Autosave_Titel_Phase_als_Vorlage_speichern_und_Loeschen()
    {
        Antworten();

        var cut = DetailRendern(Andacht);

        Assert.Equal(("Planen", "Aufgabe"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        Assert.Equal("Übernommen: Lea Feuerstein", cut.Find(".elkw-status").TextContent);
        Assert.Equal($"freizeiten/{Freizeit}/programm/{Lagerfeuer}", cut.FindAll("a").Single(a => a.TextContent == "Am Programmpunkt: Lagerfeuer").GetAttribute("href"));
        Assert.Equal("Andacht am Lagerfeuer vorbereiten", cut.Find("#aufgabe-titel").GetAttribute("value"));
        Assert.NotNull(cut.Find("input[name=aufgabe-phase][value=Waehrend]").GetAttribute("checked"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() is "Ich übernehme" or "Abgeben");
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Als Vorlage speichern");
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Aufgabe löschen");
        Assert.DoesNotContain("Steht in den Aufgaben-Vorlagen der Organisation.", cut.Markup, StringComparison.Ordinal);

        var ausVorlage = DetailRendern(Kasten);
        Assert.Contains("Steht in den Aufgaben-Vorlagen der Organisation.", ausVorlage.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(ausVorlage.FindAll("button"), b => b.TextContent.Trim() == "Als Vorlage speichern");
    }

    [Fact]
    public void Als_Vorlage_speichern_danach_steht_der_Satz_und_der_Knopf_fehlt()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{AufgabePfad(Fotos)}/vorlage", () => Json(Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung", vorlage: true, version: 6)));
        var cut = DetailRendern(Fotos);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Als Vorlage speichern").Click();

        cut.WaitForAssertion(() => Assert.Contains("Steht in den Aufgaben-Vorlagen der Organisation.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Als Vorlage speichern");
    }

    [Fact]
    public void Phase_aendern_speichert_sofort_Konflikt_meldet_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Patch, AufgabePfad(Fotos), () => Json(new { version = 8, phase = "Waehrend" }));
        var cut = DetailRendern(Fotos);

        cut.Find("input[name=aufgabe-phase][value=Waehrend]").Change("Waehrend");

        cut.WaitForAssertion(() => Assert.Contains("Phase gespeichert.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("""{"version":5,"alterWert":"Nachbereitung","phase":"Waehrend"}""", Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Patch).Koerper);

        // Zweite Änderung kennt die neue Version und Phase; der Server meldet einen Konflikt, die Seite lädt neu.
        _server.Bei(HttpMethod.Patch, AufgabePfad(Fotos), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Get, AufgabenPfad, () => Json(Liste().Take(3).Append(Aufgabe(Fotos, "Fotos sammeln", "Vorbereitung", version: 9)).ToArray()));
        cut.Find("input[name=aufgabe-phase][value=Nachbereitung]").Change("Nachbereitung");

        cut.WaitForAssertion(() => Assert.Contains(AufgabeSeite.PhaseKonfliktText, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("""{"version":8,"alterWert":"Waehrend","phase":"Nachbereitung"}""", _server.Anfragen.Last(a => a.Methode == HttpMethod.Patch).Koerper);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input[name=aufgabe-phase][value=Vorbereitung]").GetAttribute("checked")));
    }

    [Fact]
    public void Loeschen_mit_Inline_Bestaetigung_fuehrt_zur_Liste()
    {
        Antworten();
        _server.Bei(HttpMethod.Delete, AufgabePfad(Fotos), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = DetailRendern(Fotos);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Aufgabe löschen").Click();
        Assert.Equal("Diese Aufgabe löschen?", cut.Find(".zl-bestaetigung__satz").TextContent);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/aufgaben", Navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void Detail_nach_dem_Abschluss_nur_lesbar_unbekannte_Aufgabe_nicht_verfuegbar()
    {
        Antworten("Gelaufen");

        var cut = DetailRendern(Fotos);

        Assert.Contains("Nachbereitung", cut.Find(".elkw-details, dl").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("#aufgabe-titel"));
        Assert.Equal("Diese Seite ist nicht verfügbar.", DetailRendern(Guid.CreateVersion7()).Find("h1").TextContent);
    }

    // ---------- Programmpunkt ----------

    [Fact]
    public void Programmpunkt_zeigt_seine_Aufgaben_ohne_Hinweis_auf_den_Punkt_und_fuegt_eine_hinzu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, AufgabenPfad, () => Json(Aufgabe(Guid.CreateVersion7(), "Liederzettel drucken", punkt: Lagerfeuer), HttpStatusCode.Created));
        var cut = Render<ProgrammpunktSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PunktId, Lagerfeuer));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#aufgaben")));

        var card = cut.Find("#aufgaben");
        Assert.Equal(["Andacht am Lagerfeuer vorbereiten"], card.QuerySelectorAll(".zl-termin h3").Select(h => h.TextContent.Trim()));
        Assert.DoesNotContain("Am Programmpunkt", card.TextContent, StringComparison.Ordinal);

        cut.Find("#aufgabe-titel").Input("Liederzettel drucken");
        cut.FindAll("form").Single(f => f.QuerySelector("#aufgabe-titel") is not null).Submit();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Andacht am Lagerfeuer vorbereiten", "Liederzettel drucken"], cut.Find("#aufgaben").QuerySelectorAll(".zl-termin h3").Select(h => h.TextContent.Trim())));
        Assert.Equal(
            $$"""{"titel":"Liederzettel drucken","phase":"Vorbereitung","programmpunktId":"{{Lagerfeuer}}"}""",
            _server.Anfragen.Last(a => a.Methode == HttpMethod.Post && a.Pfad == AufgabenPfad).Koerper);
    }

    // ---------- Startseite ----------

    private IRenderedComponent<FreizeitStart> StartRendern()
    {
        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("a"), a => a.TextContent == "Aufgaben"));
        return cut;
    }

    [Fact]
    public void Startseite_Team_Meine_Aufgaben_ohne_Warnhinweis_und_Knopf_Aufgaben()
    {
        // Mit einer offenen, unbesetzten Vorbereitungs-Aufgabe: die Leitung sähe einen Warnhinweis, das Team nicht.
        Antworten(aufgaben: () => Json(Liste(Aufgabe(Guid.CreateVersion7(), "Zeltplan zeichnen"))));

        var cut = StartRendern();

        var card = cut.Find("#meine-aufgaben");
        Assert.Equal("Meine Aufgaben", card.QuerySelector("h2")!.TextContent);
        // Nur eigene, offene Aufgaben; die erledigte von Jonas und fremde fehlen.
        Assert.Equal(["Erste-Hilfe-Kasten auffüllen"], card.QuerySelectorAll("li a").Select(a => a.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/aufgaben/{Kasten}", card.QuerySelector("li a")!.GetAttribute("href"));
        Assert.Equal($"freizeiten/{Freizeit}/aufgaben", card.QuerySelectorAll("a").Single(a => a.TextContent == "Alle Aufgaben").GetAttribute("href"));
        Assert.DoesNotContain("Schaden melden", card.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("noch nicht übernommen", cut.Markup, StringComparison.Ordinal);
        Assert.Equal($"freizeiten/{Freizeit}/aufgaben", cut.FindAll("a").Single(a => a.TextContent == "Aufgaben").GetAttribute("href"));
        var knoepfe = cut.FindAll("a.elkw-btn").Select(a => a.TextContent).ToList();
        Assert.Equal(knoepfe.IndexOf("Programm") + 1, knoepfe.IndexOf("Aufgaben"));
    }

    [Fact]
    public void Startseite_ohne_eigene_Aufgaben_waehrend_der_Freizeit_mit_Schaden_melden_nach_dem_Abschluss_ohne_Card()
    {
        Antworten("Laeuft", aufgaben: () => Json(new[] { Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung") }));

        var cut = StartRendern();

        var card = cut.Find("#meine-aufgaben");
        Assert.Contains("Keine Aufgaben übernommen.", card.TextContent, StringComparison.Ordinal);
        Assert.Equal($"freizeiten/{Freizeit}/schaeden", card.QuerySelectorAll("a").Single(a => a.TextContent == "Schaden melden").GetAttribute("href"));

        Antworten("Gelaufen");
        var vorher = _server.Anfragen.Count(a => a.Pfad == AufgabenPfad);
        var gelaufen = StartRendern();
        Assert.Empty(gelaufen.FindAll("#meine-aufgaben"));
        Assert.Equal(vorher, _server.Anfragen.Count(a => a.Pfad == AufgabenPfad));

        Antworten(aufgaben: () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Empty(StartRendern().FindAll("#meine-aufgaben"));
    }

    [Fact]
    public void Startseite_alle_eigenen_Aufgaben_erledigt_nennt_das_statt_Keine_Aufgaben_uebernommen()
    {
        Antworten(aufgaben: () => Json(new[] { Aufgabe(Kasten, "Erste-Hilfe-Kasten auffüllen", von: Ich, meins: true, erledigt: true) }));

        var card = StartRendern().Find("#meine-aufgaben");

        Assert.Contains("Alle übernommenen Aufgaben sind erledigt.", card.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Keine Aufgaben übernommen.", card.TextContent, StringComparison.Ordinal);
        Assert.Empty(card.QuerySelectorAll("li"));
    }

    [Theory]
    [InlineData("InVorbereitung", 0)]
    [InlineData("Laeuft", 1)]
    [InlineData("Nachbereitung", 2)]
    public void Leitung_sieht_den_Warnhinweis_mit_den_unbesetzten_offenen_Aufgaben_bis_zur_Phase_der_Freizeit(string phase, int erwartet)
    {
        // Unbesetzt und offen: „Fotos sammeln“ (Nachbereitung) und „Lagerplan“ (Während); unbesetzt, aber erledigt: „Zelte zählen“.
        Antworten(phase, [Aktionsschluessel.FreizeitEinstellungen], () => Json(new[]
        {
            Aufgabe(Kasten, "Erste-Hilfe-Kasten auffüllen", von: Ich, meins: true),
            Aufgabe(Vertrag, "Zelte zählen", erledigt: true),
            Aufgabe(Andacht, "Lagerplan", "Waehrend"),
            Aufgabe(Fotos, "Fotos sammeln", "Nachbereitung"),
        }));

        var cut = StartRendern();

        cut.WaitForAssertion(() => Assert.Contains("Warnhinweise", cut.Markup, StringComparison.Ordinal));
        var links = cut.FindAll("a").Where(a => a.TextContent.Contains("noch nicht übernommen", StringComparison.Ordinal)).ToList();
        if (erwartet == 0)
        {
            Assert.Empty(links);
            return;
        }

        var link = Assert.Single(links);
        Assert.Equal(Anzeige.AufgabenUnbesetzt(erwartet), link.TextContent);
        Assert.Equal($"freizeiten/{Freizeit}/aufgaben", link.GetAttribute("href"));
    }

    // ---------- Organisation ----------

    private void Organisation(params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" }, erlaubt }));

    private IRenderedComponent<VorlagenSeite> VorlagenRendern()
    {
        var cut = Render<VorlagenSeite>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    [Fact]
    public void Organisation_zeigt_die_Card_Aufgaben_Vorlagen_nur_mit_aufgaben_vorlagen_verwalten()
    {
        Organisation(Aktionsschluessel.AufgabenVorlagenVerwalten);
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.Contains("Aufgaben-Vorlagen öffnen", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(
            $"organisationen/{OrgId}/aufgaben-vorlagen",
            cut.FindAll("a").Single(a => a.TextContent.Contains("Aufgaben-Vorlagen öffnen", StringComparison.Ordinal)).GetAttribute("href"));

        Organisation(Aktionsschluessel.InventarSehen);
        var ueberblick = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        ueberblick.WaitForAssertion(() => Assert.Contains("Jugendwerk Musterstadt", ueberblick.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Aufgaben-Vorlagen", ueberblick.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vorlagen_je_Phase_mit_Autosave_Titel_hinzufuegen_und_loeschen()
    {
        Organisation(Aktionsschluessel.AufgabenVorlagenVerwalten);
        var vorlagen = new List<object>
        {
            new { id = Vertrag, version = 3u, titel = "Zeltplatz-Vertrag prüfen", phase = "Vorbereitung" },
            new { id = Uebergabe, version = 4u, titel = "Zeltplatz besenrein übergeben", phase = "Nachbereitung" },
        };
        _server.Bei(HttpMethod.Get, VorlagenPfad, () => Json(vorlagen));
        _server.Bei(HttpMethod.Delete, $"{VorlagenPfad}/{Vertrag}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, VorlagenPfad, () =>
        {
            vorlagen.Insert(1, new { id = Kasten, version = 5u, titel = "Erste-Hilfe-Kasten packen", phase = "Waehrend" });
            return Json(vorlagen[1], HttpStatusCode.Created);
        });
        var cut = VorlagenRendern();

        Assert.Equal(("Organisation", "Aufgaben-Vorlagen"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        Assert.Equal(["Vorlage hinzufügen", "Vorbereitung", "Nachbereitung"], cut.FindAll("section.elkw-card h2").Select(h => h.TextContent));
        Assert.Equal("Zeltplatz-Vertrag prüfen", cut.Find($"#vorlage-{Vertrag}-titel").GetAttribute("value"));

        cut.Find("#vorlage-titel").Input("Erste-Hilfe-Kasten packen");
        cut.Find("input[name=vorlage-phase][value=Waehrend]").Change("Waehrend");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Vorlage hinzufügen", "Vorbereitung", "Während der Freizeit", "Nachbereitung"], cut.FindAll("section.elkw-card h2").Select(h => h.TextContent)));
        Assert.Equal("""{"titel":"Erste-Hilfe-Kasten packen","phase":"Waehrend"}""", _server.Anfragen.Last(a => a.Methode == HttpMethod.Post && a.Pfad == VorlagenPfad).Koerper);

        var vorbereitung = cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Vorbereitung");
        Knopf(vorbereitung, "Vorlage löschen").Click();
        Assert.Equal("„Zeltplatz-Vertrag prüfen“ löschen?", cut.Find(".zl-bestaetigung__satz").TextContent);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain(cut.FindAll("section.elkw-card h2"), h => h.TextContent == "Vorbereitung"));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
    }

    [Fact]
    public void Vorlagen_leer_mit_Satz_ohne_Aktionsschluessel_nicht_verfuegbar()
    {
        Organisation(Aktionsschluessel.AufgabenVorlagenVerwalten);
        _server.Bei(HttpMethod.Get, VorlagenPfad, () => Json(Array.Empty<object>()));

        Assert.Equal(
            "Noch keine Aufgaben-Vorlagen. Neue Freizeiten übernehmen sie beim ersten Öffnen der Aufgaben.",
            VorlagenRendern().Find(".elkw-empty p").TextContent);

        Organisation(Aktionsschluessel.InventarSehen);
        var vorher = _server.Anfragen.Count(a => a.Pfad == VorlagenPfad);
        Assert.Equal("Diese Seite ist nicht verfügbar.", VorlagenRendern().Find("h1").TextContent);
        Assert.Equal(vorher, _server.Anfragen.Count(a => a.Pfad == VorlagenPfad));
    }
}
