using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-7 in der Web-App mit nachgebildetem Server und der echten Handler-Kette: die Client-Methoden der Dokumente, die Seite
/// „Dokumente“ (Liste, Suche, Leer- und Kein-Treffer-Zustand, Hochladen mit Erfolg, Feldfehler und „Erneut versuchen“, Einbinden, nur
/// lesbar nach dem Abschluss), die Seite eines Dokuments (Autosave, Einbindung nur mit Sichtbarkeit, Entfernen) und der Knopf
/// „Dokumente“ auf der Startseite der Freizeit.
/// </summary>
public sealed class DokumenteSeitenTests : BunitContext
{
    private static readonly Guid FreizeitId = Guid.Parse("0199e700-0000-7000-8000-0000000000f1");
    private static readonly Guid Packliste = Guid.Parse("0199e700-0000-7000-8000-0000000000d1");
    private static readonly Guid Infobrief = Guid.Parse("0199e700-0000-7000-8000-0000000000d2");
    private static readonly Guid Hausordnung = Guid.Parse("0199e700-0000-7000-8000-0000000000d3");
    private static readonly Guid Leitfaden = Guid.Parse("0199e700-0000-7000-8000-0000000000e1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public DokumenteSeitenTests()
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

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string ListePfad => $"/api/freizeiten/{FreizeitId}/dokumente";

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void Kontext(string phase = "InVorbereitung", HttpStatusCode? status = null) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => status is { } s
            ? new HttpResponseMessage(s)
            : Json(new { freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>() }));

    private static object Dokument(Guid id, string titel, string sichtbarkeit, bool ausOrganisation = false, uint version = 3) =>
        new { id, titel, sichtbarkeit, dateiart = "PDF", groesse = 1234L, hochgeladenAm = "2026-10-12T09:00:00+00:00", ausOrganisation, version };

    private static object[] Drei =>
    [
        Dokument(Hausordnung, "Hausordnung Zeltplatz", "ElternUndTeilnehmende", ausOrganisation: true),
        Dokument(Infobrief, "Infobrief Zeltlager Sommer 2027", "Oeffentlich"),
        Dokument(Packliste, "Packliste", "Team"),
    ];

    private void Liste(params object[] dokumente) => _server.Bei(HttpMethod.Get, ListePfad, () => Json(new { dokumente }));

    private void Auswahl(params object[] dokumente) => _server.Bei(HttpMethod.Get, $"{ListePfad}/org", () => Json(new { dokumente }));

