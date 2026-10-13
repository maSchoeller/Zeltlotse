using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-6, Erinnern zum Freizeitpass (FR-25, AD-13, AD-16, AD-30) gegen echtes PostgreSQL:
/// <c>POST /api/freizeiten/{freizeitId}/personen/freizeitpass/erinnerungen</c> merkt eine Mail über die Outbox vor (nur Kennungen), die nach
/// der Zustellung an die Person, einen Elternteil mit Konto oder den Gast-Zugang geht; höchstens einmal am Tag; nur bei aktiver Freigabe.
/// Dazu Löschen und Datenauskunft.
/// </summary>
public sealed class FreizeitpassErinnerungTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    private Task<HttpResponseMessage> ErinnernAsync(Guid? person, HttpClient? client = null) =>
        PostAsync(client ?? _s.Miriam, ErinnernPfad(_s.X), new FreizeitpassErinnerungSenden(person));

    private static DateTimeOffset Mikro(DateTimeOffset zeit) => new(zeit.Ticks - (zeit.Ticks % 10), zeit.Offset);

    [Fact]
    public async Task Miriam_erinnert_Mia_ohne_eigenes_Konto_und_nach_der_Zustellung_hat_Katrin_genau_eine_Mail()
    {
        await _s.KindAnmeldenAsync(_s.Mia);
        var mailsVorher = _s.Factory.Mails.An(FamilienHelfer.Katrin).Count;
        var jetzt = _s.Factory.Uhr.GetUtcNow();

        using var antwort = await ErinnernAsync(_s.Mia);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["erinnertAm"], Namen(json));
        Assert.Equal(jetzt, json.GetProperty("erinnertAm").GetDateTimeOffset());
        var inhalt = await WertAsync<string>(_s.Verbindung, "SELECT \"Inhalt\"::text FROM personen.outbox WHERE \"Typ\" LIKE '%FreizeitpassErinnerungMail'");
        Assert.DoesNotContain("@", inhalt, StringComparison.Ordinal);
        foreach (var name in new[] { "Mia", "Muster", "Miriam", "Zeltner", "Katrin" })
        {
            Assert.DoesNotContain(name, inhalt, StringComparison.Ordinal);
        }

        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_einsicht WHERE \"PersonId\" = @p", ("p", _s.Mia)));
        Assert.Equal(mailsVorher, _s.Factory.Mails.An(FamilienHelfer.Katrin).Count);

        await _s.ZustellenAsync();

        var mails = _s.Factory.Mails.An(FamilienHelfer.Katrin);
        Assert.Equal(mailsVorher + 1, mails.Count);
        var mail = mails[^1];
        Assert.Equal("Erinnerung: Freizeitpass in Zeltlotse", mail.Betreff);
        Assert.EndsWith("/app/familie", mail.Link, StringComparison.Ordinal);
        Assert.Contains("Miriam Zeltner aus der Leitung einer Freizeit bittet darum, den Freizeitpass von Mia Muster in Zeltlotse zu ergänzen.", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Diese Mail hat die Leitung ausgelöst.", mail.Text, StringComparison.Ordinal);
        var zeile = (await JsonAsync(await _s.Miriam.GetAsync(StandPfad(_s.X), Abbruch))).GetProperty("personen").EnumerateArray().Single(p => p.GetProperty("personId").GetGuid() == _s.Mia);
        Assert.Equal(Mikro(jetzt), zeile.GetProperty("erinnertAm").GetDateTimeOffset());
    }

    [Fact]
    public async Task Erinnern_an_ein_Kind_einer_Gast_Anmeldung_schickt_die_Mail_an_die_Adresse_des_Gast_Zugangs()
    {
        const string sabine = "sabine.beispiel@example.org";
        using var gast = await PostAsync(_s.Anonym, "/api/personen/oeffentlich/gaeste", new GastAnlegen("Sabine", "Beispiel", sabine, "Lina", "Beispiel", new DateOnly(2016, 4, 1)));
        Assert.Equal(HttpStatusCode.Created, gast.StatusCode);
        var gastJson = await JsonAsync(gast);
        var lina = gastJson.GetProperty("kindPersonId").GetGuid();
        using (var anmeldung = await PostAsync(
                   _s.Anonym, $"/api/freizeiten/oeffentlich/{_s.TeilnehmendeX}/anmeldungen", new GastAnmeldungAbsenden(gastJson.GetProperty("gastToken").GetString(), lina, true)))
        {
            Assert.Equal(HttpStatusCode.Created, anmeldung.StatusCode);
        }

        await _s.ZustellenAsync();
        var vorher = _s.Factory.Mails.An(sabine).Count;

        using var antwort = await ErinnernAsync(lina);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        await _s.ZustellenAsync();

        var mails = _s.Factory.Mails.An(sabine);
        Assert.Equal(vorher + 1, mails.Count);
        Assert.Equal("Erinnerung: Freizeitpass in Zeltlotse", mails[^1].Betreff);
        Assert.Contains("den Freizeitpass von Lina Beispiel", mails[^1].Text, StringComparison.Ordinal);
        Assert.Contains("Ohne Konto öffnet der Gast-Link aus der Mail zur Anmeldung den Freizeitpass.", mails[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grenzen_zweites_Mal_409_naechster_Tag_200_selbst_nur_in_Z_abgesagt_404_ohne_Person_400()
    {
        await _s.KindAnmeldenAsync(_s.Emma, _s.TeilnehmendeZ);
        await _s.KindAnmeldenAsync(_s.Mia);
        var paul = await FamilienHelfer.KindAsync(_s.Katrin, "Paul");
        var paulAnmeldung = await _s.KindAnmeldenAsync(paul);
        await _s.AbsagenAsync(paulAnmeldung);
        await _s.ZustellenAsync();
        // Die anfragende Leitung hat selbst eine aktive Freigabe.
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO personen.freigabe (\"Id\", \"OrganisationId\", \"PersonId\", \"FreizeitId\", \"AnmeldungId\", \"Stufe\", \"GueltigBis\", \"AngelegtAm\", \"Fragen\") " +
            "VALUES (@id, @o, @p, @f, @a, 1, @bis, @am, ARRAY['allergien'])",
            ("id", Guid.CreateVersion7()), ("o", _s.A), ("p", _s.MiriamId), ("f", _s.X), ("a", Guid.CreateVersion7()), ("bis", FreigabeEnde), ("am", _s.Factory.Uhr.GetUtcNow()));

        using (var erste = await ErinnernAsync(_s.Mia))
        {
            Assert.Equal(HttpStatusCode.OK, erste.StatusCode);
        }

        using var nochmal = await ErinnernAsync(_s.Mia);
        Assert.Equal(HttpStatusCode.Conflict, nochmal.StatusCode);
        Assert.Equal("konflikt", Code(await ProblemAsync(nochmal)));

        _s.Factory.Uhr.Advance(TimeSpan.FromDays(1));
        using var morgen = await ErinnernAsync(_s.Mia);
        Assert.Equal(HttpStatusCode.OK, morgen.StatusCode);

        using var selbst = await ErinnernAsync(_s.MiriamId);
        using var nurInZ = await ErinnernAsync(_s.Emma);
        using var abgesagt = await ErinnernAsync(paul);
        using var ohnePerson = await ErinnernAsync(null);
        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.BadRequest),
            (selbst.StatusCode, nurInZ.StatusCode, abgesagt.StatusCode, ohnePerson.StatusCode));
        Assert.Equal("Person fehlt.", (await ProblemAsync(ohnePerson)).GetProperty("errors").GetProperty("personId")[0].GetString());

        Assert.Equal(1L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.freizeitpass_erinnerung"));
        Assert.Equal(2L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%FreizeitpassErinnerungMail'"));
        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.nachweis_erinnerung"));
    }

    [Fact]
    public async Task Datenauskunft_nennt_die_Erinnerungen_zum_Freizeitpass_der_Kinder_mit_Datum()
    {
        await _s.KindAnmeldenAsync(_s.Mia);
        (await ErinnernAsync(_s.Mia)).Dispose();
        await AnmeldenAsync(_s.Factory, _s.Katrin, FamilienHelfer.Katrin);

        var auskunft = (await _s.Katrin.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!;

        var abschnitt = Assert.Single(auskunft.Abschnitte, a => a.Titel == "Erinnerungen zum Freizeitpass");
        Assert.Equal(new DatenauskunftZeile($"Erinnert am {FreizeitHelfer.Heute(_s.Factory):dd.MM.yyyy}", "Freizeitpass von Mia Muster, von Miriam Zeltner"), Assert.Single(abschnitt.Zeilen));
    }

    [Fact]
    public async Task Konto_Loeschung_der_Leitung_setzt_den_Platzhalter_die_der_Familie_loescht_die_Zeile()
    {
        await _s.KindAnmeldenAsync(_s.Mia);
        (await ErinnernAsync(_s.Mia)).Dispose();

        await AnmeldenAsync(_s.Factory, _s.Miriam, FreizeitHelfer.Miriam);
        using (var loeschen = await PostAsync(_s.Miriam, "/api/ich/profil/loeschen"))
        {
            Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        }

        Assert.Equal(
            Zeltlotse.SharedKernel.Datenschutz.GeloeschtePerson.Id,
            await WertAsync<Guid>(_s.Verbindung, "SELECT \"ErinnertVon\" FROM personen.freizeitpass_erinnerung WHERE \"PersonId\" = @p", ("p", _s.Mia)));

        await AnmeldenAsync(_s.Factory, _s.Katrin, FamilienHelfer.Katrin);
        var auskunft = (await _s.Katrin.GetFromJsonAsync<DatenauskunftAntwort>("/api/ich/daten", Abbruch))!;
        Assert.EndsWith(
            "Freizeitpass von Mia Muster, von Gelöschte Person",
            Assert.Single(Assert.Single(auskunft.Abschnitte, a => a.Titel == "Erinnerungen zum Freizeitpass").Zeilen).Wert,
            StringComparison.Ordinal);
        using (var loeschen = await PostAsync(_s.Katrin, "/api/ich/profil/loeschen"))
        {
            Assert.Equal(HttpStatusCode.NoContent, loeschen.StatusCode);
        }

        Assert.Equal(0L, await WertAsync<long>(_s.Verbindung, "SELECT count(*) FROM personen.freizeitpass_erinnerung"));
    }
}
