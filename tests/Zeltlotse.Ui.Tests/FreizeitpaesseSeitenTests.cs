using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Personen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;
using EinsichtSeite = Zeltlotse.App.Features.Freizeiten.FreizeitpassEinsicht;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-5 in der Web-App mit nachgebildetem Server und der echten Handler-Kette: die Client-Methoden (Personenliste, Pass im
/// Freizeit-Kontext, Team-Notizen), die Seite „Freizeitpässe“ (Liste, leer, kein Recht), die Seite eines Passes (drei Zustände,
/// Notiz anlegen, Feldfehler, löschen), der Knopf auf der Startseite der Freizeit und „Einsicht bis“ in Freizeitpass und Familie.
/// </summary>
public sealed class FreizeitpaesseSeitenTests : BunitContext
{
    private static readonly Guid FreizeitId = Guid.Parse("0199e500-0000-7000-8000-0000000000f1");
    private static readonly Guid Emma = Guid.Parse("0199e500-0000-7000-8000-0000000000a1");
    private static readonly Guid Mia = Guid.Parse("0199e500-0000-7000-8000-0000000000a2");
    private static readonly Guid Lea = Guid.Parse("0199e500-0000-7000-8000-0000000000a3");
    private static readonly Guid AnmeldungMia = Guid.Parse("0199e500-0000-7000-8000-0000000000c2");
    private static readonly Guid Notiz = Guid.Parse("0199e500-0000-7000-8000-0000000000d1");

    /// <summary>13.09.2027 00:00 Europe/Berlin: Ende des Freigabefensters der Freizeit.</summary>
    private const string FreigabeEnde = "2027-09-12T22:00:00+00:00";

    private readonly NachgebildeterHandler _server = new();
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
    private int _tokens;

    public FreizeitpaesseSeitenTests()
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
        Services.AddSingleton<TimeProvider>(_uhr);
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private static string ListePfad => $"/api/freizeiten/{FreizeitId}/personenliste";

    private static string PassPfad(Guid person) => $"/api/freizeiten/{FreizeitId}/personen/freizeitpass/{person}";

    private static string NotizenPfad => $"/api/freizeiten/{FreizeitId}/anmeldungen/{AnmeldungMia}/notizen";

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void Kontext(params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => Json(new
        {
            freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", phase = "InVorbereitung", rollen = new[] { "Team" }, schalter = Array.Empty<string>(), erlaubt,
        }));

    private static object Zeile(Guid anmeldung, Guid person, string vorname, string nachname, string art, string status) =>
        new { anmeldungId = anmeldung, personId = person, vorname, nachname, art, status };

    private static object[] Alle =>
    [
        Zeile(Guid.CreateVersion7(), Emma, "Emma", "Muster", "Teilnehmende", "Eingegangen"),
        Zeile(AnmeldungMia, Mia, "Mia", "Probst", "Teilnehmende", "Bestaetigt"),
        Zeile(Guid.CreateVersion7(), Lea, "Lea", "Feuerstein", "Mitarbeitende", "Bestaetigt"),
    ];

    private void Liste(params object[] zeilen) => _server.Bei(HttpMethod.Get, ListePfad, () => Json(zeilen));

    private static object Frage(string kennung, string frage, string art, string? wert, object[]? optionen = null, string? sonstiges = null) =>
        new { kennung, frage, art, optionen = optionen ?? [], mitSonstiges = sonstiges is not null, wert, sonstiges, version = 1u, veraltet = false };

    private static object Pass(Guid person, string vorname, string nachname, string zustand, string? beendetAm = null, params object[] fragen) =>
        new { personId = person, vorname, nachname, zustand, einsichtBis = beendetAm ?? FreigabeEnde, beendetAm, fragen };

    private static object[] MiasFragen =>
    [
        Frage(
            "allergien", "Allergien und Unverträglichkeiten", "Auswahl", "gluten,milch",
            [new { wert = "gluten", text = "Glutenhaltiges Getreide" }, new { wert = "milch", text = "Milch (inkl. Laktose)" }], "Kiwi"),
        Frage("notfallkontakte", "Notfallkontakte (Name, Telefon)", "Freitext", "Daniel Probst (Vater), 0123 456781"),
        Frage("tetanus", "Letzte Tetanus-Impfung", "Datum", "2024-03-05"),
        Frage("schwimmen", "Kann schwimmen", "JaNein", "nein", [new { wert = "ja", text = "Ja" }, new { wert = "nein", text = "Nein" }]),
        Frage("zecken-entfernen", "Zecken dürfen vom Team entfernt werden", "JaNein", null, [new { wert = "ja", text = "Ja" }, new { wert = "nein", text = "Nein" }]),
    ];

    private void Notizen(params object[] notizen) => _server.Bei(HttpMethod.Get, NotizenPfad, () => Json(notizen));

    private static object NotizVonLea(Guid? id = null) => new
    {
        id = id ?? Notiz, text = "Hatte letztes Jahr Heimweh.", verfasstVon = Lea, verfasserVorname = "Lea", verfasserNachname = "Feuerstein",
        angelegtAm = "2026-10-12T09:00:00+00:00",
    };

    private IRenderedComponent<EinsichtSeite> Einsicht(Guid person)
    {
        var cut = Render<EinsichtSeite>(ps => ps.Add(p => p.FreizeitId, FreizeitId).Add(p => p.PersonId, person));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wird geladen", cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_liest_Personenliste_Pass_und_Notizen_legt_an_meldet_Feldfehler_und_loescht()
    {
        Liste(Alle);
        _server.Bei(HttpMethod.Get, PassPfad(Mia), () => Json(Pass(Mia, "Mia", "Probst", "sichtbar", null, MiasFragen)));
        _server.Bei(HttpMethod.Get, PassPfad(Emma), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        Notizen(NotizVonLea());
        _server.BeiDerReihe(
            HttpMethod.Post,
            NotizenPfad,
            () => Json(new { id = Notiz }, HttpStatusCode.Created),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"text":["Text fehlt."]}}""", "application/problem+json"),
            () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.BeiDerReihe(HttpMethod.Delete, $"{NotizenPfad}/{Notiz}", () => new HttpResponseMessage(HttpStatusCode.NoContent), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var freizeiten = Services.GetRequiredService<FreizeitenApiClient>();
        var personen = Services.GetRequiredService<PersonenApiClient>();

        var liste = await freizeiten.PersonenlisteLadenAsync(FreizeitId, Abbruch);
        var pass = await personen.FreizeitpassEinsichtLadenAsync(FreizeitId, Mia, Abbruch);
        var ohne = await personen.FreizeitpassEinsichtLadenAsync(FreizeitId, Emma, Abbruch);
        var notizen = await freizeiten.TeamNotizenLadenAsync(FreizeitId, AnmeldungMia, Abbruch);
        var angelegt = await freizeiten.TeamNotizAnlegenAsync(FreizeitId, AnmeldungMia, "Isst gern Nudeln.", Abbruch);
        var fehler = await freizeiten.TeamNotizAnlegenAsync(FreizeitId, AnmeldungMia, " ", Abbruch);
        var weg = await freizeiten.TeamNotizAnlegenAsync(FreizeitId, AnmeldungMia, "x", Abbruch);

        Assert.Equal([AnmeldungStatus.Eingegangen, AnmeldungStatus.Bestaetigt, AnmeldungStatus.Bestaetigt], liste!.Select(z => z.Status));
        Assert.Equal((FreizeitpassEinsichtZustand.Sichtbar, 5, "Probst"), (pass!.Zustand, pass.Fragen.Count, pass.Nachname));
        Assert.Null(ohne);
        Assert.Equal("Hatte letztes Jahr Heimweh.", Assert.Single(notizen!).Text);
        Assert.Equal(new TeamNotizErgebnis(Notiz, null, false), angelegt);
        Assert.Equal(new TeamNotizErgebnis(null, "Text fehlt.", false), fehler);
        Assert.True(weg.NichtVerfuegbar);
        Assert.Equal("""{"text":"Isst gern Nudeln."}""", _server.Anfragen.First(a => a.Methode == HttpMethod.Post && a.Pfad == NotizenPfad).Koerper);
        Assert.True(await freizeiten.TeamNotizLoeschenAsync(FreizeitId, AnmeldungMia, Notiz, Abbruch));
        Assert.False(await freizeiten.TeamNotizLoeschenAsync(FreizeitId, AnmeldungMia, Notiz, Abbruch));
        Assert.Contains(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Kopfzeilen.ContainsKey(Kopfzeilen.Antiforgery));
    }

    // ---------- Seite „Freizeitpässe“ ----------

    [Fact]
    public void Freizeitpaesse_zeigt_je_Art_die_Namen_als_Link_auf_den_Pass_und_den_Status()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);

        var cut = Render<Freizeitpaesse>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));
        Assert.Equal("Freizeitpässe", cut.Find("h1").TextContent);
        Assert.Equal(["Teilnehmende", "Mitarbeitende"], cut.FindAll("h2").Select(h => h.TextContent));
        var links = cut.FindAll("tbody a");
        Assert.Equal(["Emma Muster", "Mia Probst", "Lea Feuerstein"], links.Select(a => a.TextContent));
        Assert.Equal($"freizeiten/{FreizeitId}/freizeitpaesse/{Mia}", links[1].GetAttribute("href"));
        Assert.Equal(["Eingegangen", "Bestätigt", "Bestätigt"], cut.FindAll("tbody .elkw-status").Select(s => s.TextContent));
    }

    [Fact]
    public void Freizeitpaesse_hat_ueber_den_Tabellen_die_Schnellsuche_Person_suchen()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);

        var cut = Render<Freizeitpaesse>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));
        var suche = cut.Find("input[type=search]");
        Assert.Equal("Person suchen", cut.Find($"label[for='{suche.Id}']").TextContent.Trim());
        Assert.True(cut.Markup.IndexOf("type=\"search\"", StringComparison.Ordinal) < cut.Markup.IndexOf("<table", StringComparison.Ordinal));

        suche.Input("mia");

        var treffer = cut.Find(".elkw-people a");
        Assert.Equal(("Mia Probst", $"freizeiten/{FreizeitId}/freizeitpaesse/{Mia}"), (treffer.TextContent, treffer.GetAttribute("href")));
        Assert.Equal(3, cut.FindAll("tbody tr").Count);
    }

    [Fact]
    public void Freizeitpaesse_hat_den_Knopf_Notfallordner_drucken()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);

        var cut = Render<Freizeitpaesse>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));
        var knopf = cut.FindAll("a.elkw-btn").Single(a => a.TextContent == "Notfallordner drucken");
        Assert.Equal($"freizeiten/{FreizeitId}/notfallordner", knopf.GetAttribute("href"));
        Assert.DoesNotContain("elkw-btn--noarrow", knopf.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Freizeitpaesse_ohne_Anmeldungen_zeigt_den_Leer_Zustand()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste();

        var cut = Render<Freizeitpaesse>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".elkw-empty").Count));
        Assert.All(cut.FindAll(".elkw-empty p"), p => Assert.Equal("Noch keine Anmeldungen.", p.TextContent));
        Assert.Empty(cut.FindAll("table"));
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(false, HttpStatusCode.NotFound)]
    public void Ohne_erlaubte_Aktion_oder_bei_403_404_sind_Liste_und_Pass_nicht_verfuegbar(bool ohneErlaubnis, HttpStatusCode status)
    {
        if (ohneErlaubnis)
        {
            Kontext();
            Liste(Alle);
            _server.Bei(HttpMethod.Get, PassPfad(Mia), () => Json(Pass(Mia, "Mia", "Probst", "sichtbar", null, MiasFragen)));
        }
        else
        {
            Kontext(Aktionsschluessel.FreizeitpaesseSehen);
            _server.Bei(HttpMethod.Get, ListePfad, () => new HttpResponseMessage(status));
            _server.Bei(HttpMethod.Get, PassPfad(Mia), () => new HttpResponseMessage(status));
        }

        var liste = Render<Freizeitpaesse>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        var pass = Render<EinsichtSeite>(ps => ps.Add(p => p.FreizeitId, FreizeitId).Add(p => p.PersonId, Mia));

        liste.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", liste.Find("h1").TextContent));
        pass.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", pass.Find("h1").TextContent));
        if (ohneErlaubnis)
        {
            Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == ListePfad || a.Pfad == PassPfad(Mia));
        }
    }

    // ---------- Seite eines Passes ----------

    [Fact]
    public void Sichtbar_zeigt_Einsicht_bis_und_die_Angaben_nur_lesend_mit_lesbaren_Antworten()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);
        _server.Bei(HttpMethod.Get, PassPfad(Mia), () => Json(Pass(Mia, "Mia", "Probst", "sichtbar", null, MiasFragen)));
        Notizen();

        var cut = Einsicht(Mia);

        Assert.Equal("Mia Probst", cut.Find("h1").TextContent);
        Assert.Contains("Einsicht bis 12.09.2027", cut.Markup, StringComparison.Ordinal);
        var angaben = cut.FindAll(".elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Angaben");
        Assert.Equal(
            ["Allergien und Unverträglichkeiten", "Notfallkontakte (Name, Telefon)", "Letzte Tetanus-Impfung", "Kann schwimmen", "Zecken dürfen vom Team entfernt werden"],
            angaben.QuerySelectorAll("dt").Select(d => d.TextContent));
        Assert.Equal(
            ["Glutenhaltiges Getreide, Milch (inkl. Laktose), Kiwi", "Daniel Probst (Vater), 0123 456781", "05.03.2024", "Nein", "keine Angabe"],
            angaben.QuerySelectorAll("dd").Select(d => d.TextContent));
        Assert.Empty(angaben.QuerySelectorAll("input, textarea, button"));
        Assert.Contains("Bleiben in der Freizeit und erscheinen nicht im Freizeitpass.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Noch keine Team-Notizen.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vor_der_Bestaetigung_steht_der_Hinweis_statt_der_Angaben()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);
        _server.Bei(HttpMethod.Get, PassPfad(Emma), () => Json(Pass(Emma, "Emma", "Muster", "abBestaetigung")));
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/notizen", StringComparison.Ordinal), _ => Task.FromResult(Json(Array.Empty<object>())));

        var cut = Einsicht(Emma);

        Assert.Equal("Emma Muster", cut.Find("h1").TextContent);
        Assert.Contains("Freizeitpass sichtbar ab Bestätigung der Anmeldung.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Einsicht bis 12.09.2027", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll(".elkw-card h2"), h => h.TextContent == "Angaben");
        Assert.Contains(cut.FindAll(".elkw-card h2"), h => h.TextContent == "Team-Notizen");
    }

    [Fact]
    public void Beendet_zeigt_Einsicht_beendet_am_ohne_Angaben_und_ohne_Anmeldung_keine_Notizen()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);
        var unbekannt = Guid.CreateVersion7();
        _server.Bei(HttpMethod.Get, PassPfad(unbekannt), () => Json(Pass(unbekannt, "Paul", "Muster", "beendet", "2026-10-01T10:00:00+00:00")));

        var cut = Einsicht(unbekannt);

        Assert.Contains("Einsicht beendet am 01.10.2026.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Einsicht bis", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".elkw-card"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.EndsWith("/notizen", StringComparison.Ordinal));
    }

    [Fact]
    public void Notizen_stehen_neueste_zuerst_mit_Verfasser_und_Datum_und_eine_neue_Notiz_wird_gespeichert()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);
        _server.Bei(HttpMethod.Get, PassPfad(Mia), () => Json(Pass(Mia, "Mia", "Probst", "sichtbar", null, MiasFragen)));
        var zweite = Guid.CreateVersion7();
        _server.BeiDerReihe(
            HttpMethod.Get,
            NotizenPfad,
            () => Json(new[] { NotizVonLea() }),
            () => Json(new object[]
            {
                new { id = zweite, text = "Isst gern Nudeln.", verfasstVon = Guid.CreateVersion7(), verfasserVorname = "Miriam", verfasserNachname = "Zeltner", angelegtAm = "2026-10-13T08:00:00+00:00" },
                NotizVonLea(),
            }));
        _server.Bei(HttpMethod.Post, NotizenPfad, () => Json(new { id = zweite }, HttpStatusCode.Created));
        var cut = Einsicht(Mia);
        Assert.Contains("Lea Feuerstein · 12.10.2026", cut.Markup, StringComparison.Ordinal);

        cut.Find("textarea#team-notiz-text").Input("Isst gern Nudeln.");
        cut.Find("form#team-notiz").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Notiz gespeichert.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal));
        Assert.Equal("""{"text":"Isst gern Nudeln."}""", _server.Anfragen.Single(a => a.Methode == HttpMethod.Post && a.Pfad == NotizenPfad).Koerper);
        Assert.Equal(["Isst gern Nudeln.", "Hatte letztes Jahr Heimweh."], cut.FindAll("[id^=notiz-] > p:first-child").Select(p => p.TextContent));
        Assert.Equal("Neue Notiz", cut.Find("label[for=team-notiz-text]").TextContent);
        Assert.Null(cut.Find("textarea#team-notiz-text").GetAttribute("value"));
    }

    [Fact]
    public void Leere_Notiz_zeigt_den_Feldfehler_und_behaelt_die_Liste()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);
        _server.Bei(HttpMethod.Get, PassPfad(Mia), () => Json(Pass(Mia, "Mia", "Probst", "sichtbar", null, MiasFragen)));
        Notizen(NotizVonLea());
        _server.Bei(HttpMethod.Post, NotizenPfad, () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"text":["Text fehlt."]}}""", "application/problem+json"));
        var cut = Einsicht(Mia);

        cut.Find("form#team-notiz").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Text fehlt.", cut.Find(".elkw-field-error").TextContent));
        Assert.Equal("true", cut.Find("textarea#team-notiz-text").GetAttribute("aria-invalid"));
        Assert.Single(cut.FindAll("[id^=notiz-]"));
    }

    [Fact]
    public void Loeschen_fragt_inline_nach_und_loescht_erst_nach_Ja()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);
        _server.Bei(HttpMethod.Get, PassPfad(Mia), () => Json(Pass(Mia, "Mia", "Probst", "sichtbar", null, MiasFragen)));
        _server.BeiDerReihe(HttpMethod.Get, NotizenPfad, () => Json(new[] { NotizVonLea() }), () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Delete, $"{NotizenPfad}/{Notiz}", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Einsicht(Mia);

        cut.FindAll("button").Single(b => b.TextContent == "Löschen").Click();
        Assert.Contains("Diese Notiz löschen?", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Delete);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Notiz gelöscht.", cut.Markup, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Delete && a.Pfad == $"{NotizenPfad}/{Notiz}");
        Assert.Contains("Noch keine Team-Notizen.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Loeschen_mit_404_zeigt_keine_Erfolgsmeldung_und_ohne_ladbare_Notizen_ist_die_Seite_nicht_verfuegbar()
    {
        Kontext(Aktionsschluessel.FreizeitpaesseSehen);
        Liste(Alle);
        _server.Bei(HttpMethod.Get, PassPfad(Mia), () => Json(Pass(Mia, "Mia", "Probst", "sichtbar", null, MiasFragen)));
        _server.BeiDerReihe(HttpMethod.Get, NotizenPfad, () => Json(new[] { NotizVonLea(), NotizVonLea(Guid.CreateVersion7()) }), () => Json(new[] { NotizVonLea() }), () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Delete, $"{NotizenPfad}/{Notiz}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Einsicht(Mia);

        // Die Notiz gibt es nicht mehr, die Notizen sind aber noch ladbar: neu laden, keine Erfolgsmeldung.
        cut.FindAll("button").First(b => b.TextContent == "Löschen").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[id^=notiz-]")));
        Assert.DoesNotContain("Notiz gelöscht.", cut.Markup, StringComparison.Ordinal);

        // Danach liefert auch das Laden 404: kein Zugriff mehr.
        cut.FindAll("button").Single(b => b.TextContent == "Löschen").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, löschen").Click();
        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }

    // ---------- Startseite und „Einsicht bis“ ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Die_Freizeit_Startseite_zeigt_den_Knopf_Freizeitpaesse_nur_mit_der_erlaubten_Aktion(bool erlaubt)
    {
        Kontext(erlaubt ? [Aktionsschluessel.FreizeitpaesseSehen] : []);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}", () => Json(new
        {
            id = FreizeitId, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        var knopf = cut.FindAll("a").Where(a => a.TextContent == "Freizeitpässe").ToList();
        Assert.Equal(erlaubt ? 1 : 0, knopf.Count);
        if (erlaubt)
        {
            Assert.Equal($"freizeiten/{FreizeitId}/freizeitpaesse", knopf[0].GetAttribute("href"));
        }
    }

    private static object AnmeldungZeile(Guid anmeldung, Guid person, string status, string? einsichtBis) => new
    {
        anmeldungId = anmeldung, personId = person, freizeitName = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13",
        art = "Teilnehmende", status, kontaktMail = (string?)null, kontaktTelefon = (string?)null, fragen = new[] { "allergien" }, einsichtBis,
    };

    [Fact]
    public void Der_eigene_Freizeitpass_nennt_Einsicht_bis_und_nach_Ablauf_Einsicht_beendet_am()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/anmeldungen", () => Json(new[] { AnmeldungZeile(AnmeldungMia, Mia, "Eingegangen", FreigabeEnde) }));
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Mia}", () => Json(new { personId = Mia, vorname = "Mia", fragen = Array.Empty<object>() }));

        var cut = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, AnmeldungMia));
        cut.WaitForAssertion(() => Assert.Contains("Einsicht bis 12.09.2027", cut.Markup, StringComparison.Ordinal));

        _uhr.SetUtcNow(new DateTimeOffset(2027, 9, 13, 8, 0, 0, TimeSpan.Zero));
        var spaeter = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, AnmeldungMia));
        spaeter.WaitForAssertion(() => Assert.Contains("Einsicht beendet am 12.09.2027", spaeter.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Die_Familie_nennt_Einsicht_bis_je_Anmeldung_auch_nach_einer_Absage()
    {
        var kinder = new[] { new { personId = Mia, version = 3u, vorname = "Mia", nachname = "Probst", geburtsdatum = "2016-05-21", volljaehrig = false, uebergabe = (object?)null } };
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => Json(new { eltern = Array.Empty<object>(), kinder, einladungen = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, "/api/ich/anmeldungen", () => Json(new[]
        {
            AnmeldungZeile(AnmeldungMia, Mia, "Bestaetigt", FreigabeEnde),
            AnmeldungZeile(Guid.CreateVersion7(), Mia, "Abgesagt", "2026-10-01T10:00:00+00:00"),
            AnmeldungZeile(Guid.CreateVersion7(), Mia, "Eingegangen", null),
        }));
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Mia}", () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<FamilieAnsicht>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".zl-statusliste li").Count));
        var zeilen = cut.FindAll(".zl-statusliste li");
        Assert.Contains("Einsicht bis 12.09.2027", zeilen[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Einsicht beendet am 01.10.2026", zeilen[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Einsicht", zeilen[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Anzeige_Einsicht_rechnet_bis_einschliesslich_und_Antwort_ohne_Wert_heisst_keine_Angabe()
    {
        var ende = DateTimeOffset.Parse(FreigabeEnde, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("Einsicht bis 12.09.2027", Anzeige.Einsicht(ende, ende.AddDays(-1)));
        Assert.Equal("Einsicht beendet am 12.09.2027", Anzeige.Einsicht(ende, ende));
        Assert.Equal("keine Angabe", Anzeige.Antwort(new FreizeitpassFrage("medikamente", "Medikamente", FrageArt.Freitext, [], false, " ", null, 0, false)));
        Assert.Equal("Freizeitpass eingesehen", Anzeige.Zweck(ZugriffZweck.FreizeitpassEingesehen));
    }
}
