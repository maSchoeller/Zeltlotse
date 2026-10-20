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
using Zeltlotse.App.Features.Programm;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using RasterSeite = Zeltlotse.App.Features.Programm.Wochenraster;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 3-1 in der Web-App mit nachgebildetem Server: der Wochenraster (Spalten je Tag, Reiter je Tag mit vorgewähltem Tag, Namen der
/// übernehmenden Personen aus Freizeit und Personenliste, „Ich übernehme“ und „Abgeben“ ohne Bestätigung, Konflikt, Anlegen mit Feldfehlern,
/// nur lesbar nach dem Abschluss, nicht verfügbar), die Detailseite (Autosave von Titel und Uhrzeit, Löschen nur ohne Mahlzeit), der Knopf auf
/// der Startseite und der Client. Story 6-2: „Alle informieren?“ nach dem Verschieben auf der Detailseite und der Hinweis „Geändert, noch nicht
/// mitgeteilt“ mit „Jetzt informieren“ und „Ausblenden“ an Detailseite und Termin.
/// </summary>
public sealed class ProgrammSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000000d1");
    private static readonly Guid Lea = Guid.Parse("0199f700-0000-7000-8000-0000000000e2");
    private static readonly Guid Jonas = Guid.Parse("0199f700-0000-7000-8000-0000000000e3");
    private static readonly Guid Ich = Guid.Parse("0199f700-0000-7000-8000-0000000000e4");
    private static readonly Guid Fruehstueck = Guid.Parse("0199f700-0000-7000-8000-0000000000a1");
    private static readonly Guid Mittag = Guid.Parse("0199f700-0000-7000-8000-0000000000a2");
    private static readonly Guid Anreise = Guid.Parse("0199f700-0000-7000-8000-0000000000a3");
    private static readonly Guid Lagerfeuer = Guid.Parse("0199f700-0000-7000-8000-0000000000a4");
    private static readonly Guid Gelaendespiel = Guid.Parse("0199f700-0000-7000-8000-0000000000a5");
    private static readonly Guid Abbau = Guid.Parse("0199f700-0000-7000-8000-0000000000a6");
    private static readonly Guid Unbekannt = Guid.Parse("0199f700-0000-7000-8000-0000000000a7");

    private readonly NachgebildeterHandler _server = new();
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
    private int _tokens;

    public ProgrammSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddSingleton<TimeProvider>(_uhr);
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string RasterPfad => $"/api/freizeiten/{Freizeit}/programm";

    private static string PunktPfad(Guid punkt) => $"{RasterPfad}/punkte/{punkt}";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Punkt(
        Guid id, string tag, string? beginn, string titel, string? mahlzeit = null, Guid? von = null, bool meins = false, uint version = 5, string? mitgeteilt = null) =>
        new { id, version, tag, beginn, titel, mahlzeit, uebernommenVon = von, meins, mitgeteilterBeginn = mitgeteilt };

    /// <summary>Drei Tage (02. bis 04.08.2027) mit Mahlzeiten nur am ersten Tag und vier Programmpunkten.</summary>
    private static object Raster(params object[] zusaetzlich) => new
    {
        ersterTag = "2027-08-02",
        letzterTag = "2027-08-04",
        punkte = new object[]
        {
            Punkt(Fruehstueck, "2027-08-02", "08:00", "Frühstück", "Fruehstueck"),
            Punkt(Mittag, "2027-08-02", "12:30", "Mittagessen", "Mittagessen"),
            Punkt(Anreise, "2027-08-02", "15:00", "Anreise und Zeltaufbau"),
            Punkt(Lagerfeuer, "2027-08-02", "20:00", "Lagerfeuer", von: Lea),
            Punkt(Gelaendespiel, "2027-08-03", "14:30", "Geländespiel", von: Jonas),
            Punkt(Abbau, "2027-08-04", null, "Abbau", von: Ich, meins: true),
            Punkt(Unbekannt, "2027-08-04", "10:00", "Bachwanderung", von: Guid.Parse("0199f700-0000-7000-8000-0000000000ff")),
        }.Concat(zusaetzlich).ToArray(),
    };

    private void Antworten(string phase = "InVorbereitung", Func<HttpResponseMessage>? raster = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(),
            erlaubt = Array.Empty<string>(),
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
        _server.Bei(HttpMethod.Get, RasterPfad, raster ?? (() => Json(Raster())));
        _server.Bei(HttpMethod.Get, $"{RasterPfad}/material", () => Json(new { material = Array.Empty<object>() }));
    }

    private IRenderedComponent<RasterSeite> RasterRendern(string? tag = null)
    {
        Navigation.NavigateTo(tag is null ? $"freizeiten/{Freizeit}/programm" : $"freizeiten/{Freizeit}/programm?tag={tag}");
        var cut = Render<RasterSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement Spalte(IRenderedComponent<RasterSeite> cut, string kopf) =>
        cut.FindAll(".zl-tagesspalte").Single(s => s.QuerySelector("h2")!.TextContent == kopf);

    private static AngleSharp.Dom.IElement Termin(AngleSharp.Dom.IElement bereich, string titel) =>
        bereich.QuerySelectorAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == titel);

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_den_Raster_403_404_heissen_nicht_verfuegbar_und_die_Aktionen_ordnen_Antworten_zu()
    {
        Antworten();
        var client = Services.GetRequiredService<ProgrammApiClient>();

        var raster = await client.LadenAsync(Freizeit, Abbruch);

        Assert.NotNull(raster);
        Assert.Equal((new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 4), 7), (raster.ErsterTag, raster.LetzterTag, raster.Punkte.Count));
        Assert.Equal(new Programmpunkt(Fruehstueck, 5, new DateOnly(2027, 8, 2), "08:00", "Frühstück", Mahlzeit.Fruehstueck, null, false), raster.Punkte[0]);
        Assert.Equal(PunktPfad(Abbau), ProgrammApiClient.PunktPfad(Freizeit, Abbau));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, RasterPfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.LadenAsync(Freizeit, Abbruch));
        }

        _server.Bei(HttpMethod.Post, $"{PunktPfad(Anreise)}/uebernehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Post, $"{PunktPfad(Anreise)}/abgeben", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));
        _server.Bei(HttpMethod.Delete, PunktPfad(Mittag), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Delete, PunktPfad(Anreise), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, $"{RasterPfad}/punkte", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"titel":["Titel fehlt."],"tag":["Dieser Tag gehört nicht zur Freizeit."]}}""", "application/problem+json"));
        Assert.Equal(ProgrammAktionErgebnis.Konflikt, (await client.UebernehmenAsync(Freizeit, Anreise, Abbruch)).Ergebnis);
        Assert.Equal(ProgrammAktionErgebnis.Abgeschlossen, (await client.AbgebenAsync(Freizeit, Anreise, Abbruch)).Ergebnis);
        Assert.Equal(ProgrammAktionErgebnis.Konflikt, await client.LoeschenAsync(Freizeit, Mittag, Abbruch));
        Assert.Equal(ProgrammAktionErgebnis.Erfolg, await client.LoeschenAsync(Freizeit, Anreise, Abbruch));
        var anlegen = await client.AnlegenAsync(Freizeit, new ProgrammpunktNeu(" ", new DateOnly(2027, 9, 1), null), Abbruch);
        Assert.Equal(ProgrammAktionErgebnis.Fehler, anlegen.Ergebnis);
        Assert.Equal("Titel fehlt.", anlegen.Feldfehler["titel"]);
        Assert.Equal("Dieser Tag gehört nicht zur Freizeit.", anlegen.Feldfehler["tag"]);

        Assert.Equal(("Mo 02.08.", "Montag, 2. August 2027"), (Anzeige.TagKurz(new DateOnly(2027, 8, 2)), Anzeige.TagLang(new DateOnly(2027, 8, 2))));
        Assert.Equal(("Übernommen: Lea Feuerstein", "Übernommen"), (Anzeige.Uebernommen("Lea Feuerstein"), Anzeige.Uebernommen(null)));
    }

    // ---------- Wochenraster ----------

    [Fact]
    public void Laptop_alle_Tage_nebeneinander_mit_Terminen_Uhrzeit_Titel_als_Link_und_Namen_der_Uebernahme()
    {
        Antworten();

        var cut = RasterRendern();

        Assert.Equal("Programm", cut.Find("h1").TextContent);
        Assert.Equal(["Mo 02.08.", "Di 03.08.", "Mi 04.08."], cut.FindAll(".zl-tagesspalte h2").Select(h => h.TextContent));
        var montag = Spalte(cut, "Mo 02.08.");
        Assert.Equal(
            ["08:00 Frühstück", "12:30 Mittagessen", "15:00 Anreise und Zeltaufbau", "20:00 Lagerfeuer"],
            montag.QuerySelectorAll(".zl-termin").Select(t => $"{t.QuerySelector(".elkw-kicker")!.TextContent} {t.QuerySelector("h3")!.TextContent.Trim()}"));
        Assert.Equal($"freizeiten/{Freizeit}/programm/{Lagerfeuer}", Termin(montag, "Lagerfeuer").QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Equal("Übernommen: Lea Feuerstein", Termin(montag, "Lagerfeuer").QuerySelector(".elkw-status")!.TextContent);
        Assert.Equal("Übernommen: Jonas Hüttner", Termin(Spalte(cut, "Di 03.08."), "Geländespiel").QuerySelector(".elkw-status")!.TextContent);
        // Ohne bekannten Namen nur „Übernommen“; ohne Uhrzeit keine Dachzeile.
        var mittwoch = Spalte(cut, "Mi 04.08.");
        Assert.Equal("Übernommen", Termin(mittwoch, "Bachwanderung").QuerySelector(".elkw-status")!.TextContent);
        Assert.Null(Termin(mittwoch, "Abbau").QuerySelector(".elkw-kicker"));
        // „Ich übernehme“ nur an freien Punkten, „Abgeben“ nur an eigenen, sonst kein Knopf.
        Assert.Equal("Ich übernehme", Termin(montag, "Anreise und Zeltaufbau").QuerySelector("button")!.TextContent);
        Assert.Null(Termin(montag, "Lagerfeuer").QuerySelector("button"));
        Assert.Equal("Übernommen: Tobias Wanderer", Termin(mittwoch, "Abbau").QuerySelector(".elkw-status")!.TextContent);
        Assert.Equal("Abgeben", Termin(mittwoch, "Abbau").QuerySelector("button")!.TextContent);
        Assert.NotNull(cut.Find("#programmpunkt-anlegen"));
    }

    [Fact]
    public void Handy_Reiter_je_Tag_ausserhalb_der_Freizeit_ist_der_erste_Tag_gewaehlt()
    {
        Antworten();

        var cut = RasterRendern();

        var reiter = cut.Find(".zl-reiter");
        Assert.Contains("zl-nur-handy", reiter.ClassList);
        var links = reiter.QuerySelectorAll("nav a");
        Assert.Equal(["Mo 02.08.", "Di 03.08.", "Mi 04.08."], links.Select(l => l.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/programm?tag=2027-08-03", links[1].GetAttribute("href"));
        Assert.Equal(["page", null, null], links.Select(l => l.GetAttribute("aria-current")));
        var inhalt = reiter.QuerySelector(".zl-reiter__inhalt")!;
        Assert.Equal("Montag, 2. August 2027", inhalt.QuerySelector("h2")!.TextContent);
        Assert.Equal(4, inhalt.QuerySelectorAll(".zl-termin").Length);
    }

    [Fact]
    public void Handy_heute_ist_vorausgewaehlt_und_die_Adresse_waehlt_einen_anderen_Tag()
    {
        Antworten();
        _uhr.SetUtcNow(new DateTimeOffset(2027, 8, 3, 9, 0, 0, TimeSpan.Zero));

        var heute = RasterRendern();
        Assert.Equal("Dienstag, 3. August 2027", heute.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Equal("Geländespiel", heute.Find(".zl-reiter__inhalt .zl-termin h3").TextContent.Trim());

        var gewaehlt = RasterRendern("2027-08-04");
        Assert.Equal("Mittwoch, 4. August 2027", gewaehlt.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Equal("page", gewaehlt.FindAll(".zl-reiter nav a")[2].GetAttribute("aria-current"));

        // Ein Tag außerhalb der Freizeit zählt nicht.
        Assert.Equal("Dienstag, 3. August 2027", RasterRendern("2027-09-01").Find(".zl-reiter__inhalt h2").TextContent);
    }

    [Fact]
    public void Ich_uebernehme_ohne_Bestaetigung_danach_steht_der_eigene_Name_und_Abgeben_am_Termin()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{PunktPfad(Anreise)}/uebernehmen", () => Json(Punkt(Anreise, "2027-08-02", "15:00", "Anreise und Zeltaufbau", von: Ich, meins: true, version: 6)));
        var cut = RasterRendern();

        Termin(Spalte(cut, "Mo 02.08."), "Anreise und Zeltaufbau").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "Übernommen: Tobias Wanderer", Termin(Spalte(cut, "Mo 02.08."), "Anreise und Zeltaufbau").QuerySelector(".elkw-status")?.TextContent));
        Assert.Equal("Abgeben", Termin(Spalte(cut, "Mo 02.08."), "Anreise und Zeltaufbau").QuerySelector("button")!.TextContent);
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{PunktPfad(Anreise)}/uebernehmen");
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
    }

    [Fact]
    public void Abgeben_macht_den_Punkt_wieder_frei()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{PunktPfad(Abbau)}/abgeben", () => Json(Punkt(Abbau, "2027-08-04", null, "Abbau", version: 7)));
        var cut = RasterRendern();

        Termin(Spalte(cut, "Mi 04.08."), "Abbau").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Equal("Ich übernehme", Termin(Spalte(cut, "Mi 04.08."), "Abbau").QuerySelector("button")?.TextContent));
        Assert.Null(Termin(Spalte(cut, "Mi 04.08."), "Abbau").QuerySelector(".elkw-status"));
    }

    [Fact]
    public void Konflikt_beim_Uebernehmen_meldet_es_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{PunktPfad(Anreise)}/uebernehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = RasterRendern();
        _server.Bei(HttpMethod.Get, RasterPfad, () => Json(new
        {
            ersterTag = "2027-08-02", letzterTag = "2027-08-04",
            punkte = new[] { Punkt(Anreise, "2027-08-02", "15:00", "Anreise und Zeltaufbau", von: Lea) },
        }));

        Termin(Spalte(cut, "Mo 02.08."), "Anreise und Zeltaufbau").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("Jemand anderes hat diesen Programmpunkt inzwischen übernommen.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Equal(
            "Übernommen: Lea Feuerstein", Termin(Spalte(cut, "Mo 02.08."), "Anreise und Zeltaufbau").QuerySelector(".elkw-status")?.TextContent));
    }

    [Fact]
    public void Anlegen_schickt_Titel_Tag_und_Uhrzeit_und_laedt_den_Raster_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{RasterPfad}/punkte", () => Json(Punkt(Guid.CreateVersion7(), "2027-08-03", "20:00", "Nachtwanderung"), HttpStatusCode.Created));
        var cut = RasterRendern();
        Assert.Equal(("2027-08-02", "2027-08-02", "2027-08-04"), (
            cut.Find("#programmpunkt-tag").GetAttribute("value"), cut.Find("#programmpunkt-tag").GetAttribute("min"), cut.Find("#programmpunkt-tag").GetAttribute("max")));
        Assert.Equal(("text", "date", "time"), (
            cut.Find("#programmpunkt-titel").GetAttribute("type"), cut.Find("#programmpunkt-tag").GetAttribute("type"), cut.Find("#programmpunkt-beginn").GetAttribute("type")));
        _server.Bei(HttpMethod.Get, RasterPfad, () => Json(Raster(Punkt(Guid.CreateVersion7(), "2027-08-03", "20:00", "Nachtwanderung"))));

        cut.Find("#programmpunkt-titel").Input("Nachtwanderung");
        cut.Find("#programmpunkt-tag").Input("2027-08-03");
        cut.Find("#programmpunkt-beginn").Input("20:00");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Programmpunkt „Nachtwanderung“ am Di 03.08. angelegt.", cut.Markup, StringComparison.Ordinal));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Post && a.Pfad == $"{RasterPfad}/punkte").Koerper!);
        Assert.Equal(("Nachtwanderung", "2027-08-03", "20:00"), (
            koerper.RootElement.GetProperty("titel").GetString(), koerper.RootElement.GetProperty("tag").GetString(), koerper.RootElement.GetProperty("beginn").GetString()));
        Assert.NotNull(Termin(Spalte(cut, "Di 03.08."), "Nachtwanderung"));
        // Am Handy wechselt der Reiter auf den Tag des neuen Punkts.
        Assert.EndsWith($"freizeiten/{Freizeit}/programm?tag=2027-08-03", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent));
        Assert.NotNull(Termin(cut.Find(".zl-reiter__inhalt"), "Nachtwanderung"));
        Assert.Equal("", cut.Find("#programmpunkt-titel").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Reiterwechsel_auf_derselben_Seite_waehlt_den_Tag_ohne_neues_Laden()
    {
        Antworten();
        var cut = RasterRendern();
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Get && a.Pfad == RasterPfad);

        Navigation.NavigateTo($"freizeiten/{Freizeit}/programm?tag=2027-08-03");

        cut.WaitForAssertion(() => Assert.Equal("Dienstag, 3. August 2027", cut.Find(".zl-reiter__inhalt h2").TextContent));
        Assert.Equal([null, "page", null], cut.FindAll(".zl-reiter nav a").Select(l => l.GetAttribute("aria-current")));
        Assert.Equal("2027-08-03", cut.Find("#programmpunkt-tag").GetAttribute("value"));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Get && a.Pfad == RasterPfad);
    }

    [Fact]
    public void Anlegen_zeigt_Feldfehler_am_Feld()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{RasterPfad}/punkte", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"titel":["Titel fehlt."],"beginn":["Uhrzeit im Format HH:mm, z. B. 14:30."]}}""", "application/problem+json"));
        var cut = RasterRendern();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Titel fehlt.", cut.Find("#programmpunkt-titel-fehler").TextContent));
        Assert.Equal("true", cut.Find("#programmpunkt-titel").GetAttribute("aria-invalid"));
        Assert.Equal("Fehler: Uhrzeit im Format HH:mm, z. B. 14:30.", cut.Find("#programmpunkt-beginn-fehler").TextContent);
    }

    [Fact]
    public void Abgeschlossen_nur_lesbar_ohne_Knoepfe_und_ohne_Anlegen()
    {
        Antworten(phase: "Gelaufen");

        var cut = RasterRendern();

        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".zl-termin button"));
        Assert.Empty(cut.FindAll("#programmpunkt-anlegen"));
        Assert.Equal(7, cut.FindAll(".zl-tagesspalte .zl-termin").Count);
    }

    [Fact]
    public void Nicht_verfuegbar_bei_404_des_Rasters()
    {
        Antworten(raster: () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RasterRendern();

        Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent);
    }

    // ---------- Detailseite ----------

    private IRenderedComponent<ProgrammpunktSeite> DetailRendern(Guid punkt)
    {
        var cut = Render<ProgrammpunktSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PunktId, punkt));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    [Fact]
    public void Detailseite_zeigt_Titel_Tag_Uhrzeit_Uebernahme_Autosave_Felder_und_Loeschen()
    {
        Antworten();

        var cut = DetailRendern(Lagerfeuer);

        Assert.Equal(("Programm", "Programmpunkt"), (cut.Find(".elkw-kicker").TextContent, cut.Find("h1").TextContent));
        Assert.Contains("Montag, 2. August 2027", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("20:00 Uhr", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Übernommen: Lea Feuerstein", cut.Find(".elkw-status").TextContent);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent is "Ich übernehme" or "Abgeben");
        Assert.Equal(("Lagerfeuer", "text"), (cut.Find("#programmpunkt-titel").GetAttribute("value"), cut.Find("#programmpunkt-titel").GetAttribute("type")));
        Assert.Equal(("20:00", "time"), (cut.Find("#programmpunkt-beginn").GetAttribute("value"), cut.Find("#programmpunkt-beginn").GetAttribute("type")));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Programmpunkt löschen");
        Assert.Equal($"freizeiten/{Freizeit}/programm?tag=2027-08-02", cut.FindAll("a").Single(a => a.TextContent == "Zurück zum Programm").GetAttribute("href"));
    }

    [Fact]
    public void Detailseite_einer_Mahlzeit_hat_die_Uhrzeit_zum_Aendern_aber_keinen_Loeschen_Knopf()
    {
        Antworten();
        _server.Bei(HttpMethod.Patch, PunktPfad(Mittag), () => Json(new { version = 6u, beginn = "13:30" }));
        var cut = DetailRendern(Mittag);

        Assert.Contains("Feste Mahlzeit", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Programmpunkt löschen");
        Assert.Equal("Ich übernehme", cut.FindAll("button").Single(b => b.TextContent == "Ich übernehme").TextContent);

        var uhrzeit = cut.Find("#programmpunkt-beginn");
        uhrzeit.Input("13:30");
        uhrzeit.Blur();

        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Patch && a.Pfad == PunktPfad(Mittag)));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal((5u, "12:30", "13:30"), (
            koerper.RootElement.GetProperty("version").GetUInt32(), koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("beginn").GetString()));
    }

    [Fact]
    public void Detailseite_Ich_uebernehme_zeigt_den_eigenen_Namen_und_Abgeben()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{PunktPfad(Mittag)}/uebernehmen", () => Json(Punkt(Mittag, "2027-08-02", "12:30", "Mittagessen", "Mittagessen", von: Ich, meins: true, version: 6)));
        var cut = DetailRendern(Mittag);

        cut.FindAll("button").Single(b => b.TextContent == "Ich übernehme").Click();

        cut.WaitForAssertion(() => Assert.Equal("Übernommen: Tobias Wanderer", cut.Find(".elkw-status").TextContent));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Abgeben");
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Ich übernehme");
    }

    [Fact]
    public void Detailseite_Konflikt_beim_Uebernehmen_meldet_es_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{PunktPfad(Mittag)}/uebernehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = DetailRendern(Mittag);
        _server.Bei(HttpMethod.Get, RasterPfad, () => Json(new
        {
            ersterTag = "2027-08-02", letzterTag = "2027-08-04",
            punkte = new[] { Punkt(Mittag, "2027-08-02", "12:30", "Mittagessen", "Mittagessen", von: Lea, version: 6) },
        }));

        cut.FindAll("button").Single(b => b.TextContent == "Ich übernehme").Click();

        cut.WaitForAssertion(() => Assert.Contains("Jemand anderes hat diesen Programmpunkt inzwischen übernommen.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Equal("Übernommen: Lea Feuerstein", cut.Find(".elkw-status").TextContent));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent is "Ich übernehme" or "Abgeben");
    }

    [Fact]
    public void Loeschen_klappt_die_Bestaetigung_auf_und_fuehrt_danach_zum_Raster_des_Tages()
    {
        Antworten();
        _server.Bei(HttpMethod.Delete, PunktPfad(Anreise), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = DetailRendern(Anreise);

        cut.FindAll("button").Single(b => b.TextContent == "Programmpunkt löschen").Click();
        Assert.Equal("Diesen Programmpunkt löschen?", cut.Find(".zl-bestaetigung__satz").TextContent);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"freizeiten/{Freizeit}/programm?tag=2027-08-02", Navigation.Uri, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
    }

    [Fact]
    public void Detailseite_abgeschlossen_ohne_Felder_Knoepfe_und_Loeschen()
    {
        Antworten(phase: "Gelaufen");

        var cut = DetailRendern(Abbau);

        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("Mittwoch, 4. August 2027", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(["Abbau", "ohne Uhrzeit"], cut.FindAll("dd").Select(d => d.TextContent));
    }

    [Fact]
    public void Detailseite_eines_unbekannten_Punkts_ist_nicht_verfuegbar()
    {
        Antworten();

        var cut = DetailRendern(Guid.CreateVersion7());

        Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent);
    }

    // ---------- Story 6-2: „Alle informieren?“ ----------

    private static string KanalAllePfad => $"/api/freizeiten/{Freizeit}/kanaele/alle/nachrichten";

    private static string MitgeteiltPfad(Guid punkt) => $"{PunktPfad(punkt)}/mitgeteilt";

    /// <summary>Der Raster mit dem Mittagessen am Montag; ist <paramref name="mitgeteilt"/> gesetzt, ist es verschoben und nicht mitgeteilt.</summary>
    private static object RasterMitMittag(string beginn, Func<string?> mitgeteilt) => new
    {
        ersterTag = "2027-08-02",
        letzterTag = "2027-08-04",
        punkte = new[] { Punkt(Mittag, "2027-08-02", beginn, "Mittagessen", "Mittagessen", version: 6, mitgeteilt: mitgeteilt()) },
    };

    /// <summary>Antworten mit verschobenem Mittagessen (13:30 statt 12:30); „mitgeteilt“ leert den Hinweis, „Alle“ nimmt die Nachricht an.</summary>
    private void VerschobenAntworten(string phase = "InVorbereitung", HttpStatusCode senden = HttpStatusCode.Created)
    {
        string? mitgeteilt = "12:30";
        Antworten(phase, () => Json(RasterMitMittag("13:30", () => mitgeteilt)));
        _server.Bei(HttpMethod.Post, KanalAllePfad, () => senden == HttpStatusCode.Created
            ? Json(new { id = Guid.CreateVersion7(), autorPersonId = Ich, text = "x", gesendetAm = "2026-10-13T08:00:00Z" }, HttpStatusCode.Created)
            : new HttpResponseMessage(senden));
        _server.Bei(HttpMethod.Post, MitgeteiltPfad(Mittag), () =>
        {
            mitgeteilt = null;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
    }

    [Fact]
    public void Texte_der_Mitteilung_nennen_Titel_Tag_neue_und_alte_Uhrzeit()
    {
        var punkt = new Programmpunkt(Gelaendespiel, 5, new DateOnly(2027, 8, 3), "14:30", "Geländespiel", null, null, false, MitgeteilterBeginn: "14:00");

        Assert.Equal("Geländespiel am Di 03.08.: 14:30 statt 14:00", ProgrammTexte.Mitteilung(punkt));
        Assert.Equal("Alle informieren? „Geländespiel am Di 03.08.: 14:30 statt 14:00“", ProgrammTexte.MitteilenSatz(punkt));
        Assert.Equal("Geländespiel am Di 03.08.: ohne Uhrzeit statt 14:00", ProgrammTexte.Mitteilung(punkt with { Beginn = null }));
    }

    [Fact]
    public async Task Client_mitgeteilt_schickt_die_Uhrzeit_und_ordnet_die_Antworten_zu()
    {
        Antworten();
        var client = Services.GetRequiredService<ProgrammApiClient>();
        _server.Bei(HttpMethod.Post, MitgeteiltPfad(Mittag), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, MitgeteiltPfad(Anreise), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Post, MitgeteiltPfad(Abbau), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));

        Assert.Equal(ProgrammAktionErgebnis.Erfolg, await client.MitgeteiltAsync(Freizeit, Mittag, "13:30", Abbruch));
        Assert.Equal("""{"beginn":"13:30"}""", _server.Anfragen.Last().Koerper);
        Assert.Equal(ProgrammAktionErgebnis.Konflikt, await client.MitgeteiltAsync(Freizeit, Anreise, null, Abbruch));
        Assert.Equal(ProgrammAktionErgebnis.Abgeschlossen, await client.MitgeteiltAsync(Freizeit, Abbau, "08:00", Abbruch));
        Assert.Equal(ProgrammAktionErgebnis.Fehler, await client.MitgeteiltAsync(Freizeit, Lagerfeuer, "20:00", Abbruch));
    }

    [Fact]
    public void Detailseite_nach_dem_Verschieben_klappt_Alle_informieren_mit_vorbereitetem_Text_auf_Fokus_auf_Nein()
    {
        Antworten();
        _server.Bei(HttpMethod.Patch, PunktPfad(Mittag), () => Json(new { version = 6u, beginn = "13:30" }));
        var cut = DetailRendern(Mittag);
        Assert.Empty(cut.FindAll(".zl-hinweiszeile"));
        _server.Bei(HttpMethod.Get, RasterPfad, () => Json(RasterMitMittag("13:30", () => "12:30")));

        var uhrzeit = cut.Find("#programmpunkt-beginn");
        uhrzeit.Input("13:30");
        uhrzeit.Blur();

        cut.WaitForAssertion(() => Assert.Equal("Alle informieren? „Mittagessen am Mo 02.08.: 13:30 statt 12:30“", cut.Find(".zl-bestaetigung__satz").TextContent));
        Assert.Equal("Geändert, noch nicht mitgeteilt", cut.Find(".zl-hinweiszeile__text").TextContent);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Ja, informieren");
        var nein = cut.FindAll("button").Single(b => b.TextContent == "Nein");
        cut.WaitForAssertion(() =>
        {
            var fokus = JSInterop.Invocations.Last(i => i.Identifier == "Blazor._internal.domWrapper.focus");
            Assert.Equal(nein.GetAttribute("blazor:elementReference"), ((ElementReference)fokus.Arguments[0]!).Id);
        });
        // Das Verschieben selbst verschickt nichts.
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == KanalAllePfad || a.Pfad == MitgeteiltPfad(Mittag));
    }

    [Fact]
    public void Detailseite_Ja_informieren_sendet_in_Alle_meldet_mitgeteilt_und_laedt_neu()
    {
        VerschobenAntworten();
        var cut = DetailRendern(Mittag);
        // Beim Öffnen der Seite ist nur der Hinweis da, die Bestätigung bleibt zu.
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
        Assert.Equal("Geändert, noch nicht mitgeteilt", cut.Find(".zl-hinweiszeile__text").TextContent);

        cut.FindAll("button").Single(b => b.TextContent == "Jetzt informieren").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, informieren").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-hinweiszeile")));
        var senden = _server.Anfragen.FindIndex(a => a.Methode == HttpMethod.Post && a.Pfad == KanalAllePfad);
        var mitgeteilt = _server.Anfragen.FindIndex(a => a.Methode == HttpMethod.Post && a.Pfad == MitgeteiltPfad(Mittag));
        var neuGeladen = _server.Anfragen.FindLastIndex(a => a.Methode == HttpMethod.Get && a.Pfad == RasterPfad);
        Assert.True(senden >= 0 && senden < mitgeteilt && mitgeteilt < neuGeladen, $"{senden} {mitgeteilt} {neuGeladen}");
        Assert.Equal("""{"text":"Mittagessen am Mo 02.08.: 13:30 statt 12:30","bestaetigt":true}""", _server.Anfragen[senden].Koerper);
        Assert.Equal("""{"beginn":"13:30"}""", _server.Anfragen[mitgeteilt].Koerper);
        Assert.Contains("Alle informiert: „Mittagessen am Mo 02.08.: 13:30 statt 12:30“", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detailseite_Fehler_beim_Senden_meldet_es_und_der_Hinweis_bleibt()
    {
        VerschobenAntworten(senden: HttpStatusCode.InternalServerError);
        var cut = DetailRendern(Mittag);

        cut.FindAll("button").Single(b => b.TextContent == "Jetzt informieren").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, informieren").Click();

        cut.WaitForAssertion(() => Assert.Contains("Die Nachricht wurde nicht gesendet.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Equal("Geändert, noch nicht mitgeteilt", cut.Find(".zl-hinweiszeile__text").TextContent);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == MitgeteiltPfad(Mittag));
    }

    [Fact]
    public void Detailseite_Nein_schickt_nichts_der_Hinweis_bleibt_mit_Jetzt_informieren_und_Ausblenden()
    {
        VerschobenAntworten();
        var cut = DetailRendern(Mittag);

        cut.FindAll("button").Single(b => b.TextContent == "Jetzt informieren").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Nein").Click();

        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
        Assert.Equal("Geändert, noch nicht mitgeteilt", cut.Find(".zl-hinweiszeile__text").TextContent);
        Assert.Equal(["Jetzt informieren", "Ausblenden"], cut.FindAll(".zl-hinweiszeile button").Select(b => b.TextContent));
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post);
    }

    [Fact]
    public void Detailseite_Ausblenden_meldet_nur_mitgeteilt()
    {
        VerschobenAntworten();
        var cut = DetailRendern(Mittag);

        cut.FindAll("button").Single(b => b.TextContent == "Ausblenden").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-hinweiszeile")));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post);
        Assert.Single(_server.Anfragen, a => a.Pfad == MitgeteiltPfad(Mittag));
    }

    [Fact]
    public void Detailseite_abgeschlossen_zeigt_den_Hinweis_ohne_Knoepfe()
    {
        VerschobenAntworten(phase: "Gelaufen");

        var cut = DetailRendern(Mittag);

        Assert.Equal("Geändert, noch nicht mitgeteilt", cut.Find(".zl-hinweiszeile__text").TextContent);
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Wochenraster_zeigt_den_Hinweis_am_Termin_Ja_informieren_sendet_meldet_mitgeteilt_und_laedt_neu()
    {
        VerschobenAntworten();
        var cut = RasterRendern();
        var termin = Termin(Spalte(cut, "Mo 02.08."), "Mittagessen");
        Assert.Equal("Geändert, noch nicht mitgeteilt", termin.QuerySelector(".zl-hinweiszeile__text")!.TextContent);
        Assert.Equal(["Jetzt informieren", "Ausblenden"], termin.QuerySelectorAll(".zl-hinweiszeile button").Select(b => b.TextContent));

        termin.QuerySelectorAll("button").Single(b => b.TextContent == "Jetzt informieren").Click();
        termin = Termin(Spalte(cut, "Mo 02.08."), "Mittagessen");
        Assert.Equal("Alle informieren? „Mittagessen am Mo 02.08.: 13:30 statt 12:30“", termin.QuerySelector(".zl-bestaetigung__satz")!.TextContent);
        termin.QuerySelectorAll("button").Single(b => b.TextContent == "Ja, informieren").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-hinweiszeile")));
        Assert.Equal([KanalAllePfad, MitgeteiltPfad(Mittag)], _server.Anfragen.Where(a => a.Methode == HttpMethod.Post).Select(a => a.Pfad));
        Assert.Contains("Alle informiert: „Mittagessen am Mo 02.08.: 13:30 statt 12:30“", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Wochenraster_Ausblenden_meldet_nur_mitgeteilt()
    {
        VerschobenAntworten();
        var cut = RasterRendern();

        Termin(Spalte(cut, "Mo 02.08."), "Mittagessen").QuerySelectorAll("button").Single(b => b.TextContent == "Ausblenden").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-hinweiszeile")));
        Assert.Equal([MitgeteiltPfad(Mittag)], _server.Anfragen.Where(a => a.Methode == HttpMethod.Post).Select(a => a.Pfad));
    }

    [Fact]
    public void Wochenraster_abgeschlossen_zeigt_den_Hinweis_ohne_Knoepfe()
    {
        VerschobenAntworten(phase: "Gelaufen");

        var cut = RasterRendern();

        var termin = Termin(Spalte(cut, "Mo 02.08."), "Mittagessen");
        Assert.Equal("Geändert, noch nicht mitgeteilt", termin.QuerySelector(".zl-hinweiszeile__text")!.TextContent);
        Assert.Empty(termin.QuerySelectorAll("button"));
    }

    [Fact]
    public void Detailseite_ohne_Hinweis_nach_dem_Speichern_bleibt_Gespeichert_mit_Rueckgaengig_stehen()
    {
        Antworten();
        _server.Bei(HttpMethod.Patch, PunktPfad(Lagerfeuer), () => Json(new { version = 6u, beginn = "20:30" }));
        var cut = DetailRendern(Lagerfeuer);

        var uhrzeit = cut.Find("#programmpunkt-beginn");
        uhrzeit.Input("20:30");
        uhrzeit.Blur();

        cut.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == RasterPfad)));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Rückgängig"));
        Assert.Empty(cut.FindAll(".zl-hinweiszeile"));
    }

    [Fact]
    public void Detailseite_ein_neuer_Titel_steht_im_vorbereiteten_Text()
    {
        VerschobenAntworten();
        _server.Bei(HttpMethod.Patch, PunktPfad(Mittag), () => Json(new { version = 7u, titel = "Grillen" }));
        var cut = DetailRendern(Mittag);

        var titel = cut.Find("#programmpunkt-titel");
        titel.Input("Grillen");
        titel.Blur();
        cut.WaitForAssertion(() => Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Patch));
        cut.FindAll("button").Single(b => b.TextContent == "Jetzt informieren").Click();

        Assert.Equal("Alle informieren? „Grillen am Mo 02.08.: 13:30 statt 12:30“", cut.Find(".zl-bestaetigung__satz").TextContent);
    }

    private void JaInformieren(IRenderedComponent<ProgrammpunktSeite> cut)
    {
        cut.FindAll("button").Single(b => b.TextContent == "Jetzt informieren").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, informieren").Click();
    }

    [Fact]
    public void Detailseite_veralteter_Punkt_sendet_nichts_meldet_es_und_laedt_neu()
    {
        VerschobenAntworten();
        var cut = DetailRendern(Mittag);
        // Inzwischen hat jemand anderes auf 14:00 verschoben.
        _server.Bei(HttpMethod.Get, RasterPfad, () => Json(RasterMitMittag("14:00", () => "12:30")));

        JaInformieren(cut);

        cut.WaitForAssertion(() => Assert.Contains(MitteilungsHinweis.ErneutVerschobenText, cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post);
        cut.FindAll("button").Single(b => b.TextContent == "Jetzt informieren").Click();
        Assert.Equal("Alle informieren? „Mittagessen am Mo 02.08.: 14:00 statt 12:30“", cut.Find(".zl-bestaetigung__satz").TextContent);
    }

    [Fact]
    public void Detailseite_mitgeteilt_409_nach_dem_Senden_meldet_erneut_verschoben()
    {
        VerschobenAntworten();
        _server.Bei(HttpMethod.Post, MitgeteiltPfad(Mittag), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = DetailRendern(Mittag);

        JaInformieren(cut);

        cut.WaitForAssertion(() => Assert.Contains(MitteilungsHinweis.ErneutVerschobenText, cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == KanalAllePfad);
    }

    [Fact]
    public void Detailseite_gesendet_aber_mitgeteilt_scheitert_meldet_es_ohne_zweites_Senden_anzubieten()
    {
        VerschobenAntworten();
        _server.Bei(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == MitgeteiltPfad(Mittag), _ => throw new HttpRequestException("weg"));
        var cut = DetailRendern(Mittag);

        JaInformieren(cut);

        cut.WaitForAssertion(() => Assert.Contains(MitteilungsHinweis.GesendetHinweisBleibtText, cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(MitteilungsHinweis.NichtGesendetText, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detailseite_verschieben_Nein_erneut_verschieben_klappt_die_Bestaetigung_mit_neuem_Text_wieder_auf()
    {
        var (beginn, mitgeteilt) = ("12:30", (string?)null);
        Antworten(raster: () => Json(RasterMitMittag(beginn, () => mitgeteilt)));
        _server.BeiDerReihe(HttpMethod.Patch, PunktPfad(Mittag),
            () => Json(new { version = 6u, beginn = "13:30" }), () => Json(new { version = 6u, beginn = "14:00" }));
        var cut = DetailRendern(Mittag);

        (beginn, mitgeteilt) = ("13:30", "12:30");
        cut.Find("#programmpunkt-beginn").Input("13:30");
        cut.Find("#programmpunkt-beginn").Blur();
        cut.WaitForAssertion(() => Assert.Equal("Alle informieren? „Mittagessen am Mo 02.08.: 13:30 statt 12:30“", cut.Find(".zl-bestaetigung__satz").TextContent));
        cut.FindAll("button").Single(b => b.TextContent == "Nein").Click();
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));

        beginn = "14:00";
        cut.Find("#programmpunkt-beginn").Input("14:00");
        cut.Find("#programmpunkt-beginn").Blur();

        cut.WaitForAssertion(() => Assert.Equal("Alle informieren? „Mittagessen am Mo 02.08.: 14:00 statt 12:30“", cut.Find(".zl-bestaetigung__satz").TextContent));
    }

    [Fact]
    public void Detailseite_zurueckschieben_auf_die_mitgeteilte_Uhrzeit_nimmt_den_Hinweis_weg()
    {
        VerschobenAntworten();
        _server.Bei(HttpMethod.Patch, PunktPfad(Mittag), () => Json(new { version = 7u, beginn = "12:30" }));
        var cut = DetailRendern(Mittag);
        Assert.Single(cut.FindAll(".zl-hinweiszeile"));
        _server.Bei(HttpMethod.Get, RasterPfad, () => Json(RasterMitMittag("12:30", () => null)));

        cut.Find("#programmpunkt-beginn").Input("12:30");
        cut.Find("#programmpunkt-beginn").Blur();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".zl-hinweiszeile")));
    }

    [Fact]
    public void Detailseite_Ausblenden_409_meldet_erneut_verschoben_und_laedt_neu()
    {
        VerschobenAntworten();
        _server.Bei(HttpMethod.Post, MitgeteiltPfad(Mittag), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = DetailRendern(Mittag);
        var geladen = _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == RasterPfad);

        cut.FindAll("button").Single(b => b.TextContent == "Ausblenden").Click();

        cut.WaitForAssertion(() => Assert.Contains("Der Programmpunkt wurde inzwischen erneut verschoben.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Equal(geladen + 1, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == RasterPfad));
    }

    // ---------- Startseite ----------

    [Fact]
    public void Startseite_hat_den_Knopf_Programm()
    {
        Antworten();

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("a"), a => a.TextContent == "Programm"));
        Assert.Equal($"freizeiten/{Freizeit}/programm", cut.FindAll("a").Single(a => a.TextContent == "Programm").GetAttribute("href"));
    }
}
