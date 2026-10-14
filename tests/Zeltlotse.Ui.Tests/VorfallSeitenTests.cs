using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Features.Sicherheit;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;
using EinsichtSeite = Zeltlotse.App.Features.Freizeiten.FreizeitpassEinsicht;
using KetteSeite = Zeltlotse.App.Features.Sicherheit.Sicherheitskette;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-10 in der Web-App mit nachgebildetem Server: der Client, die Seite „Vorfall-Protokoll“ (Liste, leer, Speichern beim Verlassen
/// eines Felds mit der richtigen Art, Fehler behält den Text, abgeschlossen ohne Felder, nicht verfügbar, unlesbarer Text), die Card
/// „Vorfall-Notizen“ auf der Startseite der Leitung, der Link im Freizeitpass, die Card in der Sicherheitskette, das Archiv der Organisation
/// und die Card auf der Organisationsseite.
/// </summary>
public sealed class VorfallSeitenTests : BunitContext
{
    private static readonly Guid Freizeit = Guid.Parse("0199fa00-0000-7000-8000-0000000000d1");
    private static readonly Guid OrgId = Guid.Parse("0199fa00-0000-7000-8000-0000000000c1");
    private static readonly Guid Mia = Guid.Parse("0199fa00-0000-7000-8000-0000000000e1");
    private static readonly Guid Lea = Guid.Parse("0199fa00-0000-7000-8000-0000000000e2");
    private static readonly Guid Jonas = Guid.Parse("0199fa00-0000-7000-8000-0000000000e3");
    private static readonly Guid AnmeldungMia = Guid.Parse("0199fa00-0000-7000-8000-0000000000a1");

    /// <summary>13.10.2026 10:15 Uhr in Berlin (Sommerzeit).</summary>
    private static readonly DateTimeOffset Erster = new(2026, 10, 13, 8, 15, 0, TimeSpan.Zero);

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public VorfallSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        // Story 3-5: Die Startseite lädt die Aufgaben mit; ohne Aufgaben weder Zeile noch eigene Aufgaben.
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/programm/aufgaben", StringComparison.Ordinal),
            _ => Task.FromResult(NachgebildeterHandler.Json(HttpStatusCode.OK, "[]")));
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

    private static string VorfaellePfad => $"/api/freizeiten/{Freizeit}/sicherheit/vorfaelle";

    private static string ArchivPfad => $"/api/organisationen/{OrgId}/sicherheit/vorfall-archiv";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object Eintrag(string art, string? text, int minuten, Guid verfasser, string verfasserVorname, string verfasserNachname) => new
    {
        id = Guid.CreateVersion7(), personId = Mia, vorname = "Mia", nachname = "Probst", art, text, erfasstAm = Erster.AddMinutes(minuten),
        verfasserVorname, verfasserNachname, verfasstVon = verfasser,
    };

    /// <summary>Die drei Demo-Einträge zu Mia, neueste oben.</summary>
    private static object[] Drei =>
    [
        Eintrag("Medikament", "Kühlpack, kein Medikament", 20, Jonas, "Jonas", "Hüttner"),
        Eintrag("Entnahme", "2 Pflaster, 1 sterile Kompresse", 10, Lea, "Lea", "Feuerstein"),
        Eintrag("Geschehen", "Beim Geländespiel am linken Knie geschnitten.", 0, Lea, "Lea", "Feuerstein"),
    ];

    private void Kontext(string phase = "InVorbereitung", params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new
        {
            freizeitId = Freizeit, name = "Zeltlager Sommer 2027", phase, rollen = new[] { "Team" }, schalter = Array.Empty<string>(),
            erlaubt = erlaubt.Length == 0 ? [Aktionsschluessel.FreizeitpaesseSehen] : erlaubt,
        }));

    private void Personenliste() => _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personenliste", () => Json(new[]
    {
        new { anmeldungId = AnmeldungMia, personId = Mia, vorname = "Mia", nachname = "Probst", art = "Teilnehmende", status = "Bestaetigt" },
    }));

    private void Freizeit_() => _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new
    {
        id = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
        leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner" } },
    }));

    private void Vorfaelle(params object[] eintraege) => _server.Bei(HttpMethod.Get, VorfaellePfad, () => Json(eintraege));

    private IRenderedComponent<VorfallProtokoll> Protokoll()
    {
        var cut = Render<VorfallProtokoll>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Mia));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private static AngleSharp.Dom.IElement? Card<T>(IRenderedComponent<T> cut, string titel)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll(".elkw-card").SingleOrDefault(c => c.QuerySelector(".elkw-card__title")?.TextContent == titel);

    /// <summary>Tippt den Text ins Feld (input) und verlässt es (focusout).</summary>
    private static void Verlassen(IRenderedComponent<VorfallProtokoll> cut, string feld, string? text = null)
    {
        if (text is not null)
        {
            cut.Find($"#{feld}").Input(text);
        }

        cut.Find($"#{feld}").FocusOut();
    }

    private Guid[] GesendeteKennungen() =>
    [
        .. _server.Anfragen.Where(a => a.Methode == HttpMethod.Post).Select(a => JsonDocument.Parse(a.Koerper!).RootElement.GetProperty("id").GetGuid()),
    ];

    // ---------- Client ----------

    [Fact]
    public async Task Client_liest_mit_und_ohne_Person_legt_an_und_liest_das_Archiv_403_404_heissen_nicht_verfuegbar()
    {
        Vorfaelle(Drei);
        _server.Bei(HttpMethod.Get, ArchivPfad, () => Json(new[] { new { freizeitId = Freizeit, name = "Zeltlager 2026", eintraege = new[] { Drei[0] } } }));
        var client = Services.GetRequiredService<SicherheitApiClient>();

        var alle = await client.VorfaelleLadenAsync(Freizeit, abbruch: Abbruch);
        var mia = await client.VorfaelleLadenAsync(Freizeit, Mia, Abbruch);
        var archiv = await client.VorfallArchivLadenAsync(OrgId, Abbruch);

        Assert.NotNull(alle);
        Assert.NotNull(mia);
        Assert.NotNull(archiv);
        Assert.Equal([VorfallArt.Medikament, VorfallArt.Entnahme, VorfallArt.Geschehen], alle.Select(e => e.Art));
        Assert.Equal(("Mia", "Kühlpack, kein Medikament", "Jonas", Erster.AddMinutes(20)), (alle[0].Vorname, alle[0].Text, alle[0].VerfasserVorname, alle[0].ErfasstAm));
        Assert.Equal(3, mia.Count);
        Assert.Contains(_server.Anfragen, a => a.Pfad == $"{VorfaellePfad}?personId={Mia}");
        Assert.Equal(("Zeltlager 2026", VorfallArt.Medikament), (Assert.Single(archiv).Name, archiv[0].Eintraege[0].Art));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            _server.Bei(HttpMethod.Get, VorfaellePfad, () => new HttpResponseMessage(status));
            _server.Bei(HttpMethod.Get, ArchivPfad, () => new HttpResponseMessage(status));
            Assert.Null(await client.VorfaelleLadenAsync(Freizeit, abbruch: Abbruch));
            Assert.Null(await client.VorfallArchivLadenAsync(OrgId, Abbruch));
        }

        var antworten = new Func<HttpResponseMessage>[]
        {
            () => new HttpResponseMessage(HttpStatusCode.Created),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"personId":["Diese Person gehört nicht zur Freizeit."]}}""", "application/problem+json"),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        _server.BeiDerReihe(HttpMethod.Post, VorfaellePfad, antworten);
        var id = Guid.CreateVersion7();
        var ergebnisse = new List<VorfallAnlegenErgebnis>();
        foreach (var _ in antworten)
        {
            ergebnisse.Add(await client.VorfallAnlegenAsync(Freizeit, new VorfallEintragNeu(id, Mia, VorfallArt.Entnahme, "Pflaster"), Abbruch));
        }

        Assert.Equal(
            [VorfallAnlegenErgebnis.Erfolg, VorfallAnlegenErgebnis.Abgeschlossen, VorfallAnlegenErgebnis.Fehler, VorfallAnlegenErgebnis.Fehler, VorfallAnlegenErgebnis.Fehler],
            ergebnisse);
        Assert.Equal(
            $$"""{"id":"{{id}}","personId":"{{Mia}}","art":"Entnahme","text":"Pflaster"}""",
            _server.Anfragen.First(a => a.Methode == HttpMethod.Post).Koerper);
    }

    // ---------- Seite „Vorfall-Protokoll“ ----------

    [Fact]
    public void Liste_zeigt_Dachzeile_Name_Hinweis_drei_Felder_ohne_Senden_Knopf_und_die_Eintraege_neueste_oben()
    {
        Kontext();
        Personenliste();
        Vorfaelle(Drei);

        var cut = Protokoll();

        Assert.Equal("Notfall", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Vorfall-Protokoll", cut.Find("h1").TextContent);
        Assert.Contains("<p>Mia Probst</p>", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(
            "Jeder Eintrag ist sofort für die Leitung sichtbar. Einträge lassen sich nicht ändern; eine Korrektur ist ein neuer Eintrag.",
            cut.Find(".elkw-alert--hinweis").TextContent,
            StringComparison.Ordinal);
        Assert.Equal(
            ["Was ist passiert?", "Medikament gegeben (welches, wie viel)", "Aus dem Erste-Hilfe-Kasten entnommen"],
            cut.FindAll("label").Select(l => l.TextContent));
        Assert.Equal(3, cut.FindAll("textarea").Count);
        Assert.Empty(cut.FindAll("button"));
        var protokoll = Card(cut, "Protokoll")!;
        var eintraege = protokoll.QuerySelectorAll("div[id^=vorfall-]").Select(d => d.QuerySelectorAll("p").Select(p => p.TextContent).ToList()).ToList();
        Assert.Equal(
            [
                ["13.10.2026 10:35 · Medikament gegeben", "Kühlpack, kein Medikament", "von Jonas Hüttner"],
                ["13.10.2026 10:25 · Aus dem Erste-Hilfe-Kasten entnommen", "2 Pflaster, 1 sterile Kompresse", "von Lea Feuerstein"],
                ["13.10.2026 10:15 · Was passiert ist", "Beim Geländespiel am linken Knie geschnitten.", "von Lea Feuerstein"],
            ],
            eintraege);
        Assert.Equal($"/api/freizeiten/{Freizeit}/sicherheit/vorfaelle?personId={Mia}", _server.Anfragen.Single(a => a.Pfad.StartsWith(VorfaellePfad, StringComparison.Ordinal)).Pfad);
        var zurueck = cut.FindAll("a").Single(a => a.TextContent == "Zum Freizeitpass");
        Assert.Equal($"freizeiten/{Freizeit}/freizeitpaesse/{Mia}", zurueck.GetAttribute("href"));
    }

    [Fact]
    public void Leer_zeigt_Noch_keine_Eintraege_und_ohne_Treffer_in_der_Personenliste_keinen_Namen()
    {
        Kontext();
        Vorfaelle();

        var cut = Protokoll();

        Assert.Contains("Noch keine Einträge.", Card(cut, "Protokoll")!.QuerySelector(".elkw-empty")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Mia Probst", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vorfall-geschehen", "Geschehen")]
    [InlineData("vorfall-medikament", "Medikament")]
    [InlineData("vorfall-entnahme", "Entnahme")]
    public void Verlassen_eines_Felds_speichert_einen_Eintrag_der_Art_leert_das_Feld_und_laedt_neu(string feld, string art)
    {
        Kontext();
        Personenliste();
        _server.BeiDerReihe(HttpMethod.Get, VorfaellePfad, () => Json(Array.Empty<object>()), () => Json(new[] { Eintrag(art, "Ibuprofen 200 mg", 30, Lea, "Lea", "Feuerstein") }));
        _server.Bei(HttpMethod.Post, VorfaellePfad, () => new HttpResponseMessage(HttpStatusCode.Created));
        var cut = Protokoll();

        Verlassen(cut, feld, "Ibuprofen 200 mg");

        cut.WaitForAssertion(() => Assert.Contains("Ibuprofen 200 mg", Card(cut, "Protokoll")!.TextContent, StringComparison.Ordinal));
        var post = Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post);
        using var koerper = JsonDocument.Parse(post.Koerper!);
        Assert.Equal(
            (Mia, art, "Ibuprofen 200 mg"),
            (koerper.RootElement.GetProperty("personId").GetGuid(), koerper.RootElement.GetProperty("art").GetString(), koerper.RootElement.GetProperty("text").GetString()));
        Assert.NotEqual(Guid.Empty, koerper.RootElement.GetProperty("id").GetGuid());
        Assert.True(string.IsNullOrEmpty(cut.Find($"#{feld}").GetAttribute("value")));
        Assert.Contains("Gespeichert", cut.Find(".zl-speicher-hinweis").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, _server.Anfragen.Count(a => a.Methode == HttpMethod.Get && a.Pfad.StartsWith(VorfaellePfad, StringComparison.Ordinal)));
    }

    [Fact]
    public void Leerer_Text_beim_Verlassen_sendet_nichts()
    {
        Kontext();
        Vorfaelle();
        var cut = Protokoll();

        Verlassen(cut, "vorfall-geschehen", "   ");

        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scheitert_das_Speichern_bleibt_der_Text_und_die_Meldung_steht_am_Feld(bool abgeschlossen)
    {
        Kontext();
        Vorfaelle();
        _server.Bei(HttpMethod.Post, VorfaellePfad, abgeschlossen
            ? () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.FreizeitAbgeschlossen)
            : () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = Protokoll();

        Verlassen(cut, "vorfall-medikament", "Kühlpack");

        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("#vorfall-medikament-fehler").SingleOrDefault()));
        Assert.Equal(
            $"Fehler: {(abgeschlossen ? "Diese Freizeit ist abgeschlossen und nur lesbar." : AnmeldungAbschluss.FehlerText)}",
            cut.Find("#vorfall-medikament-fehler").TextContent);
        Assert.Equal("Kühlpack", cut.Find("#vorfall-medikament").GetAttribute("value"));
        Assert.Equal("true", cut.Find("#vorfall-medikament").GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll(".zl-speicher-hinweis"));
    }

    [Fact]
    public void Nach_einem_Fehler_sendet_erneutes_Verlassen_ohne_Aenderung_denselben_Text_mit_derselben_Kennung()
    {
        Kontext();
        Vorfaelle();
        _server.BeiDerReihe(
            HttpMethod.Post, VorfaellePfad,
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage(HttpStatusCode.Created));
        var cut = Protokoll();

        Verlassen(cut, "vorfall-medikament", "Kühlpack");
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("#vorfall-medikament-fehler").SingleOrDefault()));
        Verlassen(cut, "vorfall-medikament");

        cut.WaitForAssertion(() => Assert.Equal(2, GesendeteKennungen().Length));
        var kennungen = GesendeteKennungen();
        Assert.Equal(kennungen[0], kennungen[1]);
        Assert.All(
            _server.Anfragen.Where(a => a.Methode == HttpMethod.Post),
            a => Assert.Equal("Kühlpack", JsonDocument.Parse(a.Koerper!).RootElement.GetProperty("text").GetString()));
        cut.WaitForAssertion(() => Assert.True(string.IsNullOrEmpty(cut.Find("#vorfall-medikament").GetAttribute("value"))));

        // Ein neuer Text danach bekommt eine neue Kennung.
        Verlassen(cut, "vorfall-medikament", "Kühlpack, zweites Mal");
        cut.WaitForAssertion(() => Assert.Equal(3, GesendeteKennungen().Length));
        Assert.NotEqual(kennungen[0], GesendeteKennungen()[2]);
    }

    [Fact]
    public void Ein_waehrend_der_Uebertragung_geaenderter_Text_bleibt_im_Feld_und_wird_danach_mit_neuer_Kennung_gesendet()
    {
        Kontext();
        Vorfaelle();
        var erste = new TaskCompletionSource<HttpResponseMessage>();
        var anzahl = 0;
        _server.Bei(
            r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == VorfaellePfad,
            _ => Interlocked.Increment(ref anzahl) == 1 ? erste.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)));
        var cut = Protokoll();

        Verlassen(cut, "vorfall-geschehen", "Knie");
        cut.WaitForAssertion(() => Assert.Single(GesendeteKennungen()));
        cut.Find("#vorfall-geschehen").Input("Knie geschnitten");
        // Verlassen während der Übertragung sendet nichts zusätzlich.
        cut.Find("#vorfall-geschehen").FocusOut();
        erste.SetResult(new HttpResponseMessage(HttpStatusCode.Created));

        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll(".zl-speicher-hinweis").SingleOrDefault()));
        Assert.Equal("Knie geschnitten", cut.Find("#vorfall-geschehen").GetAttribute("value"));
        Assert.Single(GesendeteKennungen());

        Verlassen(cut, "vorfall-geschehen");

        cut.WaitForAssertion(() => Assert.Equal(2, GesendeteKennungen().Length));
        Assert.NotEqual(GesendeteKennungen()[0], GesendeteKennungen()[1]);
        Assert.Contains("\"text\":\"Knie geschnitten\"", _server.Anfragen.Last(a => a.Methode == HttpMethod.Post).Koerper!, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.True(string.IsNullOrEmpty(cut.Find("#vorfall-geschehen").GetAttribute("value"))));
    }

    [Fact]
    public void Abgeschlossen_ohne_Felder_mit_der_Meldung_nur_lesbar()
    {
        Kontext(phase: "Gelaufen");
        Vorfaelle(Drei);

        var cut = Protokoll();

        Assert.Empty(cut.FindAll("textarea"));
        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Find(".elkw-alert").TextContent, StringComparison.Ordinal);
        Assert.Equal(3, Card(cut, "Protokoll")!.QuerySelectorAll("div[id^=vorfall-]").Length);
    }

    [Fact]
    public void Ein_unlesbarer_Text_heisst_Inhalt_nicht_mehr_lesbar()
    {
        Kontext();
        Vorfaelle(Eintrag("Geschehen", null, 0, Lea, "Gelöschte Person", null!));

        var cut = Protokoll();

        var zeilen = Card(cut, "Protokoll")!.QuerySelectorAll("p").Select(p => p.TextContent).ToList();
        Assert.Equal(["13.10.2026 10:15 · Was passiert ist", "Inhalt nicht mehr lesbar.", "von Gelöschte Person"], zeilen);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Ohne_Kontext_oder_ohne_Eintraege_ist_die_Seite_nicht_verfuegbar(bool kontextFehlt, bool eintraegeFehlen)
    {
        Kontext();
        Vorfaelle(Drei);
        if (kontextFehlt)
        {
            _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (eintraegeFehlen)
        {
            _server.Bei(HttpMethod.Get, VorfaellePfad, () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        var cut = Render<VorfallProtokoll>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Mia));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }

    // ---------- Startseite ----------

    private void StartseiteAntworten(bool leitung)
    {
        Kontext("InVorbereitung", leitung ? [Aktionsschluessel.FreizeitEinstellungen] : [Aktionsschluessel.FreizeitpaesseSehen]);
        Freizeit_();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/sicherheit/sicherheitskette", () => Json(new
        {
            wasTun = "x", leitungTelefon = "1", heimatName = (string?)null, heimatTelefon = "2", eingerichtet = true, version = 1u,
        }));
    }

    private IRenderedComponent<FreizeitStart> Startseite()
    {
        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        return cut;
    }

    [Fact]
    public void Startseite_der_Leitung_zeigt_die_Card_Vorfall_Notizen_neueste_oben_mit_Link_auf_das_Protokoll()
    {
        StartseiteAntworten(leitung: true);
        Vorfaelle(Drei);

        var cut = Startseite();

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Vorfall-Notizen")));
        var card = Card(cut, "Vorfall-Notizen")!;
        var eintraege = card.QuerySelectorAll("div[id^=vorfall-]").ToList();
        Assert.Equal(3, eintraege.Count);
        var link = eintraege[0].QuerySelector("a")!;
        Assert.Equal(("13.10.2026 10:35 · Mia Probst", $"freizeiten/{Freizeit}/vorfall/{Mia}"), (link.TextContent, link.GetAttribute("href")));
        Assert.Equal(
            ["Medikament gegeben: Kühlpack, kein Medikament", "von Jonas Hüttner"],
            eintraege[0].QuerySelectorAll("p").Skip(1).Select(p => p.TextContent));
        Assert.Equal("Aus dem Erste-Hilfe-Kasten entnommen: 2 Pflaster, 1 sterile Kompresse", eintraege[1].QuerySelectorAll("p")[1].TextContent);
        // Kein Warnhinweis: Die Card zählt nicht in „Warnhinweise“.
        var warnungen = Card(cut, "Warnhinweise")!;
        Assert.DoesNotContain("Vorfall", warnungen.TextContent, StringComparison.Ordinal);
        Assert.Contains("Keine offenen Punkte.", warnungen.TextContent, StringComparison.Ordinal);
        Assert.Equal(VorfaellePfad, _server.Anfragen.Single(a => a.Pfad.StartsWith(VorfaellePfad, StringComparison.Ordinal)).Pfad);
    }

    [Fact]
    public void Startseite_der_Leitung_ohne_Eintraege_oder_wenn_der_Abruf_scheitert_ohne_Card()
    {
        foreach (var antwort in new Func<HttpResponseMessage>[]
                 {
                     () => Json(Array.Empty<object>()),
                     () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                     () => new HttpResponseMessage(HttpStatusCode.NotFound),
                 })
        {
            StartseiteAntworten(leitung: true);
            _server.Bei(HttpMethod.Get, VorfaellePfad, antwort);

            var cut = Startseite();

            cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Warnhinweise")));
            Assert.Null(Card(cut, "Vorfall-Notizen"));
            Assert.DoesNotContain(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Startseite_des_Teams_laedt_keine_Vorfall_Notizen()
    {
        StartseiteAntworten(leitung: false);
        Vorfaelle(Drei);

        var cut = Startseite();

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("a"), a => a.TextContent.Trim() == "Sicherheitskette"));
        Assert.Null(Card(cut, "Vorfall-Notizen"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.StartsWith(VorfaellePfad, StringComparison.Ordinal));
    }

    // ---------- Freizeitpass und Sicherheitskette ----------

    [Fact]
    public void Der_Freizeitpass_verlinkt_nach_dem_Zurueck_Link_das_Vorfall_Protokoll()
    {
        Kontext();
        Personenliste();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personen/freizeitpass/{Mia}", () => Json(new
        {
            personId = Mia, vorname = "Mia", nachname = "Probst", zustand = "sichtbar", einsichtBis = "2027-09-12T22:00:00+00:00", beendetAm = (string?)null,
            fragen = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/anmeldungen/{AnmeldungMia}/notizen", () => Json(Array.Empty<object>()));

        var cut = Render<EinsichtSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.PersonId, Mia));

        cut.WaitForAssertion(() => Assert.Equal("Mia Probst", cut.Find("h1").TextContent));
        var links = cut.FindAll("a").Select(a => (a.TextContent, a.GetAttribute("href"))).ToList();
        var zurueck = links.FindIndex(l => l.TextContent == "Zurück zu den Freizeitpässen");
        Assert.Equal(("Vorfall-Protokoll", $"freizeiten/{Freizeit}/vorfall/{Mia}"), links[zurueck + 1]);
    }

    [Fact]
    public void Die_Sicherheitskette_zeigt_vor_Wen_anrufen_den_Weg_zum_Vorfall_Protokoll()
    {
        Kontext();
        Freizeit_();
        Personenliste();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/sicherheit/sicherheitskette", () => Json(new
        {
            wasTun = "Ruhe bewahren.", leitungTelefon = "0123 456782", heimatName = (string?)null, heimatTelefon = (string?)null, eingerichtet = false, version = 1u,
        }));

        var cut = Render<KetteSeite>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForAssertion(() => Assert.NotNull(Card(cut, "Vorfall-Protokoll")));
        Assert.Equal(
            "Betroffene Person oben suchen und im Freizeitpass „Vorfall-Protokoll“ wählen. Jeder Eintrag ist sofort für die Leitung sichtbar.",
            Card(cut, "Vorfall-Protokoll")!.QuerySelector("p")!.TextContent);
        var titel = cut.FindAll(".elkw-card__title").Select(t => t.TextContent).ToList();
        Assert.Equal(titel.IndexOf("Wen anrufen") - 1, titel.IndexOf("Vorfall-Protokoll"));
    }

    // ---------- Organisation ----------

    private void Organisation(params string[] erlaubt)
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" }, erlaubt }));
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten", () => Json(new { freizeiten = Array.Empty<object>(), erlaubt = Array.Empty<string>() }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Die_Organisation_zeigt_die_Card_Vorfall_Notizen_nur_mit_vorfall_archiv_sehen(bool darf)
    {
        Organisation(darf ? [Aktionsschluessel.VorfallArchivSehen] : []);

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Equal("Jugendwerk Musterstadt", cut.Find("h1").TextContent));
        var card = Card(cut, "Vorfall-Notizen");
        if (!darf)
        {
            Assert.Null(card);
            return;
        }

        Assert.Contains(
            "Nach dem Abschluss einer Freizeit liegen ihre Vorfall-Notizen hier, bis die Frist für Freizeit-Daten abläuft.", card!.TextContent, StringComparison.Ordinal);
        var knopf = card.QuerySelector("a")!;
        Assert.Equal(("Archiv öffnen", $"organisationen/{OrgId}/vorfall-archiv"), (knopf.TextContent.Trim(), knopf.GetAttribute("href")));
    }

    private IRenderedComponent<VorfallArchiv> Archiv()
    {
        var cut = Render<VorfallArchiv>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    [Fact]
    public void Archiv_zeigt_je_Freizeit_eine_Card_mit_den_Eintraegen_ohne_Link()
    {
        _server.Bei(HttpMethod.Get, ArchivPfad, () => Json(new[]
        {
            new { freizeitId = Freizeit, name = "Zeltlager Sommer 2026", eintraege = Drei },
            new { freizeitId = Guid.CreateVersion7(), name = "Pfingstfreizeit 2026", eintraege = new[] { Drei[2] } },
        }));

        var cut = Archiv();

        Assert.Equal("Organisation", cut.Find(".elkw-kicker").TextContent);
        Assert.Equal("Archiv der Vorfall-Notizen", cut.Find("h1").TextContent);
        Assert.Equal(["Zeltlager Sommer 2026", "Pfingstfreizeit 2026"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        var sommer = Card(cut, "Zeltlager Sommer 2026")!;
        Assert.Equal(3, sommer.QuerySelectorAll("div[id^=vorfall-]").Length);
        Assert.Equal("13.10.2026 10:35 · Mia Probst", sommer.QuerySelector("strong")!.TextContent);
        Assert.Empty(sommer.QuerySelectorAll("a"));
        Assert.Contains("Medikament gegeben: Kühlpack, kein Medikament", sommer.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Archiv_leer_zeigt_Noch_keine_archivierten_Vorfall_Notizen()
    {
        _server.Bei(HttpMethod.Get, ArchivPfad, () => Json(Array.Empty<object>()));

        var cut = Archiv();

        Assert.Contains("Noch keine archivierten Vorfall-Notizen.", cut.Find(".elkw-empty").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".elkw-card"));
        Assert.Equal($"organisationen/{OrgId}", cut.FindAll("a").Single(a => a.TextContent == "Zurück zur Organisation").GetAttribute("href"));
    }

    [Fact]
    public void Archiv_ohne_Recht_ist_nicht_verfuegbar()
    {
        _server.Bei(HttpMethod.Get, ArchivPfad, () => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var cut = Render<VorfallArchiv>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }
}
