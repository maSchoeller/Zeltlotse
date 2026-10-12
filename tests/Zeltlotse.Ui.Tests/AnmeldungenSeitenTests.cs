using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-3 in der Web-App mit nachgebildetem Server und der echten Handler-Kette: die Client-Methoden der Anmeldungen, die Seite
/// „Anmeldungen“ der Leitung (Balken-Zustände, Aktionen je Status, Bestätigen, Inline-Absage, 409, Leer, nicht verfügbar), der Knopf auf
/// der Freizeit-Startseite und der Baustein „Kontakt für Absagen“.
/// </summary>
public sealed class AnmeldungenSeitenTests : BunitContext
{
    private static readonly Guid FreizeitId = Guid.Parse("0199a000-0000-7000-8000-0000000000f1");
    private static readonly Guid Emma = Guid.Parse("0199a000-0000-7000-8000-0000000000e1");
    private static readonly Guid Mia = Guid.Parse("0199a000-0000-7000-8000-0000000000e2");
    private static readonly Guid Lea = Guid.Parse("0199a000-0000-7000-8000-0000000000e3");
    private static readonly Guid Tobias = Guid.Parse("0199a000-0000-7000-8000-0000000000e4");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public AnmeldungenSeitenTests()
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

    private static string ListePfad => $"/api/freizeiten/{FreizeitId}/anmeldungen";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Zeile(Guid id, string vorname, string art, string status) => new
    {
        anmeldungId = id,
        personId = Guid.CreateVersion7(),
        vorname,
        nachname = "Muster",
        art,
        status,
        eingegangenAm = "2026-10-05T08:00:00+00:00",
        entschiedenAm = status == "Eingegangen" ? null : "2026-10-06T08:00:00+00:00",
    };

    private static object[] Alle =>
    [
        Zeile(Emma, "Emma", "Teilnehmende", "Eingegangen"),
        Zeile(Mia, "Mia", "Teilnehmende", "Bestaetigt"),
        Zeile(Lea, "Lea", "Mitarbeitende", "Abgesagt"),
        Zeile(Tobias, "Tobias", "Mitarbeitende", "Eingegangen"),
    ];

    private void Kontext(params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => Json(new
        {
            freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", phase = "InVorbereitung", rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(), erlaubt,
        }));

    private void Liste(int? hoechstzahl, int bestaetigt, params object[] anmeldungen) =>
        _server.Bei(HttpMethod.Get, ListePfad, () => Json(new { hoechstzahlTeilnehmende = hoechstzahl, bestaetigteTeilnehmende = bestaetigt, anmeldungen }));

