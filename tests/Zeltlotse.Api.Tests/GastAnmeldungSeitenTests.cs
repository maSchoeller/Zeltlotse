using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-2, öffentliche Seiten der Anmeldung (FR-17 bis FR-19, AD-5, AD-25, AD-29): Knöpfe auf der Freizeitseite, das klassische
/// Gast-Formular mit Antiforgery-Feld (GET, dann POST mit demselben Client), Fehlerübersicht, Antwortseite direkt im POST, „Weiteres
/// Kind anmelden“ und die Seite des Gast-Links. Alles statisch: kein Skript, kein <c>style=</c>, <c>noindex</c>.
/// </summary>
public sealed partial class GastAnmeldungSeitenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Besucher = "203.0.113.8";
    private const string Lina = "lina.eltern@example.org";

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;
    private HttpClient _gerd = null!;
    private HttpClient _andrea = null!;
    private HttpClient _miriam = null!;
    private HttpClient _besucher = null!;
    private string _tn = null!;
    private string _ma = null!;
    private Guid _x;

    /// <summary>
    /// Gesetzt: Die öffentlichen Seiten bekommen beim Anlegen eines weiteren Gast-Kindes diese Kennung zurück, ohne dass Personen ein
    /// Kind anlegt. So lässt sich die Doppelanmeldung (409 von Freizeiten) über die Seite herstellen.
    /// </summary>
    private Guid? _kindUmleitung;

    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = OrgHelfer.Factory(_verbindung);
        _factory.ZusatzDienste.Add(s => s.Configure<Zeltlotse.Public.PublicSeitenOptionen>(o =>
            o.PrimaerHandler = () => new KindUmleitung(() => _kindUmleitung) { InnerHandler = _factory.Server.CreateHandler() }));
        _gerd = await AngemeldeterClientAsync(_factory, Gerd);
        Guid a;
        (a, _andrea) = await OrganisationMitOrgAdminAsync(_factory, _gerd, "CVJM Musterstadt", Andrea);
        _x = await AnlegenAsync(_andrea, a, "Zeltlager Kapf 2027");
        _miriam = await LeitungAsync(_factory, _andrea, a, _x, Miriam);
        var links = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
            .ToDictionary(l => l.GetProperty("art").GetString()!, l => l.GetProperty("adresse").GetString()!);
        _tn = links["Teilnehmende"][(links["Teilnehmende"].LastIndexOf('/') + 1)..];
        _ma = links["Mitarbeitende"][(links["Mitarbeitende"].LastIndexOf('/') + 1)..];
        _besucher = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _gerd, _andrea, _miriam, _besucher })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Freizeitseite_fuer_Teilnehmende_hat_Anmelden_und_Mit_Konto_anmelden_fuer_Mitarbeitende_nur_Anmelden_in_der_App()
    {
        var teilnehmende = await HtmlAsync(await SeiteAsync($"/freizeit/{_tn}"));
        var mitarbeitende = await HtmlAsync(await SeiteAsync($"/freizeit/{_ma}"));

        Assert.Matches($"<a [^>]*class=\"elkw-btn elkw-btn--block\" href=\"/freizeit/{_tn}/anmelden\"[^>]*>Anmelden</a>", teilnehmende);
        Assert.Contains($"<a href=\"/app/anmeldung/{_tn}\">Mit Konto anmelden</a>", teilnehmende, StringComparison.Ordinal);
        Assert.Matches($"<a [^>]*class=\"elkw-btn elkw-btn--block\" href=\"/app/anmeldung/{_ma}\"[^>]*>Anmelden</a>", mitarbeitende);
        Assert.DoesNotContain("/anmelden\"", mitarbeitende, StringComparison.Ordinal);
        Assert.DoesNotContain("Mit Konto anmelden", mitarbeitende, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Das_Formular_ist_statisch_mit_Kind_Elternteil_Teilnahmebedingungen_Haken_und_Antiforgery_Feld()
    {
        using var antwort = await SeiteAsync($"/freizeit/{_tn}/anmelden");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var html = await HtmlAsync(antwort);
        Assert.Matches("<h1[^>]*>Anmeldung</h1>", html);
        Assert.Contains("Ohne Konto möglich. Gefragt wird nur das Nötigste.", html, StringComparison.Ordinal);
        Assert.Contains("<form method=\"post\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"_handler\" value=\"anmelden\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        foreach (var feld in new[] { "e.KindVorname", "e.KindNachname", "e.KindGeburtsdatum", "e.ElternVorname", "e.ElternNachname", "e.Mail", "e.Bestaetigt" })
        {
            Assert.Contains($"name=\"{feld}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains(">Kind</h2>", html, StringComparison.Ordinal);
        Assert.Contains(">Elternteil</h2>", html, StringComparison.Ordinal);
        Assert.Contains("Dorthin gehen Bestätigung oder Absage.", html, StringComparison.Ordinal);
        Assert.Contains("Für diese Freizeit sind keine eigenen Teilnahmebedingungen hinterlegt.", html, StringComparison.Ordinal);
        Assert.Contains("Teilnahmebedingungen gelesen und bestätigt *", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/freizeit/{_tn}/vertrauensseite\"", html, StringComparison.Ordinal);
        Assert.Contains("Anmeldung absenden", html, StringComparison.Ordinal);
        Assert.Contains("* Pflichtfeld", html, StringComparison.Ordinal);
        Assert.Contains("<title>Anmeldung – Zeltlager Kapf 2027", html, StringComparison.Ordinal);
        Statisch(html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Link_fuer_Mitarbeitende_oder_ungueltiger_Link_ergibt_die_404_Seite(bool mitarbeitende)
    {
        using var antwort = await SeiteAsync($"/freizeit/{(mitarbeitende ? _ma : Zugangslink.NeuesToken())}/anmelden");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Contains("Dieser Anmeldelink ist nicht mehr gültig.", await HtmlAsync(antwort), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Absenden_ohne_Angaben_zeigt_die_Fehleruebersicht_und_legt_nichts_an()
    {
        using var antwort = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", []);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var html = await HtmlAsync(antwort);
        Assert.Matches("<section class=\"elkw-alert elkw-alert--fehler zl-fehleruebersicht\" role=\"alert\"", html);
        Assert.Contains("<a href=\"#kind-vorname\">Kind: Vorname fehlt.</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#kind-geburtsdatum\">Kind: Geburtsdatum fehlt.</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#eltern-vorname\">Elternteil: Vorname fehlt.</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#eltern-mail\">Elternteil: Mail-Adresse fehlt.</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#teilnahmebedingungen-bestaetigt\">Teilnahmebedingungen nicht bestätigt.</a>", html, StringComparison.Ordinal);
        Assert.Contains("Fehler: Vorname fehlt.", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang"));
        Statisch(html);
    }

    [Fact]
    public async Task Fehler_der_API_stehen_am_Feld_und_die_Eingaben_bleiben_ein_volljaehriges_Kind_bekommt_den_Weg_mit_Konto()
    {
        using var antwort = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", Gast("Lina", "2008-01-01", "keine-mail"));

        var html = await HtmlAsync(antwort);
        Assert.Contains("Fehler: Volljährige melden sich mit Konto an.", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/app/anmeldung/{_tn}\">Mit Konto anmelden</a>", html, StringComparison.Ordinal);
        Assert.Contains("Fehler: Das ist keine gültige Mail-Adresse.", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Lina\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"keine-mail\"", html, StringComparison.Ordinal);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang"));
    }

    [Fact]
    public async Task Absenden_zeigt_die_Antwortseite_direkt_im_POST_mit_Gast_Link_Angebot_und_Weiteres_Kind()
    {
        using var antwort = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", Gast("Lina", "2016-04-01", Lina));

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Null(antwort.Headers.Location);
        var html = await HtmlAsync(antwort);
        var token = _factory.Mails.LetzteAn(Lina).Token;
        Assert.Matches("<h1[^>]*>Anmeldung für Lina</h1>", html);
        Assert.Contains("Anmeldung eingegangen:", html, StringComparison.Ordinal);
        Assert.Contains("Die Leitung meldet sich in Kürze.", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"elkw-status elkw-status--offen\">Eingegangen</span>", html, StringComparison.Ordinal);
        Assert.Contains($"Eine Mail mit dem Gast-Link geht an {Lina}.", html, StringComparison.Ordinal);
        Assert.Contains("Konto aus diesen Daten erstellen?", html, StringComparison.Ordinal);
        Assert.Matches($"href=\"/app/gast/{token}/konto\"[^>]*>Konto erstellen</a>", html);
        Assert.Contains("name=\"_handler\" value=\"weiteres-kind\"", html, StringComparison.Ordinal);
        Assert.Contains($"name=\"e.GastToken\" value=\"{token}\"", html, StringComparison.Ordinal);
        Assert.Contains("Weiteres Kind anmelden", html, StringComparison.Ordinal);
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung"));
        Statisch(html);
    }

    [Fact]
    public async Task Weiteres_Kind_zeigt_die_Elternangaben_als_Text_und_meldet_das_Kind_in_derselben_Gast_Familie_an()
    {
        using (var erstes = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", Gast("Lina", "2016-04-01", Lina)))
        {
            Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        }

        var token = _factory.Mails.LetzteAn(Lina).Token;
        var verborgen = new[] { ("e.GastToken", token), ("e.ElternVorname", "Sabine"), ("e.ElternNachname", "Beispiel"), ("e.Mail", Lina) };

        using var formular = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "weiteres-kind", verborgen);
        var formularHtml = await HtmlAsync(formular);
        Assert.Contains("name=\"_handler\" value=\"weiteres-kind-absenden\"", formularHtml, StringComparison.Ordinal);
        Assert.Contains("<p>Sabine Beispiel</p>", formularHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"e.ElternVorname\" value=\"Sabine\" autocomplete", formularHtml, StringComparison.Ordinal);
        Assert.Matches("<input [^>]*id=\"kind-vorname\"[^>]*>", formularHtml);
        Assert.DoesNotContain("value=\"Lina\"", formularHtml, StringComparison.Ordinal);

        using var zweites = await AbsendenAsync(
            $"/freizeit/{_tn}/anmelden", "weiteres-kind-absenden", [.. verborgen, ("e.KindVorname", "Ben"), ("e.KindGeburtsdatum", "2018-01-05"), ("e.Bestaetigt", "true")]);

        Assert.Matches("<h1[^>]*>Anmeldung für Ben</h1>", await HtmlAsync(zweites));
        Assert.Equal(2, _factory.Mails.An(Lina).Count);
        Assert.Equal(token, _factory.Mails.LetzteAn(Lina).Token);
        Assert.Equal(1L, await WertAsync<long>(
            _verbindung, "SELECT count(DISTINCT \"FamilieId\") FROM personen.person WHERE \"Vorname\" IN ('Lina', 'Ben')"));
        var gastLink = await HtmlAsync(await SeiteAsync($"/gast/{token}"));
        Assert.Contains(">Lina</h2>", gastLink, StringComparison.Ordinal);
        Assert.Contains(">Ben</h2>", gastLink, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("weiteres-kind")]
    [InlineData("weiteres-kind-absenden")]
    public async Task Weiteres_Kind_mit_ungueltigem_Gast_Token_ergibt_die_404_Seite(string formular)
    {
        using var antwort = await AbsendenAsync(
            $"/freizeit/{_tn}/anmelden", formular,
            [("e.GastToken", Zugangslink.NeuesToken()), ("e.KindVorname", "Ben"), ("e.KindGeburtsdatum", "2018-01-05"), ("e.Bestaetigt", "true")]);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Contains("Dieser Link ist nicht mehr gültig.", await HtmlAsync(antwort), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gast_Link_Seite_zeigt_die_Anmeldungen_mit_Status_Gueltigkeit_und_Konto_Angebot_ungueltig_404()
    {
        using (var erstes = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", Gast("Lina", "2016-04-01", Lina)))
        {
            Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        }

        var token = _factory.Mails.LetzteAn(Lina).Token;

        using var antwort = await SeiteAsync($"/gast/{token}");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var html = await HtmlAsync(antwort);
        Assert.Matches("<h1[^>]*>Anmeldungen</h1>", html);
        Assert.Contains(">Lina</h2>", html, StringComparison.Ordinal);
        Assert.Contains("Zeltlager Kapf 2027", html, StringComparison.Ordinal);
        Assert.Contains("2. bis 13. August 2027", html, StringComparison.Ordinal);
        Assert.Contains(">Eingegangen</span>", html, StringComparison.Ordinal);
        Assert.Contains("Gast-Link gilt bis ", html, StringComparison.Ordinal);
        Assert.Contains("Konto aus diesen Daten erstellen?", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/app/gast/{token}/konto\"", html, StringComparison.Ordinal);
        Statisch(html);

        using var ungueltig = await SeiteAsync($"/gast/{_tn}");
        Assert.Equal(HttpStatusCode.NotFound, ungueltig.StatusCode);
        Assert.Contains("Dieser Link ist nicht mehr gültig.", await HtmlAsync(ungueltig), StringComparison.Ordinal);
    }

    // ---------- Story 2-4: Freizeitpass über den Gast-Link ----------

    [Fact]
    public async Task Gast_Freizeitpass_ist_ein_statisches_Formular_mit_den_Fragen_der_Freizeit_und_speichert_im_POST()
    {
        await FragenSetzenAsync("allergien,schwimmen,tetanus");
        var (token, anmeldung) = await GastAnmeldungAsync();
        var gastLink = await HtmlAsync(await SeiteAsync($"/gast/{token}"));
        var pfad = $"/gast/{token}/freizeitpass/{anmeldung}";
        Assert.Contains($"href=\"{pfad}\"", gastLink, StringComparison.Ordinal);
        Assert.Contains("Freizeitpass für Lina", gastLink, StringComparison.Ordinal);

        using var seite = await SeiteAsync(pfad);

        Assert.Equal(HttpStatusCode.OK, seite.StatusCode);
        var html = await HtmlAsync(seite);
        Assert.Matches("<h1[^>]*>Freizeitpass für Lina</h1>", html);
        Assert.Contains("<legend>Allergien und Unverträglichkeiten</legend>", html, StringComparison.Ordinal);
        Assert.Matches("<input [^>]*type=\"checkbox\"[^>]*name=\"a.allergien\"[^>]*value=\"milch\"", html);
        Assert.Matches("<input [^>]*type=\"radio\"[^>]*name=\"a.schwimmen\"[^>]*value=\"ja\"", html);
        Assert.Matches("<input [^>]*name=\"a.tetanus\"[^>]*type=\"date\"", html);
        Assert.Contains("name=\"s.allergien\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Medikamente", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Stimmt das noch?", html, StringComparison.Ordinal);
        Statisch(html);

        using var gespeichert = await AbsendenAsync(pfad, "freizeitpass",
            [("a.allergien", "milch"), ("a.allergien", "eier"), ("s.allergien", "Kiwi"), ("a.schwimmen", "ja"), ("a.tetanus", "2024-05-17")]);

        Assert.Equal(HttpStatusCode.OK, gespeichert.StatusCode);
        var nachher = await HtmlAsync(gespeichert);
        Assert.Contains("Freizeitpass gespeichert.", nachher, StringComparison.Ordinal);
        Assert.Matches("<input [^>]*value=\"milch\" checked", nachher);
        Assert.Matches("<input [^>]*value=\"eier\" checked", nachher);
        Assert.Contains("value=\"Kiwi\"", nachher, StringComparison.Ordinal);
        Assert.Equal(3L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));
    }

    [Fact]
    public async Task Gast_Freizeitpass_zeigt_Fehler_am_Feld_mit_Uebersicht_und_Stimmt_noch_bei_veralteten_Angaben()
    {
        await FragenSetzenAsync("schwimmen,tetanus");
        var (token, anmeldung) = await GastAnmeldungAsync();
        var pfad = $"/gast/{token}/freizeitpass/{anmeldung}";

        using var fehler = await AbsendenAsync(pfad, "freizeitpass", [("a.schwimmen", "vielleicht"), ("a.tetanus", "31.02.")]);

        var html = await HtmlAsync(fehler);
        Assert.Contains("href=\"#freizeitpass-schwimmen\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#freizeitpass-tetanus\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"31.02.\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Freizeitpass gespeichert.", html, StringComparison.Ordinal);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.freizeitpass_antwort"));

        (await AbsendenAsync(pfad, "freizeitpass", [("a.schwimmen", "ja")])).Dispose();
        await AusfuehrenAsync(_verbindung, "UPDATE personen.freizeitpass_antwort SET \"BestaetigtAm\" = @t", ("t", _factory.Uhr.GetUtcNow().AddMonths(-13)));
        var veraltet = await HtmlAsync(await SeiteAsync(pfad));
        Assert.Contains("Stimmt das noch?", veraltet, StringComparison.Ordinal);
        Assert.Contains("name=\"b.schwimmen\"", veraltet, StringComparison.Ordinal);

        using var stimmt = await AbsendenAsync(pfad, "freizeitpass", [("a.schwimmen", "ja"), ("b.schwimmen", "true")]);

        Assert.DoesNotContain("Stimmt das noch?", await HtmlAsync(stimmt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gast_Freizeitpass_mit_fremder_Anmeldung_oder_ungueltigem_Link_ergibt_die_404_Seite()
    {
        await FragenSetzenAsync("schwimmen");
        var (token, anmeldung) = await GastAnmeldungAsync();

        using var fremd = await SeiteAsync($"/gast/{token}/freizeitpass/{Guid.CreateVersion7()}");
        using var ungueltig = await SeiteAsync($"/gast/{Zugangslink.NeuesToken()}/freizeitpass/{anmeldung}");

        Assert.Equal(HttpStatusCode.NotFound, fremd.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ungueltig.StatusCode);
        Assert.Contains("Dieser Link ist nicht mehr gültig.", await HtmlAsync(ungueltig), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doppelanmeldung_ueber_die_Seite_zeigt_den_Hinweis_mit_dem_Vornamen_und_legt_nichts_an()
    {
        using (var erstes = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", Gast("Lina", "2016-04-01", Lina)))
        {
            Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        }

        var token = _factory.Mails.LetzteAn(Lina).Token;
        _kindUmleitung = await WertAsync<Guid>(_verbindung, "SELECT \"Id\" FROM personen.person WHERE \"Vorname\" = 'Lina'");

        using var zweites = await AbsendenAsync(
            $"/freizeit/{_tn}/anmelden", "weiteres-kind-absenden",
            [("e.GastToken", token), ("e.ElternVorname", "Sabine"), ("e.Mail", Lina), ("e.KindVorname", "Lina"), ("e.KindGeburtsdatum", "2016-04-01"), ("e.Bestaetigt", "true")]);

        Assert.Equal(HttpStatusCode.OK, zweites.StatusCode);
        var html = await HtmlAsync(zweites);
        Assert.Contains("<span class=\"elkw-alert__title\">Hinweis:</span>", html, StringComparison.Ordinal);
        Assert.Contains("Lina ist für diese Freizeit schon angemeldet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Anmeldung für Lina", html, StringComparison.Ordinal);
        Assert.Equal(1L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung"));
        Assert.Single(_factory.Mails.An(Lina));
    }

    [Fact]
    public async Task Das_Formular_zeigt_die_gespeicherten_Teilnahmebedingungen_statt_des_Ersatztexts()
    {
        var version = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/freizeitseite", Abbruch))).GetProperty("version").GetUInt32();
        using (var patch = await SendenAsync(
                   _miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/freizeitseite",
                   $$"""{"version":{{version}},"alterWert":null,"teilnahmebedingungen":"Die Lagerregeln gelten.\n\nTaschengeld höchstens 20 Euro."}"""))
        {
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        }

        var html = await HtmlAsync(await SeiteAsync($"/freizeit/{_tn}/anmelden"));

        Assert.Contains("<p>Die Lagerregeln gelten.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Taschengeld höchstens 20 Euro.</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Für diese Freizeit sind keine eigenen Teilnahmebedingungen hinterlegt.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nach_dem_Freizeitende_zeigen_Formular_und_Absenden_die_404_Seite()
    {
        string antiforgery;
        using (var vorher = await SeiteAsync($"/freizeit/{_tn}/anmelden"))
        {
            antiforgery = AntiforgeryFeld().Match(await vorher.Content.ReadAsStringAsync(Abbruch)).Groups[1].Value;
        }

        // Freizeitende: 14.08.2027 00:00 Europe/Berlin = 13.08.2027 22:00 UTC.
        _factory.Uhr.SetUtcNow(new DateTimeOffset(2027, 8, 13, 22, 0, 0, TimeSpan.Zero));

        using var seite = await SeiteAsync($"/freizeit/{_tn}/anmelden");
        using var absenden = await FormularSendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", antiforgery, Gast("Lina", "2016-04-01", Lina));

        Assert.Equal(HttpStatusCode.NotFound, seite.StatusCode);
        Assert.Contains("Dieser Anmeldelink ist nicht mehr gültig.", await HtmlAsync(seite), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, absenden.StatusCode);
        Assert.Contains("Dieser Anmeldelink ist nicht mehr gültig.", await HtmlAsync(absenden), StringComparison.Ordinal);
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM personen.gast_zugang"));
        Assert.Equal(0L, await WertAsync<long>(_verbindung, "SELECT count(*) FROM freizeiten.anmeldung"));
    }

    // ---------- Hilfen ----------

    /// <summary>Antwortet beim Anlegen eines weiteren Gast-Kindes mit einer festen Kennung (nur, solange sie gesetzt ist).</summary>
    private sealed class KindUmleitung(Func<Guid?> kind) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            kind() is { } id && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/kinder", StringComparison.Ordinal)
                && request.RequestUri.AbsolutePath.StartsWith("/api/personen/oeffentlich/gaeste/", StringComparison.Ordinal)
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent($"{{\"kindPersonId\":\"{id}\"}}", System.Text.Encoding.UTF8, "application/json"),
                })
                : base.SendAsync(request, cancellationToken);
    }

    private static (string, string)[] Gast(string kind, string geburtsdatum, string mail) =>
    [
        ("e.KindVorname", kind), ("e.KindNachname", "Beispiel"), ("e.KindGeburtsdatum", geburtsdatum),
        ("e.ElternVorname", "Sabine"), ("e.ElternNachname", "Beispiel"), ("e.Mail", mail), ("e.Bestaetigt", "true"),
    ];

    private async Task FragenSetzenAsync(string teilnehmende)
    {
        var version = (await JsonAsync(await _miriam.GetAsync($"/api/freizeiten/{_x}/fragen", Abbruch))).GetProperty("version").GetUInt32();
        using var antwort = await SendenAsync(_miriam, HttpMethod.Patch, $"/api/freizeiten/{_x}/fragen", $$"""{"version":{{version}},"alterWert":null,"teilnehmende":"{{teilnehmende}}"}""");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    /// <summary>Lina als Gast über die Seite anmelden; liefert Gast-Token und Kennung der Anmeldung.</summary>
    private async Task<(string Token, Guid Anmeldung)> GastAnmeldungAsync()
    {
        using (var erstes = await AbsendenAsync($"/freizeit/{_tn}/anmelden", "anmelden", Gast("Lina", "2016-04-01", Lina)))
        {
            Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
        }

        return (_factory.Mails.LetzteAn(Lina).Token, await WertAsync<Guid>(_verbindung, "SELECT \"Id\" FROM freizeiten.anmeldung"));
    }

    private static void Statisch(string html)
    {
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> HtmlAsync(HttpResponseMessage antwort) => WebUtility.HtmlDecode(await antwort.Content.ReadAsStringAsync(Abbruch));

    private async Task<HttpResponseMessage> SeiteAsync(string pfad)
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, pfad);
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, Besucher);
        return await _besucher.SendAsync(anfrage, Abbruch);
    }

    /// <summary>
    /// Wie ein Browser: die Seite laden, das Antiforgery-Feld von Blazor aus dem HTML lesen und das Formular mit demselben Client
    /// (Cookie) als <c>application/x-www-form-urlencoded</c> absenden.
    /// </summary>
    private async Task<HttpResponseMessage> AbsendenAsync(string pfad, string formular, IEnumerable<(string Name, string Wert)> felder)
    {
        string token;
        using (var seite = await SeiteAsync(pfad))
        {
            token = AntiforgeryFeld().Match(await seite.Content.ReadAsStringAsync(Abbruch)).Groups[1].Value;
        }

        return await FormularSendenAsync(pfad, formular, token, felder);
    }

    private async Task<HttpResponseMessage> FormularSendenAsync(string pfad, string formular, string token, IEnumerable<(string Name, string Wert)> felder)
    {
        Assert.NotEmpty(token);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, pfad)
        {
            Content = new FormUrlEncodedContent(
                [new("_handler", formular), new("__RequestVerificationToken", token), .. felder.Select(f => new KeyValuePair<string, string>(f.Name, f.Wert))]),
        };
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, Besucher);
        return await _besucher.SendAsync(anfrage, Abbruch);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryFeld();
}
