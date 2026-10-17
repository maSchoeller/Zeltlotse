using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Personen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Autosave;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 2-4 in der Web-App mit nachgebildetem Server: die Bausteine Auswahlgruppe (statisch tauglich) und Autosave-Auswahlgruppe
/// (gespeichert, Rückgängig, Konflikt, gescheitert), der Client, die Seite „Freizeitpass“ (nur die Fragen der Freizeit je
/// Antworttyp, „Stimmt das noch?“ mit „Stimmt“, Leer-Zustand, nicht verfügbar), die Card „Freizeitpass“ in den Einstellungen, der
/// Hinweis „Freizeitpass prüfen“ in der Familienübersicht und der Weg zum Pass nach der Anmeldung mit Konto.
/// </summary>
public sealed class FreizeitpassSeitenTests : BunitContext
{
    private static readonly Guid Emma = Guid.Parse("0199e400-0000-7000-8000-0000000000a2");
    private static readonly Guid Katrin = Guid.Parse("0199e400-0000-7000-8000-0000000000b1");
    private static readonly Guid Anmeldung = Guid.Parse("0199e400-0000-7000-8000-0000000000c1");
    private static readonly Guid FreizeitId = Guid.Parse("0199e400-0000-7000-8000-0000000000f1");

    private readonly NachgebildeterHandler _server = new();
    private object[] _anmeldungen = [];
    private int _tokens;

