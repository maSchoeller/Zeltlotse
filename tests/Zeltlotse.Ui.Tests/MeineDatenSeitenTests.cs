using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Texte;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 1-7 in der Web-App mit nachgebildetem Server und Browser: Card auf der Konto-Seite, Seite „Zugriffsprotokoll und Meine
/// Daten“ mit Protokoll-Tabelle und Leer-Zustand, Auskunft mit Abschnitten, PDF (Druck) und CSV (BOM, Anführungszeichen,
/// Formel-Schutz), Konto löschen mit Inline-Bestätigung, Rückweg bei alter Anmeldung, und die Card „Löschfristen“.
/// </summary>
public sealed class MeineDatenSeitenTests : BunitContext
{
    private static readonly Guid Jonas = Guid.Parse("0199f700-0000-7000-8000-0000000000a1");
    private static readonly Guid Miriam = Guid.Parse("0199f700-0000-7000-8000-0000000000a2");
    private static readonly Guid Emma = Guid.Parse("0199f700-0000-7000-8000-0000000000a3");
    private static readonly Guid Org = Guid.Parse("0199f700-0000-7000-8000-0000000000b1");

    private readonly NachgebildeterHandler _server = new();
    private readonly NachgebildeterBrowser _browser = new();
    private int _tokens;

    public MeineDatenSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/namen", () => Json(new[] { new { organisationId = Org, name = "Jugendwerk Musterstadt" } }));
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

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private AppSitzung Sitzung => Services.GetRequiredService<AppSitzung>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void Protokoll(params object[] eintraege) =>
        _server.Bei(HttpMethod.Get, "/api/ich/zugriffsprotokoll", () => Json(eintraege));

    private static object Eintrag(Guid betroffen, string? betroffenName, Guid lesend, string? lesendName, string am) => new
    {
        am,
        betroffenePersonId = betroffen,
        betroffenerName = betroffenName,
        lesendePersonId = lesend,
        lesenderName = lesendName,
        organisationId = Org,
        freizeitId = Guid.CreateVersion7(),
        zweck = "nachweiseEingesehen",
    };