    private IRenderedComponent<Anmeldungen> Seite()
    {
        var cut = Render<Anmeldungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForAssertion(() => Assert.Equal("Anmeldungen", cut.Find("h1").TextContent));
        return cut;
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_laedt_die_Liste_und_entscheidet_403_404_heissen_nicht_verfuegbar_409_schon_entschieden()
    {
        Liste(40, 1, Alle);
        _server.Bei(HttpMethod.Post, $"{ListePfad}/{Emma}/bestaetigen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, $"{ListePfad}/{Mia}/bestaetigen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        _server.Bei(HttpMethod.Post, $"{ListePfad}/{Emma}/absagen", () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.Bei(HttpMethod.Post, $"{ListePfad}/{Mia}/absagen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Services.GetRequiredService<FreizeitenApiClient>();

        var uebersicht = await client.AnmeldungenAsync(FreizeitId, Abbruch);

        Assert.NotNull(uebersicht);
        Assert.Equal((40, 1, 4), (uebersicht.HoechstzahlTeilnehmende, uebersicht.BestaetigteTeilnehmende, uebersicht.Anmeldungen.Count));
        Assert.Equal(
            [AnmeldungStatus.Eingegangen, AnmeldungStatus.Bestaetigt, AnmeldungStatus.Abgesagt, AnmeldungStatus.Eingegangen],
            uebersicht.Anmeldungen.Select(a => a.Status));
        Assert.Equal(EntscheidungErgebnis.Entschieden, await client.BestaetigenAsync(FreizeitId, Emma, Abbruch));
        Assert.Equal(EntscheidungErgebnis.SchonEntschieden, await client.BestaetigenAsync(FreizeitId, Mia, Abbruch));
        Assert.Equal(EntscheidungErgebnis.NichtVerfuegbar, await client.AbsagenAsync(FreizeitId, Emma, Abbruch));
        Assert.Equal(EntscheidungErgebnis.NichtVerfuegbar, await client.AbsagenAsync(FreizeitId, Mia, Abbruch));
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{ListePfad}/{Emma}/bestaetigen" && a.Kopfzeilen.ContainsKey(Kopfzeilen.Antiforgery));

        _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        Assert.Null(await client.AnmeldungenAsync(FreizeitId, Abbruch));
    }

    // ---------- Seite ----------

    [Theory]
    [InlineData(40, 1, "1 von 40", "Belegt", "elkw-status--aktiv")]
    [InlineData(2, 2, "2 von 2", "Ausgebucht", "elkw-status--warnung")]
    [InlineData(2, 3, "3 von 2", "Überbelegt", "elkw-status--fehler")]
    [InlineData(null, 1, "1 bestätigt", "Ohne Höchstzahl", "elkw-status--aktiv")]
    public void Der_Balken_Plaetze_zeigt_Zahl_und_Statuswort(int? hoechstzahl, int bestaetigt, string zahl, string wort, string klasse)
    {
        Kontext(Aktionsschluessel.AnmeldungenBearbeiten);
        Liste(hoechstzahl, bestaetigt, Alle);

        var cut = Seite();

        cut.WaitForAssertion(() => Assert.Equal(zahl, cut.Find(".zl-balken .zl-zahl").TextContent));
        Assert.Equal("Plätze", cut.Find(".zl-balken strong").TextContent);
        Assert.Equal(wort, cut.Find(".zl-balken .elkw-status").TextContent);
        Assert.Contains(klasse, cut.Find(".zl-balken .elkw-status").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Abschnitte_zeigen_Name_Eingang_Status_und_nur_die_erlaubten_Aktionen()
    {
        Kontext(Aktionsschluessel.AnmeldungenBearbeiten);
        Liste(40, 1, Alle);

        var cut = Seite();

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));
        Assert.Equal(["Teilnehmende", "Mitarbeitende"], cut.FindAll("h2").Select(h => h.TextContent));
        Assert.Equal(["Name", "Eingegangen", "Status", "Aktion"], cut.FindAll("table").First().QuerySelectorAll("th").Select(t => t.TextContent));
        string[] Knoepfe(string name) =>
            [.. cut.FindAll("tbody tr").Single(z => z.TextContent.Contains(name, StringComparison.Ordinal)).QuerySelectorAll("button").Select(b => b.TextContent)];
        Assert.Equal(["Bestätigen", "Absage eintragen"], Knoepfe("Emma Muster"));
        Assert.Equal(["Absage eintragen"], Knoepfe("Mia Muster"));
        Assert.Empty(Knoepfe("Lea Muster"));
        Assert.Equal(["Bestätigen", "Absage eintragen"], Knoepfe("Tobias Muster"));
        var emma = cut.FindAll("tbody tr").Single(z => z.TextContent.Contains("Emma", StringComparison.Ordinal));
        Assert.Contains("05.10.2026", emma.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["Eingegangen", "Bestätigt", "Abgesagt", "Eingegangen"],
            cut.FindAll("tbody .elkw-status").Select(s => s.TextContent));
        Assert.Contains("elkw-status--erfolg", cut.FindAll("tbody .elkw-status")[1].ClassName, StringComparison.Ordinal);
        Assert.Contains("elkw-status--fehler", cut.FindAll("tbody .elkw-status")[2].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Bestaetigen_meldet_die_Mail_und_laedt_die_Liste_neu()
    {
        Kontext(Aktionsschluessel.AnmeldungenBearbeiten);
        Liste(40, 1, Alle);
        _server.Bei(HttpMethod.Post, $"{ListePfad}/{Emma}/bestaetigen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Seite();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));

        cut.FindAll("tbody tr").Single(z => z.TextContent.Contains("Emma", StringComparison.Ordinal)).QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("Emma ist bestätigt. Eine Mail geht raus.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == ListePfad));
    }

    [Fact]
    public void Absage_eintragen_fragt_inline_nach_und_sagt_erst_nach_Ja_ab()
    {
        Kontext(Aktionsschluessel.AnmeldungenBearbeiten);
        Liste(40, 1, Alle);
        _server.Bei(HttpMethod.Post, $"{ListePfad}/{Tobias}/absagen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Seite();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));

        cut.FindAll("tbody tr").Single(z => z.TextContent.Contains("Tobias", StringComparison.Ordinal))
            .QuerySelectorAll("button").Single(b => b.TextContent == "Absage eintragen").Click();

        Assert.Contains("Absage für Tobias eintragen? Eine Absage-Mail geht raus.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad.EndsWith("/absagen", StringComparison.Ordinal));
        cut.FindAll("button").Single(b => b.TextContent == "Ja, Absage eintragen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Absage für Tobias eingetragen. Eine Mail geht raus.", cut.Markup, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"{ListePfad}/{Tobias}/absagen");
    }

    [Fact]
    public void Schon_entschieden_zeigt_den_Hinweis_und_laedt_neu()
    {
        Kontext(Aktionsschluessel.AnmeldungenBearbeiten);
        Liste(40, 1, Alle);
        _server.Bei(HttpMethod.Post, $"{ListePfad}/{Emma}/bestaetigen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var cut = Seite();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));

        cut.FindAll("tbody tr").Single(z => z.TextContent.Contains("Emma", StringComparison.Ordinal)).QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("Diese Anmeldung wurde schon entschieden.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad == ListePfad));
    }

    [Fact]
    public void Ohne_Anmeldungen_zeigen_beide_Abschnitte_den_Leer_Zustand()
    {
        Kontext(Aktionsschluessel.AnmeldungenBearbeiten);
        Liste(40, 0);

        var cut = Seite();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".elkw-empty").Count));
        Assert.All(cut.FindAll(".elkw-empty"), e => Assert.Contains("Noch keine Anmeldungen.", e.TextContent, StringComparison.Ordinal));
        Assert.Equal("0 von 40", cut.Find(".zl-balken .zl-zahl").TextContent);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(false, HttpStatusCode.NotFound)]
    public void Ohne_erlaubte_Aktion_oder_bei_403_404_ist_die_Seite_nicht_verfuegbar(bool ohneErlaubnis, HttpStatusCode status)
    {
        if (ohneErlaubnis)
        {
            Kontext();
            Liste(40, 0);
        }
        else
        {
            Kontext(Aktionsschluessel.AnmeldungenBearbeiten);
            _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(status));
        }

        var cut = Render<Anmeldungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Die_Freizeit_Startseite_zeigt_den_Knopf_Anmeldungen_nur_mit_der_erlaubten_Aktion(bool erlaubt)
    {
        Kontext(erlaubt ? [Aktionsschluessel.AnmeldungenBearbeiten] : []);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}", () => Json(new
        {
            id = FreizeitId, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        var knopf = cut.FindAll("a").Where(a => a.TextContent == "Anmeldungen").ToList();
        if (erlaubt)
        {
            Assert.Equal($"freizeiten/{FreizeitId}/anmeldungen", Assert.Single(knopf).GetAttribute("href"));
        }
        else
        {
            Assert.Empty(knopf);
        }
    }

    // ---------- Baustein ----------

    [Fact]
    public void Kontakt_fuer_Absagen_zeigt_Mail_und_Telefon_als_Links_oder_den_allgemeinen_Satz()
    {
        var beide = Render<AbsageKontakt>(ps => ps.Add(p => p.Mail, " zeltlager@example.org ").Add(p => p.Telefon, "+49 123 / 456-789"));
        var nurTelefon = Render<AbsageKontakt>(ps => ps.Add(p => p.Telefon, "0123 456789"));
        var ohne = Render<AbsageKontakt>(ps => ps.Add(p => p.Mail, " "));

        Assert.Equal("Absage nur über die Leitung: zeltlager@example.org · +49 123 / 456-789", beide.Find("p").TextContent.Trim());
        Assert.Equal(["mailto:zeltlager@example.org", "tel:+49123456789"], beide.FindAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal("Absage nur über die Leitung: 0123 456789", nurTelefon.Find("p").TextContent.Trim());
        Assert.Equal("tel:0123456789", nurTelefon.Find("a").GetAttribute("href"));
        Assert.Equal("Absage nur über die Leitung der Freizeit.", ohne.Find("p").TextContent.Trim());
        Assert.Empty(ohne.FindAll("a"));

        // Ohne Ziffer gibt es keinen tel:-Link, die Angabe steht als Text.
        var ohneZiffer = Render<AbsageKontakt>(ps => ps.Add(p => p.Telefon, "über die Gemeinde"));
        Assert.Equal("Absage nur über die Leitung: über die Gemeinde", ohneZiffer.Find("p").TextContent.Trim());
        Assert.Empty(ohneZiffer.FindAll("a"));
    }
}