    private IRenderedComponent<Dokumente> Seite()
    {
        var cut = Render<Dokumente>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<DokumentBearbeiten> Bearbeiten(Guid dokument)
    {
        var cut = Render<DokumentBearbeiten>(ps => ps.Add(p => p.FreizeitId, FreizeitId).Add(p => p.DokumentId, dokument));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static List<string> Titel(IRenderedComponent<Dokumente> cut) =>
        [.. cut.FindAll(".elkw-card").First(c => c.TextContent.Contains("Dokumente der Freizeit", StringComparison.Ordinal)).QuerySelectorAll("tbody tr td:first-child").Select(z => z.TextContent.Trim())];

    // ---------- Client ----------

    [Fact]
    public async Task Client_laedt_laedt_hoch_entfernt_bindet_ein_und_liest_die_Elterninfo()
    {
        Liste(Drei);
        Auswahl(new { id = Leitfaden, titel = "Leitfaden Aufsichtspflicht" });
        _server.BeiDerReihe(
            HttpMethod.Post, ListePfad,
            () => Json(Dokument(Packliste, "Packliste", "ElternUndTeilnehmende"), HttpStatusCode.Created),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"datei":["Die Datei ist leer."]}}""", "application/problem+json"),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"),
            () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.BeiDerReihe(HttpMethod.Delete, $"{ListePfad}/{Packliste}", () => new HttpResponseMessage(HttpStatusCode.NoContent), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.BeiDerReihe(
            HttpMethod.Post, $"{ListePfad}/einbindungen",
            () => Json(Dokument(Hausordnung, "Hausordnung Zeltplatz", "Team", ausOrganisation: true), HttpStatusCode.Created),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        _server.Bei(HttpMethod.Get, $"/api/ich/anmeldungen/{Infobrief}/elterninfo", () => Json(new { freizeitName = "Zeltlager Sommer 2027", dokumente = new[] { new { id = Packliste, titel = "Packliste", dateiart = "PDF" } } }));
        var client = Services.GetRequiredService<FreizeitenApiClient>();

        var liste = await client.DokumenteLadenAsync(FreizeitId, Abbruch);
        var auswahl = await client.OrgDokumenteAuswahlLadenAsync(FreizeitId, Abbruch);
        var hochgeladen = await client.DokumentHochladenAsync(FreizeitId, new MemoryStream([0x25, 0x50]), "Packliste.pdf", "application/pdf", DokumentSichtbarkeit.ElternUndTeilnehmende, Abbruch);
        var feldfehler = await client.DokumentHochladenAsync(FreizeitId, new MemoryStream([]), "leer.pdf", null, DokumentSichtbarkeit.Team, Abbruch);
        var abgeschlossen = await client.DokumentHochladenAsync(FreizeitId, new MemoryStream([1]), "x.pdf", null, DokumentSichtbarkeit.Team, Abbruch);
        var weg = await client.DokumentHochladenAsync(FreizeitId, new MemoryStream([1]), "x.pdf", null, DokumentSichtbarkeit.Team, Abbruch);
        var entfernt = await client.DokumentEntfernenAsync(FreizeitId, Packliste, Abbruch);
        var nochmal = await client.DokumentEntfernenAsync(FreizeitId, Packliste, Abbruch);
        var eingebunden = await client.DokumentEinbindenAsync(FreizeitId, Hausordnung, Abbruch);
        var schon = await client.DokumentEinbindenAsync(FreizeitId, Hausordnung, Abbruch);
        var elterninfo = await client.ElterninfoLadenAsync(Infobrief, Abbruch);

        Assert.Equal(["Hausordnung Zeltplatz", "Infobrief Zeltlager Sommer 2027", "Packliste"], liste!.Dokumente.Select(d => d.Titel));
        Assert.Equal((DokumentSichtbarkeit.ElternUndTeilnehmende, true, 3u), (liste.Dokumente[0].Sichtbarkeit, liste.Dokumente[0].AusOrganisation, liste.Dokumente[0].Version));
        Assert.Equal("Leitfaden Aufsichtspflicht", Assert.Single(auswahl!.Dokumente).Titel);
        Assert.Equal(Packliste, hochgeladen.Dokument!.Id);
        Assert.Equal("Die Datei ist leer.", feldfehler.DateiFehler);
        Assert.True(abgeschlossen.Abgeschlossen);
        Assert.True(weg.NichtVerfuegbar);
        Assert.Equal((DokumentErgebnis.Erledigt, DokumentErgebnis.NichtVerfuegbar), (entfernt, nochmal));
        Assert.Equal((DokumentErgebnis.Erledigt, DokumentErgebnis.Konflikt), (eingebunden, schon));
        Assert.Equal(("Zeltlager Sommer 2027", "Packliste"), (elterninfo!.FreizeitName, Assert.Single(elterninfo.Dokumente).Titel));
        var upload = _server.Anfragen.First(a => a.Methode == HttpMethod.Post && a.Pfad == ListePfad).Koerper!;
        Assert.Contains("name=datei; filename=Packliste.pdf", upload, StringComparison.Ordinal);
        Assert.Contains("name=sichtbarkeit", upload, StringComparison.Ordinal);
        Assert.Contains("ElternUndTeilnehmende", upload, StringComparison.Ordinal);
        Assert.Equal($$"""{"orgDokumentId":"{{Hausordnung}}"}""", _server.Anfragen.First(a => a.Pfad == $"{ListePfad}/einbindungen").Koerper);
        Assert.Equal($"{ListePfad}/{Packliste}/datei", FreizeitenApiClient.DokumentDateiPfad(FreizeitId, Packliste));
        Assert.Equal($"/api/freizeiten/oeffentlich/Tk/dokumente/{Packliste}", FreizeitenApiClient.OeffentlichesDokumentPfad("Tk", Packliste));
        Assert.Equal($"/api/freizeiten/gast/Gt/anmeldungen/{Infobrief}/elterninfo", FreizeitenApiClient.GastElterninfoPfad("Gt", Infobrief));
    }

    [Fact]
    public void Texte_fuer_Sichtbarkeit_Herkunft_Verweis_und_Suche()
    {
        Assert.Equal(
            ["Team", "Eltern und Teilnehmende", "Öffentlich"],
            Enum.GetValues<DokumentSichtbarkeit>().Select(Anzeige.Sichtbarkeit));
        Assert.Equal(("Organisation", "Freizeit"), (Anzeige.Herkunft(true), Anzeige.Herkunft(false)));
        Assert.Equal("Packliste (PDF)", Anzeige.Dokument("Packliste", "PDF"));
        Assert.True(Anzeige.TitelTrifft("Packliste", "PACK"));
        Assert.True(Anzeige.TitelTrifft("Packliste", " "));
        Assert.False(Anzeige.TitelTrifft("Infobrief", "pack"));
    }

    // ---------- Seite „Dokumente“ ----------

    [Fact]
    public void Liste_zeigt_Titel_als_Download_Sichtbarkeit_Herkunft_Bearbeiten_und_die_einbindbaren_Org_Dokumente()
    {
        Kontext();
        Liste(Drei);
        Auswahl(new { id = Leitfaden, titel = "Leitfaden Aufsichtspflicht" });

        var cut = Seite();

        Assert.Equal("Dokumente", cut.Find("h1").TextContent);
        Assert.Equal("Dokumente suchen", cut.Find("label[for=dokumente-suche]").TextContent);
        Assert.Equal("search", cut.Find("#dokumente-suche").GetAttribute("type"));
        Assert.Equal(["Hausordnung Zeltplatz", "Infobrief Zeltlager Sommer 2027", "Packliste"], Titel(cut));
        var packliste = cut.FindAll("tbody tr").Single(z => z.TextContent.Contains("Packliste", StringComparison.Ordinal));
        var link = packliste.QuerySelector("a")!;
        Assert.Equal(($"{ListePfad}/{Packliste}/datei", "_blank", "noopener"), (link.GetAttribute("href"), link.GetAttribute("target"), link.GetAttribute("rel")));
        Assert.Equal(
            ["Packliste", "Team", "Freizeit", "Bearbeiten"],
            packliste.QuerySelectorAll("td").Select(z => z.TextContent.Trim()));
        Assert.Equal($"freizeiten/{FreizeitId}/dokumente/{Packliste}", packliste.QuerySelectorAll("a")[1].GetAttribute("href"));
        Assert.Contains("Eltern und Teilnehmende", cut.FindAll("tbody tr")[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Organisation", cut.FindAll("tbody tr")[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Öffentlich", cut.FindAll("tbody tr")[1].TextContent, StringComparison.Ordinal);
        var einbinden = cut.FindAll(".elkw-card").Single(c => c.TextContent.Contains("Aus der Organisation einbinden", StringComparison.Ordinal));
        Assert.Contains("Leitfaden Aufsichtspflicht", einbinden.TextContent, StringComparison.Ordinal);
        Assert.Equal("Einbinden", einbinden.QuerySelector("button")!.TextContent);
        Assert.Contains("Dokument hochladen", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Team", cut.FindAll("input[name=dokument-sichtbarkeit]").Single(r => r.HasAttribute("checked")).GetAttribute("value"));
    }

    [Fact]
    public void Suche_filtert_beide_Listen_beim_Tippen_ohne_Treffer_steht_Keine_Dokumente_gefunden()
    {
        Kontext();
        Liste(Drei);
        Auswahl(new { id = Leitfaden, titel = "Leitfaden Aufsichtspflicht" });
        var cut = Seite();

        cut.Find("#dokumente-suche").Input("pack");

        Assert.Equal(["Packliste"], Titel(cut));
        var einbinden = cut.FindAll(".elkw-card").Single(c => c.TextContent.Contains("Aus der Organisation einbinden", StringComparison.Ordinal));
        Assert.Contains("Keine Dokumente gefunden.", einbinden.TextContent, StringComparison.Ordinal);

        cut.Find("#dokumente-suche").Input("xyz");

        Assert.Equal(2, cut.FindAll(".elkw-empty").Count(e => e.TextContent.Contains("Keine Dokumente gefunden.", StringComparison.Ordinal)));
        Assert.Empty(cut.FindAll("tbody tr"));

        cut.Find("#dokumente-suche").Input("");

        Assert.Equal(3, Titel(cut).Count);
    }

    [Fact]
    public void Ohne_Dokumente_steht_der_Leer_Zustand_ohne_einbindbare_fehlt_die_Card()
    {
        Kontext();
        Liste();
        Auswahl();

        var cut = Seite();

        Assert.Contains("Noch keine Dokumente.", cut.Find(".elkw-empty").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Aus der Organisation einbinden", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Hochladen_mit_gewaehlter_Sichtbarkeit_meldet_Hochgeladen_und_laedt_die_Liste_neu()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, ListePfad, () => Json(new { dokumente = Array.Empty<object>() }), () => Json(new { dokumente = new[] { Dokument(Packliste, "Packliste", "ElternUndTeilnehmende") } }));
        Auswahl();
        _server.Bei(HttpMethod.Post, ListePfad, () => Json(Dokument(Packliste, "Packliste", "ElternUndTeilnehmende"), HttpStatusCode.Created));
        var cut = Seite();

        cut.FindAll("input[name=dokument-sichtbarkeit]").Single(r => r.GetAttribute("value") == "ElternUndTeilnehmende").Change("ElternUndTeilnehmende");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "Packliste.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains("Hochgeladen: Packliste.", Normalisiert(cut.Find(".elkw-alert--erfolg").TextContent), StringComparison.Ordinal));
        Assert.Equal(["Packliste"], Titel(cut));
        var koerper = _server.Anfragen.Single(a => a.Methode == HttpMethod.Post && a.Pfad == ListePfad).Koerper!;
        Assert.Contains("filename=Packliste.pdf", koerper, StringComparison.Ordinal);
        Assert.Contains("ElternUndTeilnehmende", koerper, StringComparison.Ordinal);
    }

    [Fact]
    public void Feldfehler_steht_am_Feld_Netzfehler_zeigt_Erneut_versuchen()
    {
        Kontext();
        Liste();
        Auswahl();
        _server.BeiDerReihe(
            HttpMethod.Post, ListePfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"datei":["Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF."]}}""", "application/problem+json"),
            () => throw new HttpRequestException("offline"),
            () => Json(Dokument(Packliste, "Packliste", "Team"), HttpStatusCode.Created));
        var cut = Seite();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "text.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF.", cut.Find(".elkw-field-error").TextContent));
        Assert.Equal("true", cut.Find("#dokument-datei").GetAttribute("aria-invalid"));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "Packliste.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains("Datei nicht hochgeladen.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-field-error"));

        cut.FindAll("button").Single(b => b.TextContent == "Erneut versuchen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Packliste.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-alert--fehler"));
    }

    [Fact]
    public void Einbinden_meldet_Sichtbar_fuer_das_Team_und_laedt_beide_Listen_neu()
    {
        Kontext();
        _server.BeiDerReihe(HttpMethod.Get, ListePfad, () => Json(new { dokumente = Array.Empty<object>() }), () => Json(new { dokumente = new[] { Dokument(Leitfaden, "Leitfaden Aufsichtspflicht", "Team", ausOrganisation: true) } }));
        _server.BeiDerReihe(HttpMethod.Get, $"{ListePfad}/org", () => Json(new { dokumente = new[] { new { id = Leitfaden, titel = "Leitfaden Aufsichtspflicht" } } }), () => Json(new { dokumente = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Post, $"{ListePfad}/einbindungen", () => Json(Dokument(Leitfaden, "Leitfaden Aufsichtspflicht", "Team", ausOrganisation: true), HttpStatusCode.Created));
        var cut = Seite();

        cut.FindAll("button").Single(b => b.TextContent == "Einbinden").Click();

        cut.WaitForAssertion(() => Assert.Contains("Eingebunden: Leitfaden Aufsichtspflicht. Sichtbar für das Team.", Normalisiert(cut.Find(".elkw-alert--erfolg").TextContent), StringComparison.Ordinal));
        Assert.Equal(["Leitfaden Aufsichtspflicht"], Titel(cut));
        Assert.DoesNotContain("Aus der Organisation einbinden", cut.Markup, StringComparison.Ordinal);
        Assert.Equal($$"""{"orgDokumentId":"{{Leitfaden}}"}""", _server.Anfragen.Single(a => a.Pfad == $"{ListePfad}/einbindungen").Koerper);
    }

    [Fact]
    public void Einbinden_mit_409_meldet_den_Fehler_und_laedt_Kontext_und_Listen_neu()
    {
        _server.BeiDerReihe(
            HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext",
            () => Json(new { freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", phase = "InVorbereitung", rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>() }),
            () => Json(new { freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", phase = "Gelaufen", rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>() }));
        Liste(Drei);
        Auswahl(new { id = Leitfaden, titel = "Leitfaden Aufsichtspflicht" });
        _server.Bei(HttpMethod.Post, $"{ListePfad}/einbindungen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = Seite();
        var vorher = _server.Anfragen.Count(a => a.Pfad == ListePfad);

        cut.FindAll("button").Single(b => b.TextContent == "Einbinden").Click();

        cut.WaitForAssertion(() => Assert.Contains("Fehler: Das Org-Dokument ließ sich nicht einbinden.", Normalisiert(cut.Find(".elkw-alert--fehler").TextContent), StringComparison.Ordinal));
        Assert.Equal(vorher + 1, _server.Anfragen.Count(a => a.Pfad == ListePfad));
        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Aus der Organisation einbinden", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Scheitert_nach_dem_Hochladen_nur_das_Neuladen_bleibt_die_Erfolgsmeldung()
    {
        Kontext();
        _server.BeiDerReihe(
            HttpMethod.Get, ListePfad,
            () => Json(new { dokumente = new[] { Dokument(Infobrief, "Infobrief Zeltlager Sommer 2027", "Team") } }),
            () => throw new HttpRequestException("offline"));
        Auswahl();
        _server.Bei(HttpMethod.Post, ListePfad, () => Json(Dokument(Packliste, "Packliste", "Team"), HttpStatusCode.Created));
        var cut = Seite();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "Packliste.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains("Hochgeladen: Packliste.", Normalisiert(cut.Find(".elkw-alert--erfolg").TextContent), StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-alert--fehler"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Erneut versuchen");
        Assert.Equal(["Infobrief Zeltlager Sommer 2027"], Titel(cut));
    }

    [Fact]
    public void Nach_dem_Abschluss_nur_lesbar_ohne_Hochladen_und_Einbinden()
    {
        Kontext("Gelaufen");
        Liste(Drei);
        Auswahl(new { id = Leitfaden, titel = "Leitfaden Aufsichtspflicht" });

        var cut = Seite();

        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Find(".elkw-alert--hinweis").TextContent, StringComparison.Ordinal);
        Assert.Equal(3, Titel(cut).Count);
        Assert.DoesNotContain("Dokument hochladen", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Aus der Organisation einbinden", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindComponents<InputFile>());
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == $"{ListePfad}/org");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(null, HttpStatusCode.Forbidden)]
    public void Ohne_Rolle_ist_die_Seite_nicht_verfuegbar(HttpStatusCode? kontext, HttpStatusCode? liste)
    {
        Kontext(status: kontext);
        _server.Bei(HttpMethod.Get, ListePfad, () => liste is { } s ? new HttpResponseMessage(s) : Json(new { dokumente = Array.Empty<object>() }));

        var cut = Render<Dokumente>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }

    // ---------- Seite eines Dokuments ----------

    [Fact]
    public void Bearbeiten_speichert_Titel_und_Sichtbarkeit_per_Autosave_mit_Datei_Link()
    {
        Kontext();
        Liste(Drei);
        _server.Bei(HttpMethod.Patch, $"{ListePfad}/{Packliste}", () => Json(new Dictionary<string, object> { ["version"] = 4u, ["titel"] = "Packliste 2027" }));
        var cut = Bearbeiten(Packliste);

        Assert.Equal("Packliste", cut.Find("h1").TextContent);
        Assert.Equal("Zurück zu den Dokumenten", cut.FindAll("a").Single(a => a.GetAttribute("href") == $"freizeiten/{FreizeitId}/dokumente").TextContent);
        var datei = cut.FindAll("a").Single(a => a.TextContent == "Datei öffnen");
        Assert.Equal(($"{ListePfad}/{Packliste}/datei", "_blank"), (datei.GetAttribute("href"), datei.GetAttribute("target")));
        Assert.Equal("Team", cut.FindAll("#dokument-sichtbarkeit input[type=radio]").Single(r => r.HasAttribute("checked")).GetAttribute("value"));

        cut.Find("#dokument-titel").Input("Packliste 2027");
        cut.Find("#dokument-titel").Blur();

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("""{"version":3,"alterWert":"Packliste","titel":"Packliste 2027"}""", _server.Anfragen.Last(a => a.Methode == HttpMethod.Patch).Koerper);

        _server.Bei(HttpMethod.Patch, $"{ListePfad}/{Packliste}", () => Json(new Dictionary<string, object> { ["version"] = 5u, ["sichtbarkeit"] = "Oeffentlich" }));
        cut.FindAll("#dokument-sichtbarkeit input[type=radio]").Single(r => r.GetAttribute("value") == "Oeffentlich").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(
            """{"version":3,"alterWert":"Team","sichtbarkeit":"Oeffentlich"}""", _server.Anfragen.Last(a => a.Methode == HttpMethod.Patch).Koerper));
    }

    [Fact]
    public void Bei_einer_Einbindung_steht_der_Titel_als_Text_nur_die_Sichtbarkeit_ist_bearbeitbar()
    {
        Kontext();
        Liste(Drei);

        var cut = Bearbeiten(Hausordnung);

        Assert.Empty(cut.FindAll("#dokument-titel"));
        Assert.Contains("Hausordnung Zeltplatz", cut.Find("dl").TextContent, StringComparison.Ordinal);
        Assert.Contains("Titel und Datei pflegt die Organisation.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("ElternUndTeilnehmende", cut.FindAll("#dokument-sichtbarkeit input[type=radio]").Single(r => r.HasAttribute("checked")).GetAttribute("value"));
    }

    [Fact]
    public void Entfernen_fragt_inline_nach_und_fuehrt_zur_Liste_zurueck()
    {
        Kontext();
        Liste(Drei);
        _server.Bei(HttpMethod.Delete, $"{ListePfad}/{Packliste}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Bearbeiten(Packliste);

        cut.FindAll("button").Single(b => b.TextContent == "Entfernen").Click();

        Assert.Contains("Dokument Packliste aus der Freizeit entfernen?", cut.Find(".zl-bestaetigung").TextContent, StringComparison.Ordinal);

        cut.FindAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"/freizeiten/{FreizeitId}/dokumente", Navigation.Uri, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
    }

    [Fact]
    public void Nach_dem_Abschluss_ist_das_Dokument_nur_lesbar_ein_unbekanntes_nicht_verfuegbar()
    {
        Kontext("Gelaufen");
        Liste(Drei);

        var cut = Bearbeiten(Packliste);

        Assert.Empty(cut.FindAll("input"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Entfernen");
        Assert.Contains("Sichtbarkeit: Team", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Markup, StringComparison.Ordinal);

        var unbekannt = Render<DokumentBearbeiten>(ps => ps.Add(p => p.FreizeitId, FreizeitId).Add(p => p.DokumentId, Guid.CreateVersion7()));

        unbekannt.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", unbekannt.Find("h1").TextContent));
    }

    // ---------- Startseite ----------

    [Fact]
    public void Startseite_zeigt_dem_Team_den_Knopf_Dokumente()
    {
        Kontext();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}", () => Json(new
        {
            id = FreizeitId, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal($"freizeiten/{FreizeitId}/dokumente", cut.FindAll("a").Single(a => a.TextContent.Trim() == "Dokumente").GetAttribute("href")));
    }

    private static string Normalisiert(string text) => string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
