using System.Net;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.NachweisSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Eigene Nachweise über <c>/api/ich/nachweise</c> (FR-8, AD-14, AD-15, AD-26) mit echtem S3: anlegen, Feldregeln, Art doppelt,
/// Datei-Fälle, nur Chiffre im Speicher, persönlich (fremde Person und Elternteil → <c>404</c>, nur Vertragsfelder).
/// </summary>
public sealed class NachweiseTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private NachweisSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await ErstellenAsync(postgres, s3);

    public ValueTask DisposeAsync() => _s.DisposeAsync();

    [Fact]
    public async Task Anlegen_und_PDF_hochladen_im_Speicher_liegt_nur_Chiffre()
    {
        Guid id;
        using (var anlegen = await AnlegenAsync(_s.JonasClient, "fuehrungszeugnis", "2025-11-03"))
        {
            Assert.Equal(HttpStatusCode.Created, anlegen.StatusCode);
            var json = await JsonAsync(anlegen);
            Assert.Equal(["nachweisId"], Namen(json));
            id = json.GetProperty("nachweisId").GetGuid();
        }

        using var hochladen = await HochladenAsync(_s.JonasClient, id, Pdf);

        Assert.Equal(HttpStatusCode.OK, hochladen.StatusCode);
        var antwort = await JsonAsync(hochladen);
        Assert.Equal(["hochgeladenAm"], Namen(antwort));
        Assert.Equal(_s.Factory.Uhr.GetUtcNow(), antwort.GetProperty("hochgeladenAm").GetDateTimeOffset());
        var roh = await _s.RohAsync(s3, id);
        Assert.NotEqual(Pdf, roh);
        Assert.Null(DateiPruefung.ArtErkennen(roh));
        Assert.Equal(Pdf, await _s.EntschluesseltAsync(id));
    }

    [Theory]
    [InlineData("fuehrungszeugnis", null, "ausstellungsdatum", "Ausstellungsdatum fehlt.")]
    [InlineData("fuehrungszeugnis", "2099-01-01", "ausstellungsdatum", "Das Ausstellungsdatum liegt in der Zukunft.")]
    [InlineData("fuehrungszeugnis", "1899-12-31", "ausstellungsdatum", "Das Ausstellungsdatum ist ungültig.")]
    [InlineData("fuehrungszeugnis", "03.11.2025", "ausstellungsdatum", "Das Ausstellungsdatum ist ungültig.")]
    [InlineData(null, "2025-11-03", "art", "Art fehlt.")]
    [InlineData("reisepass", "2025-11-03", "art", "Art fehlt.")]
    [InlineData("3", "2025-11-03", "art", "Art fehlt.")]
    public async Task Anlegen_prueft_jedes_Feld(string? art, string? datum, string feld, string meldung)
    {
        using var antwort = await AnlegenAsync(_s.JonasClient, art, datum);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(meldung, (await ProblemAsync(antwort)).GetProperty("errors").GetProperty(feld)[0].GetString());
    }

    [Fact]
    public async Task Art_in_beliebiger_Schreibweise_und_doppelt_ergibt_409()
    {
        using (var erste = await AnlegenAsync(_s.JonasClient, "ErsteHilfe", "2025-04-15"))
        {
            Assert.Equal(HttpStatusCode.Created, erste.StatusCode);
        }

        using var zweite = await AnlegenAsync(_s.JonasClient, "ersteHilfe", "2025-05-01");

        Assert.Equal(HttpStatusCode.Conflict, zweite.StatusCode);
        Assert.Equal("konflikt", Code(await ProblemAsync(zweite)));
    }

    [Fact]
    public async Task Datei_Faelle_werden_am_Inhalt_geprueft()
    {
        var id = await NachweisAsync(_s.JonasClient, "juleica", "2024-05-20");
        var faelle = new (byte[]? Inhalt, string Name, HttpStatusCode Status, string? Meldung)[]
        {
            ([], "leer.pdf", HttpStatusCode.BadRequest, "Die Datei ist leer."),
            (new byte[(10 * 1024 * 1024) + 1], "gross.pdf", HttpStatusCode.BadRequest, "Die Datei ist größer als 10 MB."),
            ("Kein Bild und kein PDF"u8.ToArray(), "text.pdf", HttpStatusCode.BadRequest, "Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF."),
            (null, "", HttpStatusCode.BadRequest, "Datei fehlt."),
            (Png, "juleica.pdf", HttpStatusCode.OK, null),
        };

        foreach (var (inhalt, name, status, meldung) in faelle)
        {
            using var antwort = await HochladenAsync(_s.JonasClient, id, inhalt, name);
            Assert.Equal(status, antwort.StatusCode);
            if (meldung is not null)
            {
                Assert.Equal(meldung, (await ProblemAsync(antwort)).GetProperty("errors").GetProperty("datei")[0].GetString());
            }
        }

        Assert.Equal(Png, await _s.EntschluesseltAsync(id));
    }

    [Fact]
    public async Task Datum_aendern_nach_AD_19()
    {
        var id = await NachweisAsync(_s.JonasClient, "juleica", "2024-05-20");
        var version = (await EigeneAsync(_s.JonasClient)).Nachweise.Single().Version;

        using var ok = await SendenAsync(_s.JonasClient, HttpMethod.Patch, $"/api/ich/nachweise/{id}", $$"""{"version":{{version}},"alterWert":"2024-05-20","ausstellungsdatum":"2024-05-21"}""");
        using var konflikt = await SendenAsync(_s.JonasClient, HttpMethod.Patch, $"/api/ich/nachweise/{id}", $$"""{"version":{{version}},"alterWert":"2024-05-20","ausstellungsdatum":"2024-06-01"}""");
        using var falsch = await SendenAsync(_s.JonasClient, HttpMethod.Patch, $"/api/ich/nachweise/{id}", $$"""{"version":{{version}},"alterWert":"2024-05-21","ausstellungsdatum":"2099-01-01"}""");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var json = await JsonAsync(ok);
        Assert.Equal(["ausstellungsdatum", "version"], Namen(json));
        Assert.Equal("2024-05-21", json.GetProperty("ausstellungsdatum").GetString());
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, falsch.StatusCode);
    }

    [Fact]
    public async Task Waehrend_der_Pruefung_409_nach_der_Entscheidung_und_nach_Ablauf_wieder_200()
    {
        var (_, fz, eh, _) = await _s.FreigegebenUndUebernommenAsync();
        var versionen = (await EigeneAsync(_s.JonasClient)).Nachweise.ToDictionary(n => n.Id, n => n.Version);

        using (var patch = await SendenAsync(_s.JonasClient, HttpMethod.Patch, $"/api/ich/nachweise/{fz}", $$"""{"version":{{versionen[fz]}},"alterWert":"2025-11-03","ausstellungsdatum":"2025-11-04"}"""))
        {
            Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
            Assert.Equal("konflikt", Code(await ProblemAsync(patch)));
        }

        using (var datei = await HochladenAsync(_s.JonasClient, fz, Png))
        {
            Assert.Equal(HttpStatusCode.Conflict, datei.StatusCode);
            Assert.Equal("konflikt", Code(await ProblemAsync(datei)));
        }

        var einsicht = await AnfrageAsync(_s.JonasClient);
        (await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh)).Dispose();
        using (var nachEntscheidung = await SendenAsync(_s.JonasClient, HttpMethod.Patch, $"/api/ich/nachweise/{eh}", $$"""{"version":{{versionen[eh]}},"alterWert":"2025-04-15","ausstellungsdatum":"2025-04-16"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, nachEntscheidung.StatusCode);
        }

        _s.Factory.Uhr.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        using var nachAblauf = await HochladenAsync(_s.JonasClient, fz, Png);
        using var patchNachAblauf = await SendenAsync(_s.JonasClient, HttpMethod.Patch, $"/api/ich/nachweise/{fz}", $$"""{"version":{{versionen[fz]}},"alterWert":"2025-11-03","ausstellungsdatum":"2025-11-04"}""");
        Assert.Equal(HttpStatusCode.OK, nachAblauf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, patchNachAblauf.StatusCode);
        Assert.Equal(Png, await _s.EntschluesseltAsync(fz));
    }

    [Fact]
    public async Task Persoenlich_fremde_Person_bekommt_404_und_sieht_nur_eigene_Daten()
    {
        var id = await NachweisAsync(_s.JonasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        var einsicht = await AnfrageAsync(_s.JonasClient);
        using var lea = await _s.AngemeldetAsync(Lea);

        using var patch = await SendenAsync(lea, HttpMethod.Patch, $"/api/ich/nachweise/{id}", """{"version":1,"alterWert":"2025-11-03","ausstellungsdatum":"2025-11-04"}""");
        using var datei = await HochladenAsync(lea, id, Png);
        using var freigeben = await FreigebenAsync(lea, einsicht);
        using var miriamFreigeben = await FreigebenAsync(_s.MiriamClient, einsicht);

        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, datei.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, freigeben.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, miriamFreigeben.StatusCode);
        var leaSieht = await EigeneJsonAsync(lea);
        Assert.Empty(leaSieht.GetProperty("nachweise").EnumerateArray());
        Assert.Empty(leaSieht.GetProperty("anfragen").EnumerateArray());
        Assert.Equal(Pdf, await _s.EntschluesseltAsync(id));

        var jonas = await EigeneJsonAsync(_s.JonasClient);
        Assert.Equal(["anfragen", "nachweise"], Namen(jonas));
        var nachweis = Assert.Single(jonas.GetProperty("nachweise").EnumerateArray());
        Assert.Equal(["ablehnungshinweis", "art", "ausstellungsdatum", "hochgeladenAm", "id", "version"], Namen(nachweis));
        Assert.Equal("fuehrungszeugnis", nachweis.GetProperty("art").GetString());
        var anfrage = Assert.Single(jonas.GetProperty("anfragen").EnumerateArray());
        Assert.Equal(["angefragtAm", "angefragtVon", "freigegebenBis", "freizeitId", "id", "organisationId", "status"], Namen(anfrage));
        Assert.Equal(
            ("angefragt", _s.FreizeitId, _s.OrgId),
            (anfrage.GetProperty("status").GetString(), anfrage.GetProperty("freizeitId").GetGuid(), anfrage.GetProperty("organisationId").GetGuid()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, anfrage.GetProperty("angefragtVon").ValueKind);
    }

    [Fact]
    public async Task Ein_Elternteil_hat_keinen_Zugriff_auf_Nachweise_eines_Kindes()
    {
        using var katrin = await _s.AngemeldetAsync(FamilienHelfer.Katrin);
        var kind = await FamilienHelfer.KindAsync(katrin, "Emma");
        var nachweis = Guid.CreateVersion7();
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO personen.nachweis (\"Id\", \"PersonId\", \"Art\", \"Ausstellungsdatum\") VALUES (@id, @kind, 4, '2025-01-01')",
            ("id", nachweis), ("kind", kind));

        var einsicht = Guid.CreateVersion7();
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO personen.nachweis_einsicht (\"Id\", \"OrganisationId\", \"PersonId\", \"Status\", \"AngefragtAm\", \"Folgenummer\") VALUES (@id, @org, @kind, 1, now(), 0)",
            ("id", einsicht), ("org", _s.OrgId), ("kind", kind));
        await AusfuehrenAsync(
            _s.Verbindung,
            "INSERT INTO organisationen.nachweis_bestaetigung (\"OrganisationId\", \"PersonId\", \"Art\", \"EingesehenAm\", \"GueltigBis\", \"GeprueftVon\") VALUES (@org, @kind, 4, now(), '2030-01-01', @kind)",
            ("org", _s.OrgId), ("kind", kind));

        using var patch = await SendenAsync(katrin, HttpMethod.Patch, $"/api/ich/nachweise/{nachweis}", """{"version":1,"alterWert":"2025-01-01","ausstellungsdatum":"2025-01-02"}""");
        using var datei = await HochladenAsync(katrin, nachweis, Pdf);
        using var freigeben = await FreigebenAsync(katrin, einsicht);

        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, datei.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, freigeben.StatusCode);
        var eigene = await EigeneAsync(katrin);
        Assert.Empty(eigene.Nachweise);
        Assert.Empty(eigene.Anfragen);
        using var bestaetigungen = await katrin.GetAsync("/api/ich/organisationen/nachweis-bestaetigungen", Abbruch);
        Assert.Empty((await JsonAsync(bestaetigungen)).EnumerateArray());
    }
}
