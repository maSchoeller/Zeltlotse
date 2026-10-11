using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Pages;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Seitenlogik der Story 1-1 mit nachgebildetem Server und der echten Handler-Kette (Sitzung → Antiforgery):
/// Anmelden per Link, Code und Passwort, Startseite, Konto mit Autosave, Passwort und Abmelden.
/// </summary>
public sealed class AnmeldeSeitenTests : BunitContext
{
    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public AnmeldeSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Post, "/api/ich/profil", () => Profil("Jonas"));
        Services.AddScoped<AppSitzung>();
        Services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        Services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = _server });
        Services.AddScoped(sp => new HttpClient(
            new SitzungsHandler(sp.GetRequiredService<ISitzung>()) { InnerHandler = sp.GetRequiredService<AntiforgeryHandler>() },
            disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") });
        Services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private AppSitzung Sitzung => Services.GetRequiredService<AppSitzung>();

    private static HttpResponseMessage Profil(string? vorname, uint version = 5) =>
        NachgebildeterHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { version, vorname, nachname = (string?)null }));

    private IEnumerable<string> Aufrufe => _server.Anfragen.Where(a => a.Pfad != "/api/betrieb/antiforgery").Select(a => $"{a.Methode} {a.Pfad}");

    // ---------- Anmelden ----------

    [Fact]
    public void Anmelden_zeigt_Feld_Mail_Adresse_den_Knopf_und_den_Link_zum_Passwort()
    {
        var cut = Render<Anmelden>();

        Assert.Equal("Anmelden", cut.Find("h1").TextContent);
        var mail = cut.Find("input#anmelden-mail");
        Assert.Equal("email", mail.GetAttribute("type"));
        Assert.Equal("email", mail.GetAttribute("autocomplete"));
        Assert.Equal("Mail-Adresse", cut.Find("label[for=anmelden-mail]").TextContent);
        Assert.Equal("Login-Link schicken", cut.Find("form button[type=submit]").TextContent);
        Assert.Equal("anmelden/passwort", cut.Find("a[href='anmelden/passwort']").GetAttribute("href"));
        Assert.Equal("Mit Passwort anmelden", cut.Find("a[href='anmelden/passwort']").TextContent);
        Assert.Empty(cut.FindAll("#anmelden-code"));
        Assert.DoesNotContain("elkw-", cut.Find("a[href='anmelden/passwort']").GetAttribute("class") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Link_schicken_zeigt_den_Hinweis_und_das_Feld_Login_Code()
    {
        var id = Guid.CreateVersion7();
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => NachgebildeterHandler.Json(HttpStatusCode.Accepted, $$"""{"anfrageId":"{{id}}"}"""));
        var cut = Render<Anmelden>();

        cut.Find("#anmelden-mail").Input("jonas.huettner@example.org");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Login-Link ist verschickt. Bitte im Postfach nachsehen.", cut.Markup, StringComparison.Ordinal));
        var hinweis = cut.Find("section.elkw-alert");
        Assert.Equal("Hinweis:", hinweis.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Equal("status", hinweis.GetAttribute("role"));
        var code = cut.Find("input#anmelden-code");
        Assert.Equal("numeric", code.GetAttribute("inputmode"));
        Assert.Equal("one-time-code", code.GetAttribute("autocomplete"));
        Assert.Equal("Login-Code", cut.Find("label[for=anmelden-code]").TextContent);
        Assert.Contains(cut.FindAll("button[type=submit]"), k => k.TextContent == "Mit Code anmelden");
        Assert.Equal("jonas.huettner@example.org", JsonDocument.Parse(_server.Anfragen.Single(a => a.Pfad == "/api/konten/login-link").Koerper!).RootElement.GetProperty("mail").GetString());
    }

    [Fact]
    public void Ungueltige_Mail_zeigt_den_Fehler_am_Feld()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"mail":["Das ist keine gültige Mail-Adresse."]}}""", "application/problem+json"));
        var cut = Render<Anmelden>();

        cut.Find("#anmelden-mail").Input("jonas");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Das ist keine gültige Mail-Adresse.", cut.Find(".elkw-field-error").TextContent));
        Assert.Equal("true", cut.Find("#anmelden-mail").GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll("#anmelden-code"));
    }

    [Fact]
    public void Falscher_Code_zeigt_den_Fehler_mit_Neuen_Link_schicken_und_ein_neuer_Link_hebt_ihn_auf()
    {
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var link = 0;
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => NachgebildeterHandler.Json(HttpStatusCode.Accepted, $$"""{"anfrageId":"{{ids[link++]}}"}"""));
        _server.Bei(HttpMethod.Post, "/api/konten/login-code/einloesen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Render<Anmelden>();
        cut.Find("#anmelden-mail").Input("jonas.huettner@example.org");
        cut.Find("form").Submit();
        cut.WaitForElement("#anmelden-code").Input("111111");

        cut.FindAll("form")[1].Submit();

        cut.WaitForAssertion(() => Assert.Contains("Der Code stimmt nicht oder ist abgelaufen.", cut.Markup, StringComparison.Ordinal));
        var fehler = cut.Find("section.elkw-alert--fehler");
        Assert.Equal("Fehler:", fehler.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Equal("alert", fehler.GetAttribute("role"));
        Assert.Equal("http://localhost/", Navigation.Uri);
        Assert.False(Sitzung.Angemeldet);
        Assert.Equal(ids[0], JsonDocument.Parse(_server.Anfragen.Single(a => a.Pfad == "/api/konten/login-code/einloesen").Koerper!).RootElement.GetProperty("anfrageId").GetGuid());

        fehler.QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Der Code stimmt nicht", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Login-Link ist verschickt.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, _server.Anfragen.Count(a => a.Pfad == "/api/konten/login-link"));
    }

    [Fact]
    public void Richtiger_Code_meldet_an_legt_das_Profil_an_und_fuehrt_zur_Startseite()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => NachgebildeterHandler.Json(HttpStatusCode.Accepted, $$"""{"anfrageId":"{{Guid.CreateVersion7()}}"}"""));
        _server.Bei(HttpMethod.Post, "/api/konten/login-code/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<Anmelden>();
        cut.Find("#anmelden-mail").Input("jonas.huettner@example.org");
        cut.Find("form").Submit();
        cut.WaitForElement("#anmelden-code").Input("123456");

        cut.FindAll("form")[1].Submit();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", Navigation.Uri));
        Assert.True(Sitzung.Angemeldet);
        Assert.Equal("Jonas", Sitzung.Vorname);
        Assert.Equal(["POST /api/konten/login-link", "POST /api/konten/login-code/einloesen", "POST /api/ich/profil"], Aufrufe);
        // Nach der Anmeldung ein neues Antiforgery-Token (es hängt an der Identität).
        var tokenNachAnmeldung = _server.Anfragen.Single(a => a.Pfad == "/api/ich/profil").Kopfzeilen[Zeltlotse.Api.Contracts.Gemeinsam.Kopfzeilen.Antiforgery];
        Assert.NotEqual(_server.Anfragen.Single(a => a.Pfad == "/api/konten/login-code/einloesen").Kopfzeilen[Zeltlotse.Api.Contracts.Gemeinsam.Kopfzeilen.Antiforgery], tokenNachAnmeldung);
    }

    // ---------- Login-Link ----------

    [Fact]
    public void Link_Seite_loest_erst_beim_Klick_ein_und_fuehrt_dann_zur_Startseite()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<AnmeldenLink>(ps => ps.Add(p => p.Token, "Tk_123"));

        Assert.Equal("Anmelden", cut.Find("h1").TextContent);
        Assert.Empty(_server.Anfragen);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", Navigation.Uri));
        Assert.Equal("Tk_123", JsonDocument.Parse(_server.Anfragen.Single(a => a.Pfad == "/api/konten/login-link/einloesen").Koerper!).RootElement.GetProperty("token").GetString());
        Assert.True(Sitzung.Angemeldet);
    }

    [Fact]
    public void Ungueltiger_Link_zeigt_den_Satz_und_Neuen_Link_schicken()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link/einloesen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Render<AnmeldenLink>(ps => ps.Add(p => p.Token, "alt"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("Dieser Link ist nicht mehr gültig.", cut.Markup, StringComparison.Ordinal));
        var neu = cut.Find("section.elkw-alert--fehler a");
        Assert.Equal("Neuen Link schicken", neu.TextContent);
        Assert.Equal("anmelden", neu.GetAttribute("href"));
        Assert.Empty(cut.FindAll("button"));
        Assert.False(Sitzung.Angemeldet);
    }

    // ---------- Passwort ----------

    [Fact]
    public void Passwort_Seite_zeigt_bei_401_den_Fehler_und_bleibt()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/anmelden", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        Navigation.NavigateTo("anmelden/passwort");
        var cut = Render<AnmeldenPasswort>();
        Assert.Equal("current-password", cut.Find("#passwort-passwort").GetAttribute("autocomplete"));
        Assert.Equal("password", cut.Find("#passwort-passwort").GetAttribute("type"));
        Assert.Equal("Mail-Adresse", cut.Find("label[for=passwort-mail]").TextContent);

        cut.Find("#passwort-mail").Input("jonas.huettner@example.org");
        cut.Find("#passwort-passwort").Input("falsch-falsch");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Mail-Adresse oder Passwort stimmt nicht.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Fehler:", cut.Find("section.elkw-alert--fehler .elkw-alert__title").TextContent);
        Assert.Equal("http://localhost/anmelden/passwort", Navigation.Uri);
        Assert.Equal("Anmelden", cut.Find("form button[type=submit]").TextContent);
    }

    [Fact]
    public void Passwort_Seite_meldet_bei_204_an()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/anmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<AnmeldenPasswort>();

        cut.Find("#passwort-mail").Input("jonas.huettner@example.org");
        cut.Find("#passwort-passwort").Input("richtig-richtig");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", Navigation.Uri));
        Assert.Equal(["POST /api/konten/anmelden", "POST /api/ich/profil"], Aufrufe);
    }

    // ---------- Startseite ----------

    [Fact]
    public void Startseite_begruesst_mit_Vornamen()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => Profil("Jonas"));

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Equal("Hallo, Jonas", cut.Find("h1").TextContent));
        Assert.Contains("Noch keine Freizeit geöffnet.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Jonas", Sitzung.Vorname);
    }

    [Fact]
    public void Startseite_ohne_Vornamen_sagt_Hallo_und_legt_ein_fehlendes_Profil_an()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Post, "/api/ich/profil", () => Profil(null));

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Freizeit geöffnet.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Hallo", cut.Find("h1").TextContent);
        Assert.Equal(["GET /api/ich/profil", "POST /api/ich/profil"], Aufrufe.Take(2));
        Assert.Equal(["GET /api/ich/konto", "GET /api/ich/organisationen"], Aufrufe.Skip(2).Order(StringComparer.Ordinal));
        Assert.True(Sitzung.Angemeldet);
    }

    [Fact]
    public void Startseite_ohne_Sitzung_fuehrt_zur_Seite_Anmelden()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.False(Sitzung.Angemeldet);
    }

    [Fact]
    public void Startseite_ohne_Netz_bleibt_bei_Hallo()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => throw new HttpRequestException("Kein Netz"));

        var cut = Render<Start>();

        cut.WaitForAssertion(() => Assert.Contains("Noch keine Freizeit geöffnet.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Hallo", cut.Find("h1").TextContent);
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    // ---------- Konto ----------

    private void KontoServer(bool hatPasswort)
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"mail":"jonas.huettner@example.org","hatPasswort":{{(hatPasswort ? "true" : "false")}}}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => Profil("Jonas", 9));
    }

    [Fact]
    public void Konto_zeigt_Mail_Autosave_Felder_und_Passwort_festlegen()
    {
        KontoServer(hatPasswort: false);

        var cut = Render<Konto>();

        cut.WaitForAssertion(() => Assert.Contains("jonas.huettner@example.org", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Konto", cut.Find("h1").TextContent);
        Assert.Equal("Jonas", cut.Find("input#konto-vorname").GetAttribute("value"));
        Assert.Equal("Vorname", cut.Find("label[for=konto-vorname]").TextContent);
        Assert.Equal("Nachname", cut.Find("label[for=konto-nachname]").TextContent);
        Assert.Contains(cut.FindAll("h2"), h => h.TextContent == "Passwort festlegen");
        Assert.Equal("new-password", cut.Find("#konto-passwort").GetAttribute("autocomplete"));
        Assert.Contains(cut.FindAll("button"), k => k.TextContent == "Passwort speichern");
        Assert.Contains(cut.FindAll("button"), k => k.TextContent == "Abmelden");
        Assert.Equal("Jonas", Sitzung.Vorname);
    }

    [Fact]
    public void Konto_Vorname_speichert_per_Autosave_an_api_ich_profil()
    {
        KontoServer(hatPasswort: true);
        _server.Bei(new HttpMethod("PATCH"), "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":10,"vorname":"Jonas Peter"}"""));
        var cut = Render<Konto>();
        var feld = cut.WaitForElement("input#konto-vorname");
        Assert.Contains(cut.FindAll("h2"), h => h.TextContent == "Passwort ändern");

        feld.Focus();
        feld.Input("Jonas Peter");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("#konto-vorname").ParentElement!.TextContent, StringComparison.Ordinal));
        var patch = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode.Method == "PATCH").Koerper!).RootElement;
        Assert.Equal(9u, patch.GetProperty("version").GetUInt32());
        Assert.Equal("Jonas", patch.GetProperty("alterWert").GetString());
        Assert.Equal("Jonas Peter", patch.GetProperty("vorname").GetString());
    }

    [Fact]
    public void Konto_Passwort_speichern_zeigt_Gespeichert_oder_den_Fehler_am_Feld()
    {
        KontoServer(hatPasswort: false);
        _server.BeiDerReihe(
            HttpMethod.Put,
            "/api/ich/konto/passwort",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"passwort":["Das Passwort braucht mindestens 10 Zeichen."]}}""", "application/problem+json"),
            () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<Konto>();
        cut.WaitForElement("#konto-passwort").Input("kurz");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Das Passwort braucht mindestens 10 Zeichen.", cut.Find(".elkw-field-error").TextContent));

        cut.Find("#konto-passwort").Input("lang-genug-2027");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Passwort ist festgelegt.", cut.Markup, StringComparison.Ordinal));
        var meldung = cut.Find("section.elkw-alert--erfolg");
        Assert.Equal("Gespeichert:", meldung.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Contains(cut.FindAll("h2"), h => h.TextContent == "Passwort ändern");
        Assert.Empty(cut.FindAll(".elkw-field-error"));
        Assert.Equal("lang-genug-2027", JsonDocument.Parse(_server.Anfragen.Last(a => a.Methode == HttpMethod.Put).Koerper!).RootElement.GetProperty("passwort").GetString());
    }

    [Fact]
    public async Task Konto_Abmelden_meldet_ab_verwirft_das_Token_und_fuehrt_zur_Seite_Anmelden()
    {
        KontoServer(hatPasswort: false);
        _server.Bei(HttpMethod.Post, "/api/konten/abmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, "/api/betrieb/fehler", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<Konto>();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), k => k.TextContent == "Abmelden"));

        cut.FindAll("button").Single(k => k.TextContent == "Abmelden").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.False(Sitzung.Angemeldet);
        var tokenAbmelden = _server.Anfragen.Single(a => a.Pfad == "/api/konten/abmelden").Kopfzeilen[Zeltlotse.Api.Contracts.Gemeinsam.Kopfzeilen.Antiforgery];

        // Der nächste ändernde Aufruf holt ein neues Token.
        (await Services.GetRequiredService<HttpClient>().PostAsync("/api/betrieb/fehler", null, Xunit.TestContext.Current.CancellationToken)).Dispose();
        Assert.NotEqual(tokenAbmelden, _server.Anfragen.Last().Kopfzeilen[Zeltlotse.Api.Contracts.Gemeinsam.Kopfzeilen.Antiforgery]);
    }

    [Fact]
    public void Konto_ohne_Sitzung_fuehrt_zur_Seite_Anmelden()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, ""));
        Navigation.NavigateTo("konto");

        Render<Konto>();

        Assert.Equal("http://localhost/anmelden", Navigation.Uri);
    }

    // ---------- Fehler (Aktion) ----------

    private const string FehlerText = "Das hat nicht geklappt. Bitte gleich noch einmal versuchen.";

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData((HttpStatusCode)429)]
    public void Link_anfordern_mit_Serverfehler_zeigt_die_Meldung_und_die_Eingabe_bleibt(HttpStatusCode status)
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => NachgebildeterHandler.Problem(status, "x"));
        var cut = Render<Anmelden>();

        cut.Find("#anmelden-mail").Input("jonas.huettner@example.org");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(FehlerText, cut.Markup, StringComparison.Ordinal));
        var fehler = cut.FindAll("section.elkw-alert--fehler").Single(m => m.TextContent.Contains(FehlerText, StringComparison.Ordinal));
        Assert.Equal("Fehler:", fehler.QuerySelector(".elkw-alert__title")!.TextContent);
        Assert.Equal("alert", fehler.GetAttribute("role"));
        Assert.Equal("jonas.huettner@example.org", cut.Find("#anmelden-mail").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#anmelden-code"));
    }

    [Fact]
    public void Link_anfordern_ohne_Netz_zeigt_die_Meldung()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link", () => throw new HttpRequestException("Kein Netz"));
        var cut = Render<Anmelden>();

        cut.Find("#anmelden-mail").Input("jonas.huettner@example.org");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(FehlerText, cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Fehler_am_Feld_bei_einer_zweiten_Anfrage_hebt_Hinweis_und_Code_der_vorigen_auf()
    {
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/konten/login-link",
            () => NachgebildeterHandler.Json(HttpStatusCode.Accepted, $$"""{"anfrageId":"{{Guid.CreateVersion7()}}"}"""),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"mail":["Das ist keine gültige Mail-Adresse."]}}""", "application/problem+json"));
        var cut = Render<Anmelden>();
        cut.Find("#anmelden-mail").Input("jonas.huettner@example.org");
        cut.Find("form").Submit();
        cut.WaitForElement("#anmelden-code");

        cut.Find("#anmelden-mail").Input("jonas");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Fehler: Das ist keine gültige Mail-Adresse.", cut.Find(".elkw-field-error").TextContent));
        Assert.Empty(cut.FindAll("#anmelden-code"));
        Assert.DoesNotContain("Login-Link ist verschickt.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_einloesen_mit_Serverfehler_zeigt_die_Meldung_und_nicht_ungueltig()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link/einloesen", () => NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "x"));
        var cut = Render<AnmeldenLink>(ps => ps.Add(p => p.Token, "Tk_123"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Dieser Link ist nicht mehr gültig.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Anmelden", cut.Find("button").TextContent);
        Assert.False(Sitzung.Angemeldet);
    }

    [Fact]
    public void Passwort_Anmeldung_mit_Serverfehler_zeigt_die_Meldung_und_die_Mail_bleibt()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/anmelden", () => NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "x"));
        var cut = Render<AnmeldenPasswort>();

        cut.Find("#passwort-mail").Input("jonas.huettner@example.org");
        cut.Find("#passwort-passwort").Input("richtig-richtig");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Mail-Adresse oder Passwort stimmt nicht.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("jonas.huettner@example.org", cut.Find("#passwort-mail").GetAttribute("value"));
    }

    [Fact]
    public void Scheitert_nach_der_Anmeldung_das_Profil_geht_es_trotzdem_zur_Startseite()
    {
        _server.Bei(HttpMethod.Post, "/api/konten/login-link/einloesen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, "/api/ich/profil", () => NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "x"));
        var cut = Render<AnmeldenLink>(ps => ps.Add(p => p.Token, "Tk_123"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", Navigation.Uri));
        Assert.True(Sitzung.Angemeldet);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Konto_laden_mit_Fehler_zeigt_die_Meldung_statt_Wird_geladen(bool netzfehler)
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => netzfehler
            ? throw new TaskCanceledException("Zeitüberschreitung")
            : NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "x"));

        var cut = Render<Konto>();

        cut.WaitForAssertion(() => Assert.Contains(FehlerText, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("[aria-busy=true]"));
        Assert.Equal("Konto", cut.Find("h1").TextContent);
    }

    [Fact]
    public void Konto_Abmelden_mit_Serverfehler_meldet_auf_dem_Geraet_trotzdem_ab()
    {
        KontoServer(hatPasswort: false);
        _server.Bei(HttpMethod.Post, "/api/konten/abmelden", () => NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "x"));
        var cut = Render<Konto>();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), k => k.TextContent == "Abmelden"));

        cut.FindAll("button").Single(k => k.TextContent == "Abmelden").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.False(Sitzung.Angemeldet);
    }
}
