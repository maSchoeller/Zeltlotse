using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.NachweisSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Nachweis-Richtlinie über die echten Endpunkte (FR-9, NFR-5, AD-19): vier Vorgabezeilen einer neuen Organisation, Bearbeiten
/// mit Feldregeln und Konflikt, unbekannte Art → <c>404</c>; dazu die Rollenlisten der Prüf-Endpunkte gegen die erlaubten
/// Aktionen (AD-29).
/// </summary>
public sealed class NachweisRichtlinieTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private NachweisSzenario _s = null!;

    private string Pfad => $"/api/organisationen/{_s.OrgId}/nachweis-richtlinie";

    public async ValueTask InitializeAsync() => _s = await ErstellenAsync(postgres, s3);

    public ValueTask DisposeAsync() => _s.DisposeAsync();

    [Fact]
    public async Task Neue_Organisation_hat_vier_Vorgabezeilen_in_Katalogreihenfolge()
    {
        using var antwort = await _s.AndreaClient.GetAsync(Pfad, Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var zeilen = (await JsonAsync(antwort)).EnumerateArray().ToList();
        Assert.Equal(["art", "gueltigkeitMonate", "pflicht", "version"], Namen(zeilen[0]));
        Assert.Equal(
            [("fuehrungszeugnis", true, 60), ("juleica", false, 36), ("ersteHilfe", false, 24), ("praeventionsschulung", false, 60)],
            zeilen.Select(z => (z.GetProperty("art").GetString(), z.GetProperty("pflicht").GetBoolean(), z.GetProperty("gueltigkeitMonate").GetInt32())));
        Assert.All(zeilen, z => Assert.NotEqual(0u, z.GetProperty("version").GetUInt32()));
    }

    [Fact]
    public async Task Nur_gespeicherte_Zeilen_ohne_Ersatzwerte()
    {
        await AusfuehrenAsync(_s.Verbindung, "DELETE FROM organisationen.nachweis_richtlinie WHERE \"OrganisationId\" = @o AND \"Art\" = 2", ("o", _s.OrgId));

        using var antwort = await _s.AndreaClient.GetAsync(Pfad, Abbruch);
        using var patch = await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/juleica", """{"version":1,"alterWert":"36","gueltigkeitMonate":"48"}""");

        Assert.Equal(["fuehrungszeugnis", "ersteHilfe", "praeventionsschulung"], (await JsonAsync(antwort)).EnumerateArray().Select(z => z.GetProperty("art").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task Bearbeiten_nach_AD_19_mit_Feldregeln_Konflikt_und_unbekannter_Art()
    {
        var version = (await JsonAsync(await _s.AndreaClient.GetAsync(Pfad, Abbruch)))[1].GetProperty("version").GetUInt32();

        using var ok = await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/juleica", $$"""{"version":{{version}},"alterWert":"36","gueltigkeitMonate":"48"}""");
        using var null_ = await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/juleica", $$"""{"version":{{version}},"alterWert":"48","gueltigkeitMonate":"0"}""");
        using var konflikt = await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/JULEICA", $$"""{"version":{{version}},"alterWert":"true","pflicht":"false"}""");
        using var pflicht = await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/Juleica", $$"""{"version":{{version}},"alterWert":"false","pflicht":"true"}""");
        using var xyz = await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/xyz", $$"""{"version":{{version}},"alterWert":"false","pflicht":"true"}""");
        using var zahl = await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/2", $$"""{"version":{{version}},"alterWert":"false","pflicht":"true"}""");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var json = await JsonAsync(ok);
        Assert.Equal(["gueltigkeitMonate", "version"], Namen(json));
        Assert.Equal("48", json.GetProperty("gueltigkeitMonate").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, null_.StatusCode);
        Assert.Equal(
            "Die Gültigkeit muss zwischen 1 und 120 Monaten liegen.",
            (await ProblemAsync(null_)).GetProperty("errors").GetProperty("gueltigkeitMonate")[0].GetString());
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        Assert.Equal("konflikt", Code(await ProblemAsync(konflikt)));
        Assert.Equal(HttpStatusCode.OK, pflicht.StatusCode);
        Assert.Equal("true", (await JsonAsync(pflicht)).GetProperty("pflicht").GetString());
        Assert.Equal(HttpStatusCode.NotFound, xyz.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, zahl.StatusCode);
    }

    [Fact]
    public async Task Geaenderte_Gueltigkeit_wirkt_ab_der_naechsten_Bestaetigung()
    {
        var version = (await JsonAsync(await _s.AndreaClient.GetAsync(Pfad, Abbruch)))[2].GetProperty("version").GetUInt32();
        (await SendenAsync(_s.AndreaClient, HttpMethod.Patch, $"{Pfad}/ersteHilfe", $$"""{"version":{{version}},"alterWert":"24","gueltigkeitMonate":"12"}""")).Dispose();
        var (einsicht, _, eh, _) = await _s.FreigegebenUndUebernommenAsync();

        (await BestaetigenAsync(_s.MiriamClient, _s.FreizeitPfad, einsicht, eh)).Dispose();
        await _s.Factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(new DateOnly(2026, 4, 15), await WertAsync<DateOnly>(_s.Verbindung, "SELECT \"GueltigBis\" FROM organisationen.nachweis_bestaetigung"));
    }

    [Fact]
    public void Pruef_Endpunkte_nutzen_die_Rollen_der_erlaubten_Aktion()
    {
        var endpunkte = _s.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var freizeit = endpunkte.Where(e => (e.RoutePattern.RawText ?? "").Contains("/freizeiten/{freizeitId:guid}/personen/nachweise", StringComparison.Ordinal)).ToList();
        var org = endpunkte.Where(e => (e.RoutePattern.RawText ?? "").Contains("/organisationen/{orgId:guid}/personen/nachweise", StringComparison.Ordinal)).ToList();

        Assert.Equal(7, freizeit.Count);
        Assert.Equal(7, org.Count);
        // Leitung → „nachweise-pruefen“ im Freizeit-Kontext, Org-Admin → in „erlaubt“ der Organisation (je eigene Tests).
        Assert.All(freizeit, e => Assert.Equal([FreizeitRolle.Leitung], e.Metadata.GetMetadata<NurFreizeitRolleMetadaten>()!.Rollen));
        Assert.All(org, e => Assert.Equal([OrgRolle.OrgAdmin], e.Metadata.GetMetadata<NurOrgRolleMetadaten>()!.Rollen));
    }
}
