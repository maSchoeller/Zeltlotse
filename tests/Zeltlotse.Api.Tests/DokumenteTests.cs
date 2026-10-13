using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.DokumentHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-7, Dokumente der Freizeit (CAP-25, FR-43, AD-6, AD-15, AD-17, AD-19, AD-30) gegen echtes PostgreSQL und S3: Hochladen mit
/// Sichtbarkeit, Liste, Datei als Anhang, Feldänderung mit Konfliktregel, Entfernen, Einbinden von Org-Dokumenten ohne Kopie, der
/// Schreibschutz nach dem Abschluss, die Rechte (Leitung und Team; sonst <c>404</c>), die öffentlichen Dokumente auf der Freizeitseite
/// und ihre Datei über den Anmeldelink mit den Token-Fällen.
/// </summary>
public sealed class DokumenteTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres, s3);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private string X => FreizeitPfad(_s.X);

    // ---------- Hochladen, Liste, Datei ----------

    [Fact]
    public async Task Tobias_laedt_den_Infobrief_fuer_Eltern_hoch_Liste_und_Datei_als_Anhang()
    {
        using var hochgeladen = await HochladenAsync(_s.Tobias, X, Pdf, "infobrief.pdf", "ElternUndTeilnehmende");

        Assert.Equal(HttpStatusCode.Created, hochgeladen.StatusCode);
        var json = await JsonAsync(hochgeladen);
        Assert.Equal(["ausOrganisation", "dateiart", "groesse", "hochgeladenAm", "id", "sichtbarkeit", "titel", "version"], Namen(json));
        Assert.Equal(
            ("infobrief", "PDF", "ElternUndTeilnehmende", false, (long)Pdf.Length),
            (json.GetProperty("titel").GetString(), json.GetProperty("dateiart").GetString(), json.GetProperty("sichtbarkeit").GetString(),
                json.GetProperty("ausOrganisation").GetBoolean(), json.GetProperty("groesse").GetInt64()));
        var id = json.GetProperty("id").GetGuid();

        var liste = Assert.Single(await DokumenteAsync(_s.Miriam, X));
        Assert.Equal(id, liste.GetProperty("id").GetGuid());
        Assert.Equal(Pdf, await RohAsync(s3, _s.Bucket!, $"freizeiten/{id}"));
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung,
            "SELECT count(*) FROM freizeiten.dokument WHERE \"Id\" = @id AND \"OrganisationId\" = @o AND \"FreizeitId\" = @f AND \"OrgDokumentId\" IS NULL",
            ("id", id), ("o", _s.A), ("f", _s.X)));
        using var datei = await _s.Tobias.GetAsync($"{X}/{id}/datei", Abbruch);
        await DateiPruefenAsync(datei, Pdf, "application/pdf", "infobrief.pdf");
    }

    [Fact]
    public async Task Ohne_Sichtbarkeit_gilt_Team_ein_Bild_geht_ebenfalls_als_Anhang_hinaus_Liste_nach_Titel()
    {
        await HochgeladenAsync(_s.Miriam, X, Pdf, "packliste.pdf");
        var bild = await HochgeladenAsync(_s.Miriam, X, Png, "Anfahrt.png");
        await HochgeladenAsync(_s.Miriam, X, Pdf, "Notfallnummern.pdf");

        var dokumente = await DokumenteAsync(_s.Miriam, X);
        Assert.Equal(["Anfahrt", "Notfallnummern", "packliste"], dokumente.Select(d => d.GetProperty("titel").GetString()));
        Assert.All(dokumente, d => Assert.Equal("Team", d.GetProperty("sichtbarkeit").GetString()));
        using var datei = await _s.Miriam.GetAsync($"{X}/{bild}/datei", Abbruch);
        await DateiPruefenAsync(datei, Png, "image/png", "Anfahrt.png");
    }

    [Theory]
    [InlineData("ohne Datei", "datei")]
    [InlineData("Text", "datei")]
    [InlineData("zu groß", "datei")]
    [InlineData("Geheim", "sichtbarkeit")]
    [InlineData("2", "sichtbarkeit")]
    public async Task Ungueltiger_Upload_ergibt_400_am_Feld(string fall, string feld)
    {
        var inhalt = fall switch
        {
            "ohne Datei" => null,
            "Text" => "Kein PDF, nur Text."u8.ToArray(),
            "zu groß" => (byte[])[.. Pdf, .. new byte[(10 * 1024 * 1024) + 1 - Pdf.Length]],
            _ => Pdf,
        };

        using var antwort = await HochladenAsync(_s.Tobias, X, inhalt, "infobrief.pdf", fall is "Geheim" or "2" ? fall : null);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.dokument"));
    }

    // ---------- Rechte ----------

    [Fact]
    public async Task Org_Admin_ohne_Rolle_Leitung_nur_von_Z_und_eine_Freizeit_einer_fremden_Organisation_ergeben_404()
    {
        var id = await HochgeladenAsync(_s.Miriam, X, Pdf, "infobrief.pdf");
        var orgDokument = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung.pdf");
        await FreizeitHelfer.EinladenAsync(_s.Lea, _s.B, _s.Z, OrgHelfer.Jonas);
        using var jonas = await AngemeldeterClientAsync(_s.Factory, OrgHelfer.Jonas);
        await FreizeitHelfer.AnnehmenMitAsync(_s.Factory, jonas, OrgHelfer.Jonas);
        // Tobias (Team in X) an Z: Freizeit einer fremden Organisation.
        var faelle = new[] { (_s.Andrea, X, id), (jonas, X, id), (_s.Katrin, X, id), (_s.Tobias, FreizeitPfad(_s.Z), id) };

        foreach (var (client, pfad, dokument) in faelle)
        {
            Assert.Equal(
                Enumerable.Repeat(HttpStatusCode.NotFound, 7),
                [
                    (await client.GetAsync(pfad, Abbruch)).StatusCode,
                    (await HochladenAsync(client, pfad, Pdf)).StatusCode,
                    (await SendenAsync(client, HttpMethod.Patch, $"{pfad}/{dokument}", """{"version":0,"alterWert":"infobrief","titel":"x"}""")).StatusCode,
                    (await SendenAsync(client, HttpMethod.Delete, $"{pfad}/{dokument}")).StatusCode,
                    (await client.GetAsync($"{pfad}/{dokument}/datei", Abbruch)).StatusCode,
                    (await client.GetAsync($"{pfad}/org", Abbruch)).StatusCode,
                    (await PostAsync(client, $"{pfad}/einbindungen", new DokumentEinbinden(orgDokument))).StatusCode,
                ]);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Anonym.GetAsync(X, Abbruch)).StatusCode);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.dokument"));
        Assert.Equal(HttpStatusCode.OK, (await _s.Tobias.GetAsync(X, Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await jonas.GetAsync(FreizeitPfad(_s.Z), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Ein_Dokument_der_Freizeit_Z_ist_ueber_X_nicht_erreichbar_unbekannt_404()
    {
        var inZ = await HochgeladenAsync(_s.Miriam, FreizeitPfad(_s.Z), Pdf, "Z.pdf");

        Assert.Empty(await DokumenteAsync(_s.Miriam, X));
        foreach (var dokument in new[] { inZ, Guid.CreateVersion7() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync($"{X}/{dokument}/datei", Abbruch)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendenAsync(_s.Miriam, HttpMethod.Patch, $"{X}/{dokument}", """{"version":0,"alterWert":"Z","titel":"neu"}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{X}/{dokument}")).StatusCode);
        }

        Assert.Equal(["Z"], await TitelAsync(_s.Miriam, FreizeitPfad(_s.Z)));
    }

    // ---------- Bearbeiten und Entfernen ----------

    [Fact]
    public async Task Titel_und_Sichtbarkeit_aendern_sich_einzeln_ein_veralteter_alter_Wert_desselben_Felds_ergibt_409()
    {
        var id = await HochgeladenAsync(_s.Tobias, X, Pdf, "infobrief.pdf");
        var version = (await DokumenteAsync(_s.Tobias, X)).Single().GetProperty("version").GetUInt32();

        using var titel = await PatchAsync(id, version, "infobrief", "titel", "  Infobrief 2027 ");
        Assert.Equal(HttpStatusCode.OK, titel.StatusCode);
        var gespeichert = await JsonAsync(titel);
        Assert.Equal(["titel", "version"], Namen(gespeichert));
        Assert.Equal("Infobrief 2027", gespeichert.GetProperty("titel").GetString());
        Assert.NotEqual(version, gespeichert.GetProperty("version").GetUInt32());

        // Mit der alten Version, aber einem anderen Feld: angewendet (AD-19).
        using var sichtbarkeit = await PatchAsync(id, version, "Team", "sichtbarkeit", "Oeffentlich");
        Assert.Equal(HttpStatusCode.OK, sichtbarkeit.StatusCode);
        Assert.Equal("Oeffentlich", (await JsonAsync(sichtbarkeit)).GetProperty("sichtbarkeit").GetString());

        using var konflikt = await PatchAsync(id, version, "infobrief", "titel", "Anders");
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(konflikt)));

        var dokument = (await DokumenteAsync(_s.Miriam, X)).Single();
        Assert.Equal(("Infobrief 2027", "Oeffentlich"), (dokument.GetProperty("titel").GetString(), dokument.GetProperty("sichtbarkeit").GetString()));
        using var datei = await _s.Miriam.GetAsync($"{X}/{id}/datei", Abbruch);
        await DateiPruefenAsync(datei, Pdf, "application/pdf", "Infobrief 2027.pdf");
    }

    [Fact]
    public async Task Gleichzeitige_Aenderungen_an_Titel_und_Sichtbarkeit_gelingen_beide()
    {
        var id = await HochgeladenAsync(_s.Tobias, X, Pdf, "infobrief.pdf");
        var (titel, sichtbarkeit) = ("infobrief", "Team");

        for (var runde = 0; runde < 5; runde++)
        {
            // Beide Geräte kennen dieselbe Version; jedes ändert ein anderes Feld.
            var version = (await DokumenteAsync(_s.Tobias, X)).Single().GetProperty("version").GetUInt32();
            var neueSichtbarkeit = runde % 2 == 0 ? "Oeffentlich" : "Team";
            var antworten = await Task.WhenAll(
                Task.Run(() => PatchAsync(id, version, titel, "titel", $"Infobrief {runde}")),
                Task.Run(() => PatchAsync(id, version, sichtbarkeit, "sichtbarkeit", neueSichtbarkeit)));
            foreach (var antwort in antworten)
            {
                using (antwort)
                {
                    Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
                }
            }

            (titel, sichtbarkeit) = ($"Infobrief {runde}", neueSichtbarkeit);
            var dokument = (await DokumenteAsync(_s.Miriam, X)).Single();
            Assert.Equal((titel, sichtbarkeit), (dokument.GetProperty("titel").GetString(), dokument.GetProperty("sichtbarkeit").GetString()));
        }
    }

    [Theory]
    [InlineData("titel", "   ")]
    [InlineData("titel", null)]
    [InlineData("titel", "lang")]
    [InlineData("sichtbarkeit", "Geheim")]
    [InlineData("sichtbarkeit", null)]
    public async Task Leerer_oder_zu_langer_Titel_und_unbekannte_Sichtbarkeit_ergeben_400_am_Feld(string feld, string? wert)
    {
        var id = await HochgeladenAsync(_s.Tobias, X, Pdf, "infobrief.pdf");

        using var antwort = await PatchAsync(id, 0, feld == "titel" ? "infobrief" : "Team", feld, wert == "lang" ? new string('x', 201) : wert);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.True((await ProblemAsync(antwort)).GetProperty("errors").TryGetProperty(feld, out _));
        Assert.Equal(["infobrief"], await TitelAsync(_s.Tobias, X));
        using var genau = await PatchAsync(id, 0, "infobrief", "titel", new string('x', 200));
        Assert.Equal(HttpStatusCode.OK, genau.StatusCode);
    }

    [Fact]
    public async Task Zwei_Felder_oder_keins_im_Koerper_ergeben_400()
    {
        var id = await HochgeladenAsync(_s.Tobias, X, Pdf, "infobrief.pdf");

        using var zwei = await SendenAsync(_s.Tobias, HttpMethod.Patch, $"{X}/{id}", """{"version":0,"alterWert":null,"titel":"a","sichtbarkeit":"Team"}""");
        using var keins = await SendenAsync(_s.Tobias, HttpMethod.Patch, $"{X}/{id}", """{"version":0,"alterWert":null}""");

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest], [zwei.StatusCode, keins.StatusCode]);
    }

    [Fact]
    public async Task Entfernen_loescht_Zeile_und_eigene_Datei_danach_404()
    {
        var id = await HochgeladenAsync(_s.Tobias, X, Pdf, "infobrief.pdf");

        using var entfernt = await SendenAsync(_s.Tobias, HttpMethod.Delete, $"{X}/{id}");
        using var nochmal = await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{X}/{id}");

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], [entfernt.StatusCode, nochmal.StatusCode]);
        Assert.Empty(await DokumenteAsync(_s.Miriam, X));
        Assert.Null(await RohAsync(s3, _s.Bucket!, $"freizeiten/{id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync($"{X}/{id}/datei", Abbruch)).StatusCode);
    }

    // ---------- Einbinden ----------

    [Fact]
    public async Task Miriam_bindet_die_Hausordnung_ein_ohne_Kopie_Auswahl_danach_ohne_zweites_Einbinden_409()
    {
        var hausordnung = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung.pdf");
        await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Png, "Aufsichtspflicht.png");
        var vonB = await HochgeladenAsync(_s.Lea, OrgPfad(_s.B), Pdf, "Fremd.pdf");
        Assert.Equal(["Aufsichtspflicht", "Hausordnung"], await TitelAsync(_s.Miriam, $"{X}/org"));

        using var eingebunden = await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(hausordnung));

        Assert.Equal(HttpStatusCode.Created, eingebunden.StatusCode);
        var json = await JsonAsync(eingebunden);
        Assert.Equal(("Hausordnung", "Team", true, "PDF"), (
            json.GetProperty("titel").GetString(), json.GetProperty("sichtbarkeit").GetString(), json.GetProperty("ausOrganisation").GetBoolean(),
            json.GetProperty("dateiart").GetString()));
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal(["Aufsichtspflicht"], await TitelAsync(_s.Miriam, $"{X}/org"));
        var liste = Assert.Single(await DokumenteAsync(_s.Tobias, X));
        Assert.True(liste.GetProperty("ausOrganisation").GetBoolean());
        Assert.Equal((long)Pdf.Length, liste.GetProperty("groesse").GetInt64());
        // Keine Kopie: kein Titel, keine Datei bei Freizeiten.
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM freizeiten.dokument WHERE \"Id\" = @id AND \"Titel\" IS NULL AND \"OrgDokumentId\" = @o", ("id", id), ("o", hausordnung)));
        Assert.Null(await RohAsync(s3, _s.Bucket!, $"freizeiten/{id}"));
        using var datei = await _s.Tobias.GetAsync($"{X}/{id}/datei", Abbruch);
        await DateiPruefenAsync(datei, Pdf, "application/pdf", "Hausordnung.pdf");

        using var zweites = await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(hausordnung));
        using var fremd = await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(vonB));
        using var unbekannt = await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(Guid.CreateVersion7()));
        using var ohne = await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(null));

        Assert.Equal(HttpStatusCode.Conflict, zweites.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(zweites)));
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound], [fremd.StatusCode, unbekannt.StatusCode]);
        Assert.Equal(HttpStatusCode.BadRequest, ohne.StatusCode);
        Assert.True((await ProblemAsync(ohne)).GetProperty("errors").TryGetProperty(DokumentFelder.OrgDokumentId, out _));
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.dokument"));
    }

    [Fact]
    public async Task Bei_einer_Einbindung_aendert_sich_nur_die_Sichtbarkeit_der_Titel_ergibt_400()
    {
        var hausordnung = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung.pdf");
        var id = (await JsonAsync(await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(hausordnung)))).GetProperty("id").GetGuid();

        using var titel = await PatchAsync(id, 0, "Hausordnung", "titel", "Neu");
        using var sichtbarkeit = await PatchAsync(id, 0, "Team", "sichtbarkeit", "ElternUndTeilnehmende");

        Assert.Equal(HttpStatusCode.BadRequest, titel.StatusCode);
        Assert.Equal("Den Titel pflegt die Organisation.", (await ProblemAsync(titel)).GetProperty("errors").GetProperty("titel")[0].GetString());
        Assert.Equal(HttpStatusCode.OK, sichtbarkeit.StatusCode);
        Assert.Equal("ElternUndTeilnehmende", (await DokumenteAsync(_s.Miriam, X)).Single().GetProperty("sichtbarkeit").GetString());
    }

    [Fact]
    public async Task Loescht_die_Organisation_das_Org_Dokument_faellt_die_Einbindung_aus_der_Liste_und_die_Datei_antwortet_404()
    {
        var hausordnung = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung.pdf");
        var id = (await JsonAsync(await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(hausordnung)))).GetProperty("id").GetGuid();
        await HochgeladenAsync(_s.Miriam, X, Pdf, "Packliste.pdf");

        Assert.Equal(HttpStatusCode.NoContent, (await SendenAsync(_s.Andrea, HttpMethod.Delete, $"{OrgPfad(_s.A)}/{hausordnung}")).StatusCode);

        Assert.Equal(["Packliste"], await TitelAsync(_s.Miriam, X));
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync($"{X}/{id}/datei", Abbruch)).StatusCode);
        Assert.Empty(await TitelAsync(_s.Miriam, $"{X}/org"));
    }

    // ---------- Abgeschlossen ----------

    [Fact]
    public async Task Nach_dem_Abschluss_ergeben_Hochladen_Aendern_Entfernen_und_Einbinden_409_Lesen_bleibt()
    {
        var id = await HochgeladenAsync(_s.Miriam, X, Pdf, "infobrief.pdf");
        var hausordnung = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung.pdf");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        foreach (var antwort in new[]
                 {
                     await HochladenAsync(_s.Miriam, X, Pdf), await PatchAsync(id, 0, "infobrief", "titel", "neu"),
                     await SendenAsync(_s.Tobias, HttpMethod.Delete, $"{X}/{id}"), await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(hausordnung)),
                 })
        {
            Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
            Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
        }

        Assert.Equal(["infobrief"], await TitelAsync(_s.Miriam, X));
        Assert.Equal(HttpStatusCode.OK, (await _s.Miriam.GetAsync($"{X}/{id}/datei", Abbruch)).StatusCode);
    }

    // ---------- Öffentlich ----------

    [Fact]
    public async Task Die_Freizeitseite_zeigt_nur_oeffentliche_Dokumente_und_liefert_nur_deren_Datei()
    {
        var (t, e, o, h) = await VierSichtbarkeitenAsync();

        using var seite = await _s.Anonym.GetAsync($"/api/freizeiten/oeffentlich/{_s.TeilnehmendeX}", Abbruch);

        Assert.Equal(HttpStatusCode.OK, seite.StatusCode);
        var dokument = Assert.Single((await JsonAsync(seite)).GetProperty("dokumente").EnumerateArray());
        Assert.Equal(["dateiart", "id", "titel"], Namen(dokument));
        Assert.Equal((o, "Anfahrt", "PDF"), (dokument.GetProperty("id").GetGuid(), dokument.GetProperty("titel").GetString(), dokument.GetProperty("dateiart").GetString()));
        using var datei = await _s.Anonym.GetAsync(OeffentlichPfad(_s.TeilnehmendeX, o), Abbruch);
        await DateiPruefenAsync(datei, Pdf, "application/pdf", "Anfahrt.pdf");
        foreach (var andere in new[] { t, e, h, Guid.CreateVersion7() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync(OeffentlichPfad(_s.TeilnehmendeX, andere), Abbruch)).StatusCode);
        }
    }

    [Fact]
    public async Task Ohne_oeffentliche_Dokumente_ist_die_Liste_auf_der_Freizeitseite_leer()
    {
        await HochgeladenAsync(_s.Miriam, X, Pdf, "intern.pdf");

        var seite = await JsonAsync(await _s.Anonym.GetAsync($"/api/freizeiten/oeffentlich/{_s.TeilnehmendeX}", Abbruch));

        Assert.Empty(seite.GetProperty("dokumente").EnumerateArray());
    }

    [Fact]
    public async Task Oeffentliche_Datei_mit_erneuertem_Anmeldelink_Gast_Token_oder_Anmeldelink_von_Z_ergibt_404()
    {
        var (_, _, o, _) = await VierSichtbarkeitenAsync();
        var (gast, _, _) = await _s.GastAnmeldenAsync("lina.eltern@example.org", "Lina");
        var alt = _s.TeilnehmendeX;

        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync(OeffentlichPfad(_s.TeilnehmendeZ, o), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync(OeffentlichPfad(gast, o), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync(OeffentlichPfad(Zugangslink.NeuesToken(), o), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_s.Miriam, $"/api/freizeiten/{_s.X}/anmeldelinks/teilnehmende/erneuern")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Anonym.GetAsync(OeffentlichPfad(alt, o), Abbruch)).StatusCode);
    }

    // ---------- Hilfen ----------

    private static string OeffentlichPfad(string token, Guid dokument) => $"/api/freizeiten/oeffentlich/{token}/dokumente/{dokument}";

    private Task<HttpResponseMessage> PatchAsync(Guid dokument, uint version, string? alterWert, string feld, string? neu) =>
        SendenAsync(
            _s.Tobias, HttpMethod.Patch, $"{X}/{dokument}",
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["version"] = version, ["alterWert"] = alterWert, [feld] = neu }));

    /// <summary>
    /// Je ein Dokument in X mit Sichtbarkeit Team (T, „Programmideen“), Eltern und Teilnehmende (E, „Infobrief“) und öffentlich (O,
    /// „Anfahrt“) und die eingebundene Hausordnung von A mit Sichtbarkeit Eltern und Teilnehmende (H).
    /// </summary>
    private async Task<(Guid T, Guid E, Guid O, Guid H)> VierSichtbarkeitenAsync()
    {
        var t = await HochgeladenAsync(_s.Miriam, X, Pdf, "Programmideen.pdf", "Team");
        var e = await HochgeladenAsync(_s.Miriam, X, Pdf, "Infobrief.pdf", "ElternUndTeilnehmende");
        var o = await HochgeladenAsync(_s.Miriam, X, Pdf, "Anfahrt.pdf", "Oeffentlich");
        var hausordnung = await HochgeladenAsync(_s.Andrea, OrgPfad(_s.A), Pdf, "Hausordnung.pdf");
        var h = (await JsonAsync(await PostAsync(_s.Miriam, $"{X}/einbindungen", new DokumentEinbinden(hausordnung)))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(h, 0, "Team", "sichtbarkeit", "ElternUndTeilnehmende")).StatusCode);
        return (t, e, o, h);
    }
}
