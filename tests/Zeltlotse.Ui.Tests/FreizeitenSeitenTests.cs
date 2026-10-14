using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Pages;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 1-3 in der Web-App mit nachgebildetem Server und der echten Handler-Kette: der Client von Freizeiten, die Freizeiten
/// auf der Organisationsseite, „Freizeit verwalten“, „Leitung übernehmen“ (mit Rückweg), die Startseite mit Freizeiten, die
/// Freizeit-Startseite und die Texte für Phase und Zeitraum.
/// </summary>
public sealed class FreizeitenSeitenTests : BunitContext
{
    private static readonly Guid OrgId = Guid.Parse("0199a000-0000-7000-8000-0000000000a1");
    private static readonly Guid FreizeitId = Guid.Parse("0199a000-0000-7000-8000-0000000000f1");
    private static readonly Guid PersonId = Guid.Parse("0199a000-0000-7000-8000-0000000000b1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public FreizeitenSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Post, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Jonas","nachname":"Hüttner"}"""));
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

    private string Koerper(string pfad) => _server.Anfragen.Last(a => a.Pfad == pfad && a.Methode != HttpMethod.Get).Koerper!;

    // ---------- Texte ----------

    [Fact]
    public void Texte_fuer_Phase_Zeitraum_Tag_und_Freizeit_Rolle()
    {
        Assert.Equal(["in Vorbereitung", "läuft", "Nachbereitung", "gelaufen"], Enum.GetValues<Phase>().Select(Anzeige.Phase));
        Assert.Equal([StatusArt.Offen, StatusArt.Aktiv, StatusArt.Warnung, StatusArt.Erfolg], Enum.GetValues<Phase>().Select(Anzeige.PhaseArt));
        Assert.Equal("02.08.2027 – 13.08.2027", Anzeige.Zeitraum(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13)));
        Assert.Equal("12.09.2027", Anzeige.Datum(new DateOnly(2027, 9, 12)));
        Assert.Equal("Leitung, Team", Anzeige.Rollen([FreizeitRolle.Team, FreizeitRolle.Leitung]));
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_spricht_die_Endpunkte_mit_den_Vertragsformen_an_und_403_404_heissen_nicht_verfuegbar()
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten", () => Json(new
        {
            freizeiten = new[] { new { id = FreizeitId, name = "Zeltlager", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung" } },
            erlaubt = new[] { Aktionsschluessel.FreizeitenAnlegen },
        }));
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/freizeiten", () => Json(new { id = FreizeitId }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}", () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}/leitung/einladungen", () => Json(new { id = PersonId }, HttpStatusCode.Accepted));
        _server.Bei(HttpMethod.Post, "/api/freizeiten/leitung/annehmen", () => Json(new { freizeitId = FreizeitId }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(new[]
        {
            new { freizeitId = FreizeitId, name = "Zeltlager", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "Laeuft", rollen = new[] { "Leitung" } },
        }));
        var client = Services.GetRequiredService<FreizeitenApiClient>();

        var liste = await client.ListeLadenAsync(OrgId, Abbruch);
        var angelegt = await client.AnlegenAsync(OrgId, "Zeltlager", new DateOnly(2027, 8, 2), null, Abbruch);
        var verwaltung = await client.VerwaltungLadenAsync(OrgId, FreizeitId, Abbruch);
        var eingeladen = await client.LeitungEinladenAsync(OrgId, FreizeitId, "jonas@example.org", Abbruch);
        var angenommen = await client.LeitungAnnehmenAsync("Tk_1", Abbruch);
        var freizeit = await client.LadenAsync(FreizeitId, Abbruch);
        var meine = await client.MeineLadenAsync(Abbruch);

        var zeile = Assert.Single(liste!.Freizeiten);
        Assert.Equal((FreizeitId, "Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13), Phase.InVorbereitung), (zeile.Id, zeile.Name, zeile.ErsterTag, zeile.LetzterTag, zeile.Phase));
        Assert.Equal([Aktionsschluessel.FreizeitenAnlegen], liste.Erlaubt);
        Assert.Equal(new FreizeitAnlegenErgebnis(FreizeitId, null, null, null), angelegt);
        Assert.Null(verwaltung);
        Assert.Equal(new LeitungEinladenErgebnis(PersonId, null), eingeladen);
        Assert.Equal(FreizeitId, angenommen);
        Assert.Null(freizeit);
        var eigene = Assert.Single(meine);
        Assert.Equal((Phase.Laeuft, FreizeitRolle.Leitung), (eigene.Phase, Assert.Single(eigene.Rollen)));
        Assert.Equal("""{"name":"Zeltlager","ersterTag":"2027-08-02","letzterTag":null}""", Koerper($"/api/organisationen/{OrgId}/freizeiten"));
        Assert.Equal("""{"mail":"jonas@example.org"}""", Koerper($"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}/leitung/einladungen"));
        Assert.Equal("""{"token":"Tk_1"}""", Koerper("/api/freizeiten/leitung/annehmen"));
    }

    [Fact]
    public async Task Client_liefert_Feldfehler_und_404_beim_Annehmen_als_null()
    {
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/freizeiten", () => Feldfehler("""{"errors":{"name":["Name fehlt."],"ersterTag":["Erster Tag fehlt."],"letzterTag":["Letzter Tag fehlt."]}}"""));
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}/leitung/einladungen", () => Feldfehler("""{"errors":{"mail":["Mail-Adresse fehlt."]}}"""));
        _server.Bei(HttpMethod.Post, "/api/freizeiten/leitung/annehmen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Services.GetRequiredService<FreizeitenApiClient>();

        Assert.Equal(new FreizeitAnlegenErgebnis(null, "Name fehlt.", "Erster Tag fehlt.", "Letzter Tag fehlt."), await client.AnlegenAsync(OrgId, null, null, null, Abbruch));
        Assert.Equal(new LeitungEinladenErgebnis(null, "Mail-Adresse fehlt."), await client.LeitungEinladenAsync(OrgId, FreizeitId, null, Abbruch));
        Assert.Null(await client.LeitungAnnehmenAsync("alt", Abbruch));
    }

    // ---------- Organisationsseite ----------

    private void Organisation(string rolle, params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { rolle }, erlaubt }));

    private void Freizeiten(string[] erlaubt, params object[] freizeiten) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten", () => Json(new { freizeiten, erlaubt }));

    private static object Zeltlager(string phase = "InVorbereitung") =>
        new { id = FreizeitId, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase };

    [Fact]
    public void Org_Admin_sieht_die_Freizeiten_mit_Link_auf_die_Verwaltung_und_das_Formular_Freizeit_anlegen()
    {
        Organisation("OrgAdmin", Aktionsschluessel.OrgRollenVergeben);
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/rollen", () => Json(new { personen = Array.Empty<object>(), einladungen = Array.Empty<object>() }));
        Freizeiten([Aktionsschluessel.FreizeitenAnlegen, Aktionsschluessel.LeitungEinsetzen], Zeltlager());

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForElement("table");
        Assert.Equal(["Freizeiten", "Freizeit anlegen", "Rollen", "Offene Einladungen", "Person einladen"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        var tabelle = cut.Find("table");
        Assert.Equal("Freizeiten", tabelle.QuerySelector("caption")!.TextContent);
        Assert.Equal(["Name", "Zeitraum", "Phase"], tabelle.QuerySelectorAll("thead th").Select(t => t.TextContent));
        Assert.Equal(["Zeltlager Sommer 2027", "02.08.2027 – 13.08.2027", "in Vorbereitung"], tabelle.QuerySelectorAll("tbody td").Select(t => t.TextContent.Trim()));
        Assert.Equal($"organisationen/{OrgId}/freizeiten/{FreizeitId}", tabelle.QuerySelector("tbody a")!.GetAttribute("href"));
        Assert.NotNull(tabelle.QuerySelector(".elkw-status.elkw-status--offen"));
        Assert.Equal("Name der Freizeit", cut.Find("label[for=freizeit-name]").TextContent);
        Assert.Equal("Erster Tag", cut.Find("label[for=freizeit-erster-tag]").TextContent);
        Assert.Equal("Letzter Tag", cut.Find("label[for=freizeit-letzter-tag]").TextContent);
        Assert.Equal(["date", "date"], new[] { "#freizeit-erster-tag", "#freizeit-letzter-tag" }.Select(f => cut.Find(f).GetAttribute("type")));
        Assert.Equal("Freizeit anlegen", cut.Find("#freizeit-anlegen button[type=submit]").TextContent);
    }

    [Fact]
    public void Org_Ueberblick_sieht_die_Freizeiten_ohne_Link_und_ohne_Formular()
    {
        Organisation("OrgUeberblick");
        Freizeiten([], Zeltlager("Laeuft"));

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForElement("table");
        Assert.Equal(["Freizeiten"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        Assert.Empty(cut.FindAll("table a"));
        Assert.Empty(cut.FindAll("form"));
        Assert.Equal("läuft", cut.Find("tbody .elkw-status").TextContent);
    }

    [Fact]
    public void Ohne_Freizeit_zeigt_die_Card_den_Leer_Zustand()
    {
        Organisation("OrgUeberblick");
        Freizeiten([]);

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Equal("Noch keine Freizeit angelegt.", cut.Find(".elkw-empty p").TextContent));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Freizeit_anlegen_meldet_Gespeichert_leert_das_Formular_und_laedt_die_Liste_neu()
    {
        Organisation("OrgAdmin");
        var listen = 0;
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten", () => ++listen == 1
            ? Json(new { freizeiten = Array.Empty<object>(), erlaubt = new[] { Aktionsschluessel.FreizeitenAnlegen, Aktionsschluessel.LeitungEinsetzen } })
            : Json(new { freizeiten = new[] { Zeltlager() }, erlaubt = new[] { Aktionsschluessel.FreizeitenAnlegen, Aktionsschluessel.LeitungEinsetzen } }));
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/freizeiten", () => Json(new { id = FreizeitId }, HttpStatusCode.Created));
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForElement("#freizeit-name");

        cut.Find("#freizeit-name").Input("Zeltlager Sommer 2027");
        cut.Find("#freizeit-erster-tag").Input("2027-08-02");
        cut.Find("#freizeit-letzter-tag").Input("2027-08-13");
        cut.Find("#freizeit-anlegen").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Die Freizeit ist angelegt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Gespeichert:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Equal("Zeltlager Sommer 2027", cut.Find("tbody td").TextContent.Trim());
        Assert.Null(cut.Find("#freizeit-name").GetAttribute("value"));
        Assert.Equal(2, listen);
        Assert.Equal("""{"name":"Zeltlager Sommer 2027","ersterTag":"2027-08-02","letzterTag":"2027-08-13"}""", Koerper($"/api/organisationen/{OrgId}/freizeiten"));
    }

    [Fact]
    public void Freizeit_anlegen_zeigt_die_Fehler_an_den_Feldern()
    {
        Organisation("OrgAdmin");
        Freizeiten([Aktionsschluessel.FreizeitenAnlegen]);
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/freizeiten", () => Feldfehler("""{"errors":{"name":["Name fehlt."],"ersterTag":["Erster Tag fehlt."],"letzterTag":["Letzter Tag fehlt."]}}"""));
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForElement("#freizeit-name");

        cut.Find("#freizeit-erster-tag").Input("kein Datum");
        cut.Find("#freizeit-anlegen").Submit();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".elkw-field-error").Count));
        Assert.Equal(["Fehler: Name fehlt.", "Fehler: Erster Tag fehlt.", "Fehler: Letzter Tag fehlt."], cut.FindAll(".elkw-field-error").Select(f => f.TextContent));
        Assert.Equal("""{"name":null,"ersterTag":null,"letzterTag":null}""", Koerper($"/api/organisationen/{OrgId}/freizeiten"));
    }

    // ---------- Freizeit verwalten ----------

    private void Verwaltung(object[] leitung, params object[] einladungen) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}", () => Json(new
        {
            id = FreizeitId,
            name = "Zeltlager Sommer 2027",
            ersterTag = "2027-08-02",
            letzterTag = "2027-08-13",
            phase = "InVorbereitung",
            leitung,
            einladungen,
        }));

    [Fact]
    public void Verwaltung_zeigt_Name_Zeitraum_Phase_Leitung_Einladungen_Formular_und_den_Weg_zur_Organisation()
    {
        Verwaltung(
            [new { personId = PersonId, vorname = "Jonas", nachname = "Hüttner" }, new { personId = Guid.CreateVersion7(), vorname = (string?)null, nachname = (string?)null }],
            new { id = Guid.CreateVersion7(), mail = "miriam.zeltner@example.org", gueltigBis = new DateTimeOffset(2026, 10, 18, 22, 30, 0, TimeSpan.Zero) });

        var cut = Render<FreizeitVerwalten>(ps => ps.Add(p => p.OrgId, OrgId).Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForElement("table");
        Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent);
        Assert.Contains("Zeitraum: 02.08.2027 – 13.08.2027", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("in Vorbereitung", cut.Find(".elkw-status").TextContent);
        Assert.Equal(["Leitung", "Offene Einladungen", "Leitung einladen"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        var tabellen = cut.FindAll("table");
        Assert.Equal(["Jonas Hüttner", "Name fehlt"], tabellen[0].QuerySelectorAll("tbody td").Select(t => t.TextContent));
        Assert.Equal(["miriam.zeltner@example.org", "gültig bis 19.10.2026"], tabellen[1].QuerySelectorAll("tbody td").Select(t => t.TextContent));
        Assert.Equal("Mail-Adresse", cut.Find("label[for=leitung-mail]").TextContent);
        Assert.Equal("email", cut.Find("#leitung-mail").GetAttribute("type"));
        Assert.Equal("Einladung schicken", cut.Find("form button[type=submit]").TextContent);
        Assert.Equal($"organisationen/{OrgId}", cut.FindAll("a").Single(a => a.TextContent == "Zur Organisation").GetAttribute("href"));
    }

    [Fact]
    public void Verwaltung_ohne_Leitung_und_Einladungen_zeigt_die_Leer_Zustaende()
    {
        Verwaltung([]);

        var cut = Render<FreizeitVerwalten>(ps => ps.Add(p => p.OrgId, OrgId).Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".elkw-empty").Count));
        Assert.Equal(["Noch keine Leitung eingesetzt.", "Keine offenen Einladungen."], cut.FindAll(".elkw-empty p").Select(p => p.TextContent));
    }

    [Fact]
    public void Leitung_einladen_meldet_Gespeichert_und_laedt_neu_Fehler_am_Feld()
    {
        Verwaltung([]);
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}/leitung/einladungen",
            () => Feldfehler("""{"errors":{"mail":["Das ist keine gültige Mail-Adresse."]}}"""),
            () => Json(new { id = Guid.CreateVersion7() }, HttpStatusCode.Accepted));
        var cut = Render<FreizeitVerwalten>(ps => ps.Add(p => p.OrgId, OrgId).Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForElement("form");

        cut.Find("#leitung-mail").Input("kaputt");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Das ist keine gültige Mail-Adresse.", cut.Find(".elkw-field-error").TextContent));

        cut.Find("#leitung-mail").Input("jonas.huettner@example.org");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Die Einladung ist verschickt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Gespeichert:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Empty(cut.FindAll(".elkw-field-error"));
        Assert.Equal("""{"mail":"jonas.huettner@example.org"}""", Koerper($"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}/leitung/einladungen"));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Pfad == $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}" && a.Methode == HttpMethod.Get));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void Verwaltung_ohne_Recht_ist_nicht_verfuegbar(HttpStatusCode status)
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/freizeiten/{FreizeitId}", () => new HttpResponseMessage(status));

        var cut = Render<FreizeitVerwalten>(ps => ps.Add(p => p.OrgId, OrgId).Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll("form"));
    }

    // ---------- Leitung übernehmen ----------

    [Fact]
    public void Leitung_uebernehmen_nimmt_erst_beim_Klick_an_und_fuehrt_zur_Freizeit()
    {
        _server.Bei(HttpMethod.Post, "/api/freizeiten/leitung/annehmen", () => Json(new { freizeitId = FreizeitId }));
        var cut = Render<LeitungOeffnen>(ps => ps.Add(p => p.Token, "Tk_1"));

        Assert.Equal("Leitung übernehmen", cut.Find("h1").TextContent);
        Assert.Equal("Einladung annehmen", cut.Find("button").TextContent);
        Assert.NotEmpty(cut.Find("p").TextContent);
        Assert.Empty(_server.Anfragen);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost/freizeiten/{FreizeitId}", Navigation.Uri));
        Assert.Equal("""{"token":"Tk_1"}""", Koerper("/api/freizeiten/leitung/annehmen"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("Tk_1", StringComparison.Ordinal));
    }

    [Fact]
    public void Ungueltige_Leitungs_Einladung_zeigt_den_Satz_und_ein_Serverfehler_die_Aktionsmeldung()
    {
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/freizeiten/leitung/annehmen",
            () => new HttpResponseMessage(HttpStatusCode.NotFound),
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var cut = Render<LeitungOeffnen>(ps => ps.Add(p => p.Token, "alt"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            "Diese Einladung ist nicht mehr gültig oder gilt für eine andere Mail-Adresse.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal("Fehler:", cut.Find(".elkw-alert--fehler .elkw-alert__title").TextContent);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    [Fact]
    public void Ohne_Sitzung_fuehrt_Leitung_uebernehmen_zur_Anmeldung_und_nach_dem_Code_zurueck()
    {
        _server.Bei(HttpMethod.Post, "/api/freizeiten/leitung/annehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => Json(new { anfrageId = Guid.CreateVersion7() }, HttpStatusCode.Accepted));
        _server.Bei(HttpMethod.Post, "/api/konten/login-code/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Navigation.NavigateTo("leitung/Tk_1");
        var seite = Render<LeitungOeffnen>(ps => ps.Add(p => p.Token, "Tk_1"));

        seite.Find("button").Click();

        seite.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.Equal("leitung/Tk_1", Sitzung.Rueckweg);

        var anmelden = Render<Anmelden>();
        anmelden.Find("#anmelden-mail").Input("jonas.huettner@example.org");
        anmelden.Find("form").Submit();
        anmelden.WaitForElement("#anmelden-code").Input("123456");
        anmelden.FindAll("form")[1].Submit();

        anmelden.WaitForAssertion(() => Assert.Equal("http://localhost/leitung/Tk_1", Navigation.Uri));
    }

    // ---------- Startseite ----------

    private void Start(object[] freizeiten, params object[] organisationen)
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"j@example.org","hatPasswort":false,"erlaubt":[]}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen", () => Json(organisationen));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(freizeiten));
    }

    [Fact]
    public void Startseite_zeigt_je_eigener_Freizeit_eine_Card_vor_den_Organisationen()
    {
        Start(
            [new { freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", rollen = new[] { "Leitung" } }],
            new { organisationId = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgUeberblick" } });

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".elkw-card").Count));
        Assert.Equal(["Zeltlager Sommer 2027", "Jugendwerk Musterstadt"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        var card = cut.FindAll(".elkw-card")[0];
        Assert.Contains("Zeitraum: 02.08.2027 – 13.08.2027", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("Rolle: Leitung", card.TextContent, StringComparison.Ordinal);
        Assert.Equal("in Vorbereitung", card.QuerySelector(".elkw-status")!.TextContent);
        Assert.Equal("Freizeit öffnen", card.QuerySelector($"a[href='freizeiten/{FreizeitId}']")!.TextContent);
        Assert.DoesNotContain("Noch keine Freizeit geöffnet.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Startseite_ohne_Freizeit_bleibt_bei_Noch_keine_Freizeit_geoeffnet()
    {
        Start([]);

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Freizeit geöffnet.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-card"));
    }

    // ---------- Freizeit-Startseite ----------

    private void FreizeitStartAntworten(HttpStatusCode? kontextStatus = null, HttpStatusCode? freizeitStatus = null)
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => kontextStatus is { } status
            ? new HttpResponseMessage(status)
            : Json(new { freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", phase = "InVorbereitung", rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(), erlaubt = Array.Empty<string>() }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}", () => freizeitStatus is { } status
            ? new HttpResponseMessage(status)
            : Json(new
            {
                id = FreizeitId,
                name = "Zeltlager Sommer 2027",
                ersterTag = "2027-08-02",
                letzterTag = "2027-08-13",
                phase = "InVorbereitung",
                freigabeBis = "2027-09-12",
                leitung = new[] { new { vorname = "Jonas", nachname = "Hüttner" }, new { vorname = "Miriam", nachname = "Zeltner" } },
            }));
    }

    [Fact]
    public void Freizeit_Startseite_zeigt_Name_Phase_Zeitraum_Rolle_Leitung_und_Freigabefenster()
    {
        FreizeitStartAntworten();

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", cut.Find("h1").TextContent));
        Assert.Equal("in Vorbereitung", cut.Find(".elkw-status").TextContent);
        var texte = cut.FindAll("p").Select(p => p.TextContent.Trim()).ToList();
        Assert.Contains("Zeitraum: 02.08.2027 – 13.08.2027", texte);
        Assert.Contains("Rolle: Leitung", texte);
        Assert.Contains("Leitung: Jonas Hüttner, Miriam Zeltner", texte);
        Assert.Contains("Einsicht in Freizeitpässe bis 12.09.2027", texte);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(null, HttpStatusCode.NotFound)]
    public void Freizeit_Startseite_ohne_Recht_ist_nicht_verfuegbar(HttpStatusCode? kontext, HttpStatusCode? freizeit)
    {
        FreizeitStartAntworten(kontext, freizeit);

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
    }

    [Fact]
    public void Freizeit_Startseite_laden_scheitert_mit_der_Meldung_statt_Wird_geladen()
    {
        FreizeitStartAntworten(HttpStatusCode.InternalServerError);

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-loading"));
    }

    // ---------- Story 2-1: Einstellungen, Freizeitseite ----------

    [Fact]
    public void Texte_fuer_Zeitraum_in_Worten_Team_Zeile_Monate_Tage_und_Aufzaehlung()
    {
        Assert.Equal("2. bis 13. August 2027", Anzeige.ZeitraumLang(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13)));
        Assert.Equal("30. Juli bis 2. August 2027", Anzeige.ZeitraumLang(new DateOnly(2027, 7, 30), new DateOnly(2027, 8, 2)));
        Assert.Equal("30. Dezember 2027 bis 2. Januar 2028", Anzeige.ZeitraumLang(new DateOnly(2027, 12, 30), new DateOnly(2028, 1, 2)));
        Assert.Equal("2. August 2027", Anzeige.ZeitraumLang(new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 2)));
        Assert.Equal("Leitung · aus Musterstadt", Anzeige.TeamZeile(FreizeitRolle.Leitung, " Musterstadt "));
        Assert.Equal("Team", Anzeige.TeamZeile(FreizeitRolle.Team, null));
        Assert.Equal(("1 Monat", "12 Monate", "1 Tag", "30 Tage"), (Anzeige.Monate(1), Anzeige.Monate(12), Anzeige.Tage(1), Anzeige.Tage(30)));
        Assert.Equal(("", "A", "A und B", "A, B und C"), (Anzeige.Aufzaehlung([]), Anzeige.Aufzaehlung(["A"]), Anzeige.Aufzaehlung(["A", "B"]), Anzeige.Aufzaehlung(["A", "B", "C"])));
    }

    [Fact]
    public async Task Client_laedt_Einstellungen_erneuert_Links_und_liest_die_oeffentlichen_Seiten()
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/freizeitseite", () => Json(Einstellungen()));
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"/api/freizeiten/{FreizeitId}/anmeldelinks/mitarbeitende/erneuern",
            () => Json(new { art = "Mitarbeitende", adresse = "https://zeltlotse.example/freizeit/neu" }),
            () => NachgebildeterHandler.Json(HttpStatusCode.Conflict, """{"code":"freizeit-abgeschlossen"}""", "application/problem+json"),
            () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.Bei(HttpMethod.Get, "/api/freizeiten/oeffentlich/tok", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Get, "/api/freizeiten/oeffentlich/tok/vertrauensseite", () => Json(new
        {
            organisation = "CVJM",
            name = "Zeltlager",
            ersterTag = "2027-08-02",
            letzterTag = "2027-08-13",
            freigabeIntervallTage = 30,
            freigabeBis = "2027-09-12",
            freizeitDatenMonate = 12,
            nachweisBestaetigungMonate = 12,
            pflichtNachweise = new[] { "Führungszeugnis" },
        }));
        var client = Services.GetRequiredService<FreizeitenApiClient>();
        var art = Zeltlotse.Api.Contracts.Freizeiten.AnmeldelinkArt.Mitarbeitende;

        var einstellungen = await client.FreizeitseiteLadenAsync(FreizeitId, Abbruch);
        var erneuert = await client.AnmeldelinkErneuernAsync(FreizeitId, art, Abbruch);
        var abgeschlossen = await client.AnmeldelinkErneuernAsync(FreizeitId, art, Abbruch);
        var verboten = await client.AnmeldelinkErneuernAsync(FreizeitId, art, Abbruch);
        var seite = await client.FreizeitseiteOeffentlichLadenAsync("tok", Abbruch);
        var vertrauen = await client.VertrauensseiteLadenAsync("tok", Abbruch);

        Assert.Equal((7u, "Eine Woche.", "Kapf", (int?)40), (einstellungen!.Version, einstellungen.Beschreibung, einstellungen.Ort, einstellungen.HoechstzahlTeilnehmende));
        Assert.Equal(2, einstellungen.Anmeldelinks.Count);
        Assert.Equal("https://zeltlotse.example/freizeit/neu", erneuert.Link!.Adresse);
        Assert.Equal(new AnmeldelinkErneuernErgebnis(null, Abgeschlossen: true), abgeschlossen);
        Assert.Equal(new AnmeldelinkErneuernErgebnis(null, Abgeschlossen: false), verboten);
        Assert.Null(seite);
        Assert.Equal(["Führungszeugnis"], vertrauen!.PflichtNachweise);
    }

    [Fact]
    public void Einstellungen_zeigen_Felder_der_Freizeitseite_und_beide_Anmeldelinks()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen]);

        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Kapf", cut.Find("#freizeitseite-ort").GetAttribute("value")));
        var beschreibung = cut.Find("#freizeitseite-beschreibung");
        Assert.Equal("TEXTAREA", beschreibung.TagName);
        Assert.Equal("Eine Woche.", beschreibung.GetAttribute("value") ?? beschreibung.TextContent);
        Assert.Equal("40", cut.Find("#freizeitseite-hoechstzahl").GetAttribute("value"));
        Assert.Equal("number", cut.Find("#freizeitseite-hoechstzahl").GetAttribute("type"));
        // Story 2-2: Teilnahmebedingungen mehrzeilig, mit Hinweis, wo sie erscheinen.
        Assert.Equal("TEXTAREA", cut.Find("#freizeitseite-teilnahmebedingungen").TagName);
        Assert.Contains("Erscheint bei der Anmeldung vor dem Absenden.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(["Für Teilnehmende", "Für Mitarbeitende"], cut.FindAll("h3").Select(h => h.TextContent));
        Assert.Contains("https://zeltlotse.example/freizeit/tn", cut.Markup, StringComparison.Ordinal);
        var oeffnen = cut.FindAll("a").Where(a => a.TextContent == "Freizeitseite öffnen").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(["https://zeltlotse.example/freizeit/tn", "https://zeltlotse.example/freizeit/ma"], oeffnen);
        Assert.Equal(2, cut.FindAll("button").Count(b => b.TextContent == "Neuen Link erzeugen"));
    }

    [Fact]
    public void Neuen_Link_erzeugen_fragt_inline_nach_und_zeigt_danach_den_neuen_Link()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(
            HttpMethod.Post,
            $"/api/freizeiten/{FreizeitId}/anmeldelinks/teilnehmende/erneuern",
            () => Json(new { art = "Teilnehmende", adresse = "https://zeltlotse.example/freizeit/neu" }));
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("h3").Count));

        cut.FindAll("button").First(b => b.TextContent == "Neuen Link erzeugen").Click();
        Assert.Contains("Der bisherige Link wird ungültig.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad.EndsWith("/erneuern", StringComparison.Ordinal));
        cut.FindAll("button").First(b => b.TextContent == "Ja, neuen Link erzeugen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Neuer Link für Teilnehmende ist erzeugt.", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("https://zeltlotse.example/freizeit/neu", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("https://zeltlotse.example/freizeit/tn", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("https://zeltlotse.example/freizeit/ma", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Einstellungen_ohne_Anmeldelinks_bieten_Link_erzeugen_an()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen], mitLinks: false);

        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".elkw-empty").Count));
        Assert.All(cut.FindAll(".elkw-empty"), e => Assert.Contains("Noch kein Link.", e.TextContent, StringComparison.Ordinal));
        Assert.Equal(2, cut.FindAll("button").Count(b => b.TextContent == "Link erzeugen"));
    }

    [Fact]
    public void Einstellungen_einer_abgeschlossenen_Freizeit_zeigen_den_Hinweis_und_beim_409_keinen_Konflikt()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen], phase: "Gelaufen");
        _server.Bei(HttpMethod.Patch, $"/api/freizeiten/{FreizeitId}/freizeitseite", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForAssertion(() => Assert.Equal("Kapf", cut.Find("#freizeitseite-ort").GetAttribute("value")));
        Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Markup, StringComparison.Ordinal);

        var ort = cut.Find("#freizeitseite-ort");
        ort.Input("Neuer Ort");
        ort.Blur();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".elkw-alert").Count(m => m.TextContent.Contains("abgeschlossen und nur lesbar", StringComparison.Ordinal))));
        Assert.DoesNotContain("Inzwischen geändert", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Neuen_Link_erzeugen_bei_inzwischen_abgeschlossener_Freizeit_zeigt_den_Hinweis_statt_nicht_verfuegbar()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(
            HttpMethod.Post,
            $"/api/freizeiten/{FreizeitId}/anmeldelinks/teilnehmende/erneuern",
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("h3").Count));
        Assert.DoesNotContain("abgeschlossen und nur lesbar", cut.Markup, StringComparison.Ordinal);

        cut.FindAll("button").First(b => b.TextContent == "Neuen Link erzeugen").Click();
        cut.FindAll("button").First(b => b.TextContent == "Ja, neuen Link erzeugen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Einstellungen", cut.Find("h1").TextContent);
        Assert.DoesNotContain("Neuer Link für", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("https://zeltlotse.example/freizeit/tn", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Einstellungen_ohne_erlaubte_Aktion_sind_nicht_verfuegbar_und_die_Startseite_zeigt_keinen_Zugang()
    {
        EinstellungenAntworten([]);
        FreizeitStartAntworten();

        var einstellungen = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        var start = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        einstellungen.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", einstellungen.Find("h1").TextContent));
        start.WaitForAssertion(() => Assert.Equal("Zeltlager Sommer 2027", start.Find("h1").TextContent));
        Assert.DoesNotContain(start.FindAll("a"), a => a.TextContent == "Einstellungen");
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == $"/api/freizeiten/{FreizeitId}/freizeitseite");
    }

    [Fact]
    public void Freizeit_Startseite_zeigt_der_Leitung_den_Weg_zu_den_Einstellungen()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}", () => Json(new
        {
            id = FreizeitId,
            name = "Zeltlager Sommer 2027",
            ersterTag = "2027-08-02",
            letzterTag = "2027-08-13",
            phase = "InVorbereitung",
            freigabeBis = "2027-09-12",
            leitung = Array.Empty<object>(),
        }));

        var cut = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal(
            $"freizeiten/{FreizeitId}/einstellungen",
            cut.FindAll("a").Single(a => a.TextContent == "Einstellungen").GetAttribute("href")));
    }

    // ---------- Story 3-7: Module pro Freizeit ----------

    [Fact]
    public async Task Client_laedt_die_Module_und_liefert_null_bei_403()
    {
        _server.BeiDerReihe(
            HttpMethod.Get,
            $"/api/freizeiten/{FreizeitId}/module",
            () => Json(new { version = 9, verpflegung = true, finanzen = false, teilnehmerZugang = true }),
            () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = Services.GetRequiredService<FreizeitenApiClient>();

        var module = await client.ModuleLadenAsync(FreizeitId, Abbruch);
        var verboten = await client.ModuleLadenAsync(FreizeitId, Abbruch);

        Assert.Equal(new Zeltlotse.Api.Contracts.Freizeiten.FreizeitModule(9, true, false, true), module);
        Assert.Null(verboten);
        Assert.Equal($"/api/freizeiten/{FreizeitId}/module", FreizeitenApiClient.ModulePfad(FreizeitId));
    }

    [Fact]
    public void Einstellungen_zeigen_nach_den_Anmeldelinks_die_Card_Module_mit_drei_Ankreuzfeldern()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen]);

        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#module")));
        var titel = cut.FindAll(".elkw-card").Select(c => c.QuerySelector("h2")?.TextContent).ToList();
        Assert.Equal(titel.IndexOf("Anmeldelinks") + 1, titel.IndexOf("Module"));
        var card = cut.Find("#module");
        Assert.Contains(
            "Abgeschaltete Module erscheinen nirgends in Zeltlotse. Ihre Daten bleiben erhalten und sind nach dem Einschalten wieder da.",
            card.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["Verpflegung", "Finanzen", "Teilnehmer-Zugang"],
            card.QuerySelectorAll("label").Select(l => l.TextContent.Trim()));
        Assert.Equal(
            [("verpflegung", true), ("finanzen", false), ("teilnehmerZugang", false)],
            card.QuerySelectorAll("input[type=checkbox]").Select(i => (i.GetAttribute("name"), i.HasAttribute("checked"))));
    }

    [Fact]
    public void Einschalten_speichert_sofort_mit_Version_und_altem_Wert()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen]);
        _server.Bei(HttpMethod.Patch, $"/api/freizeiten/{FreizeitId}/module", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":12,"finanzen":"true"}"""));
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#modul-finanzen")));

        cut.Find("#modul-finanzen").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("#module [role=status]").TextContent, StringComparison.Ordinal));
        Assert.Equal(
            """{"version":11,"alterWert":"false","finanzen":"true"}""",
            _server.Anfragen.Last(a => a.Methode == HttpMethod.Patch && a.Pfad == $"/api/freizeiten/{FreizeitId}/module").Koerper);
    }

    [Fact]
    public void Module_einer_abgeschlossenen_Freizeit_zeigen_beim_409_den_Hinweis_statt_Konflikt()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitEinstellungen], phase: "Gelaufen");
        _server.Bei(HttpMethod.Patch, $"/api/freizeiten/{FreizeitId}/module", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#modul-verpflegung")));

        cut.Find("#modul-verpflegung").Change(false);

        cut.WaitForAssertion(() => Assert.Contains("Diese Freizeit ist abgeschlossen und nur lesbar.", cut.Find("#module .elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("Inzwischen geändert", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("#modul-verpflegung").HasAttribute("checked"));
    }

    [Fact]
    public void Ohne_Einstellungen_gibt_es_keine_Card_Module_und_keine_Anfrage()
    {
        EinstellungenAntworten([Aktionsschluessel.FreizeitpaesseSehen]);

        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll("#module"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == $"/api/freizeiten/{FreizeitId}/module");
    }

    private static object Einstellungen(bool mitLinks = true) => new
    {
        version = 7,
        beschreibung = "Eine Woche.",
        ort = "Kapf",
        hoechstzahlTeilnehmende = 40,
        anmeldelinks = mitLinks
            ? new object[]
            {
                new { art = "Teilnehmende", adresse = "https://zeltlotse.example/freizeit/tn" },
                new { art = "Mitarbeitende", adresse = "https://zeltlotse.example/freizeit/ma" },
            }
            : [],
    };

    private void EinstellungenAntworten(string[] erlaubt, bool mitLinks = true, string phase = "InVorbereitung")
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => Json(new
        {
            freizeitId = FreizeitId,
            name = "Zeltlager Sommer 2027",
            phase,
            rollen = new[] { "Leitung" },
            schalter = Array.Empty<string>(),
            erlaubt,
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/freizeitseite", () => Json(Einstellungen(mitLinks)));
        // Story 3-7: Module der Freizeit (Verpflegung an).
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/module", () => Json(new { version = 11, verpflegung = true, finanzen = false, teilnehmerZugang = false }));
    }
}
