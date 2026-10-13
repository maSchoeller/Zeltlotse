using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-10, Vorfall-Protokoll und Vorfall-Notiz (CAP-17, FR-31, AD-14, AD-16, AD-18, AD-30) gegen echtes PostgreSQL: Einträge anlegen und
/// lesen (Leitung und Team), Text nur verschlüsselt, doppeltes Senden, Kennung einer anderen Freizeit, Personen der Freizeit, Formfehler,
/// Filter je Person, abgeschlossene Freizeit, die Pflichtfälle der Freizeit- und der Org-Route, das Archiv der Organisation, Löschen mit
/// der Person und nach der Frist sowie die Datenauskunft.
/// </summary>
public sealed class VorfaelleTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private static string Pfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/sicherheit/vorfaelle";

    private static string ArchivPfad(Guid organisation) => $"/api/organisationen/{organisation}/sicherheit/vorfall-archiv";

    private Task<HttpResponseMessage> SchreibenAsync(HttpClient client, Guid person, string? text, VorfallArt art = VorfallArt.Geschehen, Guid? id = null, Guid? freizeit = null) =>
        PostAsync(client, Pfad(freizeit ?? _s.X), new VorfallEintragNeu(id ?? Guid.CreateVersion7(), person, art, text));

    /// <summary>Schreibt einen Eintrag und erwartet <c>201</c>; liefert die Kennung.</summary>
    private async Task<Guid> AngelegtAsync(HttpClient client, Guid person, string text, VorfallArt art = VorfallArt.Geschehen, Guid? freizeit = null)
    {
        var id = Guid.CreateVersion7();
        using var antwort = await SchreibenAsync(client, person, text, art, id, freizeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return id;
    }

    private async Task<List<JsonElement>> LesenAsync(HttpClient? client = null, string? abfrage = null, Guid? freizeit = null)
    {
        using var antwort = await (client ?? _s.Miriam).GetAsync(Pfad(freizeit ?? _s.X) + abfrage, Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).EnumerateArray()];
    }

    private Task<long> ZeilenAsync(string bedingung = "TRUE", params (string Name, object Wert)[] parameter) =>
        WertAsync<long>(_s.Verbindung, $"SELECT count(*) FROM sicherheit.vorfall_eintrag WHERE {bedingung}", parameter);

    private static async Task<string?> FeldfehlerAsync(HttpResponseMessage antwort, string feld)
    {
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var fehler = (await ProblemAsync(antwort)).GetProperty("errors");
        Assert.True(fehler.TryGetProperty(feld, out var texte), feld);
        return texte[0].GetString();
    }

    // ---------- Schreiben und Lesen ----------

    [Fact]
    public async Task Schreiben_Tobias_legt_an_Miriam_liest_getrimmt_mit_Name_und_Verfasser_in_der_Datenbank_kein_Klartext()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var jetzt = _s.VolleSekunde();
        var id = Guid.CreateVersion7();

        using var antwort = await SchreibenAsync(_s.Tobias, _s.Emma, " Knie ", id: id);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        Assert.NotNull(antwort.Headers.Location);
        Assert.Contains(Pfad(_s.X), antwort.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var eintrag = Assert.Single(await LesenAsync());
        Assert.Equal(["art", "erfasstAm", "id", "nachname", "personId", "text", "verfasserNachname", "verfasserVorname", "vorname"], Namen(eintrag));
        Assert.Equal(
            (id, _s.Emma, "Emma", "Muster", "Geschehen", "Knie", jetzt, "Tobias", "Wanderer"),
            (eintrag.GetProperty("id").GetGuid(), eintrag.GetProperty("personId").GetGuid(), eintrag.GetProperty("vorname").GetString(),
                eintrag.GetProperty("nachname").GetString(), eintrag.GetProperty("art").GetString(), eintrag.GetProperty("text").GetString(),
                eintrag.GetProperty("erfasstAm").GetDateTimeOffset(), eintrag.GetProperty("verfasserVorname").GetString(),
                eintrag.GetProperty("verfasserNachname").GetString()));
        Assert.Equal("bytea", await WertAsync<string>(_s.Verbindung, "SELECT pg_typeof(\"Text\")::text FROM sicherheit.vorfall_eintrag"));
        Assert.Equal(0L, await ZeilenAsync("position(convert_to('Knie', 'UTF8') in \"Text\") > 0"));
        Assert.Equal(1L, await ZeilenAsync(
            "\"Id\" = @id AND \"PersonId\" = @p AND \"VerfasstVon\" = @v AND \"FreizeitId\" = @f AND \"OrganisationId\" = @o AND \"Art\" = 1 AND \"ArchiviertAm\" IS NULL",
            ("id", id), ("p", _s.Emma), ("v", _s.TobiasId), ("f", _s.X), ("o", _s.A)));
        // Tobias (Team) liest dasselbe.
        Assert.Equal("Knie", Assert.Single(await LesenAsync(_s.Tobias)).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Doppelt_auch_gleichzeitig_ergibt_jedes_Mal_201_und_eine_Zeile()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var id = Guid.CreateVersion7();

        var gleichzeitig = await Task.WhenAll(SchreibenAsync(_s.Tobias, _s.Emma, "Knie", id: id), SchreibenAsync(_s.Tobias, _s.Emma, "Knie", id: id));
        using var spaeter = await SchreibenAsync(_s.Tobias, _s.Emma, "Knie", id: id);

        Assert.All(gleichzeitig.Append(spaeter), a => Assert.Equal(HttpStatusCode.Created, a.StatusCode));
        foreach (var antwort in gleichzeitig)
        {
            antwort.Dispose();
        }

        Assert.Equal(1L, await ZeilenAsync());
    }

    [Fact]
    public async Task Fremde_Id_aus_einer_anderen_Freizeit_ist_409_konflikt()
    {
        var id = Guid.CreateVersion7();
        using (var inZ = await SchreibenAsync(_s.Miriam, _s.MiriamId, "Wespenstich", id: id, freizeit: _s.Z))
        {
            Assert.Equal(HttpStatusCode.Created, inZ.StatusCode);
        }

        using var antwort = await SchreibenAsync(_s.Miriam, _s.MiriamId, "Wespenstich", id: id);

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        Assert.Equal(1L, await ZeilenAsync());
    }

    [Fact]
    public async Task Person_fremd_unbekannt_oder_geloescht_ist_400_am_Feld_personId()
    {
        foreach (var person in new[] { _s.Mia, Guid.CreateVersion7(), GeloeschtePerson.Id })
        {
            using var antwort = await SchreibenAsync(_s.Tobias, person, "Knie");
            Assert.Equal("Diese Person gehört nicht zur Freizeit.", await FeldfehlerAsync(antwort, VorfallFelder.PersonId));
        }

        Assert.Equal(0L, await ZeilenAsync());
    }

    [Fact]
    public async Task Team_als_Person_Miriam_Leitung_ohne_Anmeldung_ist_201()
    {
        await AngelegtAsync(_s.Tobias, _s.MiriamId, "Mit dem Fuß umgeknickt.");
        await AngelegtAsync(_s.Miriam, _s.TobiasId, "Kühlpack", VorfallArt.Medikament);

        Assert.Equal(2L, await ZeilenAsync());
    }

    [Fact]
    public async Task Abgesagt_Emmas_abgesagte_Anmeldung_ist_400_am_Feld_personId()
    {
        var anmeldung = await _s.KindAnmeldenAsync(_s.Emma);
        await _s.AbsagenAsync(anmeldung);

        using var antwort = await SchreibenAsync(_s.Tobias, _s.Emma, "Knie");

        Assert.Equal("Diese Person gehört nicht zur Freizeit.", await FeldfehlerAsync(antwort, VorfallFelder.PersonId));
    }

    [Fact]
    public async Task Formfehler_leerer_Text_zu_lang_unbekannte_Art_leere_Id_sind_400_am_Feld()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var faelle = new (object Koerper, string Feld)[]
        {
            (new { id = Guid.CreateVersion7(), personId = _s.Emma, art = "Geschehen", text = "   " }, VorfallFelder.Text),
            (new { id = Guid.CreateVersion7(), personId = _s.Emma, art = "Geschehen", text = (string?)null }, VorfallFelder.Text),
            (new { id = Guid.CreateVersion7(), personId = _s.Emma, art = "Geschehen", text = new string('x', VorfallFelder.TextMaximaleLaenge + 1) }, VorfallFelder.Text),
            (new { id = Guid.CreateVersion7(), personId = _s.Emma, art = 9, text = "Knie" }, VorfallFelder.Art),
            (new { id = Guid.Empty, personId = _s.Emma, art = "Geschehen", text = "Knie" }, VorfallFelder.Id),
        };

        foreach (var (koerper, feld) in faelle)
        {
            using var antwort = await PostAsync(_s.Tobias, Pfad(_s.X), koerper);
            await FeldfehlerAsync(antwort, feld);
        }

        Assert.Equal(0L, await ZeilenAsync());
    }

    [Fact]
    public async Task Filter_je_Person_nur_Emma_neueste_zuerst()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var erste = await AngelegtAsync(_s.Tobias, _s.Emma, "Knie");
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));
        await AngelegtAsync(_s.Miriam, _s.TobiasId, "Kopfweh");
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));
        var dritte = await AngelegtAsync(_s.Miriam, _s.Emma, "2 Pflaster", VorfallArt.Entnahme);

        var emma = await LesenAsync(abfrage: $"?personId={_s.Emma}");

        Assert.Equal([dritte, erste], emma.Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal(["Entnahme", "Geschehen"], emma.Select(e => e.GetProperty("art").GetString()));
        Assert.Equal(3, (await LesenAsync()).Count);
        Assert.Equal(["Kopfweh"], (await LesenAsync(abfrage: $"?personId={_s.TobiasId}")).Select(e => e.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Abgeschlossen_POST_409_freizeit_abgeschlossen_GET_200()
    {
        await AngelegtAsync(_s.Tobias, _s.MiriamId, "Knie");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        using var antwort = await SchreibenAsync(_s.Tobias, _s.MiriamId, "Nachtrag");

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.FreizeitAbgeschlossen, Code(await ProblemAsync(antwort)));
        Assert.Single(await LesenAsync(_s.Tobias));
    }

    // ---------- Rechte (AD-30) ----------

    [Fact]
    public async Task Rechte_Miriam_Tobias_Andrea_fremde_Organisation_ohne_Sitzung()
    {
        var (orgC, sara) = await OrgHelfer.OrganisationMitOrgAdminAsync(_s.Factory, _s.Gerd, "Jugendwerk C", "sara.kochloeffel@example.org");
        using var _ = sara;
        var fremde = await FreizeitHelfer.AnlegenAsync(sara, orgC, "Fremdes Zeltlager");
        var faelle = new[] { (_s.Miriam, _s.X), (_s.Tobias, _s.X), (_s.Andrea, _s.X), (_s.Miriam, fremde), (_s.Anonym, _s.X) };

        var lesen = new List<HttpStatusCode>();
        var schreiben = new List<HttpStatusCode>();
        foreach (var (client, freizeit) in faelle)
        {
            using var get = await client.GetAsync(Pfad(freizeit), Abbruch);
            lesen.Add(get.StatusCode);
            using var post = await SchreibenAsync(client, _s.MiriamId, "Knie", freizeit: freizeit);
            schreiben.Add(post.StatusCode);
        }

        HttpStatusCode[] erwartet(HttpStatusCode ok) => [ok, ok, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized];
        Assert.Equal(erwartet(HttpStatusCode.OK), lesen);
        Assert.Equal(erwartet(HttpStatusCode.Created), schreiben);
        Assert.Equal(2L, await ZeilenAsync());
    }

    // ---------- Archiv ----------

    [Fact]
    public async Task Archiv_zeigt_nur_archivierte_Eintraege_unter_dem_Namen_der_Freizeit()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var archiviert = await AngelegtAsync(_s.Tobias, _s.Emma, "Knie");
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        await AngelegtAsync(_s.Tobias, _s.MiriamId, "Noch offen");
        await AngelegtAsync(_s.Miriam, _s.MiriamId, "In Z", freizeit: _s.Z);
        await AusfuehrenAsync(_s.Verbindung, "UPDATE sicherheit.vorfall_eintrag SET \"ArchiviertAm\" = now() WHERE \"Id\" = @id", ("id", archiviert));

        using var antwort = await _s.Andrea.GetAsync(ArchivPfad(_s.A), Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var freizeit = Assert.Single((await JsonAsync(antwort)).EnumerateArray());
        Assert.Equal(["eintraege", "freizeitId", "name"], Namen(freizeit));
        Assert.Equal((_s.X, FreizeitpassSzenario.Kapf), (freizeit.GetProperty("freizeitId").GetGuid(), freizeit.GetProperty("name").GetString()));
        var eintrag = Assert.Single(freizeit.GetProperty("eintraege").EnumerateArray());
        Assert.Equal(
            (archiviert, "Knie", "Emma", "Tobias"),
            (eintrag.GetProperty("id").GetGuid(), eintrag.GetProperty("text").GetString(), eintrag.GetProperty("vorname").GetString(),
                eintrag.GetProperty("verfasserVorname").GetString()));
        // Die Organisation B sieht in ihrem (leeren) Archiv nichts aus A.
        Assert.Empty((await JsonAsync(await _s.Lea.GetAsync(ArchivPfad(_s.B), Abbruch))).EnumerateArray());
    }

    [Fact]
    public async Task Archiv_ordnet_Freizeiten_nach_ihrem_neuesten_Eintrag_und_Eintraege_neueste_zuerst()
    {
        var pfingsten = await FreizeitHelfer.AnlegenAsync(_s.Andrea, _s.A, "Pfingstlager 2027", new DateOnly(2027, 5, 14), new DateOnly(2027, 5, 17));
        await FreizeitHelfer.EinladenAsync(_s.Andrea, _s.A, pfingsten, FreizeitHelfer.Miriam);
        await FreizeitHelfer.AnnehmenMitAsync(_s.Factory, _s.Miriam, FreizeitHelfer.Miriam);
        var x1 = await AngelegtAsync(_s.Miriam, _s.MiriamId, "X eins");
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var p1 = await AngelegtAsync(_s.Miriam, _s.MiriamId, "P eins", freizeit: pfingsten);
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var x2 = await AngelegtAsync(_s.Miriam, _s.MiriamId, "X zwei");
        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var p2 = await AngelegtAsync(_s.Miriam, _s.MiriamId, "P zwei", freizeit: pfingsten);
        await AusfuehrenAsync(_s.Verbindung, "UPDATE sicherheit.vorfall_eintrag SET \"ArchiviertAm\" = now()");

        using var antwort = await _s.Andrea.GetAsync(ArchivPfad(_s.A), Abbruch);

        var gruppen = (await JsonAsync(antwort)).EnumerateArray().ToList();
        Assert.Equal(["Pfingstlager 2027", FreizeitpassSzenario.Kapf], gruppen.Select(g => g.GetProperty("name").GetString()));
        Assert.Equal([p2, p1], gruppen[0].GetProperty("eintraege").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal([x2, x1], gruppen[1].GetProperty("eintraege").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Rechte_Archiv_Andrea_Org_Ueberblick_Miriam_Lea_ohne_Sitzung()
    {
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, OrgHelfer.Jonas, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, OrgHelfer.Jonas);

        var status = new List<HttpStatusCode>();
        foreach (var client in new[] { _s.Andrea, ueberblick, _s.Miriam, _s.Lea, _s.Anonym })
        {
            using var antwort = await client.GetAsync(ArchivPfad(_s.A), Abbruch);
            status.Add(antwort.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized], status);
    }

    [Fact]
    public async Task Org_Admin_bekommt_den_Aktionsschluessel_vorfall_archiv_sehen()
    {
        await OrgHelfer.EinladenAsync(_s.Andrea, _s.A, OrgHelfer.Jonas, OrgRolle.OrgUeberblick);
        using var ueberblick = await OrgHelfer.AnnehmenAlsAsync(_s.Factory, OrgHelfer.Jonas);

        static async Task<IEnumerable<string?>> ErlaubtAsync(HttpClient client, Guid org) =>
            (await JsonAsync(await client.GetAsync($"/api/organisationen/{org}", Abbruch))).GetProperty("erlaubt").EnumerateArray().Select(e => e.GetString());

        Assert.Contains(Aktionsschluessel.VorfallArchivSehen, await ErlaubtAsync(_s.Andrea, _s.A));
        Assert.DoesNotContain(Aktionsschluessel.VorfallArchivSehen, await ErlaubtAsync(ueberblick, _s.A));
    }

    // ---------- Datenschutz ----------

    [Fact]
    public async Task Person_geloescht_die_Zeilen_bleiben_mit_Platzhalter_und_Gelöschte_Person()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        await AngelegtAsync(_s.Tobias, _s.Emma, "Knie");
        await AngelegtAsync(_s.Miriam, _s.TobiasId, "Kopfweh");

        // Katrin löscht ihr Konto (mit den Kindern), Tobias seins (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Katrin, FamilienHelfer.Katrin);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_s.Katrin, "/api/ich/profil/loeschen")).StatusCode);
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_s.Tobias, "/api/ich/profil/loeschen")).StatusCode);
        await _s.ZustellenAsync();

        Assert.Equal(2L, await ZeilenAsync());
        Assert.Equal(0L, await ZeilenAsync("\"PersonId\" IN (@e, @t) OR \"VerfasstVon\" = @t", ("e", _s.Emma), ("t", _s.TobiasId)));
        Assert.Equal(1L, await ZeilenAsync("\"PersonId\" = @g AND \"VerfasstVon\" = @g", ("g", GeloeschtePerson.Id)));
        Assert.Equal(1L, await ZeilenAsync("\"PersonId\" = @g AND \"VerfasstVon\" = @m", ("g", GeloeschtePerson.Id), ("m", _s.MiriamId)));
        var eintraege = await LesenAsync();
        Assert.All(eintraege, e => Assert.Equal(("Gelöschte Person", JsonValueKind.Null), (e.GetProperty("vorname").GetString(), e.GetProperty("nachname").ValueKind)));
        var vonTobias = Assert.Single(eintraege, e => e.GetProperty("verfasserVorname").GetString() == "Gelöschte Person");
        Assert.Equal(JsonValueKind.Null, vonTobias.GetProperty("verfasserNachname").ValueKind);
        // Die Texte sind mit den vernichteten Schlüsseln nicht mehr lesbar.
        Assert.All(eintraege, e => Assert.Equal(JsonValueKind.Null, e.GetProperty("text").ValueKind));
    }

    [Fact]
    public async Task Filter_auf_den_Platzhalter_geloeschter_Personen_liefert_keine_Eintraege()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        await AngelegtAsync(_s.Tobias, _s.Emma, "Knie");
        await AngelegtAsync(_s.Miriam, _s.TobiasId, "Kopfweh");
        await AusfuehrenAsync(_s.Verbindung, "UPDATE sicherheit.vorfall_eintrag SET \"PersonId\" = @g", ("g", GeloeschtePerson.Id));

        Assert.Empty(await LesenAsync(abfrage: $"?personId={GeloeschtePerson.Id}"));
        Assert.Equal(2, (await LesenAsync()).Count);
    }

    [Fact]
    public async Task Schluessel_weg_GET_200_mit_text_null()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        await AngelegtAsync(_s.Tobias, _s.Emma, "Knie");
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM personen.personen_schluessel WHERE \"PersonId\" = @p", ("p", _s.Emma));

        var eintrag = Assert.Single(await LesenAsync());

        Assert.Equal((JsonValueKind.Null, "Emma"), (eintrag.GetProperty("text").ValueKind, eintrag.GetProperty("vorname").GetString()));
    }

    [Fact]
    public async Task Frist_der_Loeschlauf_loescht_die_Eintraege_der_abgelaufenen_Freizeit_die_anderen_bleiben()
    {
        await AngelegtAsync(_s.Tobias, _s.MiriamId, "In X");
        await AngelegtAsync(_s.Miriam, _s.MiriamId, "In Z", freizeit: _s.Z);

        await using (var scope = _s.Factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "SicherheitLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(_s.Factory.Uhr.GetUtcNow(), [_s.X], []), Abbruch);
        }

        Assert.Equal(0L, await ZeilenAsync("\"FreizeitId\" = @f", ("f", _s.X)));
        Assert.Equal(1L, await ZeilenAsync("\"FreizeitId\" = @f", ("f", _s.Z)));
    }

    [Fact]
    public async Task Auskunft_Eintraege_ueber_Emma_bei_Katrin_mit_Text_und_von_Tobias_verfasste_als_Eintrag_verfasst()
    {
        await _s.KindAnmeldenAsync(_s.Emma);
        var jetzt = _s.VolleSekunde();
        await AngelegtAsync(_s.Tobias, _s.Emma, "Knie", VorfallArt.Geschehen);

        var katrin = (await _s.Katrin.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var tobias = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var miriam = (await _s.Miriam.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;

        var ueberEmma = Assert.Single(Assert.Single(katrin, a => a.Titel == "Vorfall-Protokoll").Zeilen);
        Assert.Equal(("Was passiert ist", $"{AuskunftFormat.Zeitpunkt(jetzt)}: Knie"), (ueberEmma.Feld, ueberEmma.Wert));
        var verfasst = Assert.Single(Assert.Single(tobias, a => a.Titel == "Vorfall-Protokoll").Zeilen);
        Assert.Equal(("Eintrag verfasst", AuskunftFormat.Zeitpunkt(jetzt)), (verfasst.Feld, verfasst.Wert));
        Assert.DoesNotContain("Knie", verfasst.Wert, StringComparison.Ordinal);
        Assert.DoesNotContain(miriam, a => a.Titel == "Vorfall-Protokoll");
    }
}
