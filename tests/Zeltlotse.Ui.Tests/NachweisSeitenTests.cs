using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App.Features.Freizeiten;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Features.Organisationen;
using Zeltlotse.App.Features.Personen;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Autosave;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Transport;
using PersonenArt = Zeltlotse.Api.Contracts.Personen.NachweisArt;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 1-5 in der Web-App mit nachgebildetem Server: Bausteine Datei-Feld, Ankreuzfeld und Autosave-Ankreuzfeld, die
/// Client-Methoden (auch Multipart), die Seiten „Nachweise“, „Nachweise prüfen“ und die Einsicht, die Richtlinie auf der
/// Organisationsseite sowie die Knöpfe auf Konto und Freizeit-Startseite.
/// </summary>
public sealed class NachweisSeitenTests : BunitContext
{
    private static readonly Guid Fz = Guid.Parse("0199f000-0000-7000-8000-0000000000a1");
    private static readonly Guid Eh = Guid.Parse("0199f000-0000-7000-8000-0000000000a2");
    private static readonly Guid Einsicht = Guid.Parse("0199f000-0000-7000-8000-0000000000b1");
    private static readonly Guid Jonas = Guid.Parse("0199f000-0000-7000-8000-0000000000c1");
    private static readonly Guid Freizeit = Guid.Parse("0199f000-0000-7000-8000-0000000000d1");
    private static readonly Guid Org = Guid.Parse("0199f000-0000-7000-8000-0000000000e1");

    private readonly NachgebildeterHandler _server = new();
    private int _tokens;