    private static DatenauskunftAntwort Auskunft() => new(
        new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.Zero),
        [
            new DatenauskunftAbschnitt("Konto", [new DatenauskunftZeile("Mail-Adresse", "jonas.huettner@example.org")]),
            new DatenauskunftAbschnitt("Profil", [new DatenauskunftZeile("Vorname", "Jonas"), new DatenauskunftZeile("Notiz", "Er sagt \"hallo\"")]),
            new DatenauskunftAbschnitt("Notiz", [new DatenauskunftZeile("Wert", "=SUMME(1)")]),
        ]);

    // ---------- Konto-Seite ----------

    [Fact]
    public void Konto_zeigt_nach_Nachweise_die_Card_Zugriffsprotokoll_und_Meine_Daten_mit_dem_Knopf()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"jonas.huettner@example.org","hatPasswort":false,"erlaubt":[]}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Jonas","nachname":"Hüttner"}"""));

        var cut = Render<Konto>();

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("h2"), h => h.TextContent == "Zugriffsprotokoll und Meine Daten"));
        var titel = cut.FindAll(".elkw-card h2").Select(h => h.TextContent).ToList();
        Assert.Equal(titel.IndexOf("Nachweise") + 1, titel.IndexOf("Zugriffsprotokoll und Meine Daten"));
        var card = cut.FindAll(".elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Zugriffsprotokoll und Meine Daten");
        Assert.Contains("Wer wann Einsicht hatte, Auskunft, Export und Konto löschen.", card.TextContent, StringComparison.Ordinal);
        var knopf = card.QuerySelector("a.elkw-btn")!;
        Assert.Equal("Meine Daten öffnen", knopf.TextContent);
        Assert.Equal("konto/daten", knopf.GetAttribute("href"));
        Assert.Contains("elkw-btn--outline", knopf.ClassList);
        Assert.DoesNotContain("elkw-btn--noarrow", knopf.ClassList);
    }

    // ---------- Zugriffsprotokoll ----------

    [Fact]
    public void Seite_zeigt_Ueberschriften_und_das_Protokoll_als_Tabelle_mit_Organisation_und_Geloeschter_Person()
    {
        Protokoll(
            Eintrag(Jonas, "Jonas Hüttner", Miriam, "Miriam Zeltner", "2026-10-12T08:00:00+00:00"),
            Eintrag(Emma, "Emma Hüttner", Guid.CreateVersion7(), null, "2026-10-01T08:00:00+00:00"));

        var cut = Render<MeineDaten>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Equal("Zugriffsprotokoll und Meine Daten", cut.Find("h1").TextContent);
        Assert.Equal(["Zugriffsprotokoll", "Auskunft und Export", "Konto löschen"], cut.FindAll("h2").Select(h => h.TextContent));
        Assert.Equal(["Wann", "Wer", "Organisation", "Was", "Betrifft"], cut.FindAll("thead th").Select(t => t.TextContent));
        var zeilen = cut.FindAll("tbody tr").Select(z => z.QuerySelectorAll("td").Select(t => t.TextContent.Trim()).ToList()).ToList();
        Assert.Equal(["12.10.2026", "Miriam Zeltner", "Jugendwerk Musterstadt", "Nachweise eingesehen", "Jonas Hüttner"], zeilen[0]);
        Assert.Equal(["01.10.2026", "Gelöschte Person", "Jugendwerk Musterstadt", "Nachweise eingesehen", "Emma Hüttner"], zeilen[1]);
        Assert.Contains(_server.Anfragen, a => a.Pfad == $"/api/ich/organisationen/namen?id={Org}");
    }

    [Fact]
    public void Allergen_Abgleich_der_Kueche_steht_ohne_lesende_Person_im_Protokoll()
    {
        // Story 4-2 (AD-8): Zweck „allergeneFuerKueche“ ohne lesende Person.
        Protokoll(new
        {
            am = "2026-10-12T08:00:00+00:00", betroffenePersonId = Emma, betroffenerName = "Emma Hüttner", lesendePersonId = (Guid?)null,
            lesenderName = (string?)null, organisationId = Org, freizeitId = Guid.CreateVersion7(), zweck = "allergeneFuerKueche",
        });

        var cut = Render<MeineDaten>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Equal(
            ["12.10.2026", "Niemand persönlich (Abgleich)", "Jugendwerk Musterstadt", "Allergen-Abgleich der Küche", "Emma Hüttner"],
            cut.FindAll("tbody td").Select(t => t.TextContent.Trim()));
        Assert.Equal("Allergen-Abgleich der Küche", Anzeige.Zweck(Zeltlotse.Api.Contracts.Personen.ZugriffZweck.AllergeneFuerKueche));
    }

    [Fact]
    public void Leeres_Protokoll_zeigt_den_Leer_Zustand()
    {
        Protokoll();

        var cut = Render<MeineDaten>();

        cut.WaitForAssertion(() => Assert.Equal("Bisher hatte niemand Einsicht.", cut.Find(".elkw-empty p").TextContent));
        Assert.Empty(cut.FindAll("tbody tr"));
    }

    // ---------- Auskunft und Export ----------

    [Fact]
    public void Auskunft_anzeigen_zeigt_je_Abschnitt_eine_Tabelle_und_die_Knoepfe_fuer_PDF_und_CSV()
    {
        Protokoll();
        _server.Bei(HttpMethod.Get, "/api/ich/daten", () => Json(Auskunft()));
        var cut = Render<MeineDaten>();
        cut.WaitForElement(".elkw-empty");
        Assert.Contains("Für Auskunft und Export ist eine frische Anmeldung nötig (höchstens 10 Minuten alt); sonst führt Zeltlotse zuerst zur Anmeldung.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Als PDF speichern");

        cut.FindAll("button").Single(b => b.TextContent == "Auskunft anzeigen").Click();

        cut.WaitForAssertion(() => Assert.Equal(["Konto", "Profil", "Notiz"], cut.FindAll("h3").Select(h => h.TextContent)));
        Assert.Contains(cut.FindAll("thead th"), t => t.TextContent == "Feld");
        Assert.Contains(cut.FindAll("td"), t => t.TextContent == "jonas.huettner@example.org");
        Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Als PDF speichern");
        Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Als CSV herunterladen");
    }

    [Fact]
    public void Als_PDF_speichern_oeffnet_den_Druck_und_CSV_speichert_die_Datei_mit_BOM_Anfuehrungszeichen_und_Formel_Schutz()
    {
        Protokoll();
        _server.Bei(HttpMethod.Get, "/api/ich/daten", () => Json(Auskunft()));
        var cut = Render<MeineDaten>();
        cut.WaitForElement(".elkw-empty");
        cut.FindAll("button").Single(b => b.TextContent == "Auskunft anzeigen").Click();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent == "Als CSV herunterladen"));

        cut.FindAll("button").Single(b => b.TextContent == "Als PDF speichern").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Als CSV herunterladen").Click();

        Assert.Equal(1, _browser.Gedruckt);
        var (name, medientyp, inhalt) = Assert.Single(_browser.Dateien);
        Assert.Equal("zeltlotse-meine-daten-2026-10-12.csv", name);
        Assert.Equal("text/csv", medientyp);
        Assert.Equal([0xEF, 0xBB, 0xBF], inhalt[..3]);
        Assert.Equal(
            "\"Bereich\";\"Feld\";\"Wert\"\r\n"
            + "\"Konto\";\"Mail-Adresse\";\"jonas.huettner@example.org\"\r\n"
            + "\"Profil\";\"Vorname\";\"Jonas\"\r\n"
            + "\"Profil\";\"Notiz\";\"Er sagt \"\"hallo\"\"\"\r\n"
            + "\"Notiz\";\"Wert\";\"'=SUMME(1)\"\r\n",
            Encoding.UTF8.GetString(inhalt[3..]));
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+49 123", "\"'+49 123\"")]
    [InlineData("-5", "\"'-5\"")]
    [InlineData("@SUMME", "\"'@SUMME\"")]
    [InlineData("\t=1", "\"'\t=1\"")]
    [InlineData("\r=1", "\"'\r=1\"")]
    [InlineData("ohne", "\"ohne\"")]
    [InlineData("", "\"\"")]
    public void Csv_Wert_mit_Formel_Schutz_und_verdoppelten_Anfuehrungszeichen(string wert, string erwartet)
    {
        Assert.Equal(erwartet, AuskunftCsv.Wert(wert));
    }

    [Fact]
    public void Auskunft_mit_alter_Anmeldung_fuehrt_zur_Anmeldung_mit_Rueckweg_auf_die_Seite()
    {
        Protokoll();
        _server.Bei(HttpMethod.Get, "/api/ich/daten", () => NachgebildeterHandler.Problem(HttpStatusCode.Unauthorized, "nicht-angemeldet"));
        Navigation.NavigateTo("konto/daten");
        var cut = Render<MeineDaten>();
        cut.WaitForElement(".elkw-empty");

        cut.FindAll("button").Single(b => b.TextContent == "Auskunft anzeigen").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/anmelden", Navigation.Uri, StringComparison.Ordinal));
        Assert.Equal("konto/daten", Sitzung.Rueckweg);
        Assert.DoesNotContain(cut.FindAll(".elkw-alert--fehler"), _ => true);
    }

    // ---------- Konto löschen ----------

    [Fact]
    public void Konto_loeschen_mit_Inline_Bestaetigung_schickt_den_POST_meldet_ab_und_zeigt_Geloescht()
    {
        Protokoll();
        _server.Bei(HttpMethod.Post, "/api/ich/profil/loeschen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        Sitzung.Anmelden("Jonas");
        var cut = Render<MeineDaten>();
        cut.WaitForElement(".elkw-empty");
        Assert.Contains(
            "Löscht Konto, Profil, Nachweise und Zugriffsprotokoll endgültig. Profile von Kindern ohne zweiten Elternteil werden mitgelöscht. Bestätigungen, die Organisationen behalten, bleiben ohne Namen.",
            cut.Markup,
            StringComparison.Ordinal);

        cut.FindAll("button").Single(b => b.TextContent == "Konto löschen").Click();
        Assert.Contains("Konto und alle eigenen Daten endgültig löschen?", cut.Find(".zl-bestaetigung").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Anfragen, a => a.Methode == HttpMethod.Post);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, endgültig löschen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Gelöscht:", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal));
        Assert.Contains("Das Konto ist gelöscht.", cut.Find(".elkw-alert--erfolg").TextContent, StringComparison.Ordinal);
        // Der Weg zurück ins Konto führt nach dem Löschen ins Leere.
        Assert.DoesNotContain(cut.FindAll("a"), l => l.TextContent == "Zurück zum Konto");
        var post = Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post);
        Assert.Equal("/api/ich/profil/loeschen", post.Pfad);
        Assert.True(post.Kopfzeilen.ContainsKey(Kopfzeilen.Antiforgery));
        Assert.False(Sitzung.Angemeldet);
    }

    [Fact]
    public void Konto_loeschen_mit_Serverfehler_zeigt_den_Fehlertext_und_bleibt_angemeldet()
    {
        Protokoll();
        _server.Bei(HttpMethod.Post, "/api/ich/profil/loeschen", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Sitzung.Anmelden("Jonas");
        var cut = Render<MeineDaten>();
        cut.WaitForElement(".elkw-empty");

        cut.FindAll("button").Single(b => b.TextContent == "Konto löschen").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Ja, endgültig löschen").Click();

        cut.WaitForAssertion(() => Assert.Contains(AnmeldungAbschluss.FehlerText, cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.True(Sitzung.Angemeldet);
        Assert.Empty(cut.FindAll(".elkw-alert--erfolg"));
    }

    // ---------- Löschfristen ----------

    [Fact]
    public void Organisation_zeigt_mit_Aktionsschluessel_die_Card_Loeschfristen_mit_zwei_Zahlenfeldern()
    {
        OrganisationServer([Aktionsschluessel.LoeschfristenVerwalten]);
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}/loeschfristen", () => Json(new { version = 4u, freizeitDatenMonate = 12, nachweisBestaetigungMonate = 6 }));

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, Org));

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".elkw-card h2"), h => h.TextContent == "Löschfristen"));
        var card = cut.FindAll(".elkw-card").Single(c => c.QuerySelector("h2")!.TextContent == "Löschfristen");
        var felder = card.QuerySelectorAll("input").ToList();
        Assert.Equal(2, felder.Count);
        Assert.All(felder, f => Assert.Equal("number", f.GetAttribute("type")));
        Assert.Equal(["12", "6"], felder.Select(f => f.GetAttribute("value")));
        var labels = card.QuerySelectorAll("label").Select(l => l.TextContent).ToList();
        Assert.Contains("Daten einer Freizeit (Monate nach Freizeitende)", labels);
        Assert.Contains("Abgelaufene Nachweis-Bestätigungen (Monate nach Ablauf)", labels);
    }

    [Fact]
    public void Loeschfristen_speichern_per_Autosave_an_den_Pfad_der_Organisation()
    {
        OrganisationServer([Aktionsschluessel.LoeschfristenVerwalten]);
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}/loeschfristen", () => Json(new { version = 4u, freizeitDatenMonate = 12, nachweisBestaetigungMonate = 12 }));
        _server.Bei(HttpMethod.Patch, $"/api/organisationen/{Org}/loeschfristen", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":5,"freizeitDatenMonate":"24"}"""));
        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, Org));
        var feld = cut.WaitForElement("input#loeschfristen-freizeit");

        feld.Focus();
        feld.Input("24");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("#loeschfristen-freizeit").ParentElement!.TextContent, StringComparison.Ordinal));
        var patch = JsonDocument.Parse(_server.Anfragen.Single(a => a.Methode == HttpMethod.Patch).Koerper!).RootElement;
        Assert.Equal((4u, "12", "24"), (patch.GetProperty("version").GetUInt32(), patch.GetProperty("alterWert").GetString(), patch.GetProperty("freizeitDatenMonate").GetString()));
    }

    [Fact]
    public void Organisation_ohne_Aktionsschluessel_zeigt_keine_Loeschfristen_und_laedt_sie_nicht()
    {
        OrganisationServer([]);

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, Org));

        cut.WaitForAssertion(() => Assert.Equal("Jugendwerk Musterstadt", cut.Find("h1").TextContent));
        Assert.DoesNotContain(cut.FindAll(".elkw-card h2"), h => h.TextContent == "Löschfristen");
        Assert.DoesNotContain(_server.Anfragen, a => a.Pfad.EndsWith("/loeschfristen", StringComparison.Ordinal));
    }

    private void OrganisationServer(string[] erlaubt)
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}", () => Json(new { id = Org, name = "Jugendwerk Musterstadt", rollen = new[] { "OrgAdmin" }, erlaubt }));
    }
}
