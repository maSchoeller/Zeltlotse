using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Personen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-7 in der Web-App: der Link „Elterninfo“ je nicht abgesagter Anmeldung in der Familienübersicht und die Seite „Elterninfo“
/// (Dachzeile mit der Freizeit, Links auf die Dateien, Leer-Zustand, nicht verfügbar, Zurück zur Familienübersicht).
/// </summary>
public sealed class ElterninfoSeitenTests : BunitContext
{
    private static readonly Guid Emma = Guid.Parse("0199e700-0000-7000-8000-0000000000a2");
    private static readonly Guid AnmeldungX = Guid.Parse("0199e700-0000-7000-8000-0000000000b1");
    private static readonly Guid AnmeldungAbgesagt = Guid.Parse("0199e700-0000-7000-8000-0000000000b2");
    private static readonly Guid Packliste = Guid.Parse("0199e700-0000-7000-8000-0000000000d1");
    private static readonly Guid Hausordnung = Guid.Parse("0199e700-0000-7000-8000-0000000000d2");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public ElterninfoSeitenTests()
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

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static string Pfad(Guid anmeldung) => $"/api/ich/anmeldungen/{anmeldung}/elterninfo";

    [Fact]
    public void Familie_zeigt_Elterninfo_je_Anmeldung_ausser_bei_einer_Absage()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => Json(new
        {
            eltern = new[] { new { personId = Guid.CreateVersion7(), vorname = "Katrin", nachname = "Muster" } },
            kinder = new[] { new { personId = Emma, version = 8u, vorname = "Emma", nachname = "Muster", geburtsdatum = "2017-09-02", volljaehrig = false, uebergabe = (object?)null } },
            einladungen = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Get, "/api/ich/anmeldungen", () => Json(new[]
        {
            new { anmeldungId = AnmeldungX, personId = Emma, freizeitName = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", art = "Teilnehmende", status = "Eingegangen" },
            new { anmeldungId = AnmeldungAbgesagt, personId = Emma, freizeitName = "Winterfreizeit", ersterTag = "2027-12-27", letzterTag = "2027-12-30", art = "Teilnehmende", status = "Abgesagt" },
        }));

        var cut = Render<FamilieAnsicht>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("a"), a => a.TextContent == "Elterninfo"));
        Assert.Equal($"familie/elterninfo/{AnmeldungX}", cut.FindAll("a").Single(a => a.TextContent == "Elterninfo").GetAttribute("href"));
        Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href")?.Contains(AnmeldungAbgesagt.ToString(), StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Elterninfo_zeigt_Freizeit_als_Dachzeile_und_die_Dokumente_als_Download_Links()
    {
        _server.Bei(HttpMethod.Get, Pfad(AnmeldungX), () => Json(new
        {
            freizeitName = "Zeltlager Sommer 2027",
            dokumente = new[] { new { id = Hausordnung, titel = "Hausordnung Zeltplatz", dateiart = "PDF" }, new { id = Packliste, titel = "Packliste", dateiart = "PNG" } },
        }));

        var cut = Render<ElterninfoAnsicht>(ps => ps.Add(p => p.AnmeldungId, AnmeldungX));

        cut.WaitForAssertion(() => Assert.Equal("Elterninfo", cut.Find("h1").TextContent));
        Assert.Equal("Zeltlager Sommer 2027", cut.Find(".elkw-kicker").TextContent);
        var links = cut.FindAll("li a");
        Assert.Equal(["Hausordnung Zeltplatz (PDF)", "Packliste (PNG)"], links.Select(a => a.TextContent));
        Assert.Equal([$"{Pfad(AnmeldungX)}/{Hausordnung}", $"{Pfad(AnmeldungX)}/{Packliste}"], links.Select(a => a.GetAttribute("href")));
        Assert.Equal("familie", cut.FindAll("a").Single(a => a.TextContent == "Zurück zur Familienübersicht").GetAttribute("href"));
    }

    [Fact]
    public void Ohne_Dokumente_steht_Noch_keine_Elterninfo_bei_404_nicht_verfuegbar()
    {
        _server.Bei(HttpMethod.Get, Pfad(AnmeldungX), () => Json(new { freizeitName = "Zeltlager Sommer 2027", dokumente = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, Pfad(AnmeldungAbgesagt), () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var leer = Render<ElterninfoAnsicht>(ps => ps.Add(p => p.AnmeldungId, AnmeldungX));
        var weg = Render<ElterninfoAnsicht>(ps => ps.Add(p => p.AnmeldungId, AnmeldungAbgesagt));

        leer.WaitForAssertion(() => Assert.Contains("Noch keine Elterninfo.", leer.Find(".elkw-empty").TextContent, StringComparison.Ordinal));
        weg.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", weg.Find("h1").TextContent));
    }
}
