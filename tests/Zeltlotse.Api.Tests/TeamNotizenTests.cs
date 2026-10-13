using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-5, Team-Notizen (FR-26, AD-14, AD-16, AD-22, AD-30) gegen echtes PostgreSQL: Anlegen, Lesen (neueste zuerst, mit Verfasser) und
/// Löschen für Leitung und Team; der Text nur als bytea, nie in Outbox, Pass oder eigenen Anmeldungen; Feldfehler; Anmeldungen anderer
/// Freizeiten, fremde Rollen und Organisationen <c>404</c>; Datenauskunft der betroffenen Person; Löschen mit Person und Löschlauf.
/// </summary>
public sealed class TeamNotizenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Text = "Hatte letztes Jahr am ersten Abend Heimweh; ein Gespräch am Lagerfeuer hat geholfen.";

    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    [Fact]
    public async Task Team_legt_an_Leitung_liest_neueste_zuerst_mit_Verfasser_und_Datum_der_Text_steht_nur_als_bytea()
    {
        var mia = await _s.KindAnmeldenAsync(_s.Mia);
        await _s.BestaetigenAsync(mia);
        var erste = _s.VolleSekunde();

        using var angelegt = await PostAsync(_s.Tobias, NotizenPfad(_s.X, mia), new TeamNotizAnlegen($"  {Text} "));

        Assert.Equal(HttpStatusCode.Created, angelegt.StatusCode);
        var id = (await JsonAsync(angelegt)).GetProperty("id").GetGuid();
        Assert.Equal("bytea", await WertAsync<string>(_s.Verbindung, "SELECT pg_typeof(\"Text\")::text FROM freizeiten.team_notiz"));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz WHERE position(convert_to('Heimweh', 'UTF8') in \"Text\") > 0"));
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung,
            "SELECT count(*) FROM freizeiten.team_notiz WHERE \"Id\" = @id AND \"PersonId\" = @p AND \"VerfasstVon\" = @v AND \"AnmeldungId\" = @a AND \"FreizeitId\" = @f AND \"OrganisationId\" = @o",
            ("id", id), ("p", _s.Mia), ("v", _s.TobiasId), ("a", mia), ("f", _s.X), ("o", _s.A)));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Inhalt\"::text LIKE '%Heimweh%'"));

        _s.Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        using (var zweite = await PostAsync(_s.Miriam, NotizenPfad(_s.X, mia), new TeamNotizAnlegen("Isst gern Nudeln.")))
        {
            Assert.Equal(HttpStatusCode.Created, zweite.StatusCode);
        }

        using var liste = await _s.Miriam.GetAsync(NotizenPfad(_s.X, mia), Abbruch);

        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);
        var notizen = (await JsonAsync(liste)).EnumerateArray().ToList();
        Assert.All(notizen, n => Assert.Equal(["angelegtAm", "id", "text", "verfasserNachname", "verfasserVorname", "verfasstVon"], Namen(n)));
        Assert.Equal(["Isst gern Nudeln.", Text], notizen.Select(n => n.GetProperty("text").GetString()));
        Assert.Equal(["Miriam Zeltner", "Tobias Wanderer"], notizen.Select(n => $"{n.GetProperty("verfasserVorname").GetString()} {n.GetProperty("verfasserNachname").GetString()}"));
        Assert.Equal(erste, notizen[1].GetProperty("angelegtAm").GetDateTimeOffset());
        Assert.Equal(id, notizen[1].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await _s.Tobias.GetAsync(NotizenPfad(_s.X, mia), Abbruch)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("zu lang")]
    public async Task Leerer_oder_zu_langer_Text_ergibt_400_am_Feld_text(string? text)
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);

        using var antwort = await PostAsync(_s.Miriam, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(text == "zu lang" ? new string('x', 2001) : text));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.True(problem.GetProperty("errors").TryGetProperty(TeamNotizFelder.Text, out _));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz"));

        using var genau = await PostAsync(_s.Miriam, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(new string('x', 2000)));
        Assert.Equal(HttpStatusCode.Created, genau.StatusCode);
    }

    [Fact]
    public async Task Loeschen_darf_jedes_Mitglied_auch_eine_fremde_Notiz_danach_404()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var id = (await JsonAsync(await PostAsync(_s.Miriam, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(Text)))).GetProperty("id").GetGuid();

        using var geloescht = await SendenAsync(_s.Tobias, HttpMethod.Delete, $"{NotizenPfad(_s.X, emma)}/{id}");
        using var nochmal = await SendenAsync(_s.Tobias, HttpMethod.Delete, $"{NotizenPfad(_s.X, emma)}/{id}");

        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nochmal.StatusCode);
        Assert.Empty((await JsonAsync(await _s.Miriam.GetAsync(NotizenPfad(_s.X, emma), Abbruch))).EnumerateArray());
    }

    [Fact]
    public async Task Notiz_einer_Anmeldung_einer_anderen_Freizeit_oder_unbekannt_ergibt_404()
    {
        var inZ = await _s.KindAnmeldenAsync(_s.Emma, _s.TeilnehmendeZ);
        var emmaX = await _s.KindAnmeldenAsync(_s.Emma);
        var id = (await JsonAsync(await PostAsync(_s.Miriam, NotizenPfad(_s.X, emmaX), new TeamNotizAnlegen(Text)))).GetProperty("id").GetGuid();

        // Miriam ist Leitung beider Freizeiten; die Anmeldung aus Z ist über X nicht erreichbar.
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(_s.Miriam, NotizenPfad(_s.X, inZ), new TeamNotizAnlegen(Text))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(NotizenPfad(_s.X, inZ), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Miriam.GetAsync(NotizenPfad(_s.X, Guid.CreateVersion7()), Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{NotizenPfad(_s.Z, inZ)}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendenAsync(_s.Miriam, HttpMethod.Delete, $"{NotizenPfad(_s.X, inZ)}/{id}")).StatusCode);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz"));
    }

    [Fact]
    public async Task Eine_abgeschlossene_Freizeit_ist_nur_lesbar_Anlegen_und_Loeschen_ergeben_409()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var id = (await JsonAsync(await PostAsync(_s.Miriam, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(Text)))).GetProperty("id").GetGuid();
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.freizeit SET \"AbgeschlossenAm\" = now() WHERE \"Id\" = @f", ("f", _s.X));

        using var anlegen = await PostAsync(_s.Miriam, NotizenPfad(_s.X, emma), new TeamNotizAnlegen("Noch eine."));
        using var loeschen = await SendenAsync(_s.Tobias, HttpMethod.Delete, $"{NotizenPfad(_s.X, emma)}/{id}");

        Assert.Equal([HttpStatusCode.Conflict, HttpStatusCode.Conflict], [anlegen.StatusCode, loeschen.StatusCode]);
        Assert.Equal(Zeltlotse.Api.Contracts.Gemeinsam.Problemcodes.FreizeitAbgeschlossen, (await ProblemAsync(anlegen)).GetProperty("code").GetString());
        Assert.Equal(Zeltlotse.Api.Contracts.Gemeinsam.Problemcodes.FreizeitAbgeschlossen, (await ProblemAsync(loeschen)).GetProperty("code").GetString());
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz"));
        Assert.Equal(HttpStatusCode.OK, (await _s.Miriam.GetAsync(NotizenPfad(_s.X, emma), Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Eine_abgesagte_Anmeldung_nimmt_keine_Notiz_an_404()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        await _s.AbsagenAsync(emma);

        using var antwort = await PostAsync(_s.Miriam, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(Text));

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz"));
    }

    [Fact]
    public async Task Org_Admin_ohne_Rolle_fremde_Organisation_Global_Admin_und_Eltern_ergeben_404()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var id = (await JsonAsync(await PostAsync(_s.Miriam, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(Text)))).GetProperty("id").GetGuid();

        foreach (var client in new[] { _s.Andrea, _s.Lea, _s.Gerd, _s.Katrin })
        {
            using var lesen = await client.GetAsync(NotizenPfad(_s.X, emma), Abbruch);
            using var anlegen = await PostAsync(client, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(Text));
            using var loeschen = await SendenAsync(client, HttpMethod.Delete, $"{NotizenPfad(_s.X, emma)}/{id}");
            Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], [lesen.StatusCode, anlegen.StatusCode, loeschen.StatusCode]);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Anonym.GetAsync(NotizenPfad(_s.X, emma), Abbruch)).StatusCode);
        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz"));
    }

    [Fact]
    public async Task Die_Notiz_erscheint_nicht_im_Pass_nicht_in_den_eigenen_Anmeldungen_nur_in_der_Datenauskunft_der_Familie()
    {
        var mia = await _s.KindAnmeldenAsync(_s.Mia);
        await _s.BestaetigenAsync(mia);
        await _s.ZustellenAsync();
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(_s.Tobias, NotizenPfad(_s.X, mia), new TeamNotizAnlegen(Text))).StatusCode);

        foreach (var (client, pfad) in new[]
                 {
                     (_s.Miriam, PassPfad(_s.X, _s.Mia)), (_s.Tobias, PassPfad(_s.X, _s.Mia)), (_s.Katrin, $"/api/ich/freizeitpass/{_s.Mia}"),
                     (_s.Katrin, "/api/ich/anmeldungen"), (_s.Miriam, $"/api/freizeiten/{_s.X}/anmeldungen"), (_s.Miriam, PersonenlistePfad(_s.X)),
                 })
        {
            using var antwort = await client.GetAsync(pfad, Abbruch);
            Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            Assert.DoesNotContain("Heimweh", await antwort.Content.ReadAsStringAsync(Abbruch), StringComparison.Ordinal);
        }

        var auskunft = (await _s.Katrin.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        var abschnitt = Assert.Single(auskunft, a => a.Titel == "Team-Notizen");
        var zeile = Assert.Single(abschnitt.Zeilen);
        Assert.Equal(Kapf, zeile.Feld);
        Assert.EndsWith($": {Text}", zeile.Wert, StringComparison.Ordinal);
        // Tobias (Verfasser) bekommt die Notiz nicht in seiner Auskunft.
        var tobias = (await _s.Tobias.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!.Abschnitte;
        Assert.DoesNotContain(tobias, a => a.Titel == "Team-Notizen");
    }

    [Fact]
    public async Task PersonGeloescht_loescht_Notizen_ueber_die_Person_und_setzt_den_Verfasser_auf_den_Platzhalter()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        var anmeldungTobias = _s.AnmeldungTobias;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(_s.Tobias, NotizenPfad(_s.X, emma), new TeamNotizAnlegen(Text))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(_s.Miriam, NotizenPfad(_s.X, anmeldungTobias), new TeamNotizAnlegen("Bringt Gitarre mit."))).StatusCode);

        // Tobias löscht sein Konto (frische Anmeldung nötig, AD-25).
        await AnmeldenAsync(_s.Factory, _s.Tobias, FreizeitHelfer.Tobias);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(_s.Tobias, "/api/ich/profil/loeschen")).StatusCode);
        await _s.ZustellenAsync();

        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz WHERE \"PersonId\" = @t", ("t", _s.TobiasId)));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz WHERE \"VerfasstVon\" = @t", ("t", _s.TobiasId)));
        Assert.Equal(1L, await WertAsync<long>(
            _s.Verbindung, "SELECT count(*) FROM freizeiten.team_notiz WHERE \"PersonId\" = @e AND \"VerfasstVon\" = @g", ("e", _s.Emma), ("g", GeloeschtePerson.Id)));
        var notiz = Assert.Single((await JsonAsync(await _s.Miriam.GetAsync(NotizenPfad(_s.X, emma), Abbruch))).EnumerateArray());
        Assert.Equal(("Gelöschte Person", JsonValueKind.Null, Text), (
            notiz.GetProperty("verfasserVorname").GetString(), notiz.GetProperty("verfasserNachname").ValueKind, notiz.GetProperty("text").GetString()));
    }
}
