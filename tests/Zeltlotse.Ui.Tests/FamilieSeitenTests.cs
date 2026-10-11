using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Personen;
using Zeltlotse.App.Pages;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 1-4 in der Web-App mit nachgebildetem Server und der echten Handler-Kette: der Client von Personen (Familie), die
/// Seite „Familie“ (Kinder, Leer-Zustand, Kind hinzufügen, zweiten Elternteil einladen, Übergabe mit Inline-Bestätigung), die
/// Seite je Kind (Autosave, nicht verfügbar), „Familienkonto beitreten“ und „Profil übernehmen“ (POST erst beim Klick,
/// 404/409, Rückweg, Neu anmelden), die Card „Familie“ auf der Startseite und der Knopf auf der Seite „Konto“.
/// </summary>
public sealed class FamilieSeitenTests : BunitContext
{
    private static readonly Guid Paul = Guid.Parse("0199e000-0000-7000-8000-0000000000a1");
    private static readonly Guid Emma = Guid.Parse("0199e000-0000-7000-8000-0000000000a2");
    private static readonly Guid Katrin = Guid.Parse("0199e000-0000-7000-8000-0000000000b1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public FamilieSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Post, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Katrin","nachname":"Muster"}"""));
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

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private AppSitzung Sitzung => Services.GetRequiredService<AppSitzung>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static HttpResponseMessage Feldfehler(string json) =>
        NachgebildeterHandler.Json(HttpStatusCode.BadRequest, json, "application/problem+json");

    private static HttpResponseMessage Konflikt() => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt");

    private string Koerper(string pfad) => _server.Anfragen.Last(a => a.Pfad == pfad && a.Methode != HttpMethod.Get).Koerper!;

    private static object PaulZeile(object? uebergabe = null) =>
        new { personId = Paul, version = 7u, vorname = "Paul", nachname = "Muster", geburtsdatum = "2008-03-14", volljaehrig = true, uebergabe };

    private static object EmmaZeile() =>
        new { personId = Emma, version = 8u, vorname = "Emma", nachname = "Muster", geburtsdatum = "2017-09-02", volljaehrig = false, uebergabe = (object?)null };

    private static object KatrinZeile() => new { personId = Katrin, vorname = "Katrin", nachname = "Muster" };

    private void Familie(object[] eltern, object[] kinder, params object[] einladungen) =>
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => Json(new { eltern, kinder, einladungen }));

    // ---------- Client ----------

    [Fact]
    public async Task Client_spricht_die_Endpunkte_mit_den_Vertragsformen_an()
    {
        Familie([KatrinZeile()], [PaulZeile(new { mail = "paul@example.org", gueltigBis = "2026-10-25T08:00:00+00:00" })]);
        _server.Bei(HttpMethod.Post, "/api/ich/familie/kinder", () => Json(new { personId = Emma }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Post, "/api/ich/familie/einladungen", () => Json(new { id = Katrin }, HttpStatusCode.Accepted));
        _server.Bei(HttpMethod.Post, $"/api/ich/familie/kinder/{Paul}/uebergabe", () => Json(new { id = Katrin }, HttpStatusCode.Accepted));
        _server.Bei(HttpMethod.Post, "/api/personen/familie/einladungen/annehmen", () => Json(new { }));
        _server.Bei(HttpMethod.Post, "/api/personen/uebergabe/annehmen", () => Json(new { personId = Paul }));
        var client = Services.GetRequiredService<PersonenApiClient>();

        var familie = await client.FamilieLadenAsync(Abbruch);
        var kind = await client.KindAnlegenAsync("Emma", null, new DateOnly(2017, 9, 2), Abbruch);
        var einladung = await client.ElternteilEinladenAsync("thomas@example.org", Abbruch);
        var uebergabe = await client.ProfilUebergebenAsync(Paul, "paul@example.org", Abbruch);
        var beitreten = await client.FamilienEinladungAnnehmenAsync("Tk_1", Abbruch);
        var uebernehmen = await client.ProfilUebernehmenAsync("Tk_2", Abbruch);

        var paul = Assert.Single(familie.Kinder);
        Assert.Equal((Paul, 7u, new DateOnly(2008, 3, 14), true, "paul@example.org"), (paul.PersonId, paul.Version, paul.Geburtsdatum, paul.Volljaehrig, paul.Uebergabe!.Mail));
        Assert.Equal("Katrin", Assert.Single(familie.Eltern).Vorname);
        Assert.Equal(new KindAnlegenErgebnis(Emma, null, null, null), kind);
        Assert.Equal(new ElternteilEinladenErgebnis(Katrin, null, false), einladung);
        Assert.Equal(new UebergabeErgebnis(Katrin, null, false), uebergabe);
        Assert.Equal(AnnahmeErgebnis.Angenommen, beitreten);
        Assert.Equal(AnnahmeErgebnis.Angenommen, uebernehmen);
        Assert.Equal("""{"vorname":"Emma","nachname":null,"geburtsdatum":"2017-09-02"}""", Koerper("/api/ich/familie/kinder"));
        Assert.Equal("""{"mail":"thomas@example.org"}""", Koerper("/api/ich/familie/einladungen"));
        Assert.Equal("""{"mail":"paul@example.org"}""", Koerper($"/api/ich/familie/kinder/{Paul}/uebergabe"));
        Assert.Equal("""{"token":"Tk_1"}""", Koerper("/api/personen/familie/einladungen/annehmen"));
        Assert.Equal("""{"token":"Tk_2"}""", Koerper("/api/personen/uebergabe/annehmen"));
        Assert.Equal($"/api/ich/familie/kinder/{Paul}", PersonenApiClient.KindPfad(Paul));
    }

    [Fact]
    public async Task Client_liefert_Feldfehler_409_und_404_als_Ergebnis()
    {
        _server.Bei(HttpMethod.Post, "/api/ich/familie/kinder", () => Feldfehler("""{"errors":{"vorname":["Vorname fehlt."],"nachname":["Zu lang."],"geburtsdatum":["Geburtsdatum fehlt."]}}"""));
        _server.BeiDerReihe(HttpMethod.Post, "/api/ich/familie/einladungen", () => Feldfehler("""{"errors":{"mail":["Mail-Adresse fehlt."]}}"""), Konflikt);
        _server.BeiDerReihe(HttpMethod.Post, $"/api/ich/familie/kinder/{Paul}/uebergabe", () => Feldfehler("""{"errors":{"mail":["Mail-Adresse fehlt."]}}"""), Konflikt);
        _server.BeiDerReihe(HttpMethod.Post, "/api/personen/familie/einladungen/annehmen", () => new HttpResponseMessage(HttpStatusCode.NotFound), Konflikt);
        _server.BeiDerReihe(HttpMethod.Post, "/api/personen/uebergabe/annehmen", () => new HttpResponseMessage(HttpStatusCode.NotFound), Konflikt);
        var client = Services.GetRequiredService<PersonenApiClient>();

        Assert.Equal(new KindAnlegenErgebnis(null, "Vorname fehlt.", "Zu lang.", "Geburtsdatum fehlt."), await client.KindAnlegenAsync(null, null, null, Abbruch));
        Assert.Equal(new ElternteilEinladenErgebnis(null, "Mail-Adresse fehlt.", false), await client.ElternteilEinladenAsync(null, Abbruch));
        Assert.Equal(new ElternteilEinladenErgebnis(null, null, true), await client.ElternteilEinladenAsync("x@example.org", Abbruch));
        Assert.Equal(new UebergabeErgebnis(null, "Mail-Adresse fehlt.", false), await client.ProfilUebergebenAsync(Paul, null, Abbruch));
        Assert.Equal(new UebergabeErgebnis(null, null, true), await client.ProfilUebergebenAsync(Paul, "x@example.org", Abbruch));
        Assert.Equal(AnnahmeErgebnis.NichtGueltig, await client.FamilienEinladungAnnehmenAsync("alt", Abbruch));
        Assert.Equal(AnnahmeErgebnis.Konflikt, await client.FamilienEinladungAnnehmenAsync("alt", Abbruch));
        Assert.Equal(AnnahmeErgebnis.NichtGueltig, await client.ProfilUebernehmenAsync("alt", Abbruch));
        Assert.Equal(AnnahmeErgebnis.Konflikt, await client.ProfilUebernehmenAsync("alt", Abbruch));
    }

    // ---------- Familie ----------

    [Fact]
    public void Familie_zeigt_je_Kind_eine_Card_und_die_Uebergabe_nur_beim_volljaehrigen_Kind()
    {
        Familie(
            [KatrinZeile()],
            [PaulZeile(new { mail = "paul@example.org", gueltigBis = "2026-10-25T08:00:00+00:00" }), EmmaZeile()],
            new { id = Guid.CreateVersion7(), mail = "thomas@example.org", gueltigBis = "2026-10-18T22:30:00+00:00" });

        var cut = Render<FamilieAnsicht>();

        cut.WaitForElement("table");
        Assert.Equal("Familie", cut.Find("h1").TextContent);
        Assert.Equal("./", cut.FindAll("a").Single(a => a.TextContent == "Zur Startseite").GetAttribute("href"));
        Assert.Equal(["Paul Muster", "Emma Muster", "Kind hinzufügen", "Eltern", "Zweiten Elternteil hinzufügen"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        var karten = cut.FindAll(".elkw-card");
        Assert.Contains("Geboren am 14.03.2008", karten[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Übergabe verschickt an paul@example.org, gültig bis 25.10.2026", karten[0].TextContent, StringComparison.Ordinal);
        Assert.Equal($"familie/kinder/{Paul}", karten[0].QuerySelector("a.elkw-btn--outline")!.GetAttribute("href"));
        Assert.Equal("Angaben ändern", karten[0].QuerySelector("a.elkw-btn--outline")!.TextContent);
        Assert.Equal("Mail-Adresse von Paul", karten[0].QuerySelector("label")!.TextContent);
        Assert.Equal("Profil an Paul übergeben", karten[0].QuerySelector("button")!.TextContent);
        Assert.Contains("Geboren am 02.09.2017", karten[1].TextContent, StringComparison.Ordinal);
        Assert.Null(karten[1].QuerySelector("button"));
        Assert.Null(karten[1].QuerySelector("input"));
        Assert.DoesNotContain("Übergabe verschickt", karten[1].TextContent, StringComparison.Ordinal);
        var eltern = cut.Find("table");
        Assert.Equal("Eltern", eltern.QuerySelector("caption")!.TextContent);
        Assert.Equal(["Katrin Muster", "Elternteil", "thomas@example.org", "eingeladen, gültig bis 19.10.2026"], eltern.QuerySelectorAll("tbody td").Select(t => t.TextContent.Trim()));
    }

    [Fact]
    public void Ohne_Kinder_zeigt_die_Familie_den_Leer_Zustand_und_das_Formular_mit_Pflichtfeldern()
    {
        Familie([], []);

        var cut = Render<FamilieAnsicht>();

        cut.WaitForAssertion(() => Assert.Equal("Noch keine Kinder im Familienkonto.", cut.Find(".elkw-empty p").TextContent));
        Assert.Equal(["Kind hinzufügen", "Zweiten Elternteil hinzufügen"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        Assert.Equal(["Vorname *", "Nachname", "Geburtsdatum *"], cut.FindAll("#kind-hinzufuegen label").Select(l => l.TextContent));
        Assert.Equal("date", cut.Find("#kind-geburtsdatum").GetAttribute("type"));
        Assert.Contains("* Pflichtfeld", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Kind hinzufügen", cut.Find("#kind-hinzufuegen button[type=submit]").TextContent);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Kind_hinzufuegen_meldet_Gespeichert_und_laedt_die_Liste_neu_Fehler_am_Feld()
    {
        var laden = 0;
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => ++laden == 1
            ? Json(new { eltern = Array.Empty<object>(), kinder = Array.Empty<object>(), einladungen = Array.Empty<object>() })
            : Json(new { eltern = new[] { KatrinZeile() }, kinder = new[] { EmmaZeile() }, einladungen = Array.Empty<object>() }));
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/ich/familie/kinder",
            () => Feldfehler("""{"errors":{"geburtsdatum":["Geburtsdatum fehlt."]}}"""),
            () => Json(new { personId = Emma }, HttpStatusCode.Created));
        var cut = Render<FamilieAnsicht>();
        cut.WaitForElement("#kind-vorname");

        cut.Find("#kind-vorname").Input("Emma");
        cut.Find("#kind-hinzufuegen").Submit();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Geburtsdatum fehlt.", cut.Find(".elkw-field-error").TextContent));
        Assert.Equal("""{"vorname":"Emma","nachname":null,"geburtsdatum":null}""", Koerper("/api/ich/familie/kinder"));

        cut.Find("#kind-nachname").Input("Muster");
        cut.Find("#kind-geburtsdatum").Input("2017-09-02");
        cut.Find("#kind-hinzufuegen").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Das Kind ist hinzugefügt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Gespeichert:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Equal("Emma Muster", cut.FindAll(".elkw-card__title")[0].TextContent);
        Assert.Empty(cut.FindAll(".elkw-field-error"));
        Assert.Equal(2, laden);
        Assert.Equal("""{"vorname":"Emma","nachname":"Muster","geburtsdatum":"2017-09-02"}""", Koerper("/api/ich/familie/kinder"));
    }

    [Fact]
    public void Zweiten_Elternteil_einladen_meldet_Gespeichert_und_409_als_Fehler()
    {
        Familie([KatrinZeile()], []);
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/ich/familie/einladungen",
            () => Json(new { id = Guid.CreateVersion7() }, HttpStatusCode.Accepted),
            Konflikt);
        var cut = Render<FamilieAnsicht>();
        cut.WaitForElement("#elternteil-mail");

        Assert.Equal("Mail-Adresse", cut.Find("label[for=elternteil-mail]").TextContent);
        cut.Find("#elternteil-mail").Input("thomas@example.org");
        cut.Find("#elternteil-einladen").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Die Einladung ist verschickt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("""{"mail":"thomas@example.org"}""", Koerper("/api/ich/familie/einladungen"));

        cut.Find("#elternteil-mail").Input("dritte@example.org");
        cut.Find("#elternteil-einladen").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Das Familienkonto hat schon zwei Eltern.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal("Fehler:", cut.Find(".elkw-alert--fehler .elkw-alert__title").TextContent);
    }

    [Fact]
    public void Mit_zwei_Eltern_gibt_es_kein_Formular_fuer_einen_weiteren_Elternteil()
    {
        Familie([KatrinZeile(), new { personId = Guid.CreateVersion7(), vorname = "Thomas", nachname = "Muster" }], [EmmaZeile()]);

        var cut = Render<FamilieAnsicht>();

        cut.WaitForElement("table");
        Assert.DoesNotContain("Zweiten Elternteil hinzufügen", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(["Katrin Muster", "Elternteil", "Thomas Muster", "Elternteil"], cut.Find("table").QuerySelectorAll("tbody td").Select(t => t.TextContent.Trim()));
    }

    [Fact]
    public void Uebergabe_klappt_die_Inline_Bestaetigung_auf_und_verschickt_erst_nach_Ja()
    {
        Familie([KatrinZeile()], [PaulZeile()]);
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"/api/ich/familie/kinder/{Paul}/uebergabe",
            () => Feldfehler("""{"errors":{"mail":["Das ist keine gültige Mail-Adresse."]}}"""),
            () => Json(new { id = Guid.CreateVersion7() }, HttpStatusCode.Accepted),
            Konflikt);
        var cut = Render<FamilieAnsicht>();
        cut.WaitForElement($"#uebergabe-mail-{Paul}");

        cut.Find($"#uebergabe-mail-{Paul}").Input("kaputt");
        cut.FindAll("button").Single(b => b.TextContent == "Profil an Paul übergeben").Click();
        Assert.Contains("Profil an Paul übergeben? Danach hat das Familienkonto keinen Zugriff mehr.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.EndsWith("/uebergabe", StringComparison.Ordinal));
        cut.FindAll("button").Single(b => b.TextContent == "Ja, übergeben").Click();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Das ist keine gültige Mail-Adresse.", cut.Find(".elkw-field-error").TextContent));
        Assert.Equal("""{"mail":"kaputt"}""", Koerper($"/api/ich/familie/kinder/{Paul}/uebergabe"));

        cut.Find($"#uebergabe-mail-{Paul}").Input("paul@example.org");
        cut.FindAll("button").Single(b => b.TextContent == "Profil an Paul übergeben").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, übergeben").Click();
        cut.WaitForAssertion(() => Assert.Contains("Die Übergabe ist verschickt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Gespeichert:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Empty(cut.FindAll(".elkw-field-error"));

        cut.Find($"#uebergabe-mail-{Paul}").Input("paul@example.org");
        cut.FindAll("button").Single(b => b.TextContent == "Profil an Paul übergeben").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, übergeben").Click();
        cut.WaitForAssertion(() => Assert.Contains("Paul ist noch nicht volljährig.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Zweites_Ja_waehrend_der_Uebertragung_verschickt_keine_zweite_Uebergabe()
    {
        Familie([KatrinZeile()], [PaulZeile()]);
        var antwort = new TaskCompletionSource<HttpResponseMessage>();
        _server.Bei(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == $"/api/ich/familie/kinder/{Paul}/uebergabe", _ => antwort.Task);
        var cut = Render<FamilieAnsicht>();
        cut.WaitForElement($"#uebergabe-mail-{Paul}");
        cut.Find($"#uebergabe-mail-{Paul}").Input("paul@example.org");

        for (var i = 0; i < 2; i++)
        {
            cut.FindAll("button").Single(b => b.TextContent == "Profil an Paul übergeben").Click();
            cut.FindAll("button").Single(b => b.TextContent == "Ja, übergeben").Click();
        }

        antwort.SetResult(Json(new { id = Guid.CreateVersion7() }, HttpStatusCode.Accepted));
        cut.WaitForAssertion(() => Assert.Contains("Die Übergabe ist verschickt.", cut.Markup, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Pfad == $"/api/ich/familie/kinder/{Paul}/uebergabe");
    }

    [Fact]
    public void Uebergabe_ohne_frische_Anmeldung_fuehrt_zur_Anmeldung_mit_Rueckweg_zur_Familie()
    {
        Familie([KatrinZeile()], [PaulZeile()]);
        _server.Bei(HttpMethod.Post, $"/api/ich/familie/kinder/{Paul}/uebergabe", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        Navigation.NavigateTo("familie");
        var cut = Render<FamilieAnsicht>();
        cut.WaitForElement($"#uebergabe-mail-{Paul}");

        cut.Find($"#uebergabe-mail-{Paul}").Input("paul@example.org");
        cut.FindAll("button").Single(b => b.TextContent == "Profil an Paul übergeben").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, übergeben").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.Equal("familie", Sitzung.Rueckweg);
    }

    [Fact]
    public void Familie_laden_scheitert_mit_der_Meldung_statt_Wird_geladen()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = Render<FamilieAnsicht>();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-loading"));
    }

    // ---------- Kind-Seite ----------

    [Fact]
    public void Kind_Seite_zeigt_Name_und_drei_Autosave_Felder_am_PATCH_Pfad()
    {
        Familie([KatrinZeile()], [PaulZeile(), EmmaZeile()]);
        _server.Bei(HttpMethod.Patch, $"/api/ich/familie/kinder/{Emma}", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":9,"vorname":"Emilia"}"""));

        var cut = Render<KindAngaben>(ps => ps.Add(p => p.PersonId, Emma));

        cut.WaitForAssertion(() => Assert.Equal("Emma Muster", cut.Find("h1").TextContent));
        Assert.Equal(["Vorname", "Nachname", "Geburtsdatum"], cut.FindAll("label").Select(l => l.TextContent));
        Assert.Equal(["Emma", "Muster", "2017-09-02"], cut.FindAll("input").Select(i => i.GetAttribute("value")));
        Assert.Equal("date", cut.FindAll("input")[2].GetAttribute("type"));
        Assert.Equal("familie", cut.FindAll("a").Single(a => a.TextContent == "Zurück zur Familie").GetAttribute("href"));

        var vorname = cut.FindAll("input")[0];
        vorname.Focus();
        vorname.Input("Emilia");
        vorname.Blur();

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("""{"version":8,"alterWert":"Emma","vorname":"Emilia"}""", Koerper($"/api/ich/familie/kinder/{Emma}"));
    }

    [Fact]
    public void Kind_Seite_fuer_ein_Kind_ausserhalb_der_Familie_ist_nicht_verfuegbar()
    {
        Familie([KatrinZeile()], [EmmaZeile()]);

        var cut = Render<KindAngaben>(ps => ps.Add(p => p.PersonId, Guid.CreateVersion7()));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll("input"));
    }

    // ---------- Familienkonto beitreten ----------

    [Fact]
    public void Beitreten_nimmt_erst_beim_Klick_an_und_fuehrt_zur_Familie()
    {
        _server.Bei(HttpMethod.Post, "/api/personen/familie/einladungen/annehmen", () => Json(new { }));
        var cut = Render<FamilienEinladungOeffnen>(ps => ps.Add(p => p.Token, "Tk_1"));

        Assert.Equal("Familienkonto beitreten", cut.Find("h1").TextContent);
        Assert.Equal("Einladung annehmen", cut.Find("button").TextContent);
        Assert.NotEmpty(cut.Find("p").TextContent);
        Assert.Empty(_server.Anfragen);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/familie", Navigation.Uri));
        Assert.Equal("""{"token":"Tk_1"}""", Koerper("/api/personen/familie/einladungen/annehmen"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("Tk_1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Diese Einladung ist nicht mehr gültig oder gilt für eine andere Mail-Adresse.")]
    [InlineData(HttpStatusCode.Conflict, "Dieses Konto gehört schon zu einer anderen Familie, oder das Familienkonto hat schon zwei Eltern.")]
    public void Beitreten_zeigt_404_und_409_als_Fehler(HttpStatusCode status, string satz)
    {
        _server.Bei(HttpMethod.Post, "/api/personen/familie/einladungen/annehmen", () => status == HttpStatusCode.Conflict ? Konflikt() : new HttpResponseMessage(status));
        var cut = Render<FamilienEinladungOeffnen>(ps => ps.Add(p => p.Token, "alt"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(satz, cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal("Fehler:", cut.Find(".elkw-alert--fehler .elkw-alert__title").TextContent);
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    [Fact]
    public void Ohne_Sitzung_fuehrt_Beitreten_zur_Anmeldung_mit_Rueckweg()
    {
        _server.Bei(HttpMethod.Post, "/api/personen/familie/einladungen/annehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        Navigation.NavigateTo("familie/einladung/Tk_1");
        var cut = Render<FamilienEinladungOeffnen>(ps => ps.Add(p => p.Token, "Tk_1"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.Equal("familie/einladung/Tk_1", Sitzung.Rueckweg);
    }

    // ---------- Profil übernehmen ----------

    [Fact]
    public void Uebernehmen_nimmt_erst_beim_Klick_an_und_bietet_Neu_anmelden()
    {
        _server.Bei(HttpMethod.Post, "/api/personen/uebergabe/annehmen", () => Json(new { personId = Paul }));
        _server.Bei(HttpMethod.Post, "/api/konten/abmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<UebergabeOeffnen>(ps => ps.Add(p => p.Token, "Tk_2"));

        Assert.Equal("Profil übernehmen", cut.Find("h1").TextContent);
        Assert.Equal("Profil übernehmen", cut.Find("button").TextContent);
        Assert.NotEmpty(cut.Find("p").TextContent);
        Assert.Empty(_server.Anfragen);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("Das Profil ist übernommen. Zum Abschluss neu anmelden.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Gespeichert:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Equal("""{"token":"Tk_2"}""", Koerper("/api/personen/uebergabe/annehmen"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Profil übernehmen");

        cut.FindAll("button").Single(b => b.TextContent == "Neu anmelden").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.Contains(_server.Anfragen, a => a.Pfad == "/api/konten/abmelden");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Dieser Link ist nicht mehr gültig. Die Eltern können die Übergabe erneut auslösen.")]
    [InlineData(HttpStatusCode.Conflict, "Dieses Konto hat schon ein eigenes Profil. Zum Übernehmen mit einer Mail-Adresse ohne eigenes Profil anmelden.")]
    public void Uebernehmen_zeigt_404_und_409(HttpStatusCode status, string satz)
    {
        _server.Bei(HttpMethod.Post, "/api/personen/uebergabe/annehmen", () => status == HttpStatusCode.Conflict ? Konflikt() : new HttpResponseMessage(status));
        var cut = Render<UebergabeOeffnen>(ps => ps.Add(p => p.Token, "alt"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(satz, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-alert--erfolg"));
    }

    [Fact]
    public void Ohne_Sitzung_fuehrt_Uebernehmen_zur_Anmeldung_mit_Rueckweg()
    {
        _server.Bei(HttpMethod.Post, "/api/personen/uebergabe/annehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        Navigation.NavigateTo("uebergabe/Tk_2");
        var cut = Render<UebergabeOeffnen>(ps => ps.Add(p => p.Token, "Tk_2"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.Equal("uebergabe/Tk_2", Sitzung.Rueckweg);
    }

    // ---------- Startseite und Konto ----------

    private void Start(object[] freizeiten)
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"katrin@example.org","hatPasswort":false,"erlaubt":[]}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(freizeiten));
    }

    [Fact]
    public void Startseite_zeigt_Eltern_die_Card_Familie_vor_den_Freizeiten()
    {
        Start([new { freizeitId = Guid.CreateVersion7(), name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", rollen = new[] { "Leitung" } }]);
        Familie([KatrinZeile()], [PaulZeile(), EmmaZeile()]);

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Equal(["Familie", "Zeltlager Sommer 2027"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent)));
        var karte = cut.FindAll(".elkw-card")[0];
        Assert.Contains("Paul Muster, Emma Muster", karte.TextContent, StringComparison.Ordinal);
        Assert.Equal("Familie öffnen", karte.QuerySelector("a[href='familie']")!.TextContent);
    }

    [Fact]
    public void Startseite_zeigt_Eltern_ohne_Kinder_den_Satz_und_anderen_keine_Familie()
    {
        Start([]);
        Familie([KatrinZeile()], []);

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Equal(["Familie"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent)));
        Assert.Contains("Noch keine Kinder im Familienkonto.", cut.Find(".elkw-card").TextContent, StringComparison.Ordinal);

        Familie([], []);
        var ohne = Render<Start>();
        ohne.WaitForAssertion(() => Assert.Contains("Noch keine Freizeit geöffnet.", ohne.Markup, StringComparison.Ordinal));
        Assert.Empty(ohne.FindAll(".elkw-card"));
    }

    [Fact]
    public void Konto_hat_den_Knopf_Familienkonto_oeffnen()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"katrin@example.org","hatPasswort":false,"erlaubt":[]}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Katrin","nachname":"Muster"}"""));

        var cut = Render<Konto>();

        cut.WaitForElement("a[href='familie']");
        var knopf = cut.Find("a[href='familie']");
        Assert.Equal("Familienkonto öffnen", knopf.TextContent);
        Assert.Contains("elkw-btn--outline", knopf.GetAttribute("class"), StringComparison.Ordinal);
    }
}