    public FreizeitpassSeitenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _server.Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}"}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/anmeldungen", () => Json(_anmeldungen));
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

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private Aufgezeichnet Letzte(HttpMethod methode, string pfad) => _server.Anfragen.Last(a => a.Methode == methode && a.Pfad == pfad);

    private static readonly IReadOnlyList<AuswahlOption> Allergene =
        [new("eier", "Eier"), new("milch", "Milch (inkl. Laktose)"), new("senf", "Senf")];

    // ---------- Auswahlgruppe ----------

    [Fact]
    public void Auswahlgruppe_ist_statisch_tauglich_mit_Kaestchen_oder_Optionsknoepfen_Legende_und_Fehler()
    {
        Assert.Empty(typeof(Auswahlgruppe).GetCustomAttributes(typeof(NurInteraktivAttribute), false));
        Assert.DoesNotContain(typeof(Auswahlgruppe).GetProperties(), p => p.PropertyType.Name.StartsWith("EventCallback", StringComparison.Ordinal));
        Assert.NotNull(typeof(AutosaveAuswahlgruppe).GetCustomAttributes(typeof(NurInteraktivAttribute), false).SingleOrDefault());

        var mehrfach = Render<Auswahlgruppe>(ps => ps
            .Add(p => p.Legende, "Allergien")
            .Add(p => p.Id, "allergien")
            .Add(p => p.Name, "a.allergien")
            .Add(p => p.Optionen, Allergene)
            .Add(p => p.Werte, ["milch", "senf"])
            .Add(p => p.Fehler, "Unbekannte Auswahl."));

        Assert.Equal("Allergien", mehrfach.Find("fieldset > legend").TextContent);
        var kaestchen = mehrfach.FindAll("input[type=checkbox]");
        Assert.Equal(["eier", "milch", "senf"], kaestchen.Select(k => k.GetAttribute("value")));
        Assert.Equal([false, true, true], kaestchen.Select(k => k.HasAttribute("checked")));
        Assert.All(kaestchen, k => Assert.Equal("a.allergien", k.GetAttribute("name")));
        Assert.Equal("allergien-milch", kaestchen[1].GetAttribute("id"));
        Assert.Equal("Milch (inkl. Laktose)", kaestchen[1].ParentElement!.TextContent.Trim());
        Assert.Equal("Fehler: Unbekannte Auswahl.", mehrfach.Find(".elkw-field-error").TextContent);
        Assert.Equal("allergien-fehler", mehrfach.Find("fieldset").GetAttribute("aria-describedby"));

        var einfach = Render<Auswahlgruppe>(ps => ps
            .Add(p => p.Legende, "Kann schwimmen")
            .Add(p => p.Optionen, [new AuswahlOption("ja", "Ja"), new AuswahlOption("nein", "Nein")])
            .Add(p => p.Mehrfach, false)
            .Add(p => p.Werte, ["nein"]));
        var knoepfe = einfach.FindAll("input[type=radio]");
        Assert.Equal(2, knoepfe.Count);
        Assert.Equal(knoepfe[0].GetAttribute("name"), knoepfe[1].GetAttribute("name"));
        Assert.False(string.IsNullOrEmpty(knoepfe[0].GetAttribute("name")));
        Assert.True(knoepfe[1].HasAttribute("checked"));
    }

    // ---------- Autosave-Auswahlgruppe ----------

    private IRenderedComponent<AutosaveAuswahlgruppe> AutosaveGruppe(string pfad, string? wert, bool mehrfach = true) =>
        Render<AutosaveAuswahlgruppe>(ps => ps
            .Add(p => p.Legende, "Allergien")
            .Add(p => p.Feldname, "wert")
            .Add(p => p.Pfad, pfad)
            .Add(p => p.Optionen, mehrfach ? Allergene : [new AuswahlOption("ja", "Ja"), new AuswahlOption("nein", "Nein")])
            .Add(p => p.Mehrfach, mehrfach)
            .Add(p => p.Wert, wert)
            .Add(p => p.Version, 4u));

    [Fact]
    public void Autosave_Auswahlgruppe_sendet_die_Auswahl_als_Komma_Liste_in_Optionsreihenfolge_mit_Rueckgaengig()
    {
        const string pfad = "/api/ich/freizeitpass/x/antworten/allergien";
        _server.BeiDerReihe(
            HttpMethod.Patch,
            pfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":5,"wert":"eier,milch"}"""),
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":6,"wert":"milch"}"""));
        var cut = AutosaveGruppe(pfad, "milch");

        cut.Find("input[value=eier]").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("[role=status]").TextContent, StringComparison.Ordinal));
        Assert.Equal("""{"version":4,"alterWert":"milch","wert":"eier,milch"}""", Letzte(HttpMethod.Patch, pfad).Koerper);
        Assert.True(cut.Find("input[value=eier]").HasAttribute("checked"));

        cut.FindAll("button").Single(b => b.TextContent == "Rückgängig").Click();

        cut.WaitForAssertion(() => Assert.False(cut.Find("input[value=eier]").HasAttribute("checked")));
        Assert.Equal("""{"version":5,"alterWert":"eier,milch","wert":"milch"}""", Letzte(HttpMethod.Patch, pfad).Koerper);
    }

    [Fact]
    public void Autosave_Auswahlgruppe_meldet_jedes_gelungene_Speichern_mit_dem_gespeicherten_Wert_ein_Konflikt_nicht()
    {
        const string pfad = "/api/freizeiten/x/kueche/rezepte/r/zutaten/z";
        _server.BeiDerReihe(
            HttpMethod.Patch,
            pfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":5,"wert":"eier,milch"}"""),
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":6,"wert":"milch"}"""),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var gemeldet = new List<string?>();
        var cut = Render<AutosaveAuswahlgruppe>(ps => ps
            .Add(p => p.Legende, "Allergene")
            .Add(p => p.Feldname, "wert")
            .Add(p => p.Pfad, pfad)
            .Add(p => p.Optionen, Allergene)
            .Add(p => p.Wert, "milch")
            .Add(p => p.Version, 4u)
            .Add(p => p.Gespeichert, (string? wert) => gemeldet.Add(wert)));

        cut.Find("input[value=eier]").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(["eier,milch"], gemeldet));
        cut.FindAll("button").Single(b => b.TextContent == "Rückgängig").Click();
        cut.WaitForAssertion(() => Assert.Equal(["eier,milch", "milch"], gemeldet));
        cut.Find("input[value=eier]").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Inzwischen geändert", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(["eier,milch", "milch"], gemeldet);
    }

    [Fact]
    public void Autosave_Auswahlgruppe_mit_Optionsknoepfen_waehlt_genau_einen_Wert_und_ohne_Auswahl_wird_null_gesendet()
    {
        const string pfad = "/api/ich/freizeitpass/x/antworten/schwimmen";
        _server.Bei(HttpMethod.Patch, pfad, () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":5,"wert":"nein"}"""));
        var einfach = AutosaveGruppe(pfad, "ja", mehrfach: false);

        einfach.Find("input[value=nein]").Change(true);

        einfach.WaitForAssertion(() => Assert.Equal("""{"version":4,"alterWert":"ja","wert":"nein"}""", Letzte(HttpMethod.Patch, pfad).Koerper));

        const string allergien = "/api/ich/freizeitpass/x/antworten/allergien";
        _server.Bei(HttpMethod.Patch, allergien, () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":5,"wert":null}"""));
        var mehrfach = AutosaveGruppe(allergien, "milch");
        mehrfach.Find("input[value=milch]").Change(false);
        mehrfach.WaitForAssertion(() => Assert.Equal("""{"version":4,"alterWert":"milch","wert":null}""", Letzte(HttpMethod.Patch, allergien).Koerper));
    }

    [Fact]
    public void Autosave_Auswahlgruppe_zeigt_Konflikt_und_Nicht_gespeichert_mit_Erneut_versuchen()
    {
        const string pfad = "/api/ich/freizeitpass/x/antworten/allergien";
        _server.BeiDerReihe(
            HttpMethod.Patch,
            pfad,
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":5,"wert":"milch,senf"}"""));
        var cut = AutosaveGruppe(pfad, "milch");

        cut.Find("input[value=senf]").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("Inzwischen geändert – neu laden?", cut.Markup, StringComparison.Ordinal));
        cut.Find("input[value=eier]").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("Nicht gespeichert.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.True(cut.Find("input[value=eier]").HasAttribute("checked"));

        cut.FindAll("button").Single(b => b.TextContent == "Erneut versuchen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("[role=status]").TextContent, StringComparison.Ordinal));
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_spricht_die_Freizeitpass_Endpunkte_mit_den_Vertragsformen_an()
    {
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(Pass()));
        _server.Bei(HttpMethod.Post, $"/api/ich/freizeitpass/{Emma}/antworten/schwimmen/bestaetigen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Get, "/api/personen/oeffentlich/gaeste/Tk_1/freizeitpass/" + Emma, () => Json(Pass()));
        _server.Bei(HttpMethod.Post, "/api/personen/oeffentlich/gaeste/Tk_1/freizeitpass/" + Emma, () => NachgebildeterHandler.Json(
            HttpStatusCode.BadRequest, """{"code":"validierung","errors":{"schwimmen.wert":["Nur „ja“ oder „nein“."]}}""", "application/problem+json"));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/fragen", () => Json(new
        {
            version = 3u, teilnehmende = "allergien,schwimmen", mitarbeitende = (string?)null,
            katalog = new[] { new { kennung = "allergien", frage = "Allergien und Unverträglichkeiten" } },
        }));
        var personen = Services.GetRequiredService<PersonenApiClient>();
        var freizeiten = Services.GetRequiredService<FreizeitenApiClient>();

        var pass = await personen.FreizeitpassLadenAsync(Emma, Abbruch);
        var stimmt = await personen.FreizeitpassBestaetigenAsync(Emma, "schwimmen", Abbruch);
        var ohne = await personen.FreizeitpassBestaetigenAsync(Emma, "tetanus", Abbruch);
        var gast = await personen.GastFreizeitpassLadenAsync("Tk_1", Emma, Abbruch);
        var speichern = await personen.GastFreizeitpassSpeichernAsync("Tk_1", Emma, new FreizeitpassFormular([new("schwimmen", "vielleicht")], ["allergien"]), Abbruch);
        var fragen = await freizeiten.FragenLadenAsync(FreizeitId, Abbruch);

        Assert.Equal(("Emma", FrageArt.JaNein, true), (pass!.Vorname, pass.Fragen[^1].Art, pass.Fragen[^1].Veraltet));
        Assert.True(stimmt);
        Assert.False(ohne);
        Assert.NotNull(gast);
        Assert.Equal("Nur „ja“ oder „nein“.", speichern.Fehler["schwimmen.wert"]);
        Assert.Equal("""{"antworten":[{"kennung":"schwimmen","wert":"vielleicht","sonstiges":null}],"bestaetigt":["allergien"]}""", Letzte(HttpMethod.Post, "/api/personen/oeffentlich/gaeste/Tk_1/freizeitpass/" + Emma).Koerper);
        Assert.Equal(("allergien,schwimmen", 3u), (fragen!.Teilnehmende, fragen.Version));
        Assert.Equal($"/api/ich/freizeitpass/{Emma}/antworten/zecken-entfernen", PersonenApiClient.FreizeitpassAntwortPfad(Emma, "zecken-entfernen"));
    }

    // ---------- Seite „Freizeitpass“ ----------

    private static object Frage(string kennung, string frage, string art, object[]? optionen = null, bool mitSonstiges = false, string? wert = null, bool veraltet = false) =>
        new { kennung, frage, art, optionen = optionen ?? [], mitSonstiges, wert, sonstiges = (string?)null, version = wert is null ? 0u : 9u, veraltet };

    private static object Pass(bool veraltet = true) => new
    {
        personId = Emma,
        vorname = "Emma",
        fragen = new[]
        {
            Frage("allergien", "Allergien und Unverträglichkeiten", "Auswahl", [new { wert = "eier", text = "Eier" }, new { wert = "milch", text = "Milch (inkl. Laktose)" }], true, "milch"),
            Frage("medikamente", "Medikamente (Name, Dosierung, wann)", "Freitext"),
            Frage("notfallkontakte", "Notfallkontakte (Name, Telefon)", "Freitext", wert: "Oma, 0171 1234567"),
            Frage("tetanus", "Letzte Tetanus-Impfung", "Datum"),
            Frage("schwimmen", "Kann schwimmen", "JaNein", [new { wert = "ja", text = "Ja" }, new { wert = "nein", text = "Nein" }], wert: "ja", veraltet: veraltet),
        },
    };

    private static object Zeile(string[] fragen, string status = "Eingegangen", Guid? person = null) => new
    {
        anmeldungId = Anmeldung, personId = person ?? Emma, freizeitName = "Zeltlager Kapf 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13",
        art = "Teilnehmende", status, kontaktMail = (string?)null, kontaktTelefon = (string?)null, fragen,
    };

    [Fact]
    public void Freizeitpass_zeigt_nur_die_Fragen_der_Freizeit_in_Katalogreihenfolge_je_Antworttyp()
    {
        _anmeldungen = [Zeile(["tetanus", "allergien", "notfallkontakte", "schwimmen"])];
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(Pass(veraltet: false)));

        var cut = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, Anmeldung));

        cut.WaitForAssertion(() => Assert.Equal("Freizeitpass für Emma", cut.Find("h1").TextContent));
        Assert.Equal(
            ["Allergien und Unverträglichkeiten", "Sonstiges", "Notfallkontakte (Name, Telefon)", "Letzte Tetanus-Impfung", "Kann schwimmen"],
            cut.FindAll("legend, label:not(.elkw-choice)").Select(e => e.TextContent.Trim()));
        Assert.Equal(2, cut.FindAll("#freizeitpass-allergien input[type=checkbox]").Count);
        Assert.True(cut.Find("input[value=milch]").HasAttribute("checked"));
        Assert.Equal(2, cut.FindAll("#freizeitpass-schwimmen input[type=radio]").Count);
        Assert.Equal("Oma, 0171 1234567", cut.Find("textarea#freizeitpass-notfallkontakte").GetAttribute("value"));
        Assert.Equal("date", cut.Find("#freizeitpass-tetanus").GetAttribute("type"));
        Assert.DoesNotContain("Medikamente", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Stimmt das noch?", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Textantwort_speichert_beim_Verlassen_mit_dem_Pfad_der_Antwort()
    {
        _anmeldungen = [Zeile(["medikamente"])];
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(Pass()));
        var pfad = $"/api/ich/freizeitpass/{Emma}/antworten/medikamente";
        _server.Bei(HttpMethod.Patch, pfad, () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":3,"wert":"Ibuprofen"}"""));
        var cut = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, Anmeldung));
        cut.WaitForElement("textarea#freizeitpass-medikamente");

        var feld = cut.Find("textarea#freizeitpass-medikamente");
        feld.Focus();
        feld.Input("Ibuprofen");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Equal("""{"version":0,"alterWert":null,"wert":"Ibuprofen"}""", Letzte(HttpMethod.Patch, pfad).Koerper));
    }

    [Fact]
    public void Sonstiges_speichert_beim_Verlassen_als_Feld_sonstiges_der_Antwort()
    {
        _anmeldungen = [Zeile(["allergien"])];
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(Pass()));
        var pfad = $"/api/ich/freizeitpass/{Emma}/antworten/allergien";
        _server.Bei(HttpMethod.Patch, pfad, () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":10,"sonstiges":"Kiwi"}"""));
        var cut = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, Anmeldung));
        cut.WaitForElement("#freizeitpass-allergien-sonstiges");

        var feld = cut.Find("#freizeitpass-allergien-sonstiges");
        feld.Focus();
        feld.Input("Kiwi");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Equal("""{"version":9,"alterWert":null,"sonstiges":"Kiwi"}""", Letzte(HttpMethod.Patch, pfad).Koerper));
    }

    [Fact]
    public void Veraltete_Angabe_traegt_Stimmt_das_noch_und_Stimmt_bestaetigt_sie()
    {
        _anmeldungen = [Zeile(["schwimmen"])];
        var veraltet = true;
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(Pass(veraltet)));
        _server.Bei(HttpMethod.Post, $"/api/ich/freizeitpass/{Emma}/antworten/schwimmen/bestaetigen", () =>
        {
            veraltet = false;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var cut = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, Anmeldung));
        cut.WaitForAssertion(() => Assert.Contains("Stimmt das noch?", cut.Find(".elkw-status--warnung").TextContent, StringComparison.Ordinal));

        cut.FindAll("button").Single(b => b.TextContent == "Stimmt").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Stimmt das noch?", cut.Markup, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad.EndsWith("/schwimmen/bestaetigen", StringComparison.Ordinal));
    }

    [Fact]
    public void Ohne_Fragen_steht_der_Leer_Zustand_und_eine_fremde_Anmeldung_ist_nicht_verfuegbar()
    {
        _anmeldungen = [Zeile([])];
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(Pass()));

        var leer = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, Anmeldung));
        leer.WaitForAssertion(() => Assert.Equal("Diese Freizeit fragt keine Angaben ab.", leer.Find(".elkw-empty p").TextContent));

        var fremd = Render<Freizeitpass>(ps => ps.Add(p => p.AnmeldungId, Guid.CreateVersion7()));
        fremd.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", fremd.Find("h1").TextContent));
    }

    // ---------- Einstellungen, Familie, Anmeldung mit Konto ----------

    [Fact]
    public void Einstellungen_zeigen_die_Card_Freizeitpass_mit_beiden_Auswahlgruppen()
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/kontext", () => Json(new
        {
            freizeitId = FreizeitId, name = "Zeltlager Sommer 2027", phase = "InVorbereitung", rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(),
            erlaubt = new[] { Aktionsschluessel.FreizeitEinstellungen },
        }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/freizeitseite", () => Json(new { version = 3u, anmeldelinks = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{FreizeitId}/fragen", () => Json(new
        {
            version = 3u, teilnehmende = "allergien,schwimmen", mitarbeitende = (string?)null,
            katalog = new[] { new { kennung = "allergien", frage = "Allergien und Unverträglichkeiten" }, new { kennung = "schwimmen", frage = "Kann schwimmen" } },
        }));

        var cut = Render<Einstellungen>(ps => ps.Add(p => p.FreizeitId, FreizeitId));

        cut.WaitForAssertion(() => Assert.Contains("Ohne Auswahl fragt der Freizeitpass nichts ab.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(["Fragen für Teilnehmende", "Fragen für Mitarbeitende"], cut.FindAll("legend").Select(l => l.TextContent));
        Assert.Equal(2, cut.FindAll("#fragen-teilnehmende input[type=checkbox][checked]").Count);
        Assert.Empty(cut.FindAll("#fragen-mitarbeitende input[type=checkbox][checked]"));
    }

    [Fact]
    public void Familie_zeigt_je_Anmeldung_den_Link_Freizeitpass_und_Freizeitpass_pruefen_bei_fehlender_oder_veralteter_Angabe()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => Json(new
        {
            eltern = new[] { new { personId = Katrin, vorname = "Katrin", nachname = "Muster" } },
            kinder = new[] { new { personId = Emma, version = 8u, vorname = "Emma", nachname = "Muster", geburtsdatum = "2017-09-02", volljaehrig = false, uebergabe = (object?)null } },
            einladungen = Array.Empty<object>(),
        }));
        var pass = Pass(veraltet: false);
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(pass));
        _anmeldungen = [Zeile(["allergien", "notfallkontakte"])];

        var vollstaendig = Render<FamilieAnsicht>();
        vollstaendig.WaitForAssertion(() => Assert.Equal($"anmeldungen/{Anmeldung}/freizeitpass", vollstaendig.Find("a[href$=freizeitpass]").GetAttribute("href")));
        Assert.DoesNotContain("Freizeitpass prüfen", vollstaendig.Markup, StringComparison.Ordinal);

        _anmeldungen = [Zeile(["allergien", "tetanus"])];
        var fehlt = Render<FamilieAnsicht>();
        fehlt.WaitForAssertion(() => Assert.Equal("Freizeitpass prüfen", fehlt.Find(".elkw-status--warnung").TextContent));

        pass = Pass(veraltet: true);
        _anmeldungen = [Zeile(["schwimmen"])];
        var veraltet = Render<FamilieAnsicht>();
        veraltet.WaitForAssertion(() => Assert.Equal("Freizeitpass prüfen", veraltet.Find(".elkw-status--warnung").TextContent));
    }

    [Fact]
    public void Familie_zeigt_keinen_Freizeitpass_Link_ohne_Fragen_ohne_zugaenglichen_Pass_oder_wenn_das_Laden_scheitert()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => Json(new
        {
            eltern = new[] { new { personId = Katrin, vorname = "Katrin", nachname = "Muster" } },
            kinder = new[] { new { personId = Emma, version = 8u, vorname = "Emma", nachname = "Muster", geburtsdatum = "2017-09-02", volljaehrig = false, uebergabe = (object?)null } },
            einladungen = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => Json(Pass()));

        _anmeldungen = [Zeile([])];
        var ohneFragen = Render<FamilieAnsicht>();
        ohneFragen.WaitForAssertion(() => Assert.Contains("Zeltlager Kapf 2027", ohneFragen.Markup, StringComparison.Ordinal));
        Assert.Empty(ohneFragen.FindAll("a[href$=freizeitpass]"));

        // Volljähriges Kind oder fremde Person: der Pass ist nicht zugänglich (404).
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _anmeldungen = [Zeile(["allergien"])];
        var ohnePass = Render<FamilieAnsicht>();
        ohnePass.WaitForAssertion(() => Assert.Contains("Zeltlager Kapf 2027", ohnePass.Markup, StringComparison.Ordinal));
        Assert.Empty(ohnePass.FindAll("a[href$=freizeitpass]"));

        // Ein Serverfehler beim Pass lässt die Familie stehen, ohne Link und ohne Hinweis.
        _server.Bei(HttpMethod.Get, $"/api/ich/freizeitpass/{Emma}", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var fehler = Render<FamilieAnsicht>();
        fehler.WaitForAssertion(() => Assert.Contains("Zeltlager Kapf 2027", fehler.Markup, StringComparison.Ordinal));
        Assert.Empty(fehler.FindAll("a[href$=freizeitpass]"));
        Assert.DoesNotContain("Freizeitpass prüfen", fehler.Markup, StringComparison.Ordinal);
        Assert.Empty(fehler.FindAll(".elkw-alert--fehler"));
    }

    [Fact]
    public void Nach_der_Anmeldung_mit_Konto_fuehrt_ein_Knopf_zum_Freizeitpass_des_Kindes()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/familie", () => Json(new
        {
            eltern = new[] { new { personId = Katrin, vorname = "Katrin", nachname = "Muster" } },
            kinder = new[] { new { personId = Emma, version = 8u, vorname = "Emma", nachname = "Muster", geburtsdatum = "2017-09-02", volljaehrig = false, uebergabe = (object?)null } },
            einladungen = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Get, "/api/freizeiten/oeffentlich/Tk_1", () => Json(new
        {
            organisation = "CVJM Musterstadt", name = "Zeltlager Kapf 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", art = "Teilnehmende",
            ausgebucht = false, team = Array.Empty<object>(),
        }));
        _server.Bei(HttpMethod.Post, "/api/ich/anmeldungen", () => Json(new { anmeldungId = Anmeldung }, HttpStatusCode.Created));
        var cut = Render<AnmeldungMitKonto>(ps => ps.Add(p => p.Token, "Tk_1"));
        cut.WaitForElement("input[type=radio]");

        cut.Find($"input[value='{Emma}']").Change(Emma.ToString());
        cut.Find("#anmeldung-bestaetigt").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal($"anmeldungen/{Anmeldung}/freizeitpass", cut.FindAll("a").Single(a => a.TextContent.Contains("Freizeitpass für Emma ausfüllen", StringComparison.Ordinal)).GetAttribute("href")));
    }
}
