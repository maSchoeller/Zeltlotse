using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-7 in der Web-App: die Client-Methoden der Org-Dokumente, die Seite „Org-Dokumente“ (Liste mit Download-Link, Datum und
/// Entfernen mit Inline-Bestätigung, Hochladen, nicht verfügbar ohne erlaubte Aktion) und die Card „Org-Dokumente“ in der Organisation.
/// </summary>
public sealed class OrgDokumenteSeitenTests : BunitContext
{
    private static readonly Guid OrgId = Guid.Parse("0199e700-0000-7000-8000-0000000000c1");
    private static readonly Guid Hausordnung = Guid.Parse("0199e700-0000-7000-8000-0000000000c2");
    private static readonly Guid Leitfaden = Guid.Parse("0199e700-0000-7000-8000-0000000000c3");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public OrgDokumenteSeitenTests()
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
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string Pfad => $"/api/organisationen/{OrgId}/dokumente";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Dokument(Guid id, string titel) => new { id, titel, dateiart = "PDF", groesse = 2048L, hochgeladenAm = "2026-10-12T09:00:00+00:00" };

    private void Organisation(params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" }, erlaubt }));

    private IRenderedComponent<OrgDokumente> Seite()
    {
        var cut = Render<OrgDokumente>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    [Fact]
    public async Task Client_laedt_laedt_hoch_meldet_Feldfehler_und_entfernt()
    {
        _server.Bei(HttpMethod.Get, Pfad, () => Json(new { dokumente = new[] { Dokument(Hausordnung, "Hausordnung Zeltplatz") } }));
        _server.BeiDerReihe(
            HttpMethod.Post, Pfad,
            () => Json(Dokument(Leitfaden, "Leitfaden Aufsichtspflicht"), HttpStatusCode.Created),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"datei":["Datei fehlt."]}}""", "application/problem+json"),
            () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.BeiDerReihe(HttpMethod.Delete, $"{Pfad}/{Hausordnung}", () => new HttpResponseMessage(HttpStatusCode.NoContent), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Services.GetRequiredService<OrganisationenApiClient>();

        var liste = await client.OrgDokumenteLadenAsync(OrgId, Abbruch);
        var hochgeladen = await client.OrgDokumentHochladenAsync(OrgId, new MemoryStream([0x25]), "Leitfaden Aufsichtspflicht.pdf", "application/pdf", Abbruch);
        var fehler = await client.OrgDokumentHochladenAsync(OrgId, new MemoryStream([]), "x.pdf", null, Abbruch);
        var verboten = await client.OrgDokumentHochladenAsync(OrgId, new MemoryStream([1]), "x.pdf", null, Abbruch);
        var entfernt = await client.OrgDokumentEntfernenAsync(OrgId, Hausordnung, Abbruch);
        var nochmal = await client.OrgDokumentEntfernenAsync(OrgId, Hausordnung, Abbruch);

        Assert.Equal(("Hausordnung Zeltplatz", "PDF", 2048L), (Assert.Single(liste!.Dokumente).Titel, liste.Dokumente[0].Dateiart, liste.Dokumente[0].Groesse));
        Assert.Equal(Leitfaden, hochgeladen.Dokument!.Id);
        Assert.Equal("Datei fehlt.", fehler.DateiFehler);
        Assert.True(verboten.NichtVerfuegbar);
        Assert.Equal((true, false), (entfernt, nochmal));
        Assert.Contains("filename=\"Leitfaden Aufsichtspflicht.pdf\"", _server.Anfragen.First(a => a.Methode == HttpMethod.Post).Koerper!, StringComparison.Ordinal);
        Assert.Equal($"{Pfad}/{Hausordnung}/datei", OrganisationenApiClient.OrgDokumentDateiPfad(OrgId, Hausordnung));
    }

    [Fact]
    public void Seite_zeigt_Satz_Tabelle_mit_Download_Datum_und_Entfernen_mit_Inline_Bestaetigung()
    {
        Organisation(Aktionsschluessel.OrgDokumenteVerwalten);
        _server.BeiDerReihe(
            HttpMethod.Get, Pfad,
            () => Json(new { dokumente = new[] { Dokument(Hausordnung, "Hausordnung Zeltplatz"), Dokument(Leitfaden, "Leitfaden Aufsichtspflicht") } }),
            () => Json(new { dokumente = new[] { Dokument(Leitfaden, "Leitfaden Aufsichtspflicht") } }));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/{Hausordnung}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Seite();

        Assert.Equal("Org-Dokumente", cut.Find("h1").TextContent);
        Assert.Contains("Freizeiten der Organisation binden diese Dokumente ein, ohne sie zu kopieren.", cut.Markup, StringComparison.Ordinal);
        var zeile = cut.FindAll("tbody tr")[0];
        var link = zeile.QuerySelector("a")!;
        Assert.Equal(("Hausordnung Zeltplatz", $"{Pfad}/{Hausordnung}/datei", "_blank"), (link.TextContent, link.GetAttribute("href"), link.GetAttribute("target")));
        Assert.Contains("12.10.2026", zeile.TextContent, StringComparison.Ordinal);

        zeile.QuerySelector("button")!.Click();

        Assert.Contains(
            "Org-Dokument Hausordnung Zeltplatz entfernen? Es verschwindet auch aus allen Freizeiten.", cut.Find(".zl-bestaetigung").TextContent, StringComparison.Ordinal);

        cut.FindAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains("Leitfaden Aufsichtspflicht", cut.Find("tbody tr").TextContent, StringComparison.Ordinal);
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
    }

    [Fact]
    public void Entfernen_ohne_Erfolg_meldet_den_Fehler_und_laedt_die_Liste_neu()
    {
        Organisation(Aktionsschluessel.OrgDokumenteVerwalten);
        _server.Bei(HttpMethod.Get, Pfad, () => Json(new { dokumente = new[] { Dokument(Hausordnung, "Hausordnung Zeltplatz") } }));
        _server.Bei(HttpMethod.Delete, $"{Pfad}/{Hausordnung}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Seite();

        cut.Find("tbody tr button").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, entfernen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Das Org-Dokument ließ sich nicht entfernen.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == Pfad));
        Assert.Empty(cut.FindAll(".elkw-alert--erfolg"));
    }

    [Fact]
    public void Hochladen_meldet_Hochgeladen_ohne_Sichtbarkeit_Netzfehler_zeigt_Erneut_versuchen()
    {
        Organisation(Aktionsschluessel.OrgDokumenteVerwalten);
        _server.BeiDerReihe(
            HttpMethod.Get, Pfad,
            () => Json(new { dokumente = Array.Empty<object>() }),
            () => Json(new { dokumente = new[] { Dokument(Leitfaden, "Leitfaden Aufsichtspflicht") } }));
        _server.BeiDerReihe(
            HttpMethod.Post, Pfad,
            () => throw new HttpRequestException("offline"),
            () => Json(Dokument(Leitfaden, "Leitfaden Aufsichtspflicht"), HttpStatusCode.Created));
        var cut = Seite();
        Assert.Contains("Noch keine Org-Dokumente.", cut.Find(".elkw-empty").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input[type=radio]"));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "Leitfaden Aufsichtspflicht.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains("Datei nicht hochgeladen.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));

        cut.FindAll("button").Single(b => b.TextContent == "Erneut versuchen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Leitfaden Aufsichtspflicht.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal));
        Assert.Single(cut.FindAll("tbody tr"));
    }

    [Fact]
    public void Ohne_erlaubte_Aktion_ist_die_Seite_nicht_verfuegbar()
    {
        Organisation(Aktionsschluessel.OrgRollenVergeben);

        var cut = Render<OrgDokumente>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == Pfad);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Organisation_zeigt_die_Card_Org_Dokumente_nur_mit_der_erlaubten_Aktion(bool erlaubt)
    {
        Organisation(erlaubt ? [Aktionsschluessel.OrgDokumenteVerwalten] : []);

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Equal("Jugendwerk Musterstadt", cut.Find("h1").TextContent));
        var knopf = cut.FindAll("a").SingleOrDefault(a => a.TextContent.Trim() == "Org-Dokumente öffnen");
        Assert.Equal(erlaubt, knopf is not null);
        Assert.Equal(erlaubt, cut.FindAll(".elkw-card__title").Any(t => t.TextContent == "Org-Dokumente"));
        if (erlaubt)
        {
            Assert.Equal($"organisationen/{OrgId}/dokumente", knopf!.GetAttribute("href"));
        }
    }
}
