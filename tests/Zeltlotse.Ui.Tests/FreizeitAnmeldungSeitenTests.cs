using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-2, „Mit Konto anmelden“ in der Web-App mit nachgebildetem Server und der echten Handler-Kette: Auswahl (Kinder unter 18
/// und „Selbst“, bei Mitarbeitenden nur die eigene Person), Teilnahmebedingungen, Haken, Fehler am Feld, 404, 409, Erfolg und die
/// Anmeldung mit Rückweg; dazu die Client-Methoden der Anmeldung.
/// </summary>
public sealed class FreizeitAnmeldungSeitenTests : BunitContext
{
    private const string Token = "Anmeldelink_Tk";
    private static readonly Guid Paul = Guid.Parse("0199e000-0000-7000-8000-0000000000a1");
    private static readonly Guid Emma = Guid.Parse("0199e000-0000-7000-8000-0000000000a2");

    private readonly NachgebildeterHandler _server = new();
    private bool _ohneSitzung;
    private int _tokens;

    public FreizeitAnmeldungSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => _ohneSitzung ? NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, "") : Json(new
        {
            eltern = new[] { new { personId = Guid.CreateVersion7(), vorname = "Katrin", nachname = "Muster" } },
            kinder = new object[]
            {
                new { personId = Paul, version = 7u, vorname = "Paul", nachname = "Muster", geburtsdatum = "2008-03-14", volljaehrig = true, uebergabe = (object?)null },
                new { personId = Emma, version = 8u, vorname = "Emma", nachname = "Muster", geburtsdatum = "2017-09-02", volljaehrig = false, uebergabe = (object?)null },
            },
            einladungen = Array.Empty<object>(),
        }));
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

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private string Koerper(string pfad) => _server.Anfragen.Last(a => a.Pfad == pfad && a.Methode != HttpMethod.Get).Koerper!;

    private void Freizeitseite(string art, string? teilnahmebedingungen = null, bool ausgebucht = false) =>
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/oeffentlich/{Token}", () => Json(new
        {
            organisation = "Jugendwerk Musterstadt",
            name = "Zeltlager Sommer 2027",
            ersterTag = "2027-08-02",
            letzterTag = "2027-08-13",
            ort = (string?)null,
            beschreibung = (string?)null,
            art,
            ausgebucht,
            team = Array.Empty<object>(),
            teilnahmebedingungen,
        }));

    private IRenderedComponent<AnmeldungMitKonto> Seite()
    {
        var cut = Render<AnmeldungMitKonto>(ps => ps.Add(p => p.Token, Token));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));
        return cut;
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_spricht_die_Anmelde_Endpunkte_mit_den_Vertragsformen_an()
    {
        _server.BeiDerReihe(
            HttpMethod.Post, "/api/ich/anmeldungen",
            () => Json(new { anmeldungId = Emma }, HttpStatusCode.Created),
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"teilnahmebedingungenBestaetigt":["Teilnahmebedingungen nicht bestätigt."]}}""", "application/problem+json"),
            () => new HttpResponseMessage(HttpStatusCode.NotFound),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        _server.Bei(HttpMethod.Post, "/api/freizeiten/oeffentlich/Tk/anmeldungen", () => Json(new { anmeldungId = Paul }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Get, "/api/ich/anmeldungen", () => Json(new[]
        {
            new { anmeldungId = Paul, personId = Emma, freizeitName = "Z", ersterTag = "2027-08-02", letzterTag = "2027-08-13", art = "Teilnehmende", status = "Eingegangen" },
        }));
        _server.Bei(HttpMethod.Get, "/api/freizeiten/gast/Gt", () => Json(new
        {
            gueltigBis = "2027-09-12T22:00:00+00:00",
            anmeldungen = new[] { new { vorname = "Lina", freizeitName = "Z", ersterTag = "2027-08-02", letzterTag = "2027-08-13", status = "Eingegangen" } },
        }));
        var client = Services.GetRequiredService<FreizeitenApiClient>();

        var angelegt = await client.AnmeldenAsync(Token, Emma, true, Abbruch);
        var fehler = await client.AnmeldenAsync(Token, null, false, Abbruch);
        var ungueltig = await client.AnmeldenAsync(Token, null, true, Abbruch);
        var doppelt = await client.AnmeldenAsync(Token, null, true, Abbruch);
        var gast = await client.GastAnmeldenAsync("Tk", "Gt", Emma, true, Abbruch);
        var meine = await client.MeineAnmeldungenLadenAsync(Abbruch);
        var gastLink = await client.GastLinkLadenAsync("Gt", Abbruch);

        Assert.Equal(Emma, angelegt.AnmeldungId);
        Assert.Equal("Teilnahmebedingungen nicht bestätigt.", fehler.Fehler["teilnahmebedingungenBestaetigt"]);
        Assert.True(ungueltig.NichtGefunden);
        Assert.True(doppelt.SchonAngemeldet);
        Assert.Equal(Paul, gast.AnmeldungId);
        Assert.Equal($$"""{"anmeldelinkToken":"{{Token}}","personId":null,"teilnahmebedingungenBestaetigt":true}""", Koerper("/api/ich/anmeldungen"));
        Assert.Equal($$"""{"gastToken":"Gt","kindPersonId":"{{Emma}}","teilnahmebedingungenBestaetigt":true}""", Koerper("/api/freizeiten/oeffentlich/Tk/anmeldungen"));
        Assert.Equal((Emma, Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus.Eingegangen), (Assert.Single(meine).PersonId, meine[0].Status));
        Assert.Equal("Lina", Assert.Single(gastLink!.Anmeldungen).Vorname);
        Assert.Null(await client.GastLinkLadenAsync("unbekannt", Abbruch));
    }

    // ---------- Seite ----------

    [Fact]
    public void Teilnehmende_waehlen_ein_Kind_unter_18_oder_sich_selbst_und_sehen_die_Teilnahmebedingungen()
    {
        Freizeitseite("Teilnehmende", "Die Hausordnung gilt.\n\nTaschengeld höchstens 20 Euro.");

        var cut = Seite();

        Assert.Equal("Anmeldung", cut.Find("h1").TextContent);
        Assert.Contains("Freizeit: Zeltlager Sommer 2027, 2. bis 13. August 2027", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(["Emma Muster", "Selbst"], cut.FindAll("fieldset label").Select(l => l.TextContent.Trim()));
        Assert.DoesNotContain("Paul", cut.Find("fieldset").TextContent, StringComparison.Ordinal);
        Assert.Equal(["Die Hausordnung gilt.", "Taschengeld höchstens 20 Euro."], cut.FindAll(".elkw-card p").Select(p => p.TextContent));
        Assert.Equal("Teilnahmebedingungen gelesen und bestätigt *", cut.Find("label.elkw-choice:has(input[type=checkbox])").TextContent.Trim());
        Assert.Equal("Anmeldung absenden", cut.Find("button[type=submit]").TextContent);
        Assert.DoesNotContain("Ausgebucht", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Auswahl_fehlt_die_Person_ohne_Haken_meldet_der_Server_den_Fehler_am_Feld()
    {
        Freizeitseite("Teilnehmende");
        _server.Bei(HttpMethod.Post, "/api/ich/anmeldungen", () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"errors":{"teilnahmebedingungenBestaetigt":["Teilnahmebedingungen nicht bestätigt."]}}""", "application/problem+json"));
        var cut = Seite();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Fehler: Person fehlt.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad == "/api/ich/anmeldungen");

        cut.FindAll("fieldset input[type=radio]")[0].Change(Emma.ToString());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Fehler: Teilnahmebedingungen nicht bestätigt.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Fehler: Person fehlt.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Kind_mit_Haken_absenden_zeigt_Anmeldung_eingegangen_und_den_Weg_zur_Familienuebersicht()
    {
        Freizeitseite("Teilnehmende", ausgebucht: true);
        _server.Bei(HttpMethod.Post, "/api/ich/anmeldungen", () => Json(new { anmeldungId = Guid.CreateVersion7() }, HttpStatusCode.Created));
        var cut = Seite();
        Assert.Contains("Anmelden ist trotzdem möglich, eine Zusage gibt es erst, wenn ein Platz frei wird.", cut.Markup, StringComparison.Ordinal);

        cut.FindAll("fieldset input[type=radio]")[0].Change(Emma.ToString());
        cut.Find("input[type=checkbox]").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Anmeldung eingegangen:", cut.Find(".elkw-alert--erfolg .elkw-alert__title").TextContent));
        Assert.Contains("Die Leitung meldet sich in Kürze.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("familie", cut.FindAll("a").Single(a => a.TextContent == "Zur Familienübersicht").GetAttribute("href"));
        Assert.Equal($$"""{"anmeldelinkToken":"{{Token}}","personId":"{{Emma}}","teilnahmebedingungenBestaetigt":true}""", Koerper("/api/ich/anmeldungen"));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Mitarbeitende_melden_sich_selbst_an_ohne_Auswahl()
    {
        Freizeitseite("Mitarbeitende", ausgebucht: true);
        var anmeldungId = Guid.CreateVersion7();
        _server.Bei(HttpMethod.Post, "/api/ich/anmeldungen", () => Json(new { anmeldungId }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => Json(new { version = 1u, vorname = "Jonas", nachname = "Hüttner" }));
        var cut = Seite();

        Assert.Empty(cut.FindAll("fieldset"));
        Assert.Contains("Angemeldet wird die eigene Person", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Ausgebucht", cut.Markup, StringComparison.Ordinal);
        cut.Find("input[type=checkbox]").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Anmeldung eingegangen:", cut.Markup, StringComparison.Ordinal));
        Assert.Equal($$"""{"anmeldelinkToken":"{{Token}}","personId":null,"teilnahmebedingungenBestaetigt":true}""", Koerper("/api/ich/anmeldungen"));
        // Story 2-4: der Weg zum eigenen Freizeitpass mit dem Vornamen aus dem Profil.
        cut.WaitForAssertion(() => Assert.Equal(
            $"anmeldungen/{anmeldungId}/freizeitpass",
            cut.FindAll("a").Single(a => a.TextContent.Contains("Freizeitpass für Jonas ausfüllen", StringComparison.Ordinal)).GetAttribute("href")));
    }

    /// <summary>Story 3-8: Die Freizeit ist gelaufen (409 freizeit-abgeschlossen): Hinweis „Diese Freizeit ist zu Ende.“, kein „schon angemeldet“.</summary>
    [Fact]
    public void Abgeschlossene_Freizeit_zeigt_Diese_Freizeit_ist_zu_Ende()
    {
        Freizeitseite("Mitarbeitende");
        _server.Bei(HttpMethod.Post, "/api/ich/anmeldungen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "freizeit-abgeschlossen"));
        var cut = Seite();

        cut.Find("input[type=checkbox]").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Diese Freizeit ist zu Ende.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("besteht schon", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Anmeldung eingegangen", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Doppelt_angemeldet_zeigt_den_Hinweis_mit_dem_Vornamen()
    {
        Freizeitseite("Teilnehmende");
        _server.Bei(HttpMethod.Post, "/api/ich/anmeldungen", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var cut = Seite();

        cut.FindAll("fieldset input[type=radio]")[0].Change(Emma.ToString());
        cut.Find("input[type=checkbox]").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Emma ist für diese Freizeit schon angemeldet.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Hinweis:", cut.Find(".elkw-alert--warnung .elkw-alert__title").TextContent);
    }

    [Fact]
    public void Ein_ungueltiger_Anmeldelink_zeigt_den_Hinweis_ohne_Formular()
    {
        var cut = Render<AnmeldungMitKonto>(ps => ps.Add(p => p.Token, Token));

        cut.WaitForAssertion(() => Assert.Contains("Dieser Anmeldelink ist nicht mehr gültig.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Ohne_Sitzung_fuehrt_die_Seite_zur_Anmeldung_mit_Rueckweg()
    {
        _ohneSitzung = true;
        Freizeitseite("Teilnehmende");
        Navigation.NavigateTo($"anmeldung/{Token}");

        var cut = Render<AnmeldungMitKonto>(ps => ps.Add(p => p.Token, Token));

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/anmelden", Navigation.Uri));
        Assert.Equal($"anmeldung/{Token}", Services.GetRequiredService<AppSitzung>().Rueckweg);
    }
}
