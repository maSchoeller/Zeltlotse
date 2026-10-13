using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Programm;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using ListeSeite = Zeltlotse.App.Features.Programm.Materialliste;
using RasterSeite = Zeltlotse.App.Features.Programm.Wochenraster;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 3-2 in der Web-App mit nachgebildetem Server: die Card „Material“ auf der Detailseite (Autosave von Bezeichnung und Menge, Status per
/// Segmente mit „Gespeichert · Rückgängig“, „Ich bringe das mit“ nur für sich selbst, Anlegen mit Feldfehlern, Löschen, leer, nur lesbar), die
/// Materialliste (Card je Programmpunkt, Balken „N von M eingepackt“, leer, nur lesbar), der Link im Wochenraster und der Client.
/// </summary>
public sealed class MaterialSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199f700-0000-7000-8000-0000000001d1");
    private static readonly Guid Miriam = Guid.Parse("0199f700-0000-7000-8000-0000000001e1");
    private static readonly Guid Jonas = Guid.Parse("0199f700-0000-7000-8000-0000000001e2");
    private static readonly Guid Lea = Guid.Parse("0199f700-0000-7000-8000-0000000001e3");
    private static readonly Guid Gelaendespiel = Guid.Parse("0199f700-0000-7000-8000-0000000001a1");
    private static readonly Guid Lagerfeuer = Guid.Parse("0199f700-0000-7000-8000-0000000001a2");
    private static readonly Guid Fahnen = Guid.Parse("0199f700-0000-7000-8000-0000000001b1");
    private static readonly Guid Seile = Guid.Parse("0199f700-0000-7000-8000-0000000001b2");
    private static readonly Guid Taschenlampen = Guid.Parse("0199f700-0000-7000-8000-0000000001b3");
    private static readonly Guid Feuerholz = Guid.Parse("0199f700-0000-7000-8000-0000000001b4");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public MaterialSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
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

    private static string RasterPfad => $"/api/freizeiten/{Freizeit}/programm";

    private static string ListePfad => $"{RasterPfad}/material";

    private static string MaterialPfad(Guid material) => $"{ListePfad}/{material}";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Material(
        Guid id, Guid punkt, string bezeichnung, string? menge, string status = "Gebraucht", Guid? bringtMit = null, bool meins = false, uint version = 5)
    {
        var (titel, tag, beginn) = punkt == Gelaendespiel ? ("Geländespiel", "2027-08-03", "14:30") : ("Lagerfeuer", "2027-08-02", "20:00");
        return new { id, version, programmpunktId = punkt, programmpunkt = titel, tag, beginn, bezeichnung, menge, status, bringtMit, meins };
    }

    private static object[] Vorgabe =>
    [
        Material(Feuerholz, Lagerfeuer, "Feuerholz", "2 Kisten", "Eingepackt"),
        Material(Fahnen, Gelaendespiel, "Fahnen", "8 Stück", "Vorhanden"),
        Material(Seile, Gelaendespiel, "Seile", "4 × 10 m"),
        Material(Taschenlampen, Gelaendespiel, "Taschenlampen", "10 Stück", bringtMit: Lea),
    ];

    private void Antworten(string phase = "InVorbereitung", object[]? material = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(),
            erlaubt = Array.Empty<string>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-04", phase, freigabeBis = "2027-09-12",
            leitung = new[] { new { vorname = "Miriam", nachname = "Zeltner", personId = Miriam }, new { vorname = "Jonas", nachname = "Hüttner", personId = Jonas } },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(new object[]
        {
            new { anmeldungId = Guid.CreateVersion7(), personId = Lea, vorname = "Lea", nachname = "Feuerstein", art = "Mitarbeitende", status = "Bestaetigt" },
        }));
        _server.Bei(HttpMethod.Get, RasterPfad, () => Json(new
        {
            ersterTag = "2027-08-02", letzterTag = "2027-08-04",
            punkte = new object[]
            {
                new { id = Lagerfeuer, version = 3u, tag = "2027-08-02", beginn = "20:00", titel = "Lagerfeuer", mahlzeit = (string?)null, uebernommenVon = (Guid?)null, meins = false },
                new { id = Gelaendespiel, version = 3u, tag = "2027-08-03", beginn = "14:30", titel = "Geländespiel", mahlzeit = (string?)null, uebernommenVon = (Guid?)null, meins = false },
            },
        }));
        _server.Bei(HttpMethod.Get, ListePfad, () => Json(new { material = material ?? Vorgabe }));
    }

    private IRenderedComponent<ProgrammpunktSeite> DetailRendern(Guid punkt = default)
    {
        var cut = Render<ProgrammpunktSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PunktId, punkt == default ? Gelaendespiel : punkt));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<ListeSeite> ListeRendern()
    {
        var cut = Render<ListeSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement MaterialCard(IRenderedComponent<ProgrammpunktSeite> cut) =>
        cut.FindAll("section.elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Material");

    private static AngleSharp.Dom.IElement Eintrag(AngleSharp.Dom.IElement bereich, string bezeichnung) =>
        bereich.QuerySelectorAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == bezeichnung);

    private static AngleSharp.Dom.IElement Knopf(AngleSharp.Dom.IElement bereich, string text) =>
        bereich.QuerySelectorAll("button").Single(b => b.TextContent == text);

    // ---------- Client und Texte ----------

    [Fact]
    public async Task Client_liest_die_Liste_403_404_heissen_nicht_verfuegbar_und_die_Aktionen_ordnen_Antworten_zu()
    {
        Antworten();
        var client = Services.GetRequiredService<ProgrammApiClient>();

        var liste = await client.MaterialLadenAsync(Freizeit, Abbruch);

        Assert.NotNull(liste);
        Assert.Equal(4, liste.Material.Count);
        Assert.Equal(
            new Zeltlotse.Api.Contracts.Programm.Material(Seile, 5, Gelaendespiel, "Geländespiel", new DateOnly(2027, 8, 3), "14:30", "Seile", "4 × 10 m", MaterialStatus.Gebraucht, null, false),
            liste.Material[2]);
        Assert.Equal(MaterialPfad(Seile), ProgrammApiClient.MaterialPfad(Freizeit, Seile));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.MaterialLadenAsync(Freizeit, Abbruch));
        }

        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/mitbringen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/nicht-mitbringen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/status", () => Json(Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", "Eingepackt", version: 6)));
        _server.Bei(HttpMethod.Delete, MaterialPfad(Seile), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, $"{RasterPfad}/punkte/{Gelaendespiel}/material", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"bezeichnung":["Bezeichnung fehlt."]}}""", "application/problem+json"));
        Assert.Equal(ProgrammAktionErgebnis.Konflikt, (await client.MitbringenAsync(Freizeit, Seile, Abbruch)).Ergebnis);
        Assert.Equal(ProgrammAktionErgebnis.Abgeschlossen, (await client.NichtMitbringenAsync(Freizeit, Seile, Abbruch)).Ergebnis);
        var gesetzt = await client.MaterialStatusAsync(Freizeit, Seile, MaterialStatus.Eingepackt, Abbruch);
        Assert.Equal((ProgrammAktionErgebnis.Erfolg, MaterialStatus.Eingepackt), (gesetzt.Ergebnis, gesetzt.Material!.Status));
        using (var koerper = JsonDocument.Parse(_server.Anfragen.Last(a => a.Pfad == $"{MaterialPfad(Seile)}/status").Koerper!))
        {
            Assert.Equal("Eingepackt", koerper.RootElement.GetProperty("status").GetString());
        }

        Assert.Equal(ProgrammAktionErgebnis.Erfolg, await client.MaterialLoeschenAsync(Freizeit, Seile, Abbruch));
        var anlegen = await client.MaterialAnlegenAsync(Freizeit, Gelaendespiel, new MaterialNeu(" ", null), Abbruch);
        Assert.Equal((ProgrammAktionErgebnis.Fehler, "Bezeichnung fehlt."), (anlegen.Ergebnis, anlegen.Feldfehler["bezeichnung"]));

        Assert.Equal(("Bringt mit: Miriam Zeltner", "Bringt jemand mit"), (Anzeige.BringtMit("Miriam Zeltner"), Anzeige.BringtMit(null)));
        Assert.Equal(["gebraucht", "vorhanden", "besorgt", "eingepackt"], Enum.GetValues<MaterialStatus>().Select(Anzeige.MaterialStatus));
        Assert.Equal(
            [Zeltlotse.Ui.Komponenten.StatusArt.Offen, Zeltlotse.Ui.Komponenten.StatusArt.Aktiv, Zeltlotse.Ui.Komponenten.StatusArt.Aktiv, Zeltlotse.Ui.Komponenten.StatusArt.Erfolg],
            Enum.GetValues<MaterialStatus>().Select(Anzeige.MaterialStatusArt));
    }

    // ---------- Detailseite ----------

    [Fact]
    public void Detailseite_Card_Material_zeigt_nur_das_Material_des_Punkts_mit_Feldern_Segmenten_und_Bringt_mit()
    {
        Antworten();

        var cut = DetailRendern();

        var card = MaterialCard(cut);
        Assert.Equal(["Fahnen", "Seile", "Taschenlampen"], card.QuerySelectorAll(".zl-termin h3").Select(h => h.TextContent.Trim()));
        Assert.Equal(("Seile", "4 × 10 m"), (
            card.QuerySelector($"#material-{Seile}-bezeichnung")!.GetAttribute("value"), card.QuerySelector($"#material-{Seile}-menge")!.GetAttribute("value")));
        var segmente = Eintrag(card, "Fahnen").QuerySelector(".elkw-segments")!;
        Assert.Equal("Status von Fahnen", segmente.GetAttribute("aria-label"));
        Assert.Equal(["gebraucht", "vorhanden", "besorgt", "eingepackt"], segmente.QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Equal(["false", "true", "false", "false"], segmente.QuerySelectorAll("button").Select(b => b.GetAttribute("aria-pressed")));
        // Lea bringt die Taschenlampen mit: Status-Wort mit Namen, für alle anderen kein Knopf.
        var taschenlampen = Eintrag(card, "Taschenlampen");
        Assert.Equal("Bringt mit: Lea Feuerstein", taschenlampen.QuerySelector(".elkw-status")!.TextContent);
        Assert.DoesNotContain(taschenlampen.QuerySelectorAll("button"), b => b.TextContent is "Ich bringe das mit" or "Doch nicht mitbringen");
        Assert.NotNull(Knopf(Eintrag(card, "Seile"), "Ich bringe das mit"));
        Assert.NotNull(Knopf(Eintrag(card, "Seile"), "Material löschen"));
        Assert.Equal(("text", "text"), (card.QuerySelector("#material-bezeichnung")!.GetAttribute("type"), card.QuerySelector("#material-menge")!.GetAttribute("type")));
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent == "Material hinzufügen");
        Assert.DoesNotContain("Feuerholz", card.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Detailseite_ohne_Material_zeigt_den_Leer_Zustand_und_das_Formular()
    {
        Antworten(material: []);

        var card = MaterialCard(DetailRendern());

        Assert.Equal("Noch kein Material.", card.QuerySelector(".elkw-empty p")!.TextContent);
        Assert.NotNull(card.QuerySelector("#material-bezeichnung"));
    }

    [Fact]
    public void Material_hinzufuegen_schickt_Bezeichnung_und_Menge_danach_steht_es_mit_Status_gebraucht_und_das_Formular_ist_leer()
    {
        var neu = Guid.CreateVersion7();
        Antworten(material: []);
        _server.Bei(HttpMethod.Post, $"{RasterPfad}/punkte/{Gelaendespiel}/material", () => Json(Material(neu, Gelaendespiel, "Seile", "4 × 10 m"), HttpStatusCode.Created));
        var cut = DetailRendern();

        cut.Find("#material-bezeichnung").Input("Seile");
        cut.Find("#material-menge").Input("4 × 10 m");
        cut.FindAll("form").Single(f => f.QuerySelector("#material-bezeichnung") is not null).Submit();

        cut.WaitForAssertion(() => Assert.NotNull(Eintrag(MaterialCard(cut), "Seile")));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Post && a.Pfad.EndsWith("/material", StringComparison.Ordinal)).Koerper!);
        Assert.Equal(("Seile", "4 × 10 m"), (koerper.RootElement.GetProperty("bezeichnung").GetString(), koerper.RootElement.GetProperty("menge").GetString()));
        var eintrag = Eintrag(MaterialCard(cut), "Seile");
        Assert.Equal("true", eintrag.QuerySelectorAll(".elkw-segments button").Single(b => b.TextContent == "gebraucht").GetAttribute("aria-pressed"));
        Assert.Empty(MaterialCard(cut).QuerySelectorAll(".elkw-empty"));
        Assert.Equal("", cut.Find("#material-bezeichnung").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Material_hinzufuegen_zeigt_Feldfehler_am_Feld()
    {
        Antworten(material: []);
        _server.Bei(HttpMethod.Post, $"{RasterPfad}/punkte/{Gelaendespiel}/material", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"bezeichnung":["Bezeichnung fehlt."],"menge":["Höchstens 40 Zeichen."]}}""", "application/problem+json"));
        var cut = DetailRendern();

        cut.FindAll("form").Single(f => f.QuerySelector("#material-bezeichnung") is not null).Submit();

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Bezeichnung fehlt.", cut.Find("#material-bezeichnung-fehler").TextContent));
        Assert.Equal("true", cut.Find("#material-bezeichnung").GetAttribute("aria-invalid"));
        Assert.Equal("Fehler: Höchstens 40 Zeichen.", cut.Find("#material-menge-fehler").TextContent);
    }

    [Fact]
    public void Segment_eingepackt_speichert_sofort_Gespeichert_Rueckgaengig_und_Rueckgaengig_stellt_gebraucht_wieder_her()
    {
        Antworten();
        _server.BeiDerReihe(HttpMethod.Post, $"{MaterialPfad(Seile)}/status",
            () => Json(Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", "Eingepackt", version: 6)),
            () => Json(Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", "Gebraucht", version: 7)));
        var cut = DetailRendern();

        Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll(".elkw-segments button").Single(b => b.TextContent == "eingepackt").Click();

        cut.WaitForAssertion(() => Assert.StartsWith("Gespeichert · ", Eintrag(MaterialCard(cut), "Seile").QuerySelector("p[role=status]")?.TextContent, StringComparison.Ordinal));
        Assert.Equal(["false", "false", "false", "true"], Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll(".elkw-segments button").Select(b => b.GetAttribute("aria-pressed")));
        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Rückgängig").Click();

        cut.WaitForAssertion(() => Assert.Equal("Gespeichert", Eintrag(MaterialCard(cut), "Seile").QuerySelector("p[role=status]")?.TextContent));
        Assert.Equal(["true", "false", "false", "false"], Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll(".elkw-segments button").Select(b => b.GetAttribute("aria-pressed")));
        var koerper = _server.Anfragen.Where(a => a.Pfad == $"{MaterialPfad(Seile)}/status").Select(a => JsonDocument.Parse(a.Koerper!).RootElement.GetProperty("status").GetString());
        Assert.Equal(["Eingepackt", "Gebraucht"], koerper);
    }

    [Fact]
    public void Status_nicht_gespeichert_zeigt_Erneut_versuchen()
    {
        Antworten();
        _server.BeiDerReihe(HttpMethod.Post, $"{MaterialPfad(Seile)}/status",
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => Json(Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", "Besorgt", version: 6)));
        var cut = DetailRendern();

        Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll(".elkw-segments button").Single(b => b.TextContent == "besorgt").Click();

        cut.WaitForAssertion(() => Assert.StartsWith("Nicht gespeichert.", Eintrag(MaterialCard(cut), "Seile").QuerySelector("p[role=alert]")?.TextContent, StringComparison.Ordinal));
        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Erneut versuchen").Click();
        cut.WaitForAssertion(() => Assert.Equal("true", Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll(".elkw-segments button")[2].GetAttribute("aria-pressed")));
    }

    [Fact]
    public void Ich_bringe_das_mit_ohne_Bestaetigung_danach_Bringt_mit_und_Doch_nicht_mitbringen()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/mitbringen", () => Json(Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", bringtMit: Miriam, meins: true, version: 6)));
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/nicht-mitbringen", () => Json(Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", version: 7)));
        var cut = DetailRendern();

        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Ich bringe das mit").Click();

        cut.WaitForAssertion(() => Assert.Equal("Bringt mit: Miriam Zeltner", Eintrag(MaterialCard(cut), "Seile").QuerySelector(".elkw-status")?.TextContent));
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Doch nicht mitbringen").Click();
        cut.WaitForAssertion(() => Assert.Null(Eintrag(MaterialCard(cut), "Seile").QuerySelector(".elkw-status")));
        Assert.NotNull(Knopf(Eintrag(MaterialCard(cut), "Seile"), "Ich bringe das mit"));
    }

    [Fact]
    public void Konflikt_beim_Mitbringen_meldet_es_und_laedt_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/mitbringen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = DetailRendern();
        _server.Bei(HttpMethod.Get, ListePfad, () => Json(new { material = new[] { Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", bringtMit: Jonas, version: 6) } }));

        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Ich bringe das mit").Click();

        cut.WaitForAssertion(() => Assert.Contains("Jemand anderes bringt dieses Material inzwischen mit.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Equal("Bringt mit: Jonas Hüttner", Eintrag(MaterialCard(cut), "Seile").QuerySelector(".elkw-status")?.TextContent));
    }

    [Fact]
    public void Bezeichnung_speichert_per_Autosave_und_die_Ueberschrift_folgt()
    {
        Antworten();
        _server.Bei(HttpMethod.Patch, MaterialPfad(Seile), () => Json(new { version = 6u, bezeichnung = "Kletterseile" }));
        var cut = DetailRendern();

        var feld = cut.Find($"#material-{Seile}-bezeichnung");
        feld.Focus();
        feld.Input("Kletterseile");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.NotNull(Eintrag(MaterialCard(cut), "Kletterseile")));
        using var koerper = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!);
        Assert.Equal((5u, "Seile", "Kletterseile"), (
            koerper.RootElement.GetProperty("version").GetUInt32(), koerper.RootElement.GetProperty("alterWert").GetString(), koerper.RootElement.GetProperty("bezeichnung").GetString()));
        Assert.Equal("Status von Kletterseile", Eintrag(MaterialCard(cut), "Kletterseile").QuerySelector(".elkw-segments")!.GetAttribute("aria-label"));
    }

    [Fact]
    public void Material_loeschen_klappt_die_Bestaetigung_auf_und_nimmt_es_aus_der_Card()
    {
        Antworten();
        _server.Bei(HttpMethod.Delete, MaterialPfad(Seile), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = DetailRendern();

        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Material löschen").Click();
        Assert.Equal("„Seile“ löschen?", Eintrag(MaterialCard(cut), "Seile").QuerySelector(".zl-bestaetigung__satz")!.TextContent);
        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.Equal(["Fahnen", "Taschenlampen"], MaterialCard(cut).QuerySelectorAll(".zl-termin h3").Select(h => h.TextContent.Trim())));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
    }

    [Fact]
    public void Detailseite_abgeschlossen_zeigt_das_Material_nur_lesend_mit_Status_als_Wort()
    {
        Antworten(phase: "Gelaufen");

        var card = MaterialCard(DetailRendern());

        Assert.Empty(card.QuerySelectorAll("input"));
        Assert.Empty(card.QuerySelectorAll("button"));
        var fahnen = Eintrag(card, "Fahnen");
        Assert.Equal("Menge: 8 Stück", fahnen.QuerySelector(".zl-termin__inhalt p")!.TextContent);
        var status = fahnen.QuerySelectorAll(".elkw-status").Single();
        Assert.Equal("vorhanden", status.TextContent);
        Assert.Contains("elkw-status--aktiv", status.ClassList);
        Assert.Contains("elkw-status--offen", Eintrag(card, "Seile").QuerySelector(".elkw-status")!.ClassList);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public void Detailseite_ohne_Materialliste_zeigt_den_Rest_der_Seite_und_in_der_Card_nur_die_Meldung(HttpStatusCode status)
    {
        Antworten();
        _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(status));

        var cut = DetailRendern();

        Assert.Equal("Geländespiel", cut.Find("#programmpunkt-titel").GetAttribute("value"));
        var card = MaterialCard(cut);
        Assert.Equal("Das Material lässt sich gerade nicht laden.", card.QuerySelector("[role=alert] p")!.TextContent);
        Assert.DoesNotContain("Noch kein Material.", card.TextContent, StringComparison.Ordinal);
        Assert.Empty(card.QuerySelectorAll("form"));
        Assert.Empty(card.QuerySelectorAll(".zl-termin"));
    }

    [Fact]
    public void Material_hinzufuegen_sortiert_den_neuen_Eintrag_nach_Bezeichnung_ein()
    {
        var kompass = Guid.CreateVersion7();
        Antworten();
        _server.Bei(HttpMethod.Post, $"{RasterPfad}/punkte/{Gelaendespiel}/material", () => Json(Material(kompass, Gelaendespiel, "Kompass", null), HttpStatusCode.Created));
        var cut = DetailRendern();

        cut.Find("#material-bezeichnung").Input("Kompass");
        cut.FindAll("form").Single(f => f.QuerySelector("#material-bezeichnung") is not null).Submit();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Fahnen", "Kompass", "Seile", "Taschenlampen"], MaterialCard(cut).QuerySelectorAll(".zl-termin h3").Select(h => h.TextContent.Trim())));
    }

    [Fact]
    public void Status_bei_abgeschlossener_Freizeit_meldet_es_ohne_Erneut_versuchen()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/status", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen));
        var cut = DetailRendern();

        Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll(".elkw-segments button").Single(b => b.TextContent == "eingepackt").Click();

        cut.WaitForAssertion(() => Assert.Contains(ProgrammTexte.AbgeschlossenText, Eintrag(MaterialCard(cut), "Seile").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll("button"), b => b.TextContent == "Erneut versuchen");
        Assert.Equal("true", Eintrag(MaterialCard(cut), "Seile").QuerySelectorAll(".elkw-segments button")[0].GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Gescheitertes_Loeschen_laesst_den_Eintrag_stehen_und_meldet_den_Fehler()
    {
        Antworten();
        _server.Bei(HttpMethod.Delete, MaterialPfad(Seile), () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = DetailRendern();

        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Material löschen").Click();
        Knopf(Eintrag(MaterialCard(cut), "Seile"), "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, Eintrag(MaterialCard(cut), "Seile").QuerySelector("[role=alert]")?.TextContent ?? "", StringComparison.Ordinal));
        Assert.Equal(["Fahnen", "Seile", "Taschenlampen"], MaterialCard(cut).QuerySelectorAll(".zl-termin h3").Select(h => h.TextContent.Trim()));
    }

    // ---------- Materialliste ----------

    [Fact]
    public void Materialliste_zeigt_je_Programmpunkt_eine_Card_mit_Dachzeile_Titel_Link_Menge_Status_und_den_Balken()
    {
        Antworten();

        var cut = ListeRendern();

        Assert.Equal(("Programm", "Materialliste"), (cut.Find("p.elkw-kicker").TextContent, cut.Find("h1").TextContent));
        Assert.Equal("Eingepackt", cut.Find(".zl-balken__kopf strong").TextContent);
        Assert.Equal("1 von 4 eingepackt", cut.Find(".zl-balken__kopf .zl-zahl").TextContent);
        var cards = cut.FindAll("section.elkw-card");
        Assert.Equal(["Lagerfeuer", "Geländespiel"], cards.Select(c => c.QuerySelector("h2")!.TextContent));
        Assert.Equal(["Mo 02.08. · 20:00", "Di 03.08. · 14:30"], cards.Select(c => c.QuerySelector(".elkw-kicker")!.TextContent));
        Assert.Equal($"freizeiten/{Freizeit}/programm/{Gelaendespiel}", cards[1].QuerySelector("h2 a")!.GetAttribute("href"));
        Assert.Equal(["Fahnen", "Seile", "Taschenlampen"], cards[1].QuerySelectorAll(".zl-termin h3").Select(h => h.TextContent.Trim()));
        Assert.Equal("Menge: 4 × 10 m", Eintrag(cards[1], "Seile").QuerySelector(".zl-termin__inhalt p")!.TextContent);
        Assert.Empty(cards[1].QuerySelectorAll("input"));
        Assert.Equal("true", Eintrag(cards[0], "Feuerholz").QuerySelectorAll(".elkw-segments button")[3].GetAttribute("aria-pressed"));
        Assert.Equal("Bringt mit: Lea Feuerstein", Eintrag(cards[1], "Taschenlampen").QuerySelector(".elkw-status")!.TextContent);
        Assert.NotNull(Knopf(Eintrag(cards[1], "Seile"), "Ich bringe das mit"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Material löschen");
    }

    [Fact]
    public void Materialliste_Status_in_der_Liste_aendert_den_Balken()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/status", () => Json(Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", "Eingepackt", version: 6)));
        var cut = ListeRendern();

        cut.FindAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == "Seile").QuerySelectorAll(".elkw-segments button")[3].Click();

        cut.WaitForAssertion(() => Assert.Equal("2 von 4 eingepackt", cut.Find(".zl-balken__kopf .zl-zahl").TextContent));
    }

    [Fact]
    public void Materialliste_Konflikt_beim_Mitbringen_meldet_es_und_laedt_die_Liste_neu()
    {
        Antworten();
        _server.Bei(HttpMethod.Post, $"{MaterialPfad(Seile)}/mitbringen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var cut = ListeRendern();
        _server.Bei(HttpMethod.Get, ListePfad, () => Json(new { material = new[] { Material(Seile, Gelaendespiel, "Seile", "4 × 10 m", bringtMit: Jonas, version: 6) } }));

        Knopf(cut.FindAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == "Seile"), "Ich bringe das mit").Click();

        cut.WaitForAssertion(() => Assert.Contains("Jemand anderes bringt dieses Material inzwischen mit.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Equal(
            "Bringt mit: Jonas Hüttner", cut.FindAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == "Seile").QuerySelector(".elkw-status")?.TextContent));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == ListePfad));
    }

    [Fact]
    public void Materialliste_leer_zeigt_den_Satz_und_Zum_Programm()
    {
        Antworten(material: []);

        var cut = ListeRendern();

        Assert.Equal("Noch kein Material. Material hängt an den Programmpunkten.", cut.Find(".elkw-empty p").TextContent);
        Assert.Equal($"freizeiten/{Freizeit}/programm", cut.FindAll(".elkw-empty a").Single(a => a.TextContent.Contains("Zum Programm", StringComparison.Ordinal)).GetAttribute("href"));
        Assert.Empty(cut.FindAll(".zl-balken"));
    }

    [Fact]
    public void Materialliste_abgeschlossen_nur_lesbar()
    {
        Antworten(phase: "Gelaufen");

        var cut = ListeRendern();

        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("elkw-status--erfolg", cut.FindAll(".zl-termin").Single(t => t.QuerySelector("h3")!.TextContent.Trim() == "Feuerholz").QuerySelector(".elkw-status")!.ClassList);
    }

    [Fact]
    public void Materialliste_nicht_verfuegbar_bei_404()
    {
        Antworten();
        _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal("Diese Seite ist nicht verfügbar.", ListeRendern().Find("h1").TextContent);
    }

    [Fact]
    public void Wochenraster_hat_den_Link_Materialliste()
    {
        Antworten();
        Navigation.NavigateTo($"freizeiten/{Freizeit}/programm");

        var cut = Render<RasterSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("a"), a => a.TextContent == "Materialliste"));
        Assert.Equal($"freizeiten/{Freizeit}/programm/material", cut.FindAll("a").Single(a => a.TextContent == "Materialliste").GetAttribute("href"));
    }
}
