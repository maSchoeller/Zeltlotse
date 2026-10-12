using System.Net;
using System.Net.Http.Json;
using System.Text;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.NachweisSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Einsicht in Nachweise über die echten Endpunkte (FR-10, FR-12, AD-9, AD-30) mit S3: Freigeben, Prüfung übernehmen, Datei
/// ansehen, Bestätigen (mit Zustellung an Organisationen), Ablehnen mit neuer Anfrage, zwei Organisationen, Anfrage von Hand
/// und die Pflichtfälle der Freizeit- und Organisations-Routen.
/// </summary>
public sealed class NachweisEinsichtTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private NachweisSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await ErstellenAsync(postgres, s3);

    public ValueTask DisposeAsync() => _s.DisposeAsync();

    [Fact]
    public async Task Freigeben_fuer_14_Tage_mit_zwei_Posten_ohne_Datei_409_fremd_404()
    {
        var einsicht = await AnfrageAsync(_s.JonasClient);
        await NachweisAsync(_s.JonasClient, "juleica", "2024-05-20");
        using (var ohneDatei = await FreigebenAsync(_s.JonasClient, einsicht))
        {
            Assert.Equal(HttpStatusCode.Conflict, ohneDatei.StatusCode);
            Assert.Equal("konflikt", Code(await ProblemAsync(ohneDatei)));
        }

        await NachweisAsync(_s.JonasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        await NachweisAsync(_s.JonasClient, "ersteHilfe", "2025-04-15", Png);
        using var fremd = await FreigebenAsync(_s.MiriamClient, einsicht);
        using var freigeben = await FreigebenAsync(_s.JonasClient, einsicht);

        Assert.Equal(HttpStatusCode.NotFound, fremd.StatusCode);
        Assert.Equal(HttpStatusCode.OK, freigeben.StatusCode);
        var json = await JsonAsync(freigeben);
        Assert.Equal(["freigegebenBis"], Namen(json));
        Assert.Equal(_s.Factory.Uhr.GetUtcNow() + TimeSpan.FromDays(14), json.GetProperty("freigegebenBis").GetDateTimeOffset());
        var anfrage = Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen);
        Assert.Equal(EinsichtStatus.Freigegeben, anfrage.Status);
        using (var laeuftSchon = await FreigebenAsync(_s.JonasClient, einsicht))
        {
            Assert.Equal(HttpStatusCode.Conflict, laeuftSchon.StatusCode);
            Assert.Equal("konflikt", Code(await ProblemAsync(laeuftSchon)));
        }

        using var pruefen = await PruefenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);
        Assert.Equal(HttpStatusCode.NoContent, pruefen.StatusCode);
        var detail = await DetailAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);
        Assert.Equal(
            [(Zeltlotse.Api.Contracts.Personen.NachweisArt.Fuehrungszeugnis, PostenErgebnis.Offen), (Zeltlotse.Api.Contracts.Personen.NachweisArt.ErsteHilfe, PostenErgebnis.Offen)],
            detail.Posten.Select(p => (p.Art, p.Ergebnis)));
        Assert.Equal(("Jonas", "Hüttner", EinsichtStatus.Freigegeben, false), (detail.Vorname, detail.Nachname, detail.Status, detail.FreigabeAbgelaufen));
        using var roh = await _s.MiriamClient.GetAsync($"{_s.FreizeitPfad}/einsichten/{einsicht}", Abbruch);
        Assert.Equal(
            ["freigabeAbgelaufen", "freigegebenBis", "id", "nachname", "personId", "posten", "status", "vorname"],
            Namen(await JsonAsync(roh)));
    }

    [Fact]
    public async Task Miriam_uebernimmt_Tobias_und_Jonas_bekommen_404_nicht_freigegeben_409()
    {
        var fz = await NachweisAsync(_s.JonasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        var einsicht = await AnfrageAsync(_s.JonasClient);
        using (var nichtFrei = await PruefenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht))
        {
            Assert.Equal(HttpStatusCode.Conflict, nichtFrei.StatusCode);
        }

        (await FreigebenAsync(_s.JonasClient, einsicht)).Dispose();
        using var miriam = await PruefenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);
        using var tobias = await PruefenAsync(_s.TobiasClient, _s.FreizeitPfad, einsicht);
        using var tobiasDetail = await _s.TobiasClient.GetAsync($"{_s.FreizeitPfad}/einsichten/{einsicht}", Abbruch);
        using var tobiasDatei = await DateiAsync(_s.TobiasClient, _s.FreizeitPfad, einsicht, fz);
        using var jonasEigene = await PruefenAsync(_s.JonasClient, _s.FreizeitPfad, einsicht);

        Assert.Equal(HttpStatusCode.NoContent, miriam.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tobias.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tobiasDetail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tobiasDatei.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, jonasEigene.StatusCode);
    }

    [Fact]
    public async Task Datei_ansehen_nur_gebunden_bis_zur_Entscheidung_und_bis_zum_Ablauf()
    {
        var (einsicht, fz, eh, _) = await _s.FreigegebenUndUebernommenAsync();

        using (var pdf = await DateiAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, fz))
        {
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", pdf.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("fuehrungszeugnis.pdf", pdf.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal(Pdf, await pdf.Content.ReadAsByteArrayAsync(Abbruch));
        }

        using (var png = await DateiAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh))
        {
            Assert.Equal(HttpStatusCode.OK, png.StatusCode);
            Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);
            Assert.Equal("inline", png.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("ersteHilfe.png", png.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        }

        (await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh)).Dispose();
        using (var entschieden = await DateiAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh))
        {
            Assert.Equal(HttpStatusCode.NotFound, entschieden.StatusCode);
        }

        _s.Factory.Uhr.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        using var abgelaufen = await DateiAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, fz);
        Assert.Equal(HttpStatusCode.NotFound, abgelaufen.StatusCode);
        Assert.Equal(EinsichtStatus.Angefragt, Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen).Status);
    }

    [Fact]
    public async Task Abgelaufene_Freigabe_bleibt_offen_Anfragen_liefert_dieselbe_erneutes_Freigeben_setzt_neue_14_Tage()
    {
        var (einsicht, fz, eh, _) = await _s.FreigegebenUndUebernommenAsync();
        (await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh)).Dispose();
        var bis = (await DetailAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht)).FreigegebenBis;
        _s.Factory.Uhr.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));

        var anfrage = Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen);
        Assert.Equal((einsicht, EinsichtStatus.Angefragt, (DateTimeOffset?)null), (anfrage.Id, anfrage.Status, anfrage.FreigegebenBis));
        var zeile = (await ListeAsync(_s.MiriamClient, _s.FreizeitPfad)).Single(p => p.Vorname == "Jonas");
        Assert.Equal((einsicht, EinsichtStatus.Abgelaufen, bis, false), (zeile.Einsicht!.Id, zeile.Einsicht.Status, zeile.Einsicht.FreigegebenBis, zeile.Anfragbar));
        var detail = await DetailAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);
        Assert.Equal((EinsichtStatus.Abgelaufen, true, bis), (detail.Status, detail.FreigabeAbgelaufen, detail.FreigegebenBis));

        using (var vonHand = await AnfragenAsync(_s.TobiasClient, _s.FreizeitPfad, await _s.PersonIdAsync(Jonas)))
        {
            Assert.Equal(HttpStatusCode.OK, vonHand.StatusCode);
            Assert.Equal(einsicht, (await vonHand.Content.ReadFromJsonAsync<EinsichtAngefragt>(Abbruch))!.EinsichtId);
        }

        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_einsicht WHERE \"Status\" <> 3 AND \"PersonId\" = @p", ("p", await _s.PersonIdAsync(Jonas))));
        using var erneut = await FreigebenAsync(_s.JonasClient, einsicht);
        Assert.Equal(HttpStatusCode.OK, erneut.StatusCode);
        Assert.Equal(_s.Factory.Uhr.GetUtcNow() + TimeSpan.FromDays(14), (await JsonAsync(erneut)).GetProperty("freigegebenBis").GetDateTimeOffset());
        var danach = await DetailAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);
        Assert.Equal(
            [(fz, PostenErgebnis.Offen), (eh, PostenErgebnis.Bestaetigt)],
            danach.Posten.Select(p => (p.NachweisId, p.Ergebnis)));
        Assert.Equal((EinsichtStatus.Freigegeben, false), (danach.Status, danach.FreigabeAbgelaufen));
    }

    [Fact]
    public async Task Uebernehmen_ueber_eine_andere_Freizeit_mit_Rolle_der_Person_nicht_ohne_Rolle()
    {
        var fz = await NachweisAsync(_s.JonasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        var einsicht = await AnfrageAsync(_s.JonasClient);
        var freizeitB = await FreizeitHelfer.AnlegenAsync(_s.AndreaClient, _s.OrgId, "Herbstfreizeit");
        var freizeitC = await FreizeitHelfer.AnlegenAsync(_s.AndreaClient, _s.OrgId, "Winterfreizeit");
        using var lea = await FreizeitHelfer.LeitungAsync(_s.Factory, _s.AndreaClient, _s.OrgId, freizeitB, Lea);
        await FreizeitHelfer.EinladenAsync(_s.AndreaClient, _s.OrgId, freizeitB, Jonas);
        await FreizeitHelfer.AnnehmenMitAsync(_s.Factory, _s.JonasClient, Jonas);
        await FreizeitHelfer.EinladenAsync(_s.AndreaClient, _s.OrgId, freizeitC, Miriam);
        await FreizeitHelfer.AnnehmenMitAsync(_s.Factory, _s.MiriamClient, Miriam);
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal(einsicht, await AnfrageAsync(_s.JonasClient));
        (await FreigebenAsync(_s.JonasClient, einsicht)).Dispose();

        using var ohneRolle = await PruefenAsync(_s.MiriamClient, $"/api/freizeiten/{freizeitC}/personen/nachweise", einsicht);
        using var ohneRolleDetail = await _s.MiriamClient.GetAsync($"/api/freizeiten/{freizeitC}/personen/nachweise/einsichten/{einsicht}", Abbruch);
        var pfadB = $"/api/freizeiten/{freizeitB}/personen/nachweise";
        Assert.True((await ListeAsync(lea, pfadB)).Single(p => p.Vorname == "Jonas").Einsicht!.Pruefbar);
        using var mitRolle = await PruefenAsync(lea, pfadB, einsicht);
        using var datei = await DateiAsync(lea, pfadB, einsicht, fz);
        using var miriamDanach = await PruefenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);

        Assert.Equal(HttpStatusCode.NotFound, ohneRolle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ohneRolleDetail.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, mitRolle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, datei.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, miriamDanach.StatusCode);
        Assert.False((await ListeAsync(_s.MiriamClient, _s.FreizeitPfad)).Single(p => p.Vorname == "Jonas").Einsicht!.Pruefbar);
    }

    [Fact]
    public async Task Bestaetigen_landet_nach_Zustellung_als_Bestaetigung_bei_der_Organisation_zweites_Mal_404()
    {
        var (einsicht, _, eh, _) = await _s.FreigegebenUndUebernommenAsync();

        using var bestaetigen = await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh);
        using var zweites = await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh);

        Assert.Equal(HttpStatusCode.NoContent, bestaetigen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, zweites.StatusCode);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%.NachweisEingesehen'"));
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);

        using var antwort = await _s.JonasClient.GetAsync("/api/ich/organisationen/nachweis-bestaetigungen", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var zeile = Assert.Single((await JsonAsync(antwort)).EnumerateArray());
        Assert.Equal(["abgelaufen", "art", "eingesehenAm", "geprueftVon", "gueltigBis", "organisation", "organisationId"], Namen(zeile));
        Assert.Equal(
            ("ersteHilfe", "2027-04-15", false, "Jugendwerk Musterstadt", _s.OrgId, "Miriam", "Zeltner"),
            (zeile.GetProperty("art").GetString(), zeile.GetProperty("gueltigBis").GetString(), zeile.GetProperty("abgelaufen").GetBoolean(),
             zeile.GetProperty("organisation").GetString(), zeile.GetProperty("organisationId").GetGuid(),
             zeile.GetProperty("geprueftVon").GetProperty("vorname").GetString(), zeile.GetProperty("geprueftVon").GetProperty("nachname").GetString()));
        Assert.Equal(await _s.PersonIdAsync(Miriam), await WertAsync<Guid>(_s.Verbindung, "SELECT \"GeprueftVon\" FROM organisationen.nachweis_bestaetigung"));
        using var miriamSieht = await _s.MiriamClient.GetAsync("/api/ich/organisationen/nachweis-bestaetigungen", Abbruch);
        Assert.Empty((await JsonAsync(miriamSieht)).EnumerateArray());
    }

    [Fact]
    public async Task Ablehnen_des_letzten_Postens_schliesst_ab_fragt_neu_an_Hinweis_verschluesselt_PATCH_loescht_ihn()
    {
        var (einsicht, fz, eh, _) = await _s.FreigegebenUndUebernommenAsync();
        (await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh)).Dispose();
        foreach (var hinweis in new[] { "", new string('a', 501) })
        {
            using var ungueltig = await AblehnenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, fz, hinweis);
            Assert.Equal(HttpStatusCode.BadRequest, ungueltig.StatusCode);
            Assert.True((await ProblemAsync(ungueltig)).GetProperty("errors").TryGetProperty("hinweis", out _));
        }

        using var ablehnen = await AblehnenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, fz, "Datum passt nicht");

        Assert.Equal(HttpStatusCode.NoContent, ablehnen.StatusCode);
        var detail = await DetailAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);
        Assert.Equal(EinsichtStatus.Abgeschlossen, detail.Status);
        var eigene = await EigeneAsync(_s.JonasClient);
        var neu = Assert.Single(eigene.Anfragen);
        Assert.NotEqual(einsicht, neu.Id);
        Assert.Equal((EinsichtStatus.Angefragt, "Miriam", (Guid?)_s.FreizeitId), (neu.Status, neu.AngefragtVon?.Vorname, neu.FreizeitId));
        Assert.Equal("Datum passt nicht", eigene.Nachweise.Single(n => n.Id == fz).Ablehnungshinweis);
        var roh = await WertAsync<byte[]>(_s.Verbindung, "SELECT \"Ablehnungshinweis\" FROM personen.nachweis WHERE \"Id\" = @id", ("id", fz));
        Assert.DoesNotContain("Datum passt", Encoding.UTF8.GetString(roh), StringComparison.Ordinal);
        var liste = await ListeAsync(_s.MiriamClient, _s.FreizeitPfad);
        var jonas = liste.Single(p => p.Vorname == "Jonas");
        Assert.Equal((neu.Id, EinsichtStatus.Angefragt, true, false), (jonas.Einsicht!.Id, jonas.Einsicht.Status, jonas.Einsicht.Pruefbar, jonas.Anfragbar));

        var version = eigene.Nachweise.Single(n => n.Id == fz).Version;
        using var patch = await SendenAsync(_s.JonasClient, HttpMethod.Patch, $"/api/ich/nachweise/{fz}", $$"""{"version":{{version}},"alterWert":"2025-11-03","ausstellungsdatum":"2025-11-04"}""");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Null((await EigeneAsync(_s.JonasClient)).Nachweise.Single(n => n.Id == fz).Ablehnungshinweis);

        // Die Neu-Anfrage zeigt nur das Abgelehnte, nicht die bestätigte Erste Hilfe.
        using var freigeben = await FreigebenAsync(_s.JonasClient, neu.Id);
        Assert.Equal(HttpStatusCode.OK, freigeben.StatusCode);
        Assert.Equal([fz], (await DetailAsync(_s.MiriamClient, _s.FreizeitPfad, neu.Id)).Posten.Select(p => p.NachweisId));
    }

    [Fact]
    public async Task Zwei_Organisationen_Bestaetigung_in_B_ohne_neuen_Upload_jede_sieht_nur_ihre_Zeile()
    {
        var (einsicht, _, eh, _) = await _s.FreigegebenUndUebernommenAsync();
        (await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh)).Dispose();
        var (orgB, leaAdmin) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.GerdClient, "Jugendwerk B", Lea);
        var freizeitB = await FreizeitHelfer.AnlegenAsync(leaAdmin, orgB, "Freizeit B");
        // Jonas arbeitet in B mit (über das Ereignis): Die erste Mitarbeit in B fragt die Einsicht automatisch an.
        await FreizeitHelfer.EinladenAsync(leaAdmin, orgB, freizeitB, Jonas);
        await FreizeitHelfer.AnnehmenMitAsync(_s.Factory, _s.JonasClient, Jonas);
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);
        var orgPfadB = $"/api/organisationen/{orgB}/personen/nachweise";

        var offene = (await EigeneAsync(_s.JonasClient)).Anfragen;
        var anfrageB = Assert.Single(offene, a => a.FreizeitId == freizeitB);
        Assert.Null(anfrageB.AngefragtVon);
        var einsichtB = anfrageB.Id;
        using (var namen = await _s.JonasClient.GetAsync($"/api/ich/organisationen/namen?id={orgB}&id={_s.OrgId}&id={Guid.CreateVersion7()}", Abbruch))
        {
            Assert.Equal(HttpStatusCode.OK, namen.StatusCode);
            var zeilen = (await JsonAsync(namen)).EnumerateArray().ToList();
            Assert.All(zeilen, z => Assert.Equal(["name", "organisationId"], Namen(z)));
            Assert.Equal(
                new (Guid, string?)[] { (orgB, "Jugendwerk B"), (_s.OrgId, "Jugendwerk Musterstadt") },
                zeilen.Select(z => (z.GetProperty("organisationId").GetGuid(), z.GetProperty("name").GetString())));
        }

        (await FreigebenAsync(_s.JonasClient, einsichtB)).Dispose();
        using (var pruefen = await PruefenAsync(leaAdmin, orgPfadB, einsichtB))
        {
            Assert.Equal(HttpStatusCode.NoContent, pruefen.StatusCode);
        }

        Assert.Contains(eh, (await DetailAsync(leaAdmin, orgPfadB, einsichtB)).Posten.Select(p => p.NachweisId));
        using var bestaetigen = await BestaetigenAsync(leaAdmin, orgPfadB, einsichtB, eh);
        Assert.Equal(HttpStatusCode.NoContent, bestaetigen.StatusCode);
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis WHERE \"Art\" = 3"));
        var bestaetigungen = (await JsonAsync(await _s.JonasClient.GetAsync("/api/ich/organisationen/nachweis-bestaetigungen", Abbruch))).EnumerateArray()
            .Select(b => b.GetProperty("organisationId").GetGuid()).Order().ToList();
        Assert.Equal(new[] { _s.OrgId, orgB }.Order(), bestaetigungen);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung WHERE \"OrganisationId\" = @o", ("o", orgB)));
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM organisationen.nachweis_bestaetigung WHERE \"OrganisationId\" = @o", ("o", _s.OrgId)));
        using var fremdeEinsicht = await _s.AndreaClient.GetAsync($"{_s.OrgPfad}/einsichten/{einsichtB}", Abbruch);
        Assert.Equal(HttpStatusCode.NotFound, fremdeEinsicht.StatusCode);

        // „abgelaufen“ rechnet der Server: gültig bis vor heute (Kalendertag Berlin). Heute selbst gilt noch.
        var heute = FreizeitHelfer.Heute(_s.Factory);
        await AusfuehrenAsync(_s.Verbindung, "UPDATE organisationen.nachweis_bestaetigung SET \"GueltigBis\" = @d WHERE \"OrganisationId\" = @o", ("d", heute), ("o", _s.OrgId));
        await AusfuehrenAsync(_s.Verbindung, "UPDATE organisationen.nachweis_bestaetigung SET \"GueltigBis\" = @d WHERE \"OrganisationId\" = @o", ("d", heute.AddDays(-1)), ("o", orgB));
        var abgelaufen = (await JsonAsync(await _s.JonasClient.GetAsync("/api/ich/organisationen/nachweis-bestaetigungen", Abbruch))).EnumerateArray()
            .ToDictionary(b => b.GetProperty("organisationId").GetGuid(), b => b.GetProperty("abgelaufen").GetBoolean());
        Assert.Equal((false, true), (abgelaufen[_s.OrgId], abgelaufen[orgB]));
    }

    [Fact]
    public async Task Organisationsnamen_hoechstens_20_Kennungen_nur_existierende_angemeldet()
    {
        var zuViele = string.Join("&", Enumerable.Range(0, 21).Select(_ => $"id={Guid.CreateVersion7()}"));
        using var grenze = await _s.JonasClient.GetAsync($"/api/ich/organisationen/namen?{string.Join("&", Enumerable.Range(0, 20).Select(_ => $"id={Guid.CreateVersion7()}"))}", Abbruch);
        using var ueber = await _s.JonasClient.GetAsync($"/api/ich/organisationen/namen?{zuViele}", Abbruch);
        using var leer = await _s.JonasClient.GetAsync("/api/ich/organisationen/namen", Abbruch);
        using var anonym = _s.Factory.CreateClient();
        using var ohneAnmeldung = await anonym.GetAsync($"/api/ich/organisationen/namen?id={_s.OrgId}", Abbruch);

        Assert.Equal(HttpStatusCode.OK, grenze.StatusCode);
        Assert.Empty((await JsonAsync(grenze)).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, ueber.StatusCode);
        Assert.Equal(HttpStatusCode.OK, leer.StatusCode);
        Assert.Empty((await JsonAsync(leer)).EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, ohneAnmeldung.StatusCode);
    }

    [Fact]
    public async Task Anfrage_von_Hand_Teammitglied_201_offen_200_selbst_und_ohne_Rolle_404()
    {
        var jonas = await _s.PersonIdAsync(Jonas);
        using var lea = await _s.AngemeldetAsync(Lea);
        var leaId = await _s.PersonIdAsync(Lea);

        using var offen = await AnfragenAsync(_s.MiriamClient, _s.FreizeitPfad, jonas);
        Assert.Equal(HttpStatusCode.OK, offen.StatusCode);
        Assert.Equal(await AnfrageAsync(_s.JonasClient), (await offen.Content.ReadFromJsonAsync<EinsichtAngefragt>(Abbruch))!.EinsichtId);

        // Jonas' automatische Einsicht abschließen, dann ist die Anfrage neu und an Miriam gebunden.
        var id = await NachweisAsync(_s.JonasClient, "praeventionsschulung", "2025-01-10", Pdf);
        var auto = await AnfrageAsync(_s.JonasClient);
        (await FreigebenAsync(_s.JonasClient, auto)).Dispose();
        (await PruefenAsync(_s.MiriamClient, _s.FreizeitPfad, auto)).Dispose();
        (await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, auto, id)).Dispose();

        using var neu = await AnfragenAsync(_s.MiriamClient, _s.FreizeitPfad, jonas);
        using var selbst = await AnfragenAsync(_s.MiriamClient, _s.FreizeitPfad, await _s.PersonIdAsync(Miriam));
        using var ohneRolle = await AnfragenAsync(_s.MiriamClient, _s.FreizeitPfad, leaId);
        using var orgOhneRolle = await AnfragenAsync(_s.AndreaClient, _s.OrgPfad, leaId);
        using var orgSelbst = await AnfragenAsync(_s.AndreaClient, _s.OrgPfad, await _s.PersonIdAsync(Andrea));

        Assert.Equal(HttpStatusCode.Created, neu.StatusCode);
        var einsicht = (await neu.Content.ReadFromJsonAsync<EinsichtAngefragt>(Abbruch))!.EinsichtId;
        Assert.Equal(HttpStatusCode.NotFound, selbst.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ohneRolle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, orgOhneRolle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, orgSelbst.StatusCode);
        var detail = await DetailAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht);
        Assert.Equal((EinsichtStatus.Angefragt, 0), (detail.Status, detail.Posten.Count));
        Assert.Equal("Miriam", Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen).AngefragtVon?.Vorname);
    }

    [Fact]
    public async Task Pruefliste_der_Leitung_und_der_Organisation_ohne_die_eigene_Person()
    {
        var freizeit = await ListeAsync(_s.MiriamClient, _s.FreizeitPfad);
        var org = await ListeAsync(_s.AndreaClient, _s.OrgPfad);

        Assert.Equal(["Jonas", "Tobias"], freizeit.Select(p => p.Vorname));
        Assert.All(freizeit, p => Assert.Equal((false, EinsichtStatus.Angefragt, true), (p.Anfragbar, p.Einsicht!.Status, p.Einsicht.Pruefbar)));
        Assert.Equal(["Jonas", "Tobias", "Miriam"], org.Select(p => p.Vorname));
        // Automatische Einsichten sind ungebunden: Die Org-Admin kann jede übernehmen.
        Assert.All(org, p => Assert.True(p.Einsicht!.Pruefbar));
        using var roh = await _s.MiriamClient.GetAsync(_s.FreizeitPfad, Abbruch);
        var zeile = (await JsonAsync(roh)).EnumerateArray().First();
        Assert.Equal(["anfragbar", "einsicht", "nachname", "personId", "vorname"], Namen(zeile));
        Assert.Equal(["abgeschlossenAm", "angefragtAm", "freigegebenBis", "id", "pruefbar", "status"], Namen(zeile.GetProperty("einsicht")));
    }

    [Fact]
    public async Task Org_Admin_uebernimmt_eine_automatische_Einsicht_und_fragt_selbst_an()
    {
        var id = await NachweisAsync(_s.JonasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        var auto = await AnfrageAsync(_s.JonasClient);
        (await FreigebenAsync(_s.JonasClient, auto)).Dispose();

        using var detailVorher = await _s.AndreaClient.GetAsync($"{_s.OrgPfad}/einsichten/{auto}", Abbruch);
        using var uebernehmen = await PruefenAsync(_s.AndreaClient, _s.OrgPfad, auto);
        using var miriamVersucht = await PruefenAsync(_s.MiriamClient, _s.FreizeitPfad, auto);

        Assert.Equal(HttpStatusCode.OK, detailVorher.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, uebernehmen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, miriamVersucht.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await BestaetigenAsync(_s.AndreaClient, _s.OrgPfad, auto, id)).StatusCode);

        // Die einzige Einsicht ist abgeschlossen: Jonas ist auf beiden Routen wieder anfragbar.
        var orgZeile = (await ListeAsync(_s.AndreaClient, _s.OrgPfad)).Single(p => p.Vorname == "Jonas");
        var freizeitZeile = (await ListeAsync(_s.MiriamClient, _s.FreizeitPfad)).Single(p => p.Vorname == "Jonas");
        Assert.Equal((true, EinsichtStatus.Abgeschlossen), (orgZeile.Anfragbar, orgZeile.Einsicht!.Status));
        Assert.Equal((true, EinsichtStatus.Abgeschlossen), (freizeitZeile.Anfragbar, freizeitZeile.Einsicht!.Status));

        using var anfragen = await AnfragenAsync(_s.AndreaClient, _s.OrgPfad, await _s.PersonIdAsync(Jonas));
        Assert.Equal(HttpStatusCode.Created, anfragen.StatusCode);
        var einsicht = (await anfragen.Content.ReadFromJsonAsync<EinsichtAngefragt>(Abbruch))!.EinsichtId;
        Assert.Equal((EinsichtStatus.Angefragt, (Guid?)null), (Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen).Status, Assert.Single((await EigeneAsync(_s.JonasClient)).Anfragen).FreizeitId));
        (await FreigebenAsync(_s.JonasClient, einsicht)).Dispose();
        using var pruefen = await PruefenAsync(_s.AndreaClient, _s.OrgPfad, einsicht);
        using var datei = await DateiAsync(_s.AndreaClient, _s.OrgPfad, einsicht, id);
        using var bestaetigen = await BestaetigenAsync(_s.AndreaClient, _s.OrgPfad, einsicht, id);
        using var miriam = await _s.MiriamClient.GetAsync($"{_s.FreizeitPfad}/einsichten/{einsicht}", Abbruch);

        Assert.Equal(HttpStatusCode.NoContent, pruefen.StatusCode);
        Assert.Equal(HttpStatusCode.OK, datei.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, bestaetigen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, miriam.StatusCode);
        Assert.True((await ListeAsync(_s.AndreaClient, _s.OrgPfad)).Single(p => p.Vorname == "Jonas").Einsicht!.Pruefbar);
    }

    [Fact]
    public async Task Rechte_Freizeit_Team_403_fremde_Freizeit_404_Einsicht_einer_anderen_Organisation_404()
    {
        var (einsicht, fz, _, _) = await _s.FreigegebenUndUebernommenAsync();
        using var lea = await _s.AngemeldetAsync(Lea);
        var leaId = await _s.PersonIdAsync(Lea);
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"FreizeitId\", \"PersonId\", \"OrganisationId\", \"Rolle\", \"VergebenAm\") VALUES (@f, @p, @o, 2, now())",
            ("f", _s.FreizeitId), ("p", leaId), ("o", _s.OrgId));
        var andere = await FreizeitHelfer.AnlegenAsync(_s.AndreaClient, _s.OrgId, "Andere Freizeit");

        foreach (var (methode, pfad) in Pfade(_s.FreizeitPfad, einsicht, fz))
        {
            using var team = await Aufruf(lea, methode, pfad);
            using var fremd = await Aufruf(_s.MiriamClient, methode, pfad.Replace(_s.FreizeitId.ToString(), andere.ToString(), StringComparison.Ordinal));
            Assert.True(team.StatusCode == HttpStatusCode.Forbidden, $"Team {methode} {pfad}: {team.StatusCode}");
            Assert.True(fremd.StatusCode == HttpStatusCode.NotFound, $"Fremd {methode} {pfad}: {fremd.StatusCode}");
        }

        using var leitung = await _s.MiriamClient.GetAsync(_s.FreizeitPfad, Abbruch);
        Assert.Equal(HttpStatusCode.OK, leitung.StatusCode);
    }

    [Fact]
    public async Task Rechte_Organisation_Ueberblick_403_Org_Admin_einer_anderen_Organisation_404()
    {
        var (einsicht, fz, _, _) = await _s.FreigegebenUndUebernommenAsync();
        await OrgHelfer.EinladenAsync(_s.AndreaClient, _s.OrgId, Lea, Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, Lea);
        var (orgB, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.GerdClient, "Jugendwerk B", "sara.kochloeffel@example.org");

        var pfade = Pfade(_s.OrgPfad, einsicht, fz)
            .Append(("GET", $"/api/organisationen/{_s.OrgId}/nachweis-richtlinie"))
            .Append(("PATCH", $"/api/organisationen/{_s.OrgId}/nachweis-richtlinie/juleica"));
        foreach (var (methode, pfad) in pfade)
        {
            using var verboten = await Aufruf(ueberblick, methode, pfad);
            using var fremd = await Aufruf(sara, methode, pfad);
            Assert.True(verboten.StatusCode == HttpStatusCode.Forbidden, $"Überblick {methode} {pfad}: {verboten.StatusCode}");
            Assert.True(fremd.StatusCode == HttpStatusCode.NotFound, $"Fremd {methode} {pfad}: {fremd.StatusCode}");
        }

        using var eigeneRoute = await sara.GetAsync($"/api/organisationen/{orgB}/personen/nachweise/einsichten/{einsicht}", Abbruch);
        Assert.Equal(HttpStatusCode.NotFound, eigeneRoute.StatusCode);
        Assert.Empty(await ListeAsync(sara, $"/api/organisationen/{orgB}/personen/nachweise"));
    }

    [Fact]
    public async Task Erlaubt_nachweise_pruefen_folgt_denselben_Rollen_wie_die_Pruef_Endpunkte()
    {
        using var lea = await _s.AngemeldetAsync(Lea);
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO freizeiten.freizeit_rolle (\"FreizeitId\", \"PersonId\", \"OrganisationId\", \"Rolle\", \"VergebenAm\") VALUES (@f, @p, @o, 2, now())",
            ("f", _s.FreizeitId), ("p", await _s.PersonIdAsync(Lea)), ("o", _s.OrgId));
        await OrgHelfer.EinladenAsync(_s.AndreaClient, _s.OrgId, "uwe.ueberblick@example.org", Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, "uwe.ueberblick@example.org");

        // Freizeit-Kontext: Leitung ja, Team (Kontext lesbar) nein – genau wie die Prüf-Endpunkte.
        foreach (var (client, darf) in new[] { (_s.MiriamClient, true), (lea, false) })
        {
            using var kontext = await client.GetAsync($"/api/freizeiten/{_s.FreizeitId}/kontext", Abbruch);
            Assert.Equal(HttpStatusCode.OK, kontext.StatusCode);
            var erlaubt = (await JsonAsync(kontext)).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()).ToList();
            using var liste = await client.GetAsync(_s.FreizeitPfad, Abbruch);
            Assert.Equal(darf, erlaubt.Contains(Zeltlotse.Api.Contracts.Gemeinsam.Aktionsschluessel.NachweisePruefen));
            Assert.Equal(darf ? HttpStatusCode.OK : HttpStatusCode.Forbidden, liste.StatusCode);
        }

        // Organisation: Org-Admin ja, Org-Überblick nein.
        foreach (var (client, darf) in new[] { (_s.AndreaClient, true), (ueberblick, false) })
        {
            using var organisation = await client.GetAsync($"/api/organisationen/{_s.OrgId}", Abbruch);
            Assert.Equal(HttpStatusCode.OK, organisation.StatusCode);
            var erlaubt = (await JsonAsync(organisation)).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString()).ToList();
            using var liste = await client.GetAsync(_s.OrgPfad, Abbruch);
            using var richtlinie = await client.GetAsync($"/api/organisationen/{_s.OrgId}/nachweis-richtlinie", Abbruch);
            Assert.Equal(darf, erlaubt.Contains(Zeltlotse.Api.Contracts.Gemeinsam.Aktionsschluessel.NachweisePruefen));
            Assert.Equal(darf, erlaubt.Contains(Zeltlotse.Api.Contracts.Gemeinsam.Aktionsschluessel.NachweisRichtlinieVerwalten));
            Assert.Equal(darf ? HttpStatusCode.OK : HttpStatusCode.Forbidden, liste.StatusCode);
            Assert.Equal(darf ? HttpStatusCode.OK : HttpStatusCode.Forbidden, richtlinie.StatusCode);
        }
    }

    private static IEnumerable<(string Methode, string Pfad)> Pfade(string basis, Guid einsicht, Guid nachweis) =>
    [
        ("GET", basis),
        ("POST", $"{basis}/anfragen"),
        ("GET", $"{basis}/einsichten/{einsicht}"),
        ("POST", $"{basis}/einsichten/{einsicht}/pruefen"),
        ("GET", $"{basis}/einsichten/{einsicht}/nachweise/{nachweis}/datei"),
        ("POST", $"{basis}/einsichten/{einsicht}/nachweise/{nachweis}/bestaetigen"),
        ("POST", $"{basis}/einsichten/{einsicht}/nachweise/{nachweis}/ablehnen"),
    ];

    private static Task<HttpResponseMessage> Aufruf(HttpClient client, string methode, string pfad) => methode switch
    {
        "GET" => client.GetAsync(pfad, Abbruch),
        "PATCH" => SendenAsync(client, HttpMethod.Patch, pfad, """{"version":1,"alterWert":"false","pflicht":"true"}"""),
        _ => SendenAsync(client, HttpMethod.Post, pfad, """{"personId":null,"hinweis":"x"}"""),
    };
}
