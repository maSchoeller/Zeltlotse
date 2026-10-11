using System.Net;
using System.Text.Json;
using Bunit;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 1-1 in den App-Bausteinen: Sitzungs-Handler (401 → <see cref="ISitzung"/>), Antiforgery-Reset nach An- und
/// Abmelden, die Clients von Konten und Personen sowie die Bausteine Konto-Link und Formular.
/// </summary>
public sealed class SitzungUndKontenTests : BunitContext
{
    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    // ---------- Sitzungs-Handler ----------

    private sealed class ZaehlendeSitzung : ISitzung
    {
        public int Abgelaufen { get; private set; }

        public ValueTask AbgelaufenAsync()
        {
            Abgelaufen++;
            return ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData("GET", "/api/ich/profil", HttpStatusCode.Unauthorized, 1)]
    [InlineData("PATCH", "/api/ich/profil", HttpStatusCode.Unauthorized, 1)]
    [InlineData("POST", "/api/konten/abmelden", HttpStatusCode.Unauthorized, 1)]
    [InlineData("POST", "/api/konten/anmelden", HttpStatusCode.Unauthorized, 0)]
    [InlineData("GET", "/api/ich/profil", HttpStatusCode.OK, 0)]
    [InlineData("GET", "/api/ich/profil", HttpStatusCode.Forbidden, 0)]
    [InlineData("GET", "/api/ich/profil", HttpStatusCode.NotFound, 0)]
    public async Task Sitzungs_Handler_meldet_nur_401_ausser_beim_Passwort_Login(string methode, string pfad, HttpStatusCode status, int erwartet)
    {
        var sitzung = new ZaehlendeSitzung();
        var server = new NachgebildeterHandler().Bei(_ => true, _ => Task.FromResult(new HttpResponseMessage(status)));
        using var client = new HttpClient(new SitzungsHandler(sitzung) { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/app/") };

        using var antwort = await client.SendAsync(new HttpRequestMessage(new HttpMethod(methode), pfad), Abbruch);

        Assert.Equal(status, antwort.StatusCode);
        Assert.Equal(erwartet, sitzung.Abgelaufen);
    }

    // ---------- Antiforgery-Reset ----------

    [Fact]
    public async Task Nach_TokenVerwerfen_holt_der_naechste_aendernde_Aufruf_ein_neues_Token()
    {
        var tokens = 0;
        var server = new NachgebildeterHandler()
            .Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++tokens}}"}"""))
            .Bei(r => r.Method == HttpMethod.Post, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var handler = new AntiforgeryHandler { InnerHandler = server };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/app/") };

        (await client.PostAsync("/api/eins", null, Abbruch)).Dispose();
        (await client.PostAsync("/api/zwei", null, Abbruch)).Dispose();
        handler.TokenVerwerfen();
        (await client.PostAsync("/api/drei", null, Abbruch)).Dispose();

        var posts = server.Anfragen.Where(a => a.Methode == HttpMethod.Post).Select(a => a.Kopfzeilen[Kopfzeilen.Antiforgery]).ToList();
        Assert.Equal(["token-1", "token-1", "token-2"], posts);
    }

    // ---------- Konten-Client ----------

