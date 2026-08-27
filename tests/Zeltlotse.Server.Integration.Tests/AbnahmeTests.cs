using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Zeltlotse.Core.Persistenz;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;

namespace Zeltlotse.Server.Integration.Tests;

/// <summary>
/// Ein Test je Abnahmekriterium aus requirements.md. Jeder läuft gegen eine
/// eigene, frisch migrierte Datenbank im gemeinsamen Container.
/// </summary>
[Collection(nameof(DatenbankSammlung))]
public class AbnahmeTests(DatenbankFixture datenbank)
{
    private const string Kennwort = "Entwicklung!1";
    private const string BetreiberMail = "betreiber@zeltlotse.local";

    // AK 1
    [Fact]
    public async Task Einrichtung_legt_den_ersten_GlobalAdmin_an_und_schliesst_sich_danach()
    {
        await using var fabrik = await FabrikAsync();

        Assert.True(await fabrik.Angemeldet().GetFromJsonAsync<bool>("/api/einrichtung/noetig"));

        await fabrik.EinrichtenAsync(BetreiberMail, Kennwort);

        Assert.False(await fabrik.Angemeldet().GetFromJsonAsync<bool>("/api/einrichtung/noetig"));

        var zweiter = await fabrik.Angemeldet().PostAsJsonAsync(
            "/api/einrichtung", new EinrichtungAnfrage("Dritter", "dritter@zeltlotse.local", Kennwort));

        Assert.Equal(HttpStatusCode.Conflict, zweiter.StatusCode);
    }

    // AK 2
    [Fact]
    public async Task Organisation_bekommt_eine_Adresse_aus_dem_Namen()
    {
        await using var fabrik = await FabrikAsync();
        var betreiber = fabrik.Angemeldet(await fabrik.EinrichtenAsync(BetreiberMail, Kennwort));

        var erste = await OrganisationAnlegenAsync(betreiber, "Ev. Kirchengemeinde Musterstadt");
        var zweite = await OrganisationAnlegenAsync(betreiber, "Ev. Kirchengemeinde Musterstadt");

        Assert.Equal("ev-kirchengemeinde-musterstadt", erste.Slug);
        Assert.Equal("ev-kirchengemeinde-musterstadt-2", zweite.Slug);
    }

    // AK 3 — der Betreiber kommt an keine Inhalte, auch nicht über die Adresse.
    [Fact]
    public async Task Betreiber_sieht_keine_Inhalte_einer_Organisation()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var freizeiten = await szene.Betreiber.GetAsync($"/api/o/{szene.Slug}/freizeiten");
        var mitglieder = await szene.Betreiber.GetAsync($"/api/o/{szene.Slug}/mitglieder");
        var meine = await szene.Betreiber.GetFromJsonAsync<List<FreizeitDto>>("/api/freizeiten/meine");

