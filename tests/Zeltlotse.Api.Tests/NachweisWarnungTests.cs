using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.NachweisSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;
using WarnArt = Zeltlotse.Api.Contracts.Freizeiten.NachweisArt;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Warnhinweise und Erinnerungen bei Nachweisen über die echten Endpunkte (FR-11, CAP-5, AD-13, AD-30, Story 1-6): Warnregel mit
/// Pflicht aus der Org-Richtlinie und „gültig bis“ der Bestätigungen, nur das Team dieser Freizeit, Mandantentrennung, Erinnern
/// mit Einsicht-Anfrage und Mail über die Outbox, höchstens einmal am Tag, und die Pflichtfälle der Freizeit-Route.
/// Freizeit 02.–13.08.2027; die Uhr steht bei der echten Zeit (Sitzungs-Cookies), die Daten der Bestätigungen hängen an „heute“.
/// </summary>
public sealed class NachweisWarnungTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private NachweisSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await ErstellenAsync(postgres, s3);

    /// <summary>Der heutige Kalendertag in Europe/Berlin nach der Uhr des Hosts.</summary>
    private DateOnly Heute => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_s.Factory.Uhr.GetUtcNow(), Berlin).DateTime);

    public ValueTask DisposeAsync() => _s.DisposeAsync();

    private string WarnPfad => $"/api/freizeiten/{_s.FreizeitId}/nachweis-warnungen";

    private string ErinnernPfad => $"{_s.FreizeitPfad}/erinnerungen";

    private Task PflichtAsync(int art, Guid? org = null) => AusfuehrenAsync(
        _s.Verbindung,
        "UPDATE organisationen.nachweis_richtlinie SET \"Pflicht\" = true WHERE \"OrganisationId\" = @o AND \"Art\" = @a",
        ("o", org ?? _s.OrgId), ("a", (short)art));

    /// <summary>Eine Bestätigung wie nach dem Empfang von <c>NachweisEingesehen</c> (Art: 1 Führungszeugnis, 3 Erste Hilfe).</summary>
    private async Task BestaetigungAsync(string mail, int art, DateOnly gueltigBis, Guid? org = null) => await AusfuehrenAsync(
        _s.Verbindung,
        "INSERT INTO organisationen.nachweis_bestaetigung (\"OrganisationId\", \"PersonId\", \"Art\", \"EingesehenAm\", \"GueltigBis\", \"GeprueftVon\") VALUES (@o, @p, @a, now(), @g, @p)",
        ("o", org ?? _s.OrgId), ("p", await _s.PersonIdAsync(mail)), ("a", (short)art), ("g", gueltigBis));

    private async Task<NachweisWarnungen> WarnungenAsync(HttpClient client)
    {
        using var antwort = await client.GetAsync(WarnPfad, Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<NachweisWarnungen>(Abbruch))!;
    }

    private async Task<HttpResponseMessage> ErinnernAsync(HttpClient client, string? mail) =>
        await PostAsync(client, ErinnernPfad, new NachweisErinnerungSenden(mail is null ? null : await _s.PersonIdAsync(mail)));

    [Fact]
    public async Task Matrix_Warnregel_fehlt_laeuft_ab_und_abgelaufen_sortiert_nach_Nachname_ohne_die_eigene_Person()
    {
        await PflichtAsync(3);
        // Jonas: Erste Hilfe gilt ab morgen nicht mehr bis zum Ende; Tobias: Erste Hilfe genau bis zum letzten Tag (einschließlich),
        // Führungszeugnis seit gestern abgelaufen.
        var laeuftAb = Heute.AddDays(1);
        var abgelaufen = Heute.AddDays(-1);
        await BestaetigungAsync(Jonas, 3, laeuftAb);
        await BestaetigungAsync(Tobias, 3, FreizeitHelfer.Letzter);
        await BestaetigungAsync(Tobias, 1, abgelaufen);

        var warnungen = await WarnungenAsync(_s.MiriamClient);

        Assert.Equal(
            [$"Jonas Hüttner: Fuehrungszeugnis Fehlt, ErsteHilfe LaeuftAb {laeuftAb:yyyy-MM-dd}", $"Tobias Wanderer: Fuehrungszeugnis Abgelaufen {abgelaufen:yyyy-MM-dd}"],
            warnungen.Personen.Select(Text));
        Assert.Equal(await _s.PersonIdAsync(Jonas), warnungen.Personen[0].PersonId);

        using var roh = await _s.MiriamClient.GetAsync(WarnPfad, Abbruch);
        var json = await JsonAsync(roh);
        Assert.Equal(["personen"], Namen(json));
        var jonas = json.GetProperty("personen")[0];
        Assert.Equal(["nachname", "personId", "vorname", "warnungen"], Namen(jonas));
        var fehlt = jonas.GetProperty("warnungen")[0];
        Assert.Equal(["art", "grund"], Namen(fehlt));
        Assert.Equal(("fuehrungszeugnis", "fehlt"), (fehlt.GetProperty("art").GetString(), fehlt.GetProperty("grund").GetString()));
        var zweite = jonas.GetProperty("warnungen")[1];
        Assert.Equal(
            ("ersteHilfe", "laeuftAb", laeuftAb.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            (zweite.GetProperty("art").GetString(), zweite.GetProperty("grund").GetString(), zweite.GetProperty("gueltigBis").GetString()));
        Assert.Equal("abgelaufen", json.GetProperty("personen")[1].GetProperty("warnungen")[0].GetProperty("grund").GetString());
    }

    [Fact]
    public async Task Ohne_Warnung_fehlt_die_Person_JuLeiCa_ohne_Pflicht_zaehlt_nicht()
    {
        await BestaetigungAsync(Jonas, 1, new DateOnly(2030, 11, 3));

        var warnungen = await WarnungenAsync(_s.MiriamClient);

        Assert.Equal(["Tobias"], warnungen.Personen.Select(p => p.Vorname));
        Assert.All(warnungen.Personen.SelectMany(p => p.Warnungen), w => Assert.Equal(WarnArt.Fuehrungszeugnis, w.Art));
    }

    [Fact]
    public async Task Nach_der_Freizeit_keine_Warnung_fuer_eine_Bestaetigung_bis_nach_dem_letzten_Tag()
    {
        await BestaetigungAsync(Jonas, 1, new DateOnly(2027, 8, 20));
        await BestaetigungAsync(Tobias, 1, new DateOnly(2027, 8, 20));
        // Nach der Freizeit: Die Bestätigungen enden vor heute, galten aber bis zum letzten Tag.
        _s.Factory.Uhr.SetUtcNow(new DateTimeOffset(2027, 9, 1, 8, 0, 0, TimeSpan.Zero));
        using var miriam = await _s.AngemeldetAsync(Miriam);

        Assert.Empty((await WarnungenAsync(miriam)).Personen);
    }

    [Fact]
    public async Task Eigene_Person_und_Personen_nur_aus_einer_anderen_Freizeit_fehlen()
    {
        var andere = await FreizeitHelfer.AnlegenAsync(_s.AndreaClient, _s.OrgId, "Herbstfreizeit");
        using var lea = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.AndreaClient, _s.OrgId, andere, Lea);
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);

        var namen = (await WarnungenAsync(_s.MiriamClient)).Personen.Select(p => p.Vorname).ToList();

        Assert.Equal(["Jonas", "Tobias"], namen);
    }

    [Fact]
    public async Task Mandant_Bestaetigung_derselben_Person_in_einer_anderen_Organisation_zaehlt_nicht()
    {
        await BestaetigungAsync(Jonas, 1, new DateOnly(2030, 11, 3), org: Guid.CreateVersion7());

        var jonas = (await WarnungenAsync(_s.MiriamClient)).Personen.Single(p => p.Vorname == "Jonas");

        Assert.Equal([new NachweisWarnung(WarnArt.Fuehrungszeugnis, NachweisWarnungGrund.Fehlt, null)], jonas.Warnungen);
    }

    [Fact]
    public async Task Rechte_Leitung_200_Team_403_fremde_Freizeit_404_Org_Admin_ohne_Freizeit_Rolle_404()
    {
        using var lea = await _s.AngemeldetAsync(Lea);
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"FreizeitId\", \"PersonId\", \"OrganisationId\", \"Rolle\", \"VergebenAm\") VALUES (@f, @p, @o, 2, now())",
            ("f", _s.FreizeitId), ("p", await _s.PersonIdAsync(Lea)), ("o", _s.OrgId));
        var andere = await FreizeitHelfer.AnlegenAsync(_s.AndreaClient, _s.OrgId, "Andere Freizeit");
        var jonasId = await _s.PersonIdAsync(Jonas);

        using var leitung = await _s.MiriamClient.GetAsync(WarnPfad, Abbruch);
        using var team = await lea.GetAsync(WarnPfad, Abbruch);
        using var fremd = await _s.MiriamClient.GetAsync($"/api/freizeiten/{andere}/nachweis-warnungen", Abbruch);
        using var orgAdmin = await _s.AndreaClient.GetAsync(WarnPfad, Abbruch);
        using var teamErinnern = await PostAsync(lea, ErinnernPfad, new NachweisErinnerungSenden(jonasId));
        using var fremdErinnern = await PostAsync(_s.MiriamClient, $"/api/freizeiten/{andere}/personen/nachweise/erinnerungen", new NachweisErinnerungSenden(jonasId));
        using var orgAdminErinnern = await PostAsync(_s.AndreaClient, ErinnernPfad, new NachweisErinnerungSenden(jonasId));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
            [leitung.StatusCode, team.StatusCode, fremd.StatusCode, orgAdmin.StatusCode, teamErinnern.StatusCode, fremdErinnern.StatusCode, orgAdminErinnern.StatusCode]);
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_erinnerung"));
    }

    [Fact]
    public async Task Miriam_erinnert_Jonas_ohne_offene_Einsicht_fragt_an_und_nach_der_Zustellung_liegt_genau_eine_Mail_vor()
    {
        var jonasId = await _s.PersonIdAsync(Jonas);
        var miriamId = await _s.PersonIdAsync(Miriam);
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM personen.nachweis_einsicht WHERE \"PersonId\" = @p", ("p", jonasId));
        var mailsVorher = _s.Factory.Mails.An(Jonas).Count;
        var jetzt = _s.Factory.Uhr.GetUtcNow();

        using var antwort = await ErinnernAsync(_s.MiriamClient, Jonas);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["erinnertAm"], Namen(json));
        Assert.Equal(jetzt, json.GetProperty("erinnertAm").GetDateTimeOffset());
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung,
            "SELECT count(*) FROM personen.nachweis_einsicht WHERE \"PersonId\" = @p AND \"Status\" = 1 AND \"PruefendePersonId\" = @m AND \"AngefragtVon\" = @m AND \"FreizeitId\" = @f",
            ("p", jonasId), ("m", miriamId), ("f", _s.FreizeitId)));
        var inhalt = await WertAsync<string>(_s.Verbindung, "SELECT \"Inhalt\"::text FROM personen.outbox WHERE \"Typ\" LIKE '%NachweisErinnerungMail'");
        Assert.DoesNotContain("@", inhalt, StringComparison.Ordinal);
        Assert.DoesNotContain("Miriam", inhalt, StringComparison.Ordinal);
        Assert.DoesNotContain("Jonas", inhalt, StringComparison.Ordinal);
        Assert.Equal(mailsVorher, _s.Factory.Mails.An(Jonas).Count);

        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);

        var mails = _s.Factory.Mails.An(Jonas);
        Assert.Equal(mailsVorher + 1, mails.Count);
        var mail = mails[^1];
        Assert.Equal("Erinnerung: Nachweise in Zeltlotse", mail.Betreff);
        Assert.EndsWith("/app/konto/nachweise", mail.Link, StringComparison.Ordinal);
        Assert.Contains("Miriam Zeltner aus der Leitung einer Freizeit", mail.Text, StringComparison.Ordinal);
        var zeile = (await ListeAsync(_s.MiriamClient, _s.FreizeitPfad)).Single(p => p.PersonId == jonasId);
        Assert.Equal(Mikro(jetzt), zeile.ErinnertAm);
        var anfrage = Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen);
        Assert.Equal(("Miriam", (Guid?)_s.FreizeitId), (anfrage.AngefragtVon?.Vorname, anfrage.FreizeitId));
    }

    [Fact]
    public async Task Erinnern_mit_offener_automatischer_Einsicht_laesst_sie_unveraendert()
    {
        var jonasId = await _s.PersonIdAsync(Jonas);
        var vorher = await AnfrageAsync(_s.JonasClient);

        using var antwort = await ErinnernAsync(_s.MiriamClient, Jonas);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var anfrage = Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen);
        Assert.Equal((vorher, (PersonenNameAntwort?)null), (anfrage.Id, anfrage.AngefragtVon));
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_einsicht WHERE \"PersonId\" = @p AND \"PruefendePersonId\" IS NULL", ("p", jonasId)));
    }

    [Fact]
    public async Task Grenzen_zweites_Mal_am_selben_Tag_409_naechster_Tag_200_selbst_ohne_Rolle_404_ohne_Person_400()
    {
        (await ErinnernAsync(_s.MiriamClient, Jonas)).Dispose();

        using var nochmal = await ErinnernAsync(_s.TobiasClient, Jonas);
        Assert.Equal(HttpStatusCode.Conflict, nochmal.StatusCode);
        Assert.Equal("konflikt", Code(await ProblemAsync(nochmal)));

        _s.Factory.Uhr.Advance(TimeSpan.FromDays(1));
        using var morgen = await ErinnernAsync(_s.TobiasClient, Jonas);
        Assert.Equal(HttpStatusCode.OK, morgen.StatusCode);

        using var lea = await _s.AngemeldetAsync(Lea);
        using var selbst = await ErinnernAsync(_s.MiriamClient, Miriam);
        using var ohneRolle = await ErinnernAsync(_s.MiriamClient, Lea);
        using var ohnePerson = await ErinnernAsync(_s.MiriamClient, null);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.BadRequest), (selbst.StatusCode, ohneRolle.StatusCode, ohnePerson.StatusCode));
        var problem = await ProblemAsync(ohnePerson);
        Assert.Equal("Person fehlt.", problem.GetProperty("errors").GetProperty("personId")[0].GetString());

        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_erinnerung"));
        Assert.Equal(2L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%NachweisErinnerungMail'"));
    }

    [Fact]
    public async Task Erinnerungen_gelten_je_Freizeit()
    {
        var andere = await FreizeitHelfer.AnlegenAsync(_s.AndreaClient, _s.OrgId, "Herbstfreizeit");
        using var lea = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.AndreaClient, _s.OrgId, andere, Lea);
        using var jonasAndere = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.AndreaClient, _s.OrgId, andere, Jonas);
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);
        var jonasId = await _s.PersonIdAsync(Jonas);

        using var leaErinnert = await PostAsync(lea, $"/api/freizeiten/{andere}/personen/nachweise/erinnerungen", new NachweisErinnerungSenden(jonasId));
        Assert.Equal(HttpStatusCode.OK, leaErinnert.StatusCode);

        var zeile = (await ListeAsync(_s.MiriamClient, _s.FreizeitPfad)).Single(p => p.PersonId == jonasId);
        Assert.Null(zeile.ErinnertAm);
        using var miriamErinnert = await ErinnernAsync(_s.MiriamClient, Jonas);
        Assert.Equal(HttpStatusCode.OK, miriamErinnert.StatusCode);
        Assert.Equal(2L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_erinnerung WHERE \"PersonId\" = @p", ("p", jonasId)));
    }

    [Fact]
    public async Task Freizeit_einer_fremden_Organisation_404_fuer_Warnhinweise_und_Erinnern()
    {
        var (orgB, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.GerdClient, "Jugendwerk B", "sara.kochloeffel@example.org");
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgB, "Fremdes Zeltlager");

        using var warnungen = await _s.MiriamClient.GetAsync($"/api/freizeiten/{fremde}/nachweis-warnungen", Abbruch);
        using var erinnern = await PostAsync(
            _s.MiriamClient, $"/api/freizeiten/{fremde}/personen/nachweise/erinnerungen", new NachweisErinnerungSenden(await _s.PersonIdAsync(Jonas)));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (warnungen.StatusCode, erinnern.StatusCode));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_erinnerung"));
    }

    [Fact]
    public async Task Pruefliste_auf_der_Org_Route_zeigt_keine_Erinnerung()
    {
        (await ErinnernAsync(_s.MiriamClient, Jonas)).Dispose();
        var jetzt = _s.Factory.Uhr.GetUtcNow();

        var org = (await ListeAsync(_s.AndreaClient, _s.OrgPfad)).Single(p => p.Vorname == "Jonas");
        var freizeit = (await ListeAsync(_s.MiriamClient, _s.FreizeitPfad)).Single(p => p.Vorname == "Jonas");

        Assert.Null(org.ErinnertAm);
        Assert.Equal(Mikro(jetzt), freizeit.ErinnertAm);
    }

    /// <summary>PostgreSQL speichert Zeitpunkte auf die Mikrosekunde genau.</summary>
    private static DateTimeOffset Mikro(DateTimeOffset zeitpunkt) => new(zeitpunkt.Ticks - (zeitpunkt.Ticks % 10), zeitpunkt.Offset);

    private static string Text(NachweisWarnungPerson person) =>
        $"{person.Vorname} {person.Nachname}: " + string.Join(", ", person.Warnungen.Select(w =>
            w.GueltigBis is { } bis ? $"{w.Art} {w.Grund} {bis:yyyy-MM-dd}" : $"{w.Art} {w.Grund}"));
}