    [Fact]
    public async Task Login_Link_anfordern_liefert_die_Kennung_oder_den_Fehler_am_Feld_mail()
    {
        var id = Guid.CreateVersion7();
        var server = new NachgebildeterHandler().BeiDerReihe(
            HttpMethod.Post,
            "/api/konten/login-link",
            () => NachgebildeterHandler.Json(HttpStatusCode.Accepted, $$"""{"anfrageId":"{{id}}"}"""),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"mail":["Das ist keine gültige Mail-Adresse."]}}""", "application/problem+json"));
        var konten = new KontenApiClient(server.Client());

        var gut = await konten.LoginLinkAnfordernAsync("paul@example.org", Abbruch);
        var schlecht = await konten.LoginLinkAnfordernAsync("paul", Abbruch);

        Assert.Equal(new LoginLinkErgebnis(id, null), gut);
        Assert.Equal(new LoginLinkErgebnis(null, "Das ist keine gültige Mail-Adresse."), schlecht);
        Assert.Equal("paul@example.org", JsonDocument.Parse(server.Anfragen[0].Koerper!).RootElement.GetProperty("mail").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Link_und_Code_einloesen_ergeben_wahr_bei_204_und_falsch_bei_404(HttpStatusCode status, bool erwartet)
    {
        var server = new NachgebildeterHandler().Bei(r => r.Method == HttpMethod.Post, _ => Task.FromResult(new HttpResponseMessage(status)));
        var konten = new KontenApiClient(server.Client());
        var anfrageId = Guid.CreateVersion7();

        Assert.Equal(erwartet, await konten.LoginLinkEinloesenAsync("token", Abbruch));
        Assert.Equal(erwartet, await konten.LoginCodeEinloesenAsync(anfrageId, " 123456 ", Abbruch));

        Assert.Equal("/api/konten/login-link/einloesen", server.Anfragen[0].Pfad);
        Assert.Equal("token", JsonDocument.Parse(server.Anfragen[0].Koerper!).RootElement.GetProperty("token").GetString());
        Assert.Equal("/api/konten/login-code/einloesen", server.Anfragen[1].Pfad);
        var code = JsonDocument.Parse(server.Anfragen[1].Koerper!).RootElement;
        Assert.Equal(anfrageId, code.GetProperty("anfrageId").GetGuid());
        Assert.Equal("123456", code.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task Mit_Passwort_anmelden_ergibt_wahr_bei_204_und_falsch_bei_401(HttpStatusCode status, bool erwartet)
    {
        var server = new NachgebildeterHandler().Bei(HttpMethod.Post, "/api/konten/anmelden", () => new HttpResponseMessage(status));

        Assert.Equal(erwartet, await new KontenApiClient(server.Client()).MitPasswortAnmeldenAsync("a@example.org", "geheim-geheim", Abbruch));
        var koerper = JsonDocument.Parse(server.Anfragen[0].Koerper!).RootElement;
        Assert.Equal("a@example.org", koerper.GetProperty("mail").GetString());
        Assert.Equal("geheim-geheim", koerper.GetProperty("passwort").GetString());
    }

    [Fact]
    public async Task Andere_Fehler_beim_Anmelden_werfen()
    {
        var server = new NachgebildeterHandler().Bei(r => true, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var konten = new KontenApiClient(server.Client());

        await Assert.ThrowsAsync<HttpRequestException>(() => konten.LoginLinkEinloesenAsync("t", Abbruch));
        await Assert.ThrowsAsync<HttpRequestException>(() => konten.MitPasswortAnmeldenAsync("a", "b", Abbruch));
        await Assert.ThrowsAsync<HttpRequestException>(() => konten.LoginLinkAnfordernAsync("a@example.org", Abbruch));
    }

    [Fact]
    public async Task Konto_laden_Passwort_festlegen_und_Abmelden()
    {
        var server = new NachgebildeterHandler()
            .Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"a@example.org","hatPasswort":true}"""))
            .BeiDerReihe(
                HttpMethod.Put,
                "/api/ich/konto/passwort",
                () => new HttpResponseMessage(HttpStatusCode.NoContent),
                () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"status":400,"code":"validierung","errors":{"passwort":["Das Passwort braucht mindestens 10 Zeichen."]}}""", "application/problem+json"))
            .Bei(HttpMethod.Post, "/api/konten/abmelden", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var konten = new KontenApiClient(server.Client());

        var konto = await konten.KontoLadenAsync(Abbruch);
        var gut = await konten.PasswortFestlegenAsync("lang-genug-1", Abbruch);
        var kurz = await konten.PasswortFestlegenAsync("kurz", Abbruch);
        await konten.AbmeldenAsync(Abbruch);

        Assert.Equal(new Zeltlotse.Api.Contracts.Konten.KontoAntwort("a@example.org", true), konto);
        Assert.Null(gut);
        Assert.Equal("Das Passwort braucht mindestens 10 Zeichen.", kurz);
        Assert.Contains(server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == "/api/konten/abmelden");
    }

    // ---------- Personen-Client ----------

    [Fact]
    public async Task Profil_laden_ist_null_bei_404_und_Laden_oder_Anlegen_legt_dann_an()
    {
        var server = new NachgebildeterHandler()
            .Bei(HttpMethod.Get, "/api/ich/profil", () => new HttpResponseMessage(HttpStatusCode.NotFound))
            .Bei(HttpMethod.Post, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":7,"vorname":"Jonas","nachname":null}"""));
        var personen = new PersonenApiClient(server.Client());

        Assert.Null(await personen.ProfilLadenAsync(Abbruch));
        var profil = await personen.ProfilLadenOderAnlegenAsync(Abbruch);

        Assert.Equal(new Zeltlotse.Api.Contracts.Personen.ProfilAntwort(7, "Jonas", null), profil);
        Assert.Equal(["GET /api/ich/profil", "GET /api/ich/profil", "POST /api/ich/profil"], server.Anfragen.Select(a => $"{a.Methode} {a.Pfad}"));
    }

    // ---------- Bausteine ----------

    [Fact]
    public void App_Leiste_zeigt_den_Konto_Link_nur_mit_Ziel_und_sonst_Konto_als_Text()
    {
        var ohne = Render<AppLeiste>();
        var mitName = Render<AppLeiste>(ps => ps.Add(p => p.KontoHref, "konto").Add(p => p.KontoText, "Jonas").Add(p => p.NotfallHref, "notfall"));
        var ohneName = Render<AppLeiste>(ps => ps.Add(p => p.KontoHref, "konto").Add(p => p.KontoText, " "));

        Assert.Empty(ohne.FindAll(".zl-konto-link"));
        var link = mitName.Find("header a.zl-konto-link");
        Assert.Equal("Jonas", link.TextContent);
        Assert.Equal("konto", link.GetAttribute("href"));
        Assert.NotNull(mitName.Find("a.zl-notfall-eintrag"));
        Assert.Equal("Konto", ohneName.Find("a.zl-konto-link").TextContent);
    }

    [Fact]
    public void Formular_ist_ohne_Browser_Pruefung_und_meldet_das_Absenden()
    {
        var abgesendet = 0;
        var cut = Render<Formular>(ps => ps
            .Add(p => p.Absenden, () => abgesendet++)
            .Add(p => p.ChildContent, "<button type=\"submit\">Los</button>")
            .AddUnmatched("aria-label", "Anmelden"));

        var formular = cut.Find("form");
        Assert.True(formular.HasAttribute("novalidate"));
        Assert.Contains("zl-formular", formular.ClassList);
        Assert.Equal("Anmelden", formular.GetAttribute("aria-label"));

        formular.Submit();

        Assert.Equal(1, abgesendet);
    }
}