        Assert.Equal(HttpStatusCode.NotFound, freizeiten.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mitglieder.StatusCode);
        Assert.Empty(meine!);
    }

    // AK 4
    [Fact]
    public async Task Einladung_erzeugt_das_Konto_und_wirkt_genau_einmal()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var ich = await szene.Leitung.GetFromJsonAsync<AngemeldeterNutzerDto>("/api/ich");

        Assert.Equal("leitung@zeltlotse.local", ich!.EMail);
        Assert.Equal(OrgRolle.OrgAdmin, Assert.Single(ich.Organisationen).EigeneRolle);

        var nochmal = await fabrik.Angemeldet().PostAsJsonAsync(
            "/api/einladungen/einloesen", new EinladungEinloesen(szene.EinladungsToken, Kennwort));

        Assert.Equal(HttpStatusCode.BadRequest, nochmal.StatusCode);
    }

    // AK 5
    [Fact]
    public async Task Freizeit_laesst_sich_allein_mit_einem_Namen_anlegen()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var antwort = await szene.Leitung.PostAsJsonAsync(
            $"/api/o/{szene.Slug}/freizeiten",
            new FreizeitAnlegen("Sommerfreizeit 2027", null, null, null));

        antwort.EnsureSuccessStatusCode();
        var freizeit = await antwort.Content.ReadFromJsonAsync<FreizeitDto>();

        Assert.Equal("Sommerfreizeit 2027", freizeit!.Name);
        Assert.Null(freizeit.Beginn);
        Assert.Null(freizeit.Ort);
        Assert.Equal(FreizeitStatus.Offen, freizeit.Status);
    }

    // AK 6
    [Fact]
    public async Task Mitarbeiter_sieht_ausschliesslich_die_eigenen_Freizeiten()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var zugeordnet = await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Mit Team");
        await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Ohne Team");

        var mitarbeiter = await MitarbeiterAsync(fabrik, szene, zugeordnet.Id);

        var meine = await mitarbeiter.GetFromJsonAsync<List<FreizeitDto>>("/api/freizeiten/meine");

        Assert.Equal("Mit Team", Assert.Single(meine!).Name);
    }

    // AK 7
    [Fact]
    public async Task Fremde_Organisation_liefert_keine_Inhalte()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var fremde = await OrganisationAnlegenAsync(szene.Betreiber, "Fremdes Werk");

        var antwort = await szene.Leitung.GetAsync($"/api/o/{fremde.Slug}/freizeiten");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    // AK 8
    [Fact]
    public async Task Geloeschte_Freizeit_liegt_im_Papierkorb_und_kehrt_zurueck()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var freizeit = await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Versehen");

        (await szene.Leitung.DeleteAsync($"/api/o/{szene.Slug}/freizeiten/{freizeit.Id}"))
            .EnsureSuccessStatusCode();

        Assert.Empty(await szene.Leitung
            .GetFromJsonAsync<List<FreizeitDto>>($"/api/o/{szene.Slug}/freizeiten") ?? []);

        var papierkorb = await szene.Leitung
            .GetFromJsonAsync<List<PapierkorbEintragDto>>($"/api/o/{szene.Slug}/papierkorb");

        var eintrag = Assert.Single(papierkorb!);
        Assert.Equal("Versehen", eintrag.Name);
        Assert.InRange(eintrag.VerbleibendeTage, 29, 30);

        (await szene.Leitung.PostAsync(
            $"/api/o/{szene.Slug}/papierkorb/{freizeit.Id}/wiederherstellen", null))
            .EnsureSuccessStatusCode();

        Assert.Single(await szene.Leitung
            .GetFromJsonAsync<List<FreizeitDto>>($"/api/o/{szene.Slug}/freizeiten") ?? []);
    }

    // AK 9 — beide Schritte sind nötig, keiner allein genügt.
    [Fact]
    public async Task Organisation_wird_nur_nach_Antrag_und_nur_vom_Betreiber_geloescht()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var ohneAntrag = await szene.Betreiber.PostAsync(
            $"/api/verwaltung/organisationen/{szene.OrganisationId}/loeschung-ausfuehren", null);

        Assert.Equal(HttpStatusCode.Conflict, ohneAntrag.StatusCode);

        (await szene.Leitung.PostAsync($"/api/o/{szene.Slug}/loeschantrag", null))
            .EnsureSuccessStatusCode();

        // Die Leitung stellt den Antrag, führt ihn aber nicht selbst aus.
        var durchLeitung = await szene.Leitung.PostAsync(
            $"/api/verwaltung/organisationen/{szene.OrganisationId}/loeschung-ausfuehren", null);

        Assert.Equal(HttpStatusCode.Forbidden, durchLeitung.StatusCode);

        (await szene.Betreiber.PostAsync(
            $"/api/verwaltung/organisationen/{szene.OrganisationId}/loeschung-ausfuehren", null))
            .EnsureSuccessStatusCode();

        Assert.Empty(await szene.Betreiber
            .GetFromJsonAsync<List<OrganisationVerwaltungDto>>("/api/verwaltung/organisationen") ?? []);
    }

    // AK 11
    [Fact]
    public async Task Erneuerung_haelt_die_Anmeldung_ueber_das_Cookie()
    {
        await using var fabrik = await FabrikAsync();
        await fabrik.EinrichtenAsync(BetreiberMail, Kennwort);

        // Ein Client mit Cookie-Speicher: das Erneuerungstoken bleibt hängen,
        // das Zugriffstoken wird bewusst nicht mitgeführt.
        var browser = fabrik.Angemeldet();

        (await browser.PostAsJsonAsync("/api/auth/anmelden",
            new AnmeldungAnfrage(BetreiberMail, Kennwort))).EnsureSuccessStatusCode();

        var erneuert = await browser.PostAsync("/api/auth/erneuern", null);
        erneuert.EnsureSuccessStatusCode();

        var neu = await erneuert.Content.ReadFromJsonAsync<AnmeldungAntwort>();

        Assert.False(string.IsNullOrWhiteSpace(neu!.Zugriffstoken));

        var ich = await fabrik.Angemeldet(neu.Zugriffstoken)
            .GetFromJsonAsync<AngemeldeterNutzerDto>("/api/ich");

        Assert.Equal(BetreiberMail, ich!.EMail);
    }

    /// <summary>
    /// Ohne die Anfrage-Kennung darf die Erneuerung nicht greifen — sonst
    /// könnte ein fremdes Formular sie auslösen.
    /// </summary>
    [Fact]
    public async Task Erneuerung_ohne_Anfrage_Kennung_wird_abgewiesen()
    {
        await using var fabrik = await FabrikAsync();
        await fabrik.EinrichtenAsync(BetreiberMail, Kennwort);

        var browser = fabrik.Angemeldet();

        (await browser.PostAsJsonAsync("/api/auth/anmelden",
            new AnmeldungAnfrage(BetreiberMail, Kennwort))).EnsureSuccessStatusCode();

        var ohneKennung = fabrik.CreateClient();
        var antwort = await ohneKennung.PostAsync("/api/auth/erneuern", null);

        Assert.Equal(HttpStatusCode.Forbidden, antwort.StatusCode);
    }

    // AK 10 — die 30-Tage-Zusage muss ohne Zutun eingehalten werden.
    [Fact]
    public async Task Aufraeumen_entfernt_nur_was_die_Frist_ueberschritten_hat()
    {
        var verbindung = await datenbank.NeueDatenbankAsync();
        await using var fabrik = new ZeltlotseFabrik(verbindung);
        var szene = await SzenarioAsync(fabrik);

        var frisch = await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Gerade gelöscht");
        var alt = await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Lange gelöscht");

        foreach (var id in new[] { frisch.Id, alt.Id })
        {
            (await szene.Leitung.DeleteAsync($"/api/o/{szene.Slug}/freizeiten/{id}"))
                .EnsureSuccessStatusCode();
        }

        await using (var wartung = datenbank.Kontext(new MandantKontext { Wartung = true }, verbindung))
        {
            await wartung.Freizeiten
                .IgnoreQueryFilters()
                .Where(f => f.Id == alt.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    f => f.GeloeschtAm, DateTimeOffset.UtcNow.AddDays(-31)));
        }

        await new Aufraeumdienst(fabrik.Services, NullLogger<Aufraeumdienst>.Instance)
            .AufraeumenAsync(CancellationToken.None);

        var papierkorb = await szene.Leitung
            .GetFromJsonAsync<List<PapierkorbEintragDto>>($"/api/o/{szene.Slug}/papierkorb");

        Assert.Equal("Gerade gelöscht", Assert.Single(papierkorb!).Name);
    }

    /// <summary>
    /// Fehlende Zugehörigkeit und fehlende Rolle sind zwei verschiedene Dinge.
    /// Wer eine falsche Begründung liest, sucht den Fehler an der falschen
    /// Stelle — und die Oberfläche zeigt sonst „gehört dir nicht" an jemanden,
    /// der sehr wohl dazugehört.
    /// </summary>
    [Fact]
    public async Task Fehlende_Rolle_antwortet_anders_als_fehlende_Zugehoerigkeit()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var freizeit = await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Mit Team");
        var mitarbeiter = await MitarbeiterAsync(fabrik, szene, freizeit.Id);

        // Gehört zur Organisation, hat aber nicht die Rolle dafür.
        var ohneRolle = await mitarbeiter.GetAsync($"/api/o/{szene.Slug}/mitglieder");

        // Gehört gar nicht dazu.
        var fremde = await OrganisationAnlegenAsync(szene.Betreiber, "Fremdes Werk");
        var ohneZugehoerigkeit = await mitarbeiter.GetAsync($"/api/o/{fremde.Slug}/mitglieder");

        Assert.Equal(HttpStatusCode.Forbidden, ohneRolle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ohneZugehoerigkeit.StatusCode);
    }

    /// <summary>
    /// Szenario S4: Zwei der drei Mitarbeiter gehören bereits zur Organisation.
    /// Ohne diesen Weg bräuchten sie eine Einladung für etwas, das ein Klick
    /// sein sollte.
    /// </summary>
    [Fact]
    public async Task Bestehende_Mitglieder_lassen_sich_ohne_Einladung_zuordnen()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var erste = await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Erste");
        await MitarbeiterAsync(fabrik, szene, erste.Id);

        var zweite = await FreizeitAnlegenAsync(szene.Leitung, szene.Slug, "Zweite");

        var kandidaten = await szene.Leitung.GetFromJsonAsync<List<KandidatDto>>(
            $"/api/o/{szene.Slug}/freizeiten/{zweite.Id}/kandidaten");

        var timo = Assert.Single(kandidaten!, k => k.EMail == "team@zeltlotse.local");
        Assert.Equal("Timo Teichmann", timo.Name);

        (await szene.Leitung.PostAsJsonAsync(
            $"/api/o/{szene.Slug}/freizeiten/{zweite.Id}/team",
            new TeamZuordnung(timo.NutzerId, FreizeitRolle.Mitarbeiter)))
            .EnsureSuccessStatusCode();

        var team = await szene.Leitung.GetFromJsonAsync<List<FreizeitTeamDto>>(
            $"/api/o/{szene.Slug}/freizeiten/{zweite.Id}/team");

        Assert.Equal("Timo Teichmann", Assert.Single(team!).Name);

        // Wer im Team ist, taucht nicht noch einmal als Vorschlag auf.
        var danach = await szene.Leitung.GetFromJsonAsync<List<KandidatDto>>(
            $"/api/o/{szene.Slug}/freizeiten/{zweite.Id}/kandidaten");

        Assert.DoesNotContain(danach!, k => k.NutzerId == timo.NutzerId);
    }

    /// <summary>
    /// Ein verlorener Einladungslink lässt sich nicht wieder anzeigen — der
    /// Klartext existiert genau einmal. Stattdessen wird die alte Einladung
    /// entwertet und eine neue ausgegeben.
    /// </summary>
    [Fact]
    public async Task Einladung_neu_erzeugen_entwertet_die_alte()
    {
        await using var fabrik = await FabrikAsync();
        var szene = await SzenarioAsync(fabrik);

        var erzeugt = await szene.Leitung.PostAsJsonAsync(
            $"/api/o/{szene.Slug}/einladungen",
            new EinladungAnlegen("Neue Person", "neu@zeltlotse.local",
                Einladungsziel.Organisation, null, null, null));

        erzeugt.EnsureSuccessStatusCode();
        var alte = await erzeugt.Content.ReadFromJsonAsync<EinladungErzeugtDto>();
        var alterToken = alte!.Link[(alte.Link.LastIndexOf('/') + 1)..];

        var erneuert = await szene.Leitung.PostAsync(
            $"/api/o/{szene.Slug}/einladungen/{alte.Id}/erneuern", null);

        erneuert.EnsureSuccessStatusCode();
        var neue = await erneuert.Content.ReadFromJsonAsync<EinladungErzeugtDto>();

        Assert.NotEqual(alte.Link, neue!.Link);
        Assert.Equal("Neue Person", neue.Name);

        var alterVersuch = await fabrik.Angemeldet().GetAsync($"/api/einladungen/{alterToken}");
        Assert.Equal(HttpStatusCode.NotFound, alterVersuch.StatusCode);

        var neuerToken = neue.Link[(neue.Link.LastIndexOf('/') + 1)..];
        var neuerVersuch = await fabrik.Angemeldet().GetAsync($"/api/einladungen/{neuerToken}");
        neuerVersuch.EnsureSuccessStatusCode();
    }

    // ---------- Aufbau ----------

    private async Task<ZeltlotseFabrik> FabrikAsync()
        => new(await datenbank.NeueDatenbankAsync());

    private sealed record Szenario(
        HttpClient Betreiber,
        HttpClient Leitung,
        Guid OrganisationId,
        string Slug,
        string EinladungsToken);

    /// <summary>
    /// Betreiber richtet ein, nimmt eine Organisation auf, lädt die
    /// Organisationsleitung ein — genau der Weg aus Szenario S2.
    /// </summary>
    private static async Task<Szenario> SzenarioAsync(ZeltlotseFabrik fabrik)
    {
        var betreiber = fabrik.Angemeldet(await fabrik.EinrichtenAsync(BetreiberMail, Kennwort));
        var organisation = await OrganisationAnlegenAsync(betreiber, "Ev. Kirchengemeinde Musterstadt");

        var einladung = await betreiber.PostAsJsonAsync(
            $"/api/verwaltung/organisationen/{organisation.Id}/leitung",
            new EinladungAnlegen("Lena Leitner", "leitung@zeltlotse.local", Einladungsziel.Organisation, null, null, null));

        einladung.EnsureSuccessStatusCode();
        var erzeugt = await einladung.Content.ReadFromJsonAsync<EinladungErzeugtDto>();
        var token = erzeugt!.Link[(erzeugt.Link.LastIndexOf('/') + 1)..];

        var eingeloest = await fabrik.Angemeldet().PostAsJsonAsync(
            "/api/einladungen/einloesen", new EinladungEinloesen(token, Kennwort));

        eingeloest.EnsureSuccessStatusCode();
        var anmeldung = await eingeloest.Content.ReadFromJsonAsync<AnmeldungAntwort>();

        return new Szenario(
            betreiber,
            fabrik.Angemeldet(anmeldung!.Zugriffstoken),
            organisation.Id,
            organisation.Slug,
            token);
    }

    private static async Task<HttpClient> MitarbeiterAsync(
        ZeltlotseFabrik fabrik, Szenario szene, Guid freizeitId)
    {
        var einladung = await szene.Leitung.PostAsJsonAsync(
            $"/api/o/{szene.Slug}/freizeiten/{freizeitId}/einladungen",
            new EinladungAnlegen(
                "Timo Teichmann", "team@zeltlotse.local", Einladungsziel.Freizeit,
                null, FreizeitRolle.Mitarbeiter, freizeitId));

        einladung.EnsureSuccessStatusCode();
        var erzeugt = await einladung.Content.ReadFromJsonAsync<EinladungErzeugtDto>();
        var token = erzeugt!.Link[(erzeugt.Link.LastIndexOf('/') + 1)..];

        var eingeloest = await fabrik.Angemeldet().PostAsJsonAsync(
            "/api/einladungen/einloesen", new EinladungEinloesen(token, Kennwort));

        eingeloest.EnsureSuccessStatusCode();
        var anmeldung = await eingeloest.Content.ReadFromJsonAsync<AnmeldungAntwort>();

        return fabrik.Angemeldet(anmeldung!.Zugriffstoken);
    }

    private static async Task<OrganisationVerwaltungDto> OrganisationAnlegenAsync(
        HttpClient betreiber, string name)
    {
        var antwort = await betreiber.PostAsJsonAsync(
            "/api/verwaltung/organisationen", new OrganisationAnlegen(name));

        antwort.EnsureSuccessStatusCode();

        return (await antwort.Content.ReadFromJsonAsync<OrganisationVerwaltungDto>())!;
    }

    private static async Task<FreizeitDto> FreizeitAnlegenAsync(
        HttpClient leitung, string slug, string name)
    {
        var antwort = await leitung.PostAsJsonAsync(
            $"/api/o/{slug}/freizeiten", new FreizeitAnlegen(name, null, null, null));

        antwort.EnsureSuccessStatusCode();

        return (await antwort.Content.ReadFromJsonAsync<FreizeitDto>())!;
    }
}
