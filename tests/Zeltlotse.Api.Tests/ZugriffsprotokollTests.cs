using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.NachweisSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Einsicht-Protokoll und Zugriffsprotokoll (AD-8, FR-13, Story 1-7) über die echten Endpunkte mit S3: Die prüfende Person öffnet
/// die Einsicht oder eine Datei, das schreibt höchstens einen Eintrag am Tag; fremde Aufrufe schreiben nichts; die Person sieht
/// ihre Einträge und die ihrer Kinder, eine andere Familie nicht.
/// </summary>
public sealed class ZugriffsprotokollTests(PostgresContainer postgres, S3Container s3)
{
    private const string Pfad = "/api/ich/zugriffsprotokoll";

    [Fact]
    public async Task Einsicht_zweimal_am_selben_Tag_und_Datei_ergeben_genau_einen_Eintrag()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var (einsicht, fz, _, _) = await s.FreigegebenUndUebernommenAsync();

        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);
        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);
        using (var datei = await DateiAsync(s.MiriamClient, s.FreizeitPfad, einsicht, fz))
        {
            Assert.Equal(HttpStatusCode.OK, datei.StatusCode);
        }

        var jonas = await s.PersonIdAsync(Jonas);
        var miriam = await s.PersonIdAsync(Miriam);
        Assert.Equal(1L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll"));
        Assert.Equal(1L, await WertAsync<long>(
            s.Verbindung,
            "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @j AND \"LesendePersonId\" = @m AND \"OrganisationId\" = @o AND \"FreizeitId\" = @f AND \"Zweck\" = 1",
            ("j", jonas), ("m", miriam), ("o", s.OrgId), ("f", s.FreizeitId)));
    }

    [Fact]
    public async Task Org_Admin_auf_der_Org_Route_schreibt_den_Eintrag_mit_Organisation_und_Freizeit_der_Einsicht()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var tobias = await s.PersonIdAsync(Tobias);
        Guid einsicht;
        using (var anfragen = await AnfragenAsync(s.AndreaClient, s.OrgPfad, tobias))
        {
            // Tobias hat schon die automatische Anfrage aus der Freizeit-Rolle; Andrea übernimmt sie nach der Freigabe.
            einsicht = (await anfragen.Content.ReadFromJsonAsync<EinsichtAngefragt>(Abbruch))!.EinsichtId;
        }

        var nachweis = await NachweisAsync(s.TobiasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        (await FreigebenAsync(s.TobiasClient, einsicht)).Dispose();
        (await PruefenAsync(s.AndreaClient, s.OrgPfad, einsicht)).Dispose();

        using var datei = await DateiAsync(s.AndreaClient, s.OrgPfad, einsicht, nachweis);

        Assert.Equal(HttpStatusCode.OK, datei.StatusCode);
        var andrea = await s.PersonIdAsync(Andrea);
        Assert.Equal(1L, await WertAsync<long>(
            s.Verbindung,
            "SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = @t AND \"LesendePersonId\" = @a AND \"OrganisationId\" = @o AND \"FreizeitId\" = @f",
            ("t", tobias), ("a", andrea), ("o", s.OrgId), ("f", s.FreizeitId)));
    }

    [Fact]
    public async Task Dieselbe_Einsicht_am_Folgetag_ergibt_einen_zweiten_Eintrag()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var (einsicht, _, _, _) = await s.FreigegebenUndUebernommenAsync();
        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);

        s.Factory.Uhr.Advance(TimeSpan.FromDays(1));
        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);

        Assert.Equal(2L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll"));
        Assert.Equal(2L, await WertAsync<long>(s.Verbindung, "SELECT count(DISTINCT \"Tag\") FROM personen.einsicht_protokoll"));
    }

    [Fact]
    public async Task Fremde_Aufrufe_bekommen_403_oder_404_wie_bisher_und_schreiben_nichts()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var (einsicht, fz, _, _) = await s.FreigegebenUndUebernommenAsync();
        var lea = await s.AngemeldetAsync(Lea);

        using var tobias = await s.TobiasClient.GetAsync($"{s.FreizeitPfad}/einsichten/{einsicht}", Abbruch);
        using var tobiasDatei = await DateiAsync(s.TobiasClient, s.FreizeitPfad, einsicht, fz);
        using var andrea = await s.AndreaClient.GetAsync($"{s.OrgPfad}/einsichten/{einsicht}", Abbruch);
        using var leaAntwort = await lea.GetAsync($"{s.FreizeitPfad}/einsichten/{einsicht}", Abbruch);

        Assert.Equal(HttpStatusCode.NotFound, tobias.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tobiasDatei.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, andrea.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, leaAntwort.StatusCode);
        Assert.Equal(0L, await WertAsync<long>(s.Verbindung, "SELECT count(*) FROM personen.einsicht_protokoll"));
    }

    [Fact]
    public async Task Jonas_liest_seinen_Eintrag_mit_Miriams_Namen_und_Miriam_sieht_ihn_nicht()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var (einsicht, _, _, _) = await s.FreigegebenUndUebernommenAsync();
        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);

        using var antwort = await s.JonasClient.GetAsync(Pfad, Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        var eintrag = Assert.Single(json.EnumerateArray());
        Assert.Equal(
            ["am", "betroffenePersonId", "betroffenerName", "freizeitId", "lesendePersonId", "lesenderName", "organisationId", "zweck"],
            Namen(eintrag));
        Assert.Equal("Jonas Hüttner", eintrag.GetProperty("betroffenerName").GetString());
        Assert.Equal("Miriam Zeltner", eintrag.GetProperty("lesenderName").GetString());
        Assert.Equal(await s.PersonIdAsync(Miriam), eintrag.GetProperty("lesendePersonId").GetGuid());
        Assert.Equal(s.OrgId, eintrag.GetProperty("organisationId").GetGuid());
        Assert.Equal(s.FreizeitId, eintrag.GetProperty("freizeitId").GetGuid());
        Assert.Equal("nachweiseEingesehen", eintrag.GetProperty("zweck").GetString());
        // PostgreSQL speichert Mikrosekunden.
        Assert.True((s.Factory.Uhr.GetUtcNow() - eintrag.GetProperty("am").GetDateTimeOffset()).Duration() < TimeSpan.FromMilliseconds(1));

        var miriam = await s.MiriamClient.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>(Pfad, Abbruch);
        Assert.Empty(miriam!);
    }

    [Fact]
    public async Task Neueste_Eintraege_stehen_zuerst()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var (einsicht, _, _, _) = await s.FreigegebenUndUebernommenAsync();
        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);
        s.Factory.Uhr.Advance(TimeSpan.FromDays(1));
        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);

        var eintraege = (await s.JonasClient.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>(Pfad, Abbruch))!;

        Assert.Equal(2, eintraege.Count);
        Assert.True(eintraege[0].Am > eintraege[1].Am);
    }

    [Fact]
    public async Task Eintrag_ueber_ein_Kind_sehen_beide_Eltern_aber_keine_andere_Familie()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var katrin = await AngemeldeterClientAsync(factory, FamilienHelfer.Katrin);
        (await PostAsync(katrin, "/api/ich/profil")).Dispose();
        await FreizeitHelfer.NameSetzenAsync(katrin, "Katrin", "Muster");
        var emma = await FamilienHelfer.KindAsync(katrin, "Emma");
        using var thomas = await FamilienHelfer.ZweiterElternteilAsync(factory, katrin, FamilienHelfer.Thomas);
        using var daniel = await AngemeldeterClientAsync(factory, FamilienHelfer.Daniel);
        (await PostAsync(daniel, "/api/ich/profil")).Dispose();
        await FamilienHelfer.KindAsync(daniel, "Mia", nachname: "Probst");
        var lesend = Guid.CreateVersion7();
        await AusfuehrenAsync(
            verbindung,
            "INSERT INTO personen.einsicht_protokoll (\"Id\", \"BetroffenePersonId\", \"LesendePersonId\", \"OrganisationId\", \"FreizeitId\", \"Zweck\", \"Am\", \"Tag\") " +
            "VALUES (@id, @emma, @lesend, NULL, NULL, 1, now(), current_date)",
            ("id", Guid.CreateVersion7()), ("emma", emma), ("lesend", lesend));

        var fuerKatrin = (await katrin.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>(Pfad, Abbruch))!;
        var fuerThomas = (await thomas.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>(Pfad, Abbruch))!;
        var fuerDaniel = (await daniel.GetFromJsonAsync<List<ZugriffsprotokollEintrag>>(Pfad, Abbruch))!;

        var eintrag = Assert.Single(fuerKatrin);
        Assert.Equal((emma, "Emma Muster"), (eintrag.BetroffenePersonId, eintrag.BetroffenerName));
        // Die lesende Person gibt es nicht (mehr): kein Name.
        Assert.Null(eintrag.LesenderName);
        Assert.Equal(emma, Assert.Single(fuerThomas).BetroffenePersonId);
        Assert.Empty(fuerDaniel);
    }

    [Fact]
    public async Task Ohne_Sitzung_401()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync(Pfad, Abbruch);

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }
}
