using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-11 in der Web-App mit nachgebildetem Server: der Client des Notfallordners und die Druckansicht
/// <c>/freizeiten/{id}/notfallordner</c> (Kopf nur am Bildschirm, je Person eine Druckseite mit Kasten „Notfallkontakte“ und den übrigen
/// Angaben, Abruf nur für bestätigte Teilnehmende, leer, nicht verfügbar, Fehler, Drucken).
/// </summary>
public sealed class NotfallordnerSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199fb00-0000-7000-8000-0000000000f1");
    private static readonly Guid Emma = Guid.Parse("0199fb00-0000-7000-8000-0000000000a1");
    private static readonly Guid Mia = Guid.Parse("0199fb00-0000-7000-8000-0000000000a2");
    private static readonly Guid Paul = Guid.Parse("0199fb00-0000-7000-8000-0000000000a3");
    private static readonly Guid Lea = Guid.Parse("0199fb00-0000-7000-8000-0000000000a4");

    private readonly NachgebildeterHandler _server = new();
    private readonly NachgebildeterBrowser _browser = new();
    private int _tokens;

    public NotfallordnerSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        Services.AddSingleton<IBrowser>(_browser);
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

    private static string OrdnerPfad => $"/api/freizeiten/{Freizeit}/personen/notfallordner";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Zeile(Guid person, string vorname, string nachname, string art, string status) =>
        new { anmeldungId = Guid.CreateVersion7(), personId = person, vorname, nachname, art, status };

    private static object Frage(string kennung, string frage, string art, string? wert, object[]? optionen = null) =>
        new { kennung, frage, art, optionen = optionen ?? [], mitSonstiges = false, wert, sonstiges = (string?)null, version = 1u, veraltet = false };

    private static object MiaImOrdner(bool mitNotfallkontakten = true) => new
    {
        personId = Mia, vorname = "Mia", nachname = "Probst",
        fragen = new[]
        {
            Frage("allergien", "Allergien und Unverträglichkeiten", "Auswahl", "gluten", [new { wert = "gluten", text = "Glutenhaltiges Getreide" }]),
            Frage("medikamente", "Medikamente (Name, Dosierung, wann)", "Freitext", "Asthmaspray bei Bedarf, liegt im Kulturbeutel"),
            mitNotfallkontakten ? Frage("notfallkontakte", "Notfallkontakte (Name, Telefon)", "Freitext", "Daniel Probst (Vater), 0123 456781") : null,
            Frage("schwimmen", "Kann schwimmen", "JaNein", "nein", [new { wert = "ja", text = "Ja" }, new { wert = "nein", text = "Nein" }]),
        }.Where(f => f is not null),
    };

    private void Antworten(
        string[]? erlaubt = null, object[]? personen = null, Func<HttpResponseMessage>? ordner = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase = "InVorbereitung", rollen = new[] { "Team" }, schalter = Array.Empty<string>(),
            erlaubt = erlaubt ?? [Aktionsschluessel.FreizeitpaesseSehen],
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
        {
            id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(personen ??
        [
            Zeile(Emma, "Emma", "Muster", "Teilnehmende", "Eingegangen"),
            Zeile(Mia, "Mia", "Probst", "Teilnehmende", "Bestaetigt"),
            Zeile(Paul, "Paul", "Probst", "Teilnehmende", "Bestaetigt"),
            Zeile(Lea, "Lea", "Feuerstein", "Mitarbeitende", "Bestaetigt"),
        ]));
        _server.Bei(HttpMethod.Get, OrdnerPfad, ordner ?? (() => Json(new[] { MiaImOrdner() })));
    }

    private IRenderedComponent<Notfallordner> Seite()
    {
        var cut = Render<Notfallordner>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_fragt_je_Kennung_einen_Parameter_ab_und_403_404_heissen_nicht_verfuegbar()
    {
        Antworten();
        var client = Services.GetRequiredService<PersonenApiClient>();

        var ordner = await client.NotfallordnerLadenAsync(Freizeit, [Mia, Paul], Abbruch);

        var mia = Assert.Single(ordner!);
        Assert.Equal((Mia, "Mia", "Probst", 4), (mia.PersonId, mia.Vorname, mia.Nachname, mia.Fragen.Count));
        Assert.Contains(_server.Anfragen, a => a.Pfad == $"{OrdnerPfad}?person={Mia}&person={Paul}");
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, OrdnerPfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.NotfallordnerLadenAsync(Freizeit, [Mia], Abbruch));
        }
    }

    // ---------- Seite ----------

    [Fact]
    public void Zeigt_den_Kopf_und_je_Person_eine_Druckseite_mit_Kasten_Notfallkontakte_und_den_uebrigen_Angaben()
    {
        Antworten();

        var cut = Seite();

        var kopf = cut.Find(".zl-nur-bildschirm");
        Assert.Equal("Notfall", kopf.QuerySelector(".elkw-kicker")!.TextContent);
        Assert.Equal("Notfallordner", kopf.QuerySelector("h1")!.TextContent);
        Assert.Contains(
            "Je Kind eine Seite mit Notfallkontakten und den Angaben aus dem Freizeitpass. Ausdrucke nach der Freizeit vernichten.", kopf.TextContent, StringComparison.Ordinal);
        Assert.Equal("Drucken", kopf.QuerySelector("button")!.TextContent);
        var zurueck = kopf.QuerySelectorAll("a").Single(a => a.TextContent == "Zurück zur Sicherheitskette");
        Assert.Equal($"freizeiten/{Freizeit}/notfall", zurueck.GetAttribute("href"));

        var seite = Assert.Single(cut.FindAll(".zl-druck-seite"));
        Assert.Null(seite.Closest(".zl-nur-bildschirm"));
        Assert.Equal("Zeltlager Sommer 2027", seite.QuerySelector(".elkw-kicker")!.TextContent);
        Assert.Equal("Mia Probst", seite.QuerySelector("h2")!.TextContent);
        var kasten = seite.QuerySelector(".zl-infokasten")!;
        Assert.Equal("Notfallkontakte", kasten.QuerySelector("h3")!.TextContent);
        Assert.Contains("Daniel Probst (Vater), 0123 456781", kasten.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["Allergien und Unverträglichkeiten", "Medikamente (Name, Dosierung, wann)", "Kann schwimmen"],
            seite.QuerySelectorAll("dl dt").Select(d => d.TextContent));
        Assert.Equal(["Glutenhaltiges Getreide", "Asthmaspray bei Bedarf, liegt im Kulturbeutel", "Nein"], seite.QuerySelectorAll("dl dd").Select(d => d.TextContent));
        // Der Kasten steht vor der Liste.
        Assert.True(seite.InnerHtml.IndexOf("zl-infokasten", StringComparison.Ordinal) < seite.InnerHtml.IndexOf("<dl", StringComparison.Ordinal));
    }

    [Fact]
    public void Ruft_den_Ordner_nur_fuer_bestaetigte_Teilnehmende_ab()
    {
        Antworten();

        Seite();

        var abruf = Assert.Single(_server.Anfragen, a => a.Pfad.StartsWith(OrdnerPfad, StringComparison.Ordinal));
        Assert.Equal($"{OrdnerPfad}?person={Mia}&person={Paul}", abruf.Pfad);
    }

    [Fact]
    public void Ohne_Frage_Notfallkontakte_fehlt_der_Kasten()
    {
        Antworten(ordner: () => Json(new[] { MiaImOrdner(mitNotfallkontakten: false) }));

        var cut = Seite();

        Assert.Single(cut.FindAll(".zl-druck-seite"));
        Assert.Empty(cut.FindAll(".zl-infokasten"));
        Assert.Equal(3, cut.FindAll(".zl-druck-seite dl dt").Count);
    }

    [Fact]
    public void Mehrere_Personen_ergeben_je_eine_Druckseite_in_der_Reihenfolge_der_Antwort()
    {
        var paul = new { personId = Paul, vorname = "Paul", nachname = "Probst", fragen = Array.Empty<object>() };
        Antworten(ordner: () => Json(new object[] { MiaImOrdner(), paul }));

        var cut = Seite();

        Assert.Equal(["Mia Probst", "Paul Probst"], cut.FindAll(".zl-druck-seite h2").Select(h => h.TextContent));
    }

    [Fact]
    public void Ohne_bestaetigte_Teilnehmende_Leer_Zustand_ohne_Drucken_und_ohne_Abruf()
    {
        Antworten(personen: [Zeile(Emma, "Emma", "Muster", "Teilnehmende", "Eingegangen"), Zeile(Lea, "Lea", "Feuerstein", "Mitarbeitende", "Bestaetigt")]);

        var cut = Seite();

        Assert.Equal("Noch keine bestätigten Teilnehmenden mit sichtbarem Freizeitpass.", cut.Find(".elkw-empty p").TextContent);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Drucken");
        Assert.Empty(cut.FindAll(".zl-druck-seite"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.StartsWith(OrdnerPfad, StringComparison.Ordinal));
    }

    [Fact]
    public void Leere_Antwort_zeigt_den_Leer_Zustand_ohne_Drucken()
    {
        Antworten(ordner: () => Json(Array.Empty<object>()));

        var cut = Seite();

        Assert.Equal("Noch keine bestätigten Teilnehmenden mit sichtbarem Freizeitpass.", cut.Find(".elkw-empty p").TextContent);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Drucken");
    }

    [Fact]
    public void Ohne_erlaubte_Aktion_ist_die_Seite_nicht_verfuegbar_und_laedt_den_Ordner_nicht()
    {
        Antworten(erlaubt: []);

        var cut = Seite();

        Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.StartsWith(OrdnerPfad, StringComparison.Ordinal));
    }

    [Fact]
    public void Scheitert_der_Abruf_steht_die_Fehlermeldung()
    {
        Antworten(ordner: () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = Seite();

        Assert.Equal(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert--fehler p").TextContent);
        Assert.Empty(cut.FindAll(".zl-druck-seite"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Drucken");
    }

    [Fact]
    public void Drucken_oeffnet_den_Druckdialog_einmal()
    {
        Antworten();
        var cut = Seite();

        cut.FindAll("button").Single(b => b.TextContent == "Drucken").Click();

        Assert.Equal(1, _browser.Gedruckt);
    }
}
