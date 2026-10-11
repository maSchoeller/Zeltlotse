using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
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
/// Story 1-2 in der Web-App mit nachgebildetem Server und der echten Handler-Kette (Sitzung → Antiforgery): Bausteine
/// Datentabelle und Auswahl, der Client von Organisationen, die Seiten Organisationen, Organisation, Einladung und Start,
/// der Rückweg nach der Anmeldung und die Fehlermeldungen.
/// </summary>
public sealed class OrganisationenSeitenTests : BunitContext
{
    private static readonly Guid OrgId = Guid.Parse("0199a000-0000-7000-8000-0000000000a1");
    private static readonly Guid PersonId = Guid.Parse("0199a000-0000-7000-8000-0000000000b1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public OrganisationenSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Post, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Andrea","nachname":"Beispiel"}"""));
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

    private IEnumerable<string> Aufrufe => _server.Anfragen.Where(a => a.Pfad != "/api/betrieb/antiforgery").Select(a => $"{a.Methode} {a.Pfad}");

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static HttpResponseMessage Feldfehler(string json) =>
        NachgebildeterHandler.Json(HttpStatusCode.BadRequest, json, "application/problem+json");

    // ---------- Datentabelle ----------

    private sealed record Zeile(string Name, string Stand);

    [Fact]
    public void Datentabelle_hat_Beschriftung_Spaltenkoepfe_und_je_Zelle_die_Spaltenbezeichnung_fuer_das_Handy()
    {
        IReadOnlyList<Spalte<Zeile>> spalten =
        [
            new("Name", z => b => b.AddContent(0, z.Name)),
            new("Stand", z => b => b.AddContent(0, z.Stand)),
        ];

        var cut = Render<Datentabelle<Zeile>>(ps => ps
            .Add(p => p.Beschriftung, "Organisationen")
            .Add(p => p.Spalten, spalten)
            .Add(p => p.Zeilen, [new Zeile("A", "offen"), new Zeile("B", "fertig")]));

        Assert.NotNull(cut.Find("div.elkw-table-wrap > table.elkw-table.elkw-table--stack"));
        Assert.Equal("Organisationen", cut.Find("caption").TextContent);
        Assert.Equal(["Name", "Stand"], cut.FindAll("thead th").Select(t => t.TextContent));
        Assert.All(cut.FindAll("thead th"), t => Assert.Equal("col", t.GetAttribute("scope")));
        var zellen = cut.FindAll("tbody tr").Select(r => r.QuerySelectorAll("td").Select(td => (td.GetAttribute("data-label"), td.TextContent)).ToList()).ToList();
        Assert.Equal([[("Name", "A"), ("Stand", "offen")], [("Name", "B"), ("Stand", "fertig")]], zellen);
        Assert.False(typeof(Datentabelle<Zeile>).IsDefined(typeof(NurInteraktivAttribute), false));
    }

    // ---------- Auswahl ----------

    [Fact]
    public void Auswahl_ist_eine_Radiogruppe_mit_Legende_und_meldet_die_Wahl()
    {
        string? gewaehlt = null;
        var cut = Render<Auswahl>(ps => ps
            .Add(p => p.Legende, "Rolle")
            .Add(p => p.Gruppe, "rolle")
            .Add(p => p.Optionen, [new AuswahlOption("OrgAdmin", "Org-Admin"), new AuswahlOption("OrgUeberblick", "Org-Überblick")])
            .Add(p => p.WertGeaendert, w => gewaehlt = w));

        Assert.Equal("Rolle", cut.Find("fieldset.elkw-fieldset > legend").TextContent);
        var optionen = cut.FindAll("label.elkw-choice");
        Assert.Equal(["Org-Admin", "Org-Überblick"], optionen.Select(o => o.TextContent.Trim()));
        Assert.All(cut.FindAll("input"), i => Assert.Equal(("radio", "rolle"), (i.GetAttribute("type"), i.GetAttribute("name"))));
        Assert.DoesNotContain(cut.FindAll("input"), i => i.HasAttribute("checked"));
        Assert.Null(cut.Find("fieldset").GetAttribute("aria-describedby"));

        cut.FindAll("input")[1].Change(true);

        Assert.Equal("OrgUeberblick", gewaehlt);
        Assert.True(typeof(Auswahl).IsDefined(typeof(NurInteraktivAttribute), false));
    }

    [Fact]
    public void Auswahl_zeigt_den_gewaehlten_Wert_und_den_Fehler_an_der_Gruppe()
    {
        var cut = Render<Auswahl>(ps => ps
            .Add(p => p.Legende, "Rolle")
            .Add(p => p.Gruppe, "rolle")
            .Add(p => p.Optionen, [new AuswahlOption("A", "a"), new AuswahlOption("B", "b")])
            .Add(p => p.Wert, "B")
            .Add(p => p.Fehler, "Rolle fehlt."));

        Assert.True(cut.FindAll("input")[1].HasAttribute("checked"));
        Assert.False(cut.FindAll("input")[0].HasAttribute("checked"));
        var fehler = cut.Find(".elkw-field-error");
        Assert.Equal("Fehler: Rolle fehlt.", fehler.TextContent);
        Assert.Equal(fehler.Id, cut.Find("fieldset").GetAttribute("aria-describedby"));
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_spricht_die_Endpunkte_mit_den_Vertragsformen_an_und_403_404_heissen_nicht_verfuegbar()
    {
        _server.Bei(HttpMethod.Get, "/api/organisationen", () => Json(new[] { new { id = OrgId, name = "A", eingerichtet = true } }));
        _server.Bei(HttpMethod.Post, "/api/organisationen", () => Json(new { id = OrgId }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/rollen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/einladungen", () => Json(new { id = PersonId }, HttpStatusCode.Accepted));
        _server.Bei(HttpMethod.Post, "/api/organisationen/einladungen/annehmen", () => Json(new { organisationId = OrgId }));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen", () => Json(new[] { new { organisationId = OrgId, name = "A", rollen = new[] { "OrgAdmin", "OrgUeberblick" } } }));
        var client = Services.GetRequiredService<OrganisationenApiClient>();

        var liste = await client.ListeLadenAsync(Abbruch);
        var angelegt = await client.AnlegenAsync("A", "a@example.org", Abbruch);
        var organisation = await client.LadenAsync(OrgId, Abbruch);
        var rollen = await client.RollenLadenAsync(OrgId, Abbruch);
        var eingeladen = await client.EinladenAsync(OrgId, "b@example.org", OrgRolle.OrgUeberblick, Abbruch);
        var angenommen = await client.AnnehmenAsync("Tk_1", Abbruch);
        var meine = await client.MeineLadenAsync(Abbruch);

        Assert.Equal([new Zeltlotse.Api.Contracts.Organisationen.OrganisationZeile(OrgId, "A", true)], liste);
        Assert.Equal(new AnlegenErgebnis(OrgId, null, null), angelegt);
        Assert.Null(organisation);
        Assert.Null(rollen);
        Assert.Equal(new EinladenErgebnis(PersonId, null, null), eingeladen);
        Assert.Equal(OrgId, angenommen);
        Assert.Equal([OrgRolle.OrgAdmin, OrgRolle.OrgUeberblick], Assert.Single(meine).Rollen);
        Assert.Equal("""{"name":"A","mail":"a@example.org"}""", Koerper("/api/organisationen"));
        Assert.Equal("""{"mail":"b@example.org","rolle":"OrgUeberblick"}""", Koerper($"/api/organisationen/{OrgId}/einladungen"));
        Assert.Equal("""{"token":"Tk_1"}""", Koerper("/api/organisationen/einladungen/annehmen"));
    }

    [Fact]
    public async Task Client_liefert_Feldfehler_und_404_beim_Annehmen_als_null()
    {
        _server.Bei(HttpMethod.Post, "/api/organisationen", () => Feldfehler("""{"errors":{"name":["Name fehlt."],"mail":["Mail-Adresse fehlt."]}}"""));
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/einladungen", () => Feldfehler("""{"errors":{"rolle":["Rolle fehlt."]}}"""));
        _server.Bei(HttpMethod.Post, "/api/organisationen/einladungen/annehmen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Services.GetRequiredService<OrganisationenApiClient>();

        Assert.Equal(new AnlegenErgebnis(null, "Name fehlt.", "Mail-Adresse fehlt."), await client.AnlegenAsync(null, null, Abbruch));
        Assert.Equal(new EinladenErgebnis(null, null, "Rolle fehlt."), await client.EinladenAsync(OrgId, "b@example.org", null, Abbruch));
        Assert.Null(await client.AnnehmenAsync("alt", Abbruch));
    }

    // ---------- Organisationen (Global Admin) ----------

    [Fact]
    public void Organisationen_zeigt_die_Tabelle_mit_Stand_und_das_Formular()
    {
        _server.Bei(HttpMethod.Get, "/api/organisationen", () => Json(new object[]
        {
            new { id = OrgId, name = "Jugendwerk Musterstadt", eingerichtet = true },
            new { id = Guid.CreateVersion7(), name = "Zeltlager-Verein", eingerichtet = false },
        }));

        var cut = Render<OrganisationenListe>();

        cut.WaitForElement("table");
        Assert.Equal("Organisationen", cut.Find("h1").TextContent);
        Assert.Equal(["Name", "Stand"], cut.FindAll("thead th").Select(t => t.TextContent));
        Assert.Equal(
            [("Jugendwerk Musterstadt", "Eingerichtet", "elkw-status--erfolg"), ("Zeltlager-Verein", "Einladung offen", "elkw-status--offen")],
            cut.FindAll("tbody tr").Select(r => (r.Children[0].TextContent, r.Children[1].TextContent.Trim(), r.QuerySelector(".elkw-status")!.ClassList[1])));
        Assert.Equal("Organisation anlegen", cut.Find(".elkw-card__title").TextContent);
        Assert.Equal("Name der Organisation", cut.Find("label[for=organisation-name]").TextContent);
        Assert.Equal("Mail-Adresse des ersten Org-Admins", cut.Find("label[for=organisation-mail]").TextContent);
        Assert.Equal("email", cut.Find("#organisation-mail").GetAttribute("type"));
        Assert.Equal("Anlegen und einladen", cut.Find("form button[type=submit]").TextContent);
    }

    [Fact]
    public void Organisationen_ohne_Eintrag_zeigt_den_Leer_Zustand()
    {
        _server.Bei(HttpMethod.Get, "/api/organisationen", () => Json(Array.Empty<object>()));

        var cut = Render<OrganisationenListe>();

        cut.WaitForAssertion(() => Assert.Equal("Noch keine Organisation angelegt.", cut.Find(".elkw-empty p").TextContent));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Organisation_anlegen_meldet_Gespeichert_leert_das_Formular_und_laedt_die_Liste_neu()
    {
        var liste = 0;
        _server.Bei(HttpMethod.Get, "/api/organisationen", () => ++liste == 1
            ? Json(Array.Empty<object>())
            : Json(new[] { new { id = OrgId, name = "Jugendwerk", eingerichtet = false } }));
        _server.Bei(HttpMethod.Post, "/api/organisationen", () => Json(new { id = OrgId }, HttpStatusCode.Created));
        var cut = Render<OrganisationenListe>();
        cut.WaitForElement(".elkw-empty");

        cut.Find("#organisation-name").Input("Jugendwerk");
        cut.Find("#organisation-mail").Input("andrea.beispiel@example.org");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Die Organisation ist angelegt, die Einladung ist verschickt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Gespeichert:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Equal("Jugendwerk", cut.Find("tbody td").TextContent);
        Assert.Null(cut.Find("#organisation-name").GetAttribute("value"));
        Assert.Equal(2, liste);
        Assert.Equal("""{"name":"Jugendwerk","mail":"andrea.beispiel@example.org"}""", Koerper("/api/organisationen"));
    }

    [Fact]
    public void Organisation_anlegen_zeigt_die_Fehler_an_den_Feldern_und_ein_Serverfehler_die_Meldung()
    {
        _server.Bei(HttpMethod.Get, "/api/organisationen", () => Json(Array.Empty<object>()));
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/organisationen",
            () => Feldfehler("""{"errors":{"name":["Name fehlt."],"mail":["Das ist keine gültige Mail-Adresse."]}}"""),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = Render<OrganisationenListe>();
        cut.WaitForElement(".elkw-empty");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".elkw-field-error").Count));
        Assert.Equal(["Fehler: Name fehlt.", "Fehler: Das ist keine gültige Mail-Adresse."], cut.FindAll(".elkw-field-error").Select(f => f.TextContent));
        Assert.Equal("true", cut.Find("#organisation-name").GetAttribute("aria-invalid"));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("alert", cut.Find(".elkw-alert--fehler").GetAttribute("role"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void Organisationen_ohne_Recht_ist_nicht_verfuegbar(HttpStatusCode status)
    {
        _server.Bei(HttpMethod.Get, "/api/organisationen", () => new HttpResponseMessage(status));

        var cut = Render<OrganisationenListe>();

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll("form"));
    }

    // ---------- Organisation ----------

    private void OrganisationAntwort(string rolle, params string[] erlaubt) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => Json(new { id = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { rolle }, erlaubt }));

    private void RollenAntwort(params object[] einladungen) =>
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}/rollen", () => Json(new
        {
            personen = new object[]
            {
                new { personId = PersonId, vorname = "Andrea", nachname = "Beispiel", rolle = "OrgAdmin" },
                new { personId = Guid.CreateVersion7(), vorname = (string?)null, nachname = (string?)null, rolle = "OrgUeberblick" },
            },
            einladungen,
        }));

    [Fact]
    public void Organisation_mit_Rollen_vergeben_zeigt_Rollen_offene_Einladungen_und_Person_einladen()
    {
        OrganisationAntwort("OrgAdmin", Aktionsschluessel.OrgRollenVergeben);
        RollenAntwort(new { id = Guid.CreateVersion7(), mail = "vorstand@example.org", rolle = "OrgUeberblick", gueltigBis = new DateTimeOffset(2026, 10, 18, 22, 30, 0, TimeSpan.Zero) });

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForElement("table");
        Assert.Equal("Jugendwerk Musterstadt", cut.Find("h1").TextContent);
        Assert.Contains("Rolle: Org-Admin", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(["Rollen", "Offene Einladungen", "Person einladen"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        var tabellen = cut.FindAll("table");
        Assert.Equal(
            [["Andrea Beispiel", "Org-Admin"], ["Name fehlt", "Org-Überblick"]],
            tabellen[0].QuerySelectorAll("tbody tr").Select(r => r.QuerySelectorAll("td").Select(td => td.TextContent).ToList()));
        // 22:30 UTC am 18.10. ist in Berlin schon der 19.10.
        Assert.Equal(
            ["vorstand@example.org", "Org-Überblick", "gültig bis 19.10.2026"],
            tabellen[1].QuerySelectorAll("tbody td").Select(td => td.TextContent));
        Assert.Equal("Mail-Adresse", cut.Find("label[for=einladen-mail]").TextContent);
        Assert.Equal("Rolle", cut.Find("fieldset legend").TextContent);
        Assert.Equal(["Org-Admin", "Org-Überblick"], cut.FindAll("fieldset label").Select(l => l.TextContent.Trim()));
        Assert.Equal("Einladung schicken", cut.Find("form button[type=submit]").TextContent);
    }

    [Fact]
    public void Organisation_ohne_offene_Einladungen_zeigt_den_Leer_Zustand()
    {
        OrganisationAntwort("OrgAdmin", Aktionsschluessel.OrgRollenVergeben);
        RollenAntwort();

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForElement("table");
        Assert.Equal("Keine offenen Einladungen.", cut.Find(".elkw-empty p").TextContent);
        Assert.Single(cut.FindAll("table"));
    }

    [Fact]
    public void Org_Ueberblick_sieht_Name_und_Rolle_aber_keine_Rollen_und_kein_Formular()
    {
        OrganisationAntwort("OrgUeberblick");

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Equal("Jugendwerk Musterstadt", cut.Find("h1").TextContent));
        Assert.Contains("Rolle: Org-Überblick", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".elkw-card"));
        Assert.Empty(cut.FindAll("form"));
        Assert.DoesNotContain(Aufrufe, a => a.EndsWith("/rollen", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void Organisation_ohne_Recht_ist_nicht_verfuegbar(HttpStatusCode status)
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => new HttpResponseMessage(status));

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", cut.Find("h1").TextContent));
        Assert.Equal("./", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void Organisation_laden_scheitert_mit_der_Meldung_statt_Wird_geladen()
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{OrgId}", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-loading"));
    }

    [Fact]
    public void Person_einladen_meldet_Gespeichert_und_laedt_die_Listen_neu()
    {
        OrganisationAntwort("OrgAdmin", Aktionsschluessel.OrgRollenVergeben);
        RollenAntwort();
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/einladungen", () => Json(new { id = Guid.CreateVersion7() }, HttpStatusCode.Accepted));
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForElement("form");

        cut.Find("#einladen-mail").Input("vorstand@example.org");
        cut.FindAll("fieldset input")[1].Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Die Einladung ist verschickt.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Gespeichert:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent);
        Assert.Equal("""{"mail":"vorstand@example.org","rolle":"OrgUeberblick"}""", Koerper($"/api/organisationen/{OrgId}/einladungen"));
        Assert.Equal(2, _server.Anfragen.Count(a => a.Pfad == $"/api/organisationen/{OrgId}/rollen"));
    }

    [Fact]
    public void Person_einladen_zeigt_Fehler_an_Mail_und_Rolle()
    {
        OrganisationAntwort("OrgAdmin", Aktionsschluessel.OrgRollenVergeben);
        RollenAntwort();
        _server.Bei(HttpMethod.Post, $"/api/organisationen/{OrgId}/einladungen", () => Feldfehler("""{"errors":{"mail":["Mail-Adresse fehlt."],"rolle":["Rolle fehlt."]}}"""));
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, OrgId));
        cut.WaitForElement("form");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(["Fehler: Mail-Adresse fehlt.", "Fehler: Rolle fehlt."], cut.FindAll(".elkw-field-error").Select(f => f.TextContent)));
        Assert.Contains("\"rolle\":null", Koerper($"/api/organisationen/{OrgId}/einladungen"), StringComparison.Ordinal);
    }

    // ---------- Einladung ----------

    [Fact]
    public void Einladung_nimmt_erst_beim_Klick_an_und_fuehrt_zur_Organisation()
    {
        _server.Bei(HttpMethod.Post, "/api/organisationen/einladungen/annehmen", () => Json(new { organisationId = OrgId }));
        var cut = Render<EinladungOeffnen>(ps => ps.Add(p => p.Token, "Tk_1"));

        Assert.Equal("Einladung annehmen", cut.Find("h1").TextContent);
        Assert.Equal("Einladung annehmen", cut.Find("button").TextContent);
        Assert.Empty(_server.Anfragen);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost/organisationen/{OrgId}", Navigation.Uri));
        Assert.Equal("""{"token":"Tk_1"}""", Koerper("/api/organisationen/einladungen/annehmen"));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.Contains("Tk_1", StringComparison.Ordinal));
    }

    [Fact]
    public void Ungueltige_Einladung_zeigt_den_Satz_und_ein_Serverfehler_die_Aktionsmeldung()
    {
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/organisationen/einladungen/annehmen",
            () => new HttpResponseMessage(HttpStatusCode.NotFound),
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var cut = Render<EinladungOeffnen>(ps => ps.Add(p => p.Token, "alt"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            "Diese Einladung ist nicht mehr gültig oder gilt für eine andere Mail-Adresse.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal("Fehler:", cut.Find(".elkw-alert--fehler .elkw-alert__title").TextContent);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    // ---------- Rückweg nach der Anmeldung ----------

    [Fact]
    public void Ohne_Sitzung_fuehrt_die_Einladung_zur_Anmeldung_und_nach_dem_Code_zurueck_zur_Einladung()
    {
        _server.BeiDerReihe(HttpMethod.Post, "/api/organisationen/einladungen/annehmen", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => Json(new { anfrageId = Guid.CreateVersion7() }, HttpStatusCode.Accepted));
        _server.Bei(HttpMethod.Post, "/api/konten/login-code/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Navigation.NavigateTo("einladung/Tk_1");
        var einladung = Render<EinladungOeffnen>(ps => ps.Add(p => p.Token, "Tk_1"));

        einladung.Find("button").Click();

        einladung.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.Equal("einladung/Tk_1", Sitzung.Rueckweg);

        var anmelden = Render<Anmelden>();
        anmelden.Find("#anmelden-mail").Input("andrea.beispiel@example.org");
        anmelden.Find("form").Submit();
        anmelden.WaitForElement("#anmelden-code").Input("123456");
        anmelden.FindAll("form")[1].Submit();

        anmelden.WaitForAssertion(() => Assert.Equal("http://localhost/einladung/Tk_1", Navigation.Uri));
        Assert.Null(Sitzung.Rueckweg);
        Assert.True(Sitzung.Angemeldet);
    }

    [Fact]
    public void Nach_der_Anmeldung_mit_Passwort_geht_es_ebenfalls_zurueck()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        _server.Bei(HttpMethod.Post, "/api/konten/anmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Navigation.NavigateTo("konto");
        Render<Zeltlotse.App.Features.Konten.Konto>();
        Assert.Equal("konto", Sitzung.Rueckweg);

        var passwort = Render<AnmeldenPasswort>();
        passwort.Find("#passwort-mail").Input("andrea.beispiel@example.org");
        passwort.Find("#passwort-passwort").Input("ein-langes-Passwort");
        passwort.Find("form").Submit();

        passwort.WaitForAssertion(() => Assert.Equal("http://localhost/konto", Navigation.Uri));
    }

    [Fact]
    public async Task Abmelden_verwirft_den_Rueckweg_Ablaufen_nicht()
    {
        Navigation.NavigateTo("einladung/Tk_1");
        await Sitzung.AbgelaufenAsync();
        Assert.Equal("einladung/Tk_1", Sitzung.Rueckweg);

        await Sitzung.AbgelaufenAsync();
        Assert.Equal("einladung/Tk_1", Sitzung.Rueckweg);

        Sitzung.Abmelden();
        Assert.Null(Sitzung.Rueckweg);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost//fremd.example/x")]
    [InlineData("http://localhost/anmelden/passwort")]
    [InlineData("http://localhost/javascript:alert(1)")]
    [InlineData("http://localhost/a//b")]
    [InlineData(@"http://localhost/a\b")]
    public async Task Startseite_Anmelde_Seiten_und_fremde_Adressen_werden_kein_Rueckweg(string adresse)
    {
        Navigation.NavigateTo(adresse);

        await Sitzung.AbgelaufenAsync();

        Assert.Null(Sitzung.Rueckweg);
    }

    // ---------- Startseite ----------

    [Fact]
    public void Startseite_zeigt_Global_Admins_Organisationen_verwalten_und_je_eigener_Organisation_eine_Card()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Andrea","nachname":null}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"a@example.org","hatPasswort":false,"erlaubt":["organisationen-anlegen"]}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen", () => Json(new[] { new { organisationId = OrgId, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" } } }));

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Freizeit geöffnet.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Hallo, Andrea", cut.Find("h1").TextContent);
        var verwalten = cut.Find("a[href=organisationen]");
        Assert.Equal("Organisationen verwalten", verwalten.TextContent);
        Assert.Equal("Jugendwerk Musterstadt", cut.Find(".elkw-card__title").TextContent);
        Assert.Contains("Rolle: Org-Admin", cut.Find(".elkw-card").TextContent, StringComparison.Ordinal);
        Assert.Equal("Organisation öffnen", cut.Find($"a[href='organisationen/{OrgId}']").TextContent);
    }

    [Fact]
    public void Startseite_ohne_erlaubte_Aktion_und_ohne_Organisation_bleibt_wie_bisher()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Jonas","nachname":null}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"j@example.org","hatPasswort":false,"erlaubt":[]}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen", () => Json(Array.Empty<object>()));

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Freizeit geöffnet.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("a[href=organisationen]"));
        Assert.Empty(cut.FindAll(".elkw-card"));
    }

    // ---------- Texte ----------

    [Fact]
    public void Texte_fuer_Rollen_Namen_und_Datum()
    {
        Assert.Equal("Org-Admin, Org-Überblick", Anzeige.Rollen([OrgRolle.OrgUeberblick, OrgRolle.OrgAdmin]));
        Assert.Equal("Name fehlt", Anzeige.Name(null, " "));
        Assert.Equal("Andrea", Anzeige.Name("Andrea", null));
        Assert.Equal("01.07.2026", Anzeige.Datum(new DateTimeOffset(2026, 6, 30, 23, 0, 0, TimeSpan.Zero)));
    }

    private string Koerper(string pfad) => _server.Anfragen.Last(a => a.Pfad == pfad && a.Methode != HttpMethod.Get).Koerper!;
}
