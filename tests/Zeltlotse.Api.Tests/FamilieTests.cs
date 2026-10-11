using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FamilienHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Familie über HTTP (FR-3, AD-19, AD-26, AD-30 „persönlich“), Matrix-Zeilen „Kind hinzufügen“, „Persönlich“ und „Kind
/// bearbeiten“: Elternteil, zweiter Elternteil, Elternteil einer anderen Familie und eine fremde Person aus derselben
/// Organisation; nur Vertragsfelder in der Antwort.
/// </summary>
public sealed class FamilieTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Kind_hinzufuegen_legt_die_Familie_an_und_GET_zeigt_das_Kind_mit_Vertragsfeldern()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var katrin = await AngemeldeterClientAsync(factory, Katrin);
        await FreizeitHelfer.NameSetzenAsync(katrin, "Katrin", "Muster");

        using var antwort = await KindAnlegenAsync(katrin, "Emma", "Muster", EmmaGeboren);

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["personId"], Namen(json));
        var emma = json.GetProperty("personId").GetGuid();
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.personen_schluessel WHERE \"PersonId\" = @id", ("id", emma)));
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familien_elternteil WHERE \"PersonId\" = @id", ("id", await PersonIdAsync(verbindung, Katrin))));

        var familie = await FamilieJsonAsync(katrin);
        Assert.Equal(["einladungen", "eltern", "kinder"], Namen(familie));
        var eltern = Assert.Single(familie.GetProperty("eltern").EnumerateArray());
        Assert.Equal(["nachname", "personId", "vorname"], Namen(eltern));
        Assert.Equal("Katrin", eltern.GetProperty("vorname").GetString());
        var kind = Assert.Single(familie.GetProperty("kinder").EnumerateArray());
        Assert.Equal(["geburtsdatum", "nachname", "personId", "uebergabe", "version", "volljaehrig", "vorname"], Namen(kind));
        Assert.Equal((emma, "Emma", "Muster", "2017-09-02", false), (kind.GetProperty("personId").GetGuid(), kind.GetProperty("vorname").GetString(),
            kind.GetProperty("nachname").GetString(), kind.GetProperty("geburtsdatum").GetString(), kind.GetProperty("volljaehrig").GetBoolean()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, kind.GetProperty("uebergabe").ValueKind);
        Assert.Empty(familie.GetProperty("einladungen").EnumerateArray());
    }

    [Fact]
    public async Task Ohne_Familie_sind_alle_Listen_leer_und_Kinder_stehen_nach_Geburtsdatum()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var katrin = await AngemeldeterClientAsync(factory, Katrin);

        var leer = await FamilieAsync(katrin);
        Assert.Empty(leer.Eltern);
        Assert.Empty(leer.Kinder);
        Assert.Empty(leer.Einladungen);

        await KindAsync(katrin, "Emma", EmmaGeboren);
        await KindAsync(katrin, "Paul", new DateOnly(2015, 3, 14));
        await KindAsync(katrin, "Lina", new DateOnly(2020, 1, 1));

        Assert.Equal(["Paul", "Emma", "Lina"], (await FamilieAsync(katrin)).Kinder.Select(k => k.Vorname));
    }

    public static TheoryData<string?, string?, string?, string, string> Ungueltig => new()
    {
        { "", "Muster", "2017-09-02", "vorname", "Vorname fehlt." },
        { new string('a', 101), null, "2017-09-02", "vorname", "Der Vorname ist zu lang (höchstens 100 Zeichen)." },
        { "Emma", new string('a', 101), "2017-09-02", "nachname", "Der Nachname ist zu lang (höchstens 100 Zeichen)." },
        { "Emma", null, null, "geburtsdatum", "Geburtsdatum fehlt." },
        { "Emma", null, "morgen", "geburtsdatum", "Das Geburtsdatum liegt in der Zukunft." },
        { "Emma", null, "1899-12-31", "geburtsdatum", "Das Geburtsdatum ist ungültig." },
        { "Emma", null, "18", "geburtsdatum", "Kinder ab 18 legen ein eigenes Konto an." },
    };

    [Theory]
    [MemberData(nameof(Ungueltig))]
    public async Task Kind_hinzufuegen_mit_ungueltigem_Feld_ergibt_400_am_Feld(string? vorname, string? nachname, string? geburtsdatum, string feld, string meldung)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var katrin = await AngemeldeterClientAsync(factory, Katrin);
        var heute = FreizeitHelfer.Heute(factory);
        DateOnly? datum = geburtsdatum switch
        {
            null => null,
            "morgen" => heute.AddDays(1),
            "18" => heute.AddYears(-18),
            _ => DateOnly.Parse(geburtsdatum, System.Globalization.CultureInfo.InvariantCulture),
        };

        using var antwort = await KindAnlegenAsync(katrin, vorname, nachname, datum);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal(Problemcodes.Validierung, Code(problem));
        Assert.Equal(meldung, problem.GetProperty("errors").GetProperty(feld)[0].GetString());
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM personen.familie"));
    }

    [Fact]
    public async Task Persoenlich_jede_Person_sieht_nur_die_eigene_Familie_und_fremde_Kinder_sind_404()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = OrgHelfer.Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, OrgHelfer.Gerd);
        // Katrin und Jonas in derselben Organisation: Die Organisation verschafft keinen Zugriff auf Kinder.
        var (orgId, katrin) = await OrgHelfer.OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk", Katrin);
        await OrgHelfer.EinladenAsync(katrin, orgId, OrgHelfer.Jonas, OrgRolle.OrgUeberblick);
        using var jonas = await OrgHelfer.AnnehmenAlsAsync(factory, OrgHelfer.Jonas);
        var emma = await KindAsync(katrin, "Emma");
        using var thomas = await ZweiterElternteilAsync(factory, katrin, Thomas);
        using var daniel = await AngemeldeterClientAsync(factory, Daniel);
        var mia = await KindAsync(daniel, "Mia", new DateOnly(2016, 5, 21), "Probst");
        var paul = await VolljaehrigesKindAsync(katrin, "Paul");

        Assert.Equal([paul, emma], (await FamilieAsync(katrin)).Kinder.Select(k => k.PersonId));
        Assert.Equal([paul, emma], (await FamilieAsync(thomas)).Kinder.Select(k => k.PersonId));
        Assert.Equal(2, (await FamilieAsync(thomas)).Eltern.Count);
        Assert.Equal([mia], (await FamilieAsync(daniel)).Kinder.Select(k => k.PersonId));
        var fremd = await FamilieAsync(jonas);
        Assert.Empty(fremd.Kinder);
        Assert.Empty(fremd.Eltern);

        foreach (var client in new[] { daniel, jonas })
        {
            using var patch = await KindPatchAsync(client, emma, """{"version":1,"alterWert":"Emma","vorname":"X"}""");
            using var uebergabe = await UebergebenAsync(client, paul, PaulMail);
            Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, uebergabe.StatusCode);
        }

        using var vomZweiten = await KindPatchAsync(thomas, emma, $$"""{"version":{{await VersionAsync(thomas, emma)}},"alterWert":"Muster","nachname":"Muster-Lang"}""");
        Assert.Equal(HttpStatusCode.OK, vomZweiten.StatusCode);
        Assert.Equal("Emma", await WertAsync<string>(verbindung, "SELECT \"Vorname\" FROM personen.person WHERE \"Id\" = @id", ("id", emma)));
    }

    [Fact]
    public async Task Kind_bearbeiten_nach_AD_19_mit_Konflikt_und_Geburtsdatum()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var katrin = await AngemeldeterClientAsync(factory, Katrin);
        var emma = await KindAsync(katrin, "Emma");
        var version = await VersionAsync(katrin, emma);

        using var passend = await KindPatchAsync(katrin, emma, $$"""{"version":{{version}},"alterWert":"Emma","vorname":" Emilia "}""");
        Assert.Equal(HttpStatusCode.OK, passend.StatusCode);
        var json = await JsonAsync(passend);
        Assert.Equal(["version", "vorname"], Namen(json));
        Assert.Equal("Emilia", json.GetProperty("vorname").GetString());
        Assert.NotEqual(version, json.GetProperty("version").GetUInt32());

        using var abweichend = await KindPatchAsync(katrin, emma, $$"""{"version":{{version}},"alterWert":"Emma","vorname":"Ella"}""");
        Assert.Equal(HttpStatusCode.Conflict, abweichend.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(abweichend)));

        using var datum = await KindPatchAsync(katrin, emma, $$"""{"version":{{version}},"alterWert":"2017-09-02","geburtsdatum":"2008-03-14"}""");
        Assert.Equal(HttpStatusCode.OK, datum.StatusCode);
        var datumJson = await JsonAsync(datum);
        Assert.Equal(["geburtsdatum", "version"], Namen(datumJson));
        Assert.Equal("2008-03-14", datumJson.GetProperty("geburtsdatum").GetString());
        var kind = Assert.Single((await FamilieAsync(katrin)).Kinder);
        Assert.Equal((new DateOnly(2008, 3, 14), true, "Emilia"), (kind.Geburtsdatum, kind.Volljaehrig, kind.Vorname));
    }

    [Theory]
    [InlineData("""{"version":1,"alterWert":"Emma","vorname":""}""", "vorname", "Vorname fehlt.")]
    [InlineData("""{"version":1,"alterWert":"2017-09-02","geburtsdatum":"kaputt"}""", "geburtsdatum", "Das Geburtsdatum ist ungültig.")]
    [InlineData("""{"version":1,"alterWert":"2017-09-02","geburtsdatum":null}""", "geburtsdatum", "Geburtsdatum fehlt.")]
    [InlineData("""{"version":1,"vorname":"A","nachname":"B"}""", "feld", null)]
    [InlineData("""{"alterWert":"Emma","vorname":"A"}""", "version", null)]
    [InlineData("""{"version":1,"geburtsdatum":20170902}""", "geburtsdatum", null)]
    public async Task Kind_bearbeiten_mit_ungueltigem_Koerper_ergibt_400(string koerper, string feld, string? meldung)
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var katrin = await AngemeldeterClientAsync(factory, Katrin);
        var emma = await KindAsync(katrin, "Emma");

        using var antwort = await KindPatchAsync(katrin, emma, koerper);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var fehler = (await ProblemAsync(antwort)).GetProperty("errors").GetProperty(feld)[0].GetString();
        if (meldung is not null)
        {
            Assert.Equal(meldung, fehler);
        }
    }

    [Fact]
    public async Task Kind_bearbeiten_eines_unbekannten_Kindes_ergibt_404()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var katrin = await AngemeldeterClientAsync(factory, Katrin);
        await KindAsync(katrin, "Emma");

        using var antwort = await KindPatchAsync(katrin, Guid.CreateVersion7(), """{"version":1,"alterWert":null,"vorname":"X"}""");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Zweites_Elternteil_bekommt_Kind_Antwort_ohne_Mail_Adressen_fremder_Personen()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var katrin = await AngemeldeterClientAsync(factory, Katrin);
        await KindAsync(katrin, "Emma");
        using var thomas = await ZweiterElternteilAsync(factory, katrin, Thomas);

        var roh = (await FamilieJsonAsync(thomas)).GetRawText();

        Assert.DoesNotContain("@", roh, StringComparison.Ordinal);
    }
}