    public NachweisSeitenTests()
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
        Services.AddScoped<AnmeldungAbschluss>();
    }

    private static CancellationToken Abbruch => Xunit.TestContext.Current.CancellationToken;

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static HttpResponseMessage Json(object inhalt, HttpStatusCode status = HttpStatusCode.OK) =>
        NachgebildeterHandler.Json(status, JsonSerializer.Serialize(inhalt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private Aufgezeichnet Letzte(HttpMethod methode, string pfad) => _server.Anfragen.Last(a => a.Methode == methode && a.Pfad == pfad);

    // ---------- Bausteine ----------

    [Fact]
    public void Datei_Feld_hat_Label_accept_fuer_vier_Typen_und_Fehler_am_Feld()
    {
        IBrowserFile? gewaehlt = null;
        var cut = Render<DateiFeld>(ps => ps
            .Add(p => p.Id, "datei")
            .Add(p => p.Hinweis, "Höchstens 10 MB.")
            .Add(p => p.Fehler, "Datei fehlt.")
            .Add(p => p.DateiGewaehlt, d => gewaehlt = d));

        Assert.Equal("Foto aufnehmen oder Datei wählen", cut.Find("label[for=datei]").TextContent);
        var eingabe = cut.Find("input[type=file]");
        Assert.Equal("image/jpeg,image/png,image/webp,application/pdf", eingabe.GetAttribute("accept"));
        Assert.Equal("true", eingabe.GetAttribute("aria-invalid"));
        Assert.Equal("datei-hinweis datei-fehler", eingabe.GetAttribute("aria-describedby"));
        Assert.Equal("Fehler: Datei fehlt.", cut.Find(".elkw-field-error").TextContent);

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "nachweis.pdf", contentType: "application/pdf"));

        Assert.Equal("nachweis.pdf", gewaehlt?.Name);
    }

    [Fact]
    public void Datei_Feld_ist_nur_interaktiv_und_Ankreuzfeld_statisch_tauglich()
    {
        Assert.NotNull(typeof(DateiFeld).GetCustomAttributes(typeof(NurInteraktivAttribute), false).SingleOrDefault());
        Assert.Empty(typeof(Ankreuzfeld).GetCustomAttributes(typeof(NurInteraktivAttribute), false));
        Assert.DoesNotContain(typeof(Ankreuzfeld).GetProperties(), p => p.PropertyType.Name.StartsWith("EventCallback", StringComparison.Ordinal));
    }

    [Fact]
    public void Ankreuzfeld_hat_Beschriftung_Wert_und_Fehler()
    {
        var cut = Render<Ankreuzfeld>(ps => ps
            .Add(p => p.Label, "Pflicht")
            .Add(p => p.Id, "pflicht")
            .Add(p => p.Name, "pflicht")
            .Add(p => p.Wert, true)
            .Add(p => p.Fehler, "Pflicht ist „true“ oder „false“."));

        var kaestchen = cut.Find("input[type=checkbox]");
        Assert.True(kaestchen.HasAttribute("checked"));
        Assert.Equal(("pflicht", "true"), (kaestchen.GetAttribute("name"), kaestchen.GetAttribute("value")));
        Assert.Equal("Pflicht", cut.Find("label.elkw-choice").TextContent.Trim());
        Assert.Equal("true", kaestchen.GetAttribute("aria-invalid"));
        Assert.Equal("Fehler: Pflicht ist „true“ oder „false“.", cut.Find(".elkw-field-error").TextContent);
        Assert.False(Render<Ankreuzfeld>(ps => ps.Add(p => p.Label, "Pflicht")).Find("input").HasAttribute("checked"));
    }

    [Fact]
    public void Autosave_Ankreuzfeld_speichert_sofort_mit_Version_und_Rueckgaengig()
    {
        const string pfad = "/api/organisationen/x/nachweis-richtlinie/juleica";
        _server.BeiDerReihe(
            HttpMethod.Patch,
            pfad,
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":8,"pflicht":"true"}"""),
            () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":9,"pflicht":"false"}"""));
        var cut = Render<AutosaveAnkreuzfeld>(ps => ps
            .Add(p => p.Label, "Pflicht")
            .Add(p => p.Feldname, "pflicht")
            .Add(p => p.Pfad, pfad)
            .Add(p => p.Wert, false)
            .Add(p => p.Version, 7u));

        cut.Find("input").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Gespeichert", cut.Find("[role=status]").TextContent, StringComparison.Ordinal));
        Assert.Equal("""{"version":7,"alterWert":"false","pflicht":"true"}""", Letzte(HttpMethod.Patch, pfad).Koerper);
        Assert.True(cut.Find("input").HasAttribute("checked"));

        cut.FindAll("button").Single(b => b.TextContent == "Rückgängig").Click();

        cut.WaitForAssertion(() => Assert.False(cut.Find("input").HasAttribute("checked")));
        Assert.Equal("""{"version":8,"alterWert":"true","pflicht":"false"}""", Letzte(HttpMethod.Patch, pfad).Koerper);
    }

    [Fact]
    public void Autosave_Ankreuzfeld_zeigt_Konflikt_und_Nicht_gespeichert()
    {
        const string pfad = "/api/organisationen/x/nachweis-richtlinie/juleica";
        _server.BeiDerReihe(HttpMethod.Patch, pfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"), () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = Render<AutosaveAnkreuzfeld>(ps => ps.Add(p => p.Label, "Pflicht").Add(p => p.Feldname, "pflicht").Add(p => p.Pfad, pfad).Add(p => p.Version, 1u));

        cut.Find("input").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("Inzwischen geändert", cut.Markup, StringComparison.Ordinal));
        cut.Find("input").Change(false);
        cut.Find("input").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Nicht gespeichert.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_spricht_die_Nachweis_Endpunkte_mit_den_Vertragsformen_an()
    {
        _server.Bei(HttpMethod.Post, "/api/ich/nachweise", () => Json(new { nachweisId = Fz }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Post, $"/api/ich/nachweise/{Fz}/datei", () => Json(new { hochgeladenAm = "2026-10-12T08:00:00+00:00" }));
        _server.Bei(HttpMethod.Post, $"/api/ich/nachweise/einsichten/{Einsicht}/freigeben", () => Json(new { freigegebenBis = "2026-10-26T08:00:00+00:00" }));
        _server.Bei(HttpMethod.Post, $"/api/freizeiten/{Freizeit}/personen/nachweise/anfragen", () => Json(new { einsichtId = Einsicht }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Post, $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}/pruefen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.Bei(HttpMethod.Post, $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}/nachweise/{Fz}/ablehnen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Services.GetRequiredService<PersonenApiClient>();
        var ort = PruefOrt.Freizeit(Freizeit);

        var angelegt = await client.NachweisAnlegenAsync(PersonenArt.ErsteHilfe, new DateOnly(2025, 4, 15), Abbruch);
        var hochgeladen = await client.DateiHochladenAsync(Fz, new MemoryStream([0x25, 0x50, 0x44, 0x46]), "fz.pdf", "application/pdf", Abbruch);
        var freigabe = await client.FreigebenAsync(Einsicht, Abbruch);
        var anfrage = await client.EinsichtAnfragenAsync(ort, Jonas, Abbruch);
        var pruefen = await client.PruefenAsync(ort, Einsicht, Abbruch);
        var ablehnen = await client.AblehnenAsync(ort, Einsicht, Fz, "Datum passt nicht", Abbruch);

        Assert.Equal(new NachweisAnlegenErgebnis(Fz, null, null, false), angelegt);
        Assert.Equal("""{"art":"ersteHilfe","ausstellungsdatum":"2025-04-15"}""", Letzte(HttpMethod.Post, "/api/ich/nachweise").Koerper);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), hochgeladen.HochgeladenAm);
        var multipart = Letzte(HttpMethod.Post, $"/api/ich/nachweise/{Fz}/datei");
        Assert.Contains("name=datei", multipart.Koerper, StringComparison.Ordinal);
        Assert.Contains("filename=fz.pdf", multipart.Koerper, StringComparison.Ordinal);
        Assert.Contains("%PDF", multipart.Koerper, StringComparison.Ordinal);
        Assert.True(multipart.Kopfzeilen.ContainsKey(Kopfzeilen.Antiforgery));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero), freigabe.FreigegebenBis);
        Assert.Equal(Einsicht, anfrage);
        Assert.Equal($$"""{"personId":"{{Jonas}}"}""", Letzte(HttpMethod.Post, $"/api/freizeiten/{Freizeit}/personen/nachweise/anfragen").Koerper);
        Assert.Equal(PruefErgebnis.Gebunden, pruefen);
        Assert.Equal((true, (string?)null), ablehnen);
        Assert.Equal($"/api/organisationen/{Org}/personen/nachweise", PruefOrt.Organisation(Org).Basis);
        Assert.Equal($"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}/nachweise/{Fz}/datei", PersonenApiClient.EinsichtDateiPfad(ort, Einsicht, Fz));
        Assert.Equal($"/api/organisationen/{Org}/nachweis-richtlinie/ersteHilfe", OrganisationenApiClient.RichtliniePfad(Org, Zeltlotse.Api.Contracts.Organisationen.NachweisArt.ErsteHilfe));
    }

    [Fact]
    public void Autosave_Feld_zeigt_beim_409_den_Konflikttext_als_Fehlermeldung()
    {
        const string pfad = "/api/ich/nachweise/x";
        _server.Bei(HttpMethod.Patch, pfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var cut = Render<AutosaveFeld>(ps => ps
            .Add(p => p.Label, "Ausgestellt am")
            .Add(p => p.Feldname, "ausstellungsdatum")
            .Add(p => p.Pfad, pfad)
            .Add(p => p.Wert, "2025-11-03")
            .Add(p => p.Version, 1u)
            .Add(p => p.KonfliktText, "Der Nachweis ist gerade zur Prüfung freigegeben."));

        var feld = cut.Find("input");
        feld.Input("2025-11-04");
        feld.Blur();

        cut.WaitForAssertion(() => Assert.Contains("Der Nachweis ist gerade zur Prüfung freigegeben.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("Inzwischen geändert", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_liefert_Feldfehler_409_und_404_als_Ergebnis()
    {
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/ich/nachweise",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"art":["Art fehlt."],"ausstellungsdatum":["Ausstellungsdatum fehlt."]}}""", "application/problem+json"),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"/api/ich/nachweise/{Fz}/datei",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"datei":["Die Datei ist leer."]}}""", "application/problem+json"),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"),
            () => new HttpResponseMessage(HttpStatusCode.NotFound));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/namen", () => Json(new[] { new { organisationId = Org, name = "Jugendwerk Musterstadt" } }));
        _server.BeiDerReihe(HttpMethod.Post, $"/api/ich/nachweise/einsichten/{Einsicht}/freigeben", () => new HttpResponseMessage(HttpStatusCode.NotFound), () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var client = Services.GetRequiredService<PersonenApiClient>();
        var ort = PruefOrt.Organisation(Org);

        Assert.Equal(new NachweisAnlegenErgebnis(null, "Art fehlt.", "Ausstellungsdatum fehlt.", false), await client.NachweisAnlegenAsync(null, null, Abbruch));
        Assert.Equal(new NachweisAnlegenErgebnis(null, null, null, true), await client.NachweisAnlegenAsync(PersonenArt.Juleica, new DateOnly(2024, 5, 20), Abbruch));
        Assert.Equal(new DateiHochladenErgebnis(null, "Die Datei ist leer."), await client.DateiHochladenAsync(Fz, new MemoryStream(), "x.pdf", null, Abbruch));
        Assert.Equal(new DateiHochladenErgebnis(null, null, InPruefung: true), await client.DateiHochladenAsync(Fz, new MemoryStream([1]), "x.pdf", null, Abbruch));
        Assert.Equal(new DateiHochladenErgebnis(null, null, NichtGefunden: true), await client.DateiHochladenAsync(Fz, new MemoryStream([1]), "x.pdf", null, Abbruch));
        var namen = await Services.GetRequiredService<OrganisationenApiClient>().NamenLadenAsync([Org, Org, Einsicht], Abbruch);
        Assert.Equal(new Dictionary<Guid, string> { [Org] = "Jugendwerk Musterstadt" }, namen);
        Assert.Equal($"/api/ich/organisationen/namen?id={Org}&id={Einsicht}", Letzte(HttpMethod.Get, $"/api/ich/organisationen/namen?id={Org}&id={Einsicht}").Pfad);
        Assert.Equal(new FreigabeErgebnis(null, true, false), await client.FreigebenAsync(Einsicht, Abbruch));
        Assert.Equal(new FreigabeErgebnis(null, false, true), await client.FreigebenAsync(Einsicht, Abbruch));
        Assert.Null(await client.PruefListeLadenAsync(ort, Abbruch));
        Assert.Null(await client.EinsichtLadenAsync(ort, Einsicht, Abbruch));
        Assert.Null(await client.EinsichtAnfragenAsync(ort, Jonas, Abbruch));
        Assert.Equal(PruefErgebnis.NichtGefunden, await client.PruefenAsync(ort, Einsicht, Abbruch));
        Assert.False(await client.BestaetigenAsync(ort, Einsicht, Fz, Abbruch));
        Assert.Equal((false, (string?)null), await client.AblehnenAsync(ort, Einsicht, Fz, "x", Abbruch));
    }

    // ---------- Seite „Nachweise“ ----------

    private void Nachweise(object[] nachweise, object[] anfragen, object[]? bestaetigungen = null, object[]? freizeiten = null)
    {
        _server.Bei(HttpMethod.Get, "/api/ich/nachweise", () => Json(new { nachweise, anfragen }));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/nachweis-bestaetigungen", () => Json(bestaetigungen ?? []));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(freizeiten ?? []));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/namen", () => Json(new[] { new { organisationId = Org, name = "Jugendwerk Musterstadt" } }));
    }

    private static object Anfrage(Guid id, string status, object? angefragtVon = null, Guid? freizeitId = null, string? freigegebenBis = null) =>
        new { id, organisationId = Org, freizeitId, angefragtVon, status, angefragtAm = "2026-10-01T08:00:00+00:00", freigegebenBis };

    private static object FzZeile(string? hinweis = null) =>
        new { id = Fz, version = 3u, art = "fuehrungszeugnis", ausstellungsdatum = "2025-11-03", hochgeladenAm = "2026-10-01T08:00:00+00:00", ablehnungshinweis = hinweis };

    private static object EhZeile() =>
        new { id = Eh, version = 4u, art = "ersteHilfe", ausstellungsdatum = "2025-04-15", hochgeladenAm = (string?)null, ablehnungshinweis = (string?)null };

    [Fact]
    public void Nachweise_zeigt_Anfragen_Cards_Bestaetigungen_Hinweis_und_Hinzufuegen()
    {
        Nachweise(
            [FzZeile("Datum passt nicht"), EhZeile()],
            [
                Anfrage(Einsicht, "angefragt", freizeitId: Freizeit),
                Anfrage(Guid.CreateVersion7(), "freigegeben", new { vorname = "Andrea", nachname = "Beispiel" }, freigegebenBis: "2026-10-15T08:00:00+00:00"),
            ],
            [
                new { organisationId = Org, organisation = "Jugendwerk Musterstadt", art = "fuehrungszeugnis", eingesehenAm = "2026-10-02T08:00:00+00:00", gueltigBis = "2030-11-03", abgelaufen = false, geprueftVon = new { vorname = "Miriam", nachname = "Zeltner" } },
                new { organisationId = Guid.CreateVersion7(), organisation = "Jugendwerk B", art = "fuehrungszeugnis", eingesehenAm = "2020-10-02T08:00:00+00:00", gueltigBis = "2025-11-03", abgelaufen = true, geprueftVon = new { vorname = "Lea", nachname = "Feuerstein" } },
            ],
            [new { freizeitId = Freizeit, name = "Zeltlager Sommer 2027", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", rollen = new[] { "Leitung" } }]);

        var cut = Render<NachweiseAnsicht>();

        cut.WaitForAssertion(() => Assert.Equal(["Führungszeugnis", "Erste Hilfe", "Nachweis hinzufügen"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent)));
        Assert.Equal("Nachweise", cut.Find("h1").TextContent);
        Assert.Equal("konto", cut.FindAll("a").Single(a => a.TextContent == "Zurück zum Konto").GetAttribute("href"));
        var hinweise = cut.FindAll(".elkw-alert--hinweis").Select(m => m.TextContent).ToList();
        Assert.Contains(hinweise, h => h.Contains("Jugendwerk Musterstadt bittet um Einsicht in Nachweise.", StringComparison.Ordinal)
                                       && h.Contains("Angefragt wegen der Mitarbeit in „Zeltlager Sommer 2027“.", StringComparison.Ordinal)
                                       && h.Contains("Freigeben", StringComparison.Ordinal));
        Assert.Contains(hinweise, h => h.Contains("Jugendwerk Musterstadt bittet um Einsicht in Nachweise.", StringComparison.Ordinal)
                                       && h.Contains("Angefragt von Andrea Beispiel.", StringComparison.Ordinal)
                                       && h.Contains("Freigegeben bis 15.10.2026, wartet auf Prüfung.", StringComparison.Ordinal));
        var karten = cut.FindAll(".elkw-card");
        Assert.Contains("Abgelehnt: Datum passt nicht", karten[0].QuerySelector(".elkw-alert--fehler")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Bestätigt · gültig bis 03.11.2030 (Jugendwerk Musterstadt)", karten[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Abgelaufen am 03.11.2025 (Jugendwerk B)", karten[0].QuerySelector(".elkw-alert--warnung")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Achtung:", karten[0].QuerySelector(".elkw-alert--warnung")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("(Jugendwerk B)", karten[0].QuerySelectorAll("p").Where(p => p.TextContent.Contains("Bestätigt", StringComparison.Ordinal)).Select(p => p.TextContent).FirstOrDefault() ?? "", StringComparison.Ordinal);
        Assert.Contains("Hochgeladen am 01.10.2026", karten[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("2025-11-03", karten[0].QuerySelector("input[type=date]")!.GetAttribute("value"));
        Assert.Equal("Ausgestellt am", karten[0].QuerySelector("label")!.TextContent);
        Assert.Contains("Neue Datei hochladen", karten[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Noch keine Datei.", karten[1].TextContent, StringComparison.Ordinal);
        Assert.Equal(["JuLeiCa", "Präventionsschulung"], cut.FindAll("#nachweis-hinzufuegen fieldset label").Select(l => l.TextContent.Trim()));
        Assert.Equal("Nachweis hinzufügen", cut.Find("#nachweis-hinzufuegen button[type=submit]").TextContent);
    }

    [Fact]
    public void Scheitern_die_Zusatzabfragen_bleiben_die_Nachweise_ohne_Zusaetze()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/nachweise", () => Json(new { nachweise = new[] { FzZeile() }, anfragen = new[] { Anfrage(Einsicht, "angefragt", freizeitId: Freizeit) } }));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/nachweis-bestaetigungen", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        _server.Bei(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/api/ich/freizeiten", _ => throw new HttpRequestException("Netz weg"));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/namen", () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cut = Render<NachweiseAnsicht>();

        cut.WaitForAssertion(() => Assert.Equal("Führungszeugnis", cut.FindAll(".elkw-card__title")[0].TextContent));
        var hinweis = cut.Find(".elkw-alert--hinweis").TextContent;
        Assert.Contains("Eine Organisation bittet um Einsicht in Nachweise.", hinweis, StringComparison.Ordinal);
        Assert.DoesNotContain("Angefragt wegen", hinweis, StringComparison.Ordinal);
        Assert.DoesNotContain("Bestätigt", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".elkw-alert--fehler"));
    }

    [Fact]
    public void Hochladen_waehrend_der_Pruefung_409_und_unbekannt_404_mit_eigenem_Text_ohne_Erneut_versuchen()
    {
        Nachweise([FzZeile()], [Anfrage(Einsicht, "freigegeben", freizeitId: Freizeit, freigegebenBis: "2026-10-15T08:00:00+00:00")]);
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"/api/ich/nachweise/{Fz}/datei",
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"),
            () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Render<NachweiseAnsicht>();
        cut.WaitForElement(".elkw-card");

        cut.FindComponents<InputFile>().First().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "fz.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains(
            "Der Nachweis ist gerade zur Prüfung freigegeben. Ändern geht nach der Entscheidung oder nach Ablauf der Freigabe.",
            cut.FindAll(".elkw-card")[0].QuerySelector(".elkw-alert--fehler")!.TextContent,
            StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Erneut versuchen");

        cut.FindComponents<InputFile>().First().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "fz.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => Assert.Contains("Der Nachweis wurde nicht gefunden.", cut.FindAll(".elkw-card")[0].QuerySelector(".elkw-alert--fehler")!.TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("gerade zur Prüfung", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Erneut versuchen");
    }

    [Fact]
    public void Datum_aendern_waehrend_einer_Freigabe_zeigt_beim_409_den_Pruefungstext()
    {
        Nachweise([FzZeile()], [Anfrage(Einsicht, "freigegeben", freizeitId: Freizeit, freigegebenBis: "2026-10-15T08:00:00+00:00")]);
        _server.Bei(HttpMethod.Patch, $"/api/ich/nachweise/{Fz}", () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var cut = Render<NachweiseAnsicht>();
        cut.WaitForElement(".elkw-card");

        var datum = cut.Find($"#nachweis-datum-{Fz}");
        datum.Input("2025-11-04");
        datum.Blur();

        cut.WaitForAssertion(() => Assert.Contains(
            "Der Nachweis ist gerade zur Prüfung freigegeben. Ändern geht nach der Entscheidung oder nach Ablauf der Freigabe.",
            cut.FindAll(".elkw-card")[0].QuerySelector(".elkw-alert--fehler")!.TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Ohne_Nachweise_zeigt_die_Seite_den_Leer_Zustand()
    {
        Nachweise([], []);

        var cut = Render<NachweiseAnsicht>();

        cut.WaitForAssertion(() => Assert.Equal("Noch keine Nachweise hochgeladen.", cut.Find(".elkw-empty p").TextContent));
        Assert.Equal(["Nachweis hinzufügen"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
    }

    [Fact]
    public void Freigeben_mit_Inline_Bestaetigung()
    {
        var laden = 0;
        _server.Bei(HttpMethod.Get, "/api/ich/nachweise", () => Json(new
        {
            nachweise = new[] { FzZeile() },
            anfragen = new[]
            {
                Anfrage(Einsicht, ++laden == 1 ? "angefragt" : "freigegeben", new { vorname = "Miriam", nachname = "Zeltner" }, freigegebenBis: laden == 1 ? null : "2026-10-15T08:00:00+00:00"),
            },
        }));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/nachweis-bestaetigungen", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/namen", () => Json(new[] { new { organisationId = Org, name = "Jugendwerk Musterstadt" } }));
        _server.Bei(HttpMethod.Post, $"/api/ich/nachweise/einsichten/{Einsicht}/freigeben", () => Json(new { freigegebenBis = "2026-10-15T08:00:00+00:00" }));
        var cut = Render<NachweiseAnsicht>();
        cut.WaitForAssertion(() => Assert.Contains("Jugendwerk Musterstadt bittet um Einsicht in Nachweise.", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Angefragt von Miriam Zeltner.", cut.Markup, StringComparison.Ordinal);

        cut.FindAll("button").Single(b => b.TextContent == "Freigeben").Click();
        Assert.Contains("Nachweise einmal zur Prüfung freigeben? Die prüfende Person sieht die Dateien bis zur Entscheidung, höchstens 14 Tage.", cut.Markup, StringComparison.Ordinal);
        cut.FindAll("button").Single(b => b.TextContent == "Ja, freigeben").Click();

        cut.WaitForAssertion(() => Assert.Contains("Freigegeben bis 15.10.2026, wartet auf Prüfung.", cut.Markup, StringComparison.Ordinal));
        Assert.Single(_server.Anfragen, a => a.Methode == HttpMethod.Post && a.Pfad == $"/api/ich/nachweise/einsichten/{Einsicht}/freigeben");
    }

    [Fact]
    public void Hinzufuegen_legt_erst_an_und_laedt_dann_hoch_scheitert_der_Upload_bleibt_der_Eintrag_mit_Erneut_versuchen()
    {
        var laden = 0;
        _server.Bei(HttpMethod.Get, "/api/ich/nachweise", () => Json(new { nachweise = ++laden == 1 ? Array.Empty<object>() : new[] { EhZeile() }, anfragen = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/nachweis-bestaetigungen", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Post, "/api/ich/nachweise", () => Json(new { nachweisId = Eh }, HttpStatusCode.Created));
        var upload = 0;
        _server.Bei(
            r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == $"/api/ich/nachweise/{Eh}/datei",
            _ => ++upload == 1 ? throw new HttpRequestException("Netz weg") : Task.FromResult(Json(new { hochgeladenAm = "2026-10-12T08:00:00+00:00" })));
        var cut = Render<NachweiseAnsicht>();
        cut.WaitForElement("#nachweis-hinzufuegen");

        cut.FindAll("#nachweis-hinzufuegen input[type=radio]")[2].Change("ErsteHilfe");
        cut.Find("#nachweis-ausgestellt").Input("2025-04-15");
        cut.FindComponents<InputFile>().Last().UploadFiles(InputFileContent.CreateFromBinary([0x89, 0x50, 0x4E, 0x47], "foto.png", contentType: "image/png"));
        cut.Find("#nachweis-hinzufuegen").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Datei nicht hochgeladen.", cut.Find(".elkw-alert--fehler").TextContent, StringComparison.Ordinal));
        Assert.Equal("""{"art":"ersteHilfe","ausstellungsdatum":"2025-04-15"}""", Letzte(HttpMethod.Post, "/api/ich/nachweise").Koerper);
        Assert.Equal("Erste Hilfe", cut.FindAll(".elkw-card__title")[0].TextContent);
        Assert.Contains("Noch keine Datei.", cut.FindAll(".elkw-card")[0].TextContent, StringComparison.Ordinal);

        cut.FindAll("button").Single(b => b.TextContent == "Erneut versuchen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Die Datei ist hochgeladen.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(2, upload);
    }

    [Fact]
    public void Hinzufuegen_mit_Datei_meldet_Gespeichert_und_Feldfehler_am_Feld()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/nachweise", () => Json(new { nachweise = Array.Empty<object>(), anfragen = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/nachweis-bestaetigungen", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(Array.Empty<object>()));
        _server.BeiDerReihe(
            HttpMethod.Post,
            "/api/ich/nachweise",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"ausstellungsdatum":["Ausstellungsdatum fehlt."]}}""", "application/problem+json"),
            () => Json(new { nachweisId = Fz }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Post, $"/api/ich/nachweise/{Fz}/datei", () => Json(new { hochgeladenAm = "2026-10-12T08:00:00+00:00" }));
        var cut = Render<NachweiseAnsicht>();
        cut.WaitForElement("#nachweis-hinzufuegen");

        cut.FindAll("#nachweis-hinzufuegen input[type=radio]")[0].Change("Fuehrungszeugnis");
        cut.Find("#nachweis-hinzufuegen").Submit();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Ausstellungsdatum fehlt.", cut.Find(".elkw-field-error").TextContent));

        cut.Find("#nachweis-ausgestellt").Input("2025-11-03");
        cut.FindComponents<InputFile>().Last().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "fz.pdf", contentType: "application/pdf"));
        cut.Find("#nachweis-hinzufuegen").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Der Nachweis ist hinzugefügt.", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("filename=fz.pdf", Letzte(HttpMethod.Post, $"/api/ich/nachweise/{Fz}/datei").Koerper, StringComparison.Ordinal);
    }

    [Fact]
    public void Erfolgsmeldung_steht_ueber_den_Cards_auch_wenn_die_letzte_Art_hinzugefuegt_ist()
    {
        static object Zeile(Guid id, string art) =>
            new { id, version = 1u, art, ausstellungsdatum = "2025-01-01", hochgeladenAm = "2026-10-01T08:00:00+00:00", ablehnungshinweis = (string?)null };
        var drei = new[] { Zeile(Fz, "fuehrungszeugnis"), Zeile(Eh, "ersteHilfe"), Zeile(Guid.CreateVersion7(), "juleica") };
        var neu = Guid.CreateVersion7();
        var laden = 0;
        _server.Bei(HttpMethod.Get, "/api/ich/nachweise", () => Json(new { nachweise = ++laden == 1 ? drei : [.. drei, Zeile(neu, "praeventionsschulung")], anfragen = Array.Empty<object>() }));
        _server.Bei(HttpMethod.Get, "/api/ich/organisationen/nachweis-bestaetigungen", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Get, "/api/ich/freizeiten", () => Json(Array.Empty<object>()));
        _server.Bei(HttpMethod.Post, "/api/ich/nachweise", () => Json(new { nachweisId = neu }, HttpStatusCode.Created));
        _server.Bei(HttpMethod.Post, $"/api/ich/nachweise/{neu}/datei", () => Json(new { hochgeladenAm = "2026-10-12T08:00:00+00:00" }));
        var cut = Render<NachweiseAnsicht>();
        cut.WaitForElement("#nachweis-hinzufuegen");

        cut.FindAll("#nachweis-hinzufuegen input[type=radio]")[0].Change("Praeventionsschulung");
        cut.Find("#nachweis-ausgestellt").Input("2025-01-10");
        cut.FindComponents<InputFile>().Last().UploadFiles(InputFileContent.CreateFromBinary([0x25, 0x50, 0x44, 0x46], "p.pdf", contentType: "application/pdf"));
        cut.Find("#nachweis-hinzufuegen").Submit();

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".elkw-card").Count));
        Assert.Empty(cut.FindAll("#nachweis-hinzufuegen"));
        var erfolg = cut.Find(".elkw-alert--erfolg");
        Assert.Contains("Der Nachweis ist hinzugefügt.", erfolg.TextContent, StringComparison.Ordinal);
        Assert.Null(erfolg.Closest(".elkw-card"));
    }

    // ---------- Prüfliste und Einsicht ----------

    private static object PruefZeile(bool anfragbar, string? status, bool pruefbar = false) => new
    {
        personId = Jonas,
        vorname = "Jonas",
        nachname = "Hüttner",
        anfragbar,
        einsicht = status is null ? null : (object)new
        {
            id = Einsicht,
            status,
            angefragtAm = "2026-10-01T08:00:00+00:00",
            freigegebenBis = status is "freigegeben" or "abgelaufen" ? "2026-10-15T08:00:00+00:00" : null,
            abgeschlossenAm = status == "abgeschlossen" ? "2026-10-03T08:00:00+00:00" : null,
            pruefbar,
        },
    };

    [Fact]
    public void Pruefliste_zeigt_Status_und_Knoepfe_Pruefen_uebernimmt_und_oeffnet_die_Einsicht()
    {
        var basis = $"/api/freizeiten/{Freizeit}/personen/nachweise";
        _server.Bei(HttpMethod.Get, basis, () => Json(new[]
        {
            PruefZeile(false, "freigegeben", pruefbar: true),
            new { personId = Guid.CreateVersion7(), vorname = "Tobias", nachname = "Wanderer", anfragbar = true, einsicht = (object?)null },
            new { personId = Guid.CreateVersion7(), vorname = "Lea", nachname = "Feuerstein", anfragbar = true, einsicht = (object?)new { id = Guid.CreateVersion7(), status = "abgeschlossen", angefragtAm = "2026-09-01T08:00:00+00:00", freigegebenBis = (string?)null, abgeschlossenAm = "2026-09-03T08:00:00+00:00", pruefbar = false } },
        }));
        _server.Bei(HttpMethod.Post, $"{basis}/einsichten/{Einsicht}/pruefen", () => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForElement("table");
        Assert.Equal("Nachweise prüfen", cut.Find("h1").TextContent);
        var zeilen = cut.FindAll("tbody tr");
        Assert.Equal(["Jonas Hüttner", "Freigegeben bis 15.10.2026"], zeilen[0].QuerySelectorAll("td").Take(2).Select(t => t.TextContent.Trim()));
        Assert.Equal(["Tobias Wanderer", "Noch nicht angefragt"], zeilen[1].QuerySelectorAll("td").Take(2).Select(t => t.TextContent.Trim()));
        Assert.Equal("Geprüft am 03.09.2026", zeilen[2].QuerySelectorAll("td")[1].TextContent.Trim());
        Assert.Equal(["Prüfen"], zeilen[0].QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Equal(["Einsicht anfragen"], zeilen[1].QuerySelectorAll("button").Select(b => b.TextContent));

        zeilen[0].QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost/freizeiten/{Freizeit}/nachweise/{Einsicht}", Navigation.Uri));
    }

    [Fact]
    public void Pruefliste_zeigt_eine_abgelaufene_Freigabe_ohne_Pruefen()
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personen/nachweise", () => Json(new[] { PruefZeile(false, "abgelaufen", pruefbar: true) }));

        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));

        cut.WaitForElement("table");
        var zeile = cut.Find("tbody tr");
        Assert.Equal("Freigabe abgelaufen am 15.10.2026", zeile.QuerySelectorAll("td")[1].TextContent.Trim());
        Assert.Empty(zeile.QuerySelectorAll("button"));
    }

    [Fact]
    public void Pruefliste_fragt_an_ist_leer_oder_nicht_verfuegbar()
    {
        var basis = $"/api/organisationen/{Org}/personen/nachweise";
        var laden = 0;
        _server.Bei(HttpMethod.Get, basis, () => Json(++laden == 1 ? new[] { PruefZeile(true, null) } : new[] { PruefZeile(false, "angefragt") }));
        _server.Bei(HttpMethod.Post, $"{basis}/anfragen", () => Json(new { einsichtId = Einsicht }, HttpStatusCode.Created));
        var cut = Render<NachweisePruefen>(ps => ps.Add(p => p.OrgId, Org));
        cut.WaitForElement("table");

        cut.FindAll("button").Single(b => b.TextContent == "Einsicht anfragen").Click();

        cut.WaitForAssertion(() => Assert.Contains("Angefragt am 01.10.2026", cut.Find("tbody").TextContent, StringComparison.Ordinal));
        Assert.Contains("Die Einsicht ist angefragt.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal($$"""{"personId":"{{Jonas}}"}""", Letzte(HttpMethod.Post, $"{basis}/anfragen").Koerper);

        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personen/nachweise", () => Json(Array.Empty<object>()));
        var leer = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        leer.WaitForAssertion(() => Assert.Equal("Noch niemand im Team.", leer.Find(".elkw-empty p").TextContent));

        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personen/nachweise", () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var verboten = Render<NachweisePruefen>(ps => ps.Add(p => p.FreizeitId, Freizeit));
        verboten.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", verboten.Find("h1").TextContent));
    }

    private void Detail(string status, string fzErgebnis = "offen")
    {
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}", () => Json(new
        {
            id = Einsicht,
            personId = Jonas,
            vorname = "Jonas",
            nachname = "Hüttner",
            status,
            freigegebenBis = status is "freigegeben" or "abgelaufen" ? "2026-10-15T08:00:00+00:00" : null,
            freigabeAbgelaufen = status == "abgelaufen",
            posten = status == "angefragt"
                ? Array.Empty<object>()
                : new object[]
                {
                    new { nachweisId = Fz, art = "fuehrungszeugnis", ausstellungsdatum = "2025-11-03", hochgeladenAm = "2026-10-01T08:00:00+00:00", ergebnis = fzErgebnis, entschiedenAm = (string?)null },
                    new { nachweisId = Eh, art = "ersteHilfe", ausstellungsdatum = "2025-04-15", hochgeladenAm = "2026-10-01T08:00:00+00:00", ergebnis = "bestaetigt", entschiedenAm = "2026-10-02T08:00:00+00:00" },
                },
        }));
    }

    [Fact]
    public void Einsicht_zeigt_offene_Nachweise_mit_Datei_Link_und_entschiedene_nur_mit_Status()
    {
        Detail("freigegeben");

        var cut = Render<NachweisEinsichtAnsicht>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.EinsichtId, Einsicht));

        cut.WaitForAssertion(() => Assert.Equal("Nachweise von Jonas Hüttner", cut.Find("h1").TextContent));
        Assert.Contains("Jede Datei lässt sich nur bis zur Entscheidung öffnen. Gespeichert wird nur die Bestätigung.", cut.Markup, StringComparison.Ordinal);
        var karten = cut.FindAll(".elkw-card");
        Assert.Equal(["Führungszeugnis", "Erste Hilfe"], cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        Assert.Contains("Ausgestellt am 03.11.2025", karten[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Hochgeladen am 01.10.2026", karten[0].TextContent, StringComparison.Ordinal);
        var link = karten[0].QuerySelector("a")!;
        Assert.Equal(("Datei öffnen", $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}/nachweise/{Fz}/datei", "_blank"), (link.TextContent, link.GetAttribute("href"), link.GetAttribute("target")));
        Assert.Equal(["Bestätigen", "Ablehnen"], karten[0].QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Equal("Hinweis", karten[0].QuerySelector("label")!.TextContent);
        Assert.Equal("Bestätigt", karten[1].QuerySelector(".elkw-status")!.TextContent);
        Assert.Null(karten[1].QuerySelector("a"));
        Assert.Empty(karten[1].QuerySelectorAll("button"));
        Assert.Equal($"freizeiten/{Freizeit}/nachweise", cut.FindAll("a").Single(a => a.TextContent == "Zurück zur Liste").GetAttribute("href"));
    }

    [Fact]
    public void Einsicht_bestaetigen_und_ablehnen_mit_Hinweis_laden_neu()
    {
        var basis = $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}";
        Detail("freigegeben");
        _server.Bei(HttpMethod.Post, $"{basis}/nachweise/{Fz}/bestaetigen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        _server.BeiDerReihe(
            HttpMethod.Post,
            $"{basis}/nachweise/{Fz}/ablehnen",
            () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"hinweis":["Hinweis fehlt."]}}""", "application/problem+json"),
            () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<NachweisEinsichtAnsicht>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.EinsichtId, Einsicht));
        cut.WaitForElement(".elkw-card");

        cut.FindAll("button").Single(b => b.TextContent == "Ablehnen").Click();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Hinweis fehlt.", cut.Find(".elkw-field-error").TextContent));
        cut.Find($"#hinweis-{Fz}").Input("Datum passt nicht");
        Detail("abgeschlossen", "abgelehnt");
        cut.FindAll("button").Single(b => b.TextContent == "Ablehnen").Click();

        cut.WaitForAssertion(() => Assert.Equal("Abgelehnt", cut.FindAll(".elkw-card")[0].QuerySelector(".elkw-status")?.TextContent));
        Assert.Equal("""{"hinweis":"Datum passt nicht"}""", Letzte(HttpMethod.Post, $"{basis}/nachweise/{Fz}/ablehnen").Koerper);
        Assert.Empty(cut.FindAll(".elkw-card a"));
    }

    [Fact]
    public void Nach_einem_Feldfehler_beim_Ablehnen_laedt_Bestaetigen_neu_und_der_Fehler_verschwindet()
    {
        var basis = $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}";
        Detail("freigegeben");
        _server.Bei(HttpMethod.Post, $"{basis}/nachweise/{Fz}/ablehnen", () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"hinweis":["Hinweis fehlt."]}}""", "application/problem+json"));
        _server.Bei(HttpMethod.Post, $"{basis}/nachweise/{Fz}/bestaetigen", () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<NachweisEinsichtAnsicht>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.EinsichtId, Einsicht));
        cut.WaitForElement(".elkw-card");
        cut.FindAll("button").Single(b => b.TextContent == "Ablehnen").Click();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Hinweis fehlt.", cut.Find(".elkw-field-error").TextContent));

        Detail("freigegeben", "bestaetigt");
        cut.FindAll("button").Single(b => b.TextContent == "Bestätigen").Click();

        cut.WaitForAssertion(() => Assert.Equal("Bestätigt", cut.FindAll(".elkw-card")[0].QuerySelector(".elkw-status")?.TextContent));
        Assert.Empty(cut.FindAll(".elkw-field-error"));
        Assert.Empty(cut.FindAll(".elkw-card a"));
    }

    [Fact]
    public void Einsicht_mit_abgelaufener_Freigabe_nennt_das_Datum()
    {
        Detail("abgelaufen");

        var cut = Render<NachweisEinsichtAnsicht>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.EinsichtId, Einsicht));

        cut.WaitForAssertion(() => Assert.Contains("Die Freigabe ist am 15.10.2026 abgelaufen. Die Person muss erneut freigeben.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Noch nicht freigegeben.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".elkw-card a"));
        Assert.Empty(cut.FindAll(".elkw-card button"));
    }

    [Fact]
    public void Wechsel_zu_einer_anderen_Einsicht_uebernimmt_Hinweis_Feldfehler_und_Meldung_nicht()
    {
        var basis = $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{Einsicht}";
        Detail("freigegeben");
        _server.Bei(HttpMethod.Post, $"{basis}/nachweise/{Fz}/ablehnen", () => NachgebildeterHandler.Json(HttpStatusCode.BadRequest, """{"errors":{"hinweis":["Hinweis fehlt."]}}""", "application/problem+json"));
        _server.Bei(HttpMethod.Post, $"{basis}/nachweise/{Eh}/bestaetigen", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var andere = Guid.CreateVersion7();
        _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/personen/nachweise/einsichten/{andere}", () => Json(new
        {
            id = andere,
            personId = Jonas,
            vorname = "Tobias",
            nachname = "Wanderer",
            status = "freigegeben",
            freigegebenBis = "2026-10-15T08:00:00+00:00",
            freigabeAbgelaufen = false,
            posten = new object[]
            {
                new { nachweisId = Fz, art = "fuehrungszeugnis", ausstellungsdatum = "2025-11-03", hochgeladenAm = "2026-10-01T08:00:00+00:00", ergebnis = "offen", entschiedenAm = (string?)null },
            },
        }));
        var cut = Render<NachweisEinsichtAnsicht>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.EinsichtId, Einsicht));
        cut.WaitForElement(".elkw-card");
        cut.Find($"#hinweis-{Fz}").Input("Unleserlich");
        cut.FindAll("button").Single(b => b.TextContent == "Ablehnen").Click();
        cut.WaitForAssertion(() => Assert.Equal("Fehler: Hinweis fehlt.", cut.Find(".elkw-field-error").TextContent));

        cut.Render(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.EinsichtId, andere));

        cut.WaitForAssertion(() => Assert.Equal("Nachweise von Tobias Wanderer", cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll(".elkw-field-error"));
        Assert.Equal("", cut.Find($"#hinweis-{Fz}").GetAttribute("value") ?? "");
        Assert.DoesNotContain("Die Entscheidung ist nicht mehr möglich.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Einsicht_nicht_freigegeben_und_nicht_verfuegbar()
    {
        Detail("angefragt");
        var cut = Render<NachweisEinsichtAnsicht>(ps => ps.Add(p => p.FreizeitId, Freizeit).Add(p => p.EinsichtId, Einsicht));
        cut.WaitForAssertion(() => Assert.Contains("Noch nicht freigegeben.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".elkw-card"));

        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}/personen/nachweise/einsichten/{Einsicht}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fremd = Render<NachweisEinsichtAnsicht>(ps => ps.Add(p => p.OrgId, Org).Add(p => p.EinsichtId, Einsicht));
        fremd.WaitForAssertion(() => Assert.Equal("Diese Seite ist nicht verfügbar.", fremd.Find("h1").TextContent));
    }

    // ---------- Organisation, Konto, Freizeit-Start ----------

    [Fact]
    public void Organisation_zeigt_die_Richtlinie_mit_Autosave_und_den_Knopf_Nachweise_pruefen()
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}", () => Json(new
        {
            id = Org,
            name = "Jugendwerk Musterstadt",
            rollen = new[] { "OrgAdmin" },
            erlaubt = new[] { Aktionsschluessel.NachweisePruefen, Aktionsschluessel.NachweisRichtlinieVerwalten },
        }));
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}/nachweis-richtlinie", () => Json(new object[]
        {
            new { art = "fuehrungszeugnis", version = 5u, pflicht = true, gueltigkeitMonate = 60 },
            new { art = "juleica", version = 6u, pflicht = false, gueltigkeitMonate = 36 },
            new { art = "ersteHilfe", version = 7u, pflicht = false, gueltigkeitMonate = 24 },
            new { art = "praeventionsschulung", version = 8u, pflicht = false, gueltigkeitMonate = 60 },
        }));
        _server.Bei(HttpMethod.Patch, $"/api/organisationen/{Org}/nachweis-richtlinie/juleica", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":9,"gueltigkeitMonate":"48"}"""));

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, Org));

        cut.WaitForElement("table");
        Assert.Equal($"organisationen/{Org}/nachweise", cut.FindAll("a").Single(a => a.TextContent == "Nachweise prüfen").GetAttribute("href"));
        Assert.Contains("Richtlinie für Nachweise", cut.FindAll(".elkw-card__title").Select(t => t.TextContent));
        var zeilen = cut.FindAll("tbody tr");
        Assert.Equal(["Führungszeugnis", "JuLeiCa", "Erste Hilfe", "Präventionsschulung"], zeilen.Select(z => z.QuerySelector("td")!.TextContent));
        Assert.True(zeilen[0].QuerySelector("input[type=checkbox]")!.HasAttribute("checked"));
        Assert.False(zeilen[1].QuerySelector("input[type=checkbox]")!.HasAttribute("checked"));
        Assert.Equal("36", zeilen[1].QuerySelector("input[type=number]")!.GetAttribute("value"));

        var monate = zeilen[1].QuerySelector("input[type=number]")!;
        monate.Focus();
        monate.Input("48");
        monate.Blur();

        cut.WaitForAssertion(() => Assert.Equal(
            """{"version":6,"alterWert":"36","gueltigkeitMonate":"48"}""",
            Letzte(HttpMethod.Patch, $"/api/organisationen/{Org}/nachweis-richtlinie/juleica").Koerper));
    }

    [Fact]
    public void Organisation_zeigt_bei_Richtlinie_ohne_Antwort_die_Fehlermeldung_statt_Ladezustand()
    {
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}", () => Json(new
        {
            id = Org,
            name = "Jugendwerk Musterstadt",
            rollen = new[] { "OrgAdmin" },
            erlaubt = new[] { Aktionsschluessel.NachweisRichtlinieVerwalten },
        }));
        _server.Bei(HttpMethod.Get, $"/api/organisationen/{Org}/nachweis-richtlinie", () => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var cut = Render<OrganisationAnsicht>(ps => ps.Add(p => p.OrgId, Org));

        cut.WaitForAssertion(() =>
        {
            var card = cut.FindAll(".elkw-card").Single(c => c.QuerySelector(".elkw-card__title")!.TextContent == "Richtlinie für Nachweise");
            Assert.Contains(AnmeldungAbschluss.FehlerText, card.QuerySelector(".elkw-alert--fehler")!.TextContent, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("Wird geladen", cut.FindAll(".elkw-card").Single(c => c.TextContent.Contains("Richtlinie für Nachweise", StringComparison.Ordinal)).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Konto_hat_die_Card_Nachweise_und_die_Freizeit_Startseite_den_Knopf_nur_mit_erlaubter_Aktion()
    {
        _server.Bei(HttpMethod.Get, "/api/ich/konto", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"mail":"jonas@example.org","hatPasswort":false,"erlaubt":[]}"""));
        _server.Bei(HttpMethod.Get, "/api/ich/profil", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":1,"vorname":"Jonas","nachname":"Hüttner"}"""));
        var konto = Render<Konto>();
        konto.WaitForElement("a[href='konto/nachweise']");
        Assert.Equal("Nachweise öffnen", konto.Find("a[href='konto/nachweise']").TextContent);
        Assert.Contains("elkw-btn--outline", konto.Find("a[href='konto/nachweise']").GetAttribute("class"), StringComparison.Ordinal);
        Assert.Contains("Nachweise", konto.FindAll(".elkw-card__title").Select(t => t.TextContent));

        foreach (var (erlaubt, erwartet) in new[] { (new[] { Aktionsschluessel.NachweisePruefen }, 1), (Array.Empty<string>(), 0) })
        {
            _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}/kontext", () => Json(new { freizeitId = Freizeit, name = "Zeltlager", phase = "InVorbereitung", rollen = new[] { "Leitung" }, schalter = Array.Empty<string>(), erlaubt }));
            _server.Bei(HttpMethod.Get, $"/api/freizeiten/{Freizeit}", () => Json(new { id = Freizeit, name = "Zeltlager", ersterTag = "2027-08-02", letzterTag = "2027-08-13", phase = "InVorbereitung", freigabeBis = "2027-09-12", leitung = Array.Empty<object>() }));
            var start = Render<FreizeitStart>(ps => ps.Add(p => p.FreizeitId, Freizeit));
            start.WaitForAssertion(() => Assert.Equal("Zeltlager", start.Find("h1").TextContent));
            Assert.Equal(erwartet, start.FindAll($"a[href='freizeiten/{Freizeit}/nachweise']").Count);
        }
    }
}
